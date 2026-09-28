using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;

namespace OpenQuickHost;

public sealed partial class LocalAgentApiServer
{
    // One visible UI-testing session globally: multiple agents cannot fight for input.
    private static readonly ConcurrentDictionary<Guid, UiSession> UiSessions = new();
    private static readonly object UiSessionLock = new();

    private sealed class UiSession
    {
        private readonly object _gate = new();
        private CancellationTokenSource _operationCancel = new();
        private readonly List<CancellationTokenSource> _retiredTokens = [];
        private string _state = "active";
        private string _reason = "";
        private string _stage = "准备就绪";
        private DateTimeOffset _lastActivity = DateTimeOffset.UtcNow;

        public Guid Id { get; } = Guid.NewGuid();
        public string ExtensionId { get; }
        public IntPtr WindowHandle { get; }
        public string Mode { get; }
        public bool Ephemeral { get; }
        public DateTimeOffset StartedAtUtc { get; } = DateTimeOffset.UtcNow;
        public UiSessionOverlay? Indicator { get; set; }

        public UiSession(string extensionId, IntPtr handle, string mode, bool ephemeral)
        {
            ExtensionId = extensionId;
            WindowHandle = handle;
            Mode = mode;
            Ephemeral = ephemeral;
        }

        public string State { get { lock (_gate) return _state; } }
        public string Reason { get { lock (_gate) return _reason; } }
        public string Stage { get { lock (_gate) return _stage; } }
        public DateTimeOffset LastActivity { get { lock (_gate) return _lastActivity; } }

        public CancellationToken BeginOperation(string stage)
        {
            lock (_gate)
            {
                if (_state == "paused") throw new UiApiException(409, "ui_session_paused");
                if (_state != "active") throw new UiApiException(410, "ui_session_ended");
                _stage = stage;
                _lastActivity = DateTimeOffset.UtcNow;
                Indicator?.Refresh();
                return _operationCancel.Token;
            }
        }

        public void Touch(string? stage = null)
        {
            lock (_gate)
            {
                if (_state != "active") return;
                _lastActivity = DateTimeOffset.UtcNow;
                if (stage != null) _stage = stage;
                Indicator?.Refresh();
            }
        }

        public bool Pause(string reason)
        {
            lock (_gate)
            {
                if (_state != "active") return false;
                _state = "paused";
                _reason = reason;
                _lastActivity = DateTimeOffset.UtcNow;
                _operationCancel.Cancel();
                Indicator?.Refresh();
                HostAssets.AppendLog($"UI automation paused: extension={ExtensionId}, reason={reason}");
                return true;
            }
        }

        public bool Resume(bool confirmedInToolbar)
        {
            lock (_gate)
            {
                if (_state != "paused") return false;
                if (_reason == "user_input" && !confirmedInToolbar)
                    throw new UiApiException(409, "confirm_resume_in_desktop_toolbar");
                _retiredTokens.Add(_operationCancel);
                _operationCancel = new CancellationTokenSource();
                _state = "active";
                _reason = "";
                _stage = "等待下一步";
                _lastActivity = DateTimeOffset.UtcNow;
                Indicator?.ResetInputBaseline();
                Indicator?.Refresh();
                HostAssets.AppendLog($"UI automation resumed: extension={ExtensionId}");
                return true;
            }
        }

        public void End(string reason)
        {
            lock (_gate)
            {
                if (_state == "ended") return;
                _state = "ended";
                _reason = reason;
                _operationCancel.Cancel();
                foreach (var token in _retiredTokens) token.Cancel();
                Indicator?.Dispose();
                HostAssets.AppendLog($"UI automation ended: extension={ExtensionId}, reason={reason}");
            }
        }
    }

    private static object SessionDto(UiSession session) => new
    {
        sessionId = session.Id.ToString("N"),
        extensionId = session.ExtensionId,
        mode = session.Mode,
        state = session.State,
        stage = session.Stage,
        pauseReason = session.Reason,
        physicalInputOwned = false,
        startedAtUtc = session.StartedAtUtc,
        lastActivityUtc = session.LastActivity,
        expiresAfterIdleSeconds = 120,
        emergencyHotkey = "Ctrl+Alt+Shift+F12"
    };


    private static UiSession FindUiSession(Guid id) =>
        UiSessions.TryGetValue(id, out var session)
            ? session : throw new UiApiException(404, "ui_session_not_found");

    private static void EndUiSession(UiSession session, string reason)
    {
        if (UiSessions.TryRemove(session.Id, out _))
            session.End(reason);
    }

    private static void EndAllUiSessions(string reason)
    {
        foreach (var session in UiSessions.Values.ToArray())
            EndUiSession(session, reason);
    }

    private static void EndUiSessionsForExtension(string extensionId, string reason)
    {
        foreach (var session in UiSessions.Values.Where(x =>
            string.Equals(x.ExtensionId, extensionId, StringComparison.OrdinalIgnoreCase)).ToArray())
            EndUiSession(session, reason);
    }

    private static void EnsureNoActiveUiSession(string extensionId)
    {
        if (UiSessions.Values.Any(x =>
            string.Equals(x.ExtensionId, extensionId, StringComparison.OrdinalIgnoreCase)))
            throw new UiApiException(409, "close_ui_session_before_deploying");
    }

    private static async Task<UiSession> CreateUiSessionAsync(
        CommandItem command, UiTarget target, string mode, bool ephemeral,
        bool userConsent, CancellationToken cancellationToken)
    {
        if (mode is not ("scoped" or "foreground"))
            throw new UiApiException(400, "invalid_ui_session_mode");
        if (mode == "foreground" && !userConsent)
            throw new UiApiException(403, "foreground_mode_requires_user_consent");
        if (!IsWindow(target.Handle) || !IsWindowVisible(target.Handle))
            throw new UiApiException(409, "window_not_visible");
        var session = new UiSession(command.ExtensionId!, target.Handle, mode, ephemeral);
        lock (UiSessionLock)
        {
            if (!UiSessions.IsEmpty)
                throw new UiApiException(409, "another_ui_session_is_active");
            if (!UiSessions.TryAdd(session.Id, session))
                throw new UiApiException(409, "session_creation_conflict");
        }
        try
        {
            var overlay = new UiSessionOverlay(
                target.Handle, command.ExtensionId!, mode,
                () => session.State,
                () => session.Stage,
                () => session.LastActivity,
                reason => session.Pause(reason),
                () => session.Pause("user_pause"),
                () => session.Resume(confirmedInToolbar: true),
                () => EndUiSession(session, "user_stop_or_timeout"));
            session.Indicator = overlay;
            await overlay.StartAsync(cancellationToken);
            HostAssets.AppendLog($"UI automation session created: extension={command.ExtensionId}, mode={mode}");
            return session;
        }
        catch
        {
            EndUiSession(session, "overlay_start_failed");
            throw;
        }
    }

    private static async Task<(UiSession Session, bool Temporary)> SelectUiSessionAsync(
        CommandItem command, UiTarget target, JsonElement payload, CancellationToken cancellationToken)
    {
        var rawId = GetString(payload, "sessionId");
        if (!string.IsNullOrWhiteSpace(rawId))
        {
            if (!Guid.TryParseExact(rawId, "N", out var id))
                throw new UiApiException(400, "invalid_session_id");
            var existing = FindUiSession(id);
            if (!string.Equals(existing.ExtensionId, command.ExtensionId, StringComparison.OrdinalIgnoreCase) ||
                existing.WindowHandle != target.Handle)
                throw new UiApiException(403, "ui_session_target_mismatch");
            return (existing, false);
        }
        var mode = GetString(payload, "sessionMode") ?? "scoped";
        if (mode != "scoped")
            throw new UiApiException(400, "foreground_mode_requires_explicit_session");
        return (await CreateUiSessionAsync(command, target, mode,
            ephemeral: true, userConsent: false, cancellationToken), true);
    }


    private async Task<bool> TryHandleUiSessionApiAsync(
        HttpListenerRequest request, HttpListenerResponse response, string path)
    {
        const string globalPrefix = "/v1/ui/sessions/";
        if (path.StartsWith(globalPrefix, StringComparison.Ordinal))
        {
            if (!IsTrustedDeploymentRequest(request))
                throw new UiApiException(403, "ui_origin_or_token_invalid");
            var rest = path[globalPrefix.Length..].Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (rest.Length is < 1 or > 2 || !Guid.TryParseExact(rest[0], "N", out var id))
                throw new UiApiException(404, "ui_session_not_found");
            var session = FindUiSession(id);
            if (rest.Length == 1 && request.HttpMethod == "GET")
                await WriteJsonAsync(response, 200, new { ok = true, session = SessionDto(session) });
            else if (rest.Length == 2 && rest[1] == "pause" && request.HttpMethod == "POST")
            {
                session.Pause("api_pause");
                await WriteJsonAsync(response, 200, new { ok = true, session = SessionDto(session) });
            }
            else if (rest.Length == 2 && rest[1] == "resume" && request.HttpMethod == "POST")
            {
                session.Resume(confirmedInToolbar: false);
                await WriteJsonAsync(response, 200, new { ok = true, session = SessionDto(session) });
            }
            else if (rest.Length == 1 && request.HttpMethod == "DELETE")
            {
                EndUiSession(session, "api_stop");
                await WriteJsonAsync(response, 200, new { ok = true, ended = true });
            }
            else throw new UiApiException(404, "ui_session_endpoint_not_found");
            return true;
        }

        const string extensionsPrefix = "/v1/extensions/";
        if (!path.StartsWith(extensionsPrefix, StringComparison.Ordinal) ||
            !path.EndsWith("/ui/sessions", StringComparison.Ordinal))
            return false;
        if (request.HttpMethod != "POST")
            throw new UiApiException(405, "method_not_allowed");
        if (!IsTrustedDeploymentRequest(request))
            throw new UiApiException(403, "ui_origin_or_token_invalid");
        var extId = Uri.UnescapeDataString(path[
            extensionsPrefix.Length..^"/ui/sessions".Length]);
        var command = RequireUiExtension(extId);
        if (_deployingExtensions.ContainsKey(command.ExtensionId!))
            throw new UiApiException(409, "deployment_in_progress");
        ValidateUiBody(request);
        var payload = await ReadJsonBodyAsync(request);
        var launch = payload.TryGetProperty("launchIfNeeded", out var l) &&
            l.ValueKind == JsonValueKind.True;
        var mode = GetString(payload, "mode") ?? "scoped";
        var consent = payload.TryGetProperty("userConsent", out var c) &&
            c.ValueKind == JsonValueKind.True;
        var window = launch ? await EnsureUiOpenAsync(command, true, _cts.Token)
            : RequireUiTarget(command, GetString(payload, "windowId"));
        var createdSession = await CreateUiSessionAsync(command, window, mode,
            ephemeral: false, userConsent: consent, _cts.Token);
        await WriteJsonAsync(response, 201, new { ok = true, session = SessionDto(createdSession) });
        return true;
    }
}

