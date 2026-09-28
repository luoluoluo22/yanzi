using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Automation;
using OpenQuickHost.Sync;

namespace OpenQuickHost;

public sealed partial class LocalAgentApiServer
{
    // All UI targets are resolved from an installed extension, never an arbitrary HWND/PID.
    private sealed record UiTarget(IntPtr Handle, object? Bridge, string Title, int ProcessId);
    private sealed record CaptureInfo(Guid Id, string ExtensionId, string File, DateTimeOffset CreatedAt,
        int Width, int Height, string Mode);
    private sealed class UiApiException(int status, string code) : Exception(code)
    {
        public int Status { get; } = status;
        public string Code { get; } = code;
    }

    private static readonly ConcurrentDictionary<Guid, CaptureInfo> UiCaptures = new();
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UiGates =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly string UiCaptureRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenQuickHost", "AgentCaptures");

    private delegate bool EnumWindowsCallback(IntPtr hwnd, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr data);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out UiRect rect);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(
        IntPtr hwnd, StringBuilder buffer, int maxCount);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);
    [StructLayout(LayoutKind.Sequential)] private struct UiRect
    {
        public int Left, Top, Right, Bottom;
    }

    private static object? GetUiBridge(string id) =>
        HostObjectRegistry.TryGetObject($"{id}-window", out var bridge) ? bridge : null;

    private static IntPtr BridgeWindowHandle(object? bridge)
    {
        try
        {
            return bridge?.GetType().GetProperty("WindowHandle", BindingFlags.Public | BindingFlags.Instance)
                ?.GetValue(bridge) is IntPtr hwnd && IsWindow(hwnd) ? hwnd : IntPtr.Zero;
        }
        catch { return IntPtr.Zero; }
    }

    private static List<UiTarget> GetUiTargets(CommandItem command)
    {
        var result = new List<UiTarget>();
        var bridge = GetUiBridge(command.ExtensionId!);
        var handle = BridgeWindowHandle(bridge);
        if (handle != IntPtr.Zero)
        {
            result.Add(new UiTarget(handle, bridge, GetUiWindowTitle(handle), Environment.ProcessId));
        }
        var ids = RunningExtensionRegistry.GetSnapshot()
            .Where(x => string.Equals(x.ExtensionId, command.ExtensionId, StringComparison.OrdinalIgnoreCase) &&
                        x.ProcessId > 0 && x.ProcessId != Environment.ProcessId)
            // In-process extensions must register their own HWND. Enumerating all host
            // windows would accidentally expose unrelated Yanzi/settings windows.
            .Select(x => x.ProcessId).Distinct().ToHashSet();
        if (ids.Count > 0)
        {
            EnumWindows((hwnd, _) =>
            {
                if (IsWindow(hwnd) && IsWindowVisible(hwnd))
                {
                    GetWindowThreadProcessId(hwnd, out uint pid);
                    if (ids.Contains((int)pid) && result.All(w => w.Handle != hwnd))
                        result.Add(new UiTarget(hwnd, null, GetUiWindowTitle(hwnd), (int)pid));
                }
                return true;
            }, IntPtr.Zero);
        }
        return result.OrderByDescending(x => IsWindowVisible(x.Handle))
            .ThenByDescending(x => !string.IsNullOrWhiteSpace(x.Title)).ToList();
    }

    private static string GetUiWindowTitle(IntPtr hwnd)
    {
        var buffer = new StringBuilder(256);
        GetWindowText(hwnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    private static UiTarget RequireUiTarget(CommandItem command, string? windowId = null)
    {
        var targets = GetUiTargets(command);
        int index = 0;
        if (!string.IsNullOrWhiteSpace(windowId) && windowId != "primary")
        {
            if (!windowId.StartsWith("window-", StringComparison.Ordinal) ||
                !int.TryParse(windowId["window-".Length..], out index) || index < 1)
                throw new UiApiException(400, "invalid_window_id");
        }
        if (index >= targets.Count || !IsWindowVisible(targets[index].Handle) || IsIconic(targets[index].Handle))
            throw new UiApiException(409, "window_not_visible_call_ui_open_first");
        return targets[index];
    }

    private static UiTarget RequireCaptureTarget(CommandItem command, string? windowId, string mode)
    {
        var targets = GetUiTargets(command);
        int index = 0;
        if (!string.IsNullOrWhiteSpace(windowId) && windowId != "primary")
        {
            if (!windowId.StartsWith("window-", StringComparison.Ordinal) ||
                !int.TryParse(windowId["window-".Length..], out index) || index < 1)
                throw new UiApiException(400, "invalid_window_id");
        }
        if (index >= targets.Count)
            throw new UiApiException(409, "window_not_initialized_call_ui_open_first");
        var target = targets[index];
        if (IsWindowVisible(target.Handle) && !IsIconic(target.Handle))
            return target;
        if (mode != "window" && target.Bridge?.GetType().GetMethod(
                "CaptureSnapshot", new[] { typeof(string) }) != null)
            return target;
        throw new UiApiException(409, "window_hidden_use_visual_capture_or_ui_open");
    }

    private static object GetUiWindowDto(UiTarget target, int index)
    {
        GetWindowRect(target.Handle, out var rect);
        return new
        {
            windowId = index == 0 ? "primary" : $"window-{index}",
            title = target.Title, visible = IsWindowVisible(target.Handle),
            minimized = IsIconic(target.Handle), processId = target.ProcessId,
            bounds = new { x = rect.Left, y = rect.Top,
                width = rect.Right - rect.Left, height = rect.Bottom - rect.Top }
        };
    }

    private static CommandItem RequireUiExtension(string id) =>
        FindExtension(id) ?? throw new UiApiException(404, "extension_not_found");

    private static void ValidateUiBody(HttpListenerRequest request)
    {
        if (request.ContentLength64 > 32768) throw new UiApiException(413, "ui_request_too_large");
    }

    private static async Task<UiTarget> EnsureUiOpenAsync(CommandItem command, bool launch, CancellationToken token)
    {
        var id = command.ExtensionId!;
        var running = RunningExtensionRegistry.IsRunning(id);
        if (!running && !launch)
            throw new UiApiException(409, "extension_not_running");

        if (!running)
        {
            if (!ScriptExtensionRunner.CanExecute(command))
                throw new UiApiException(422, "extension_cannot_be_launched_by_ui_api");
            var result = await ScriptExtensionRunner.ExecuteAsync(command, null, "agent-ui-open", token);
            if (!result.Success)
                throw new UiApiException(422, "extension_launch_failed: " + result.Error);
        }

        // Show rather than toggle; toggling a visible calendar would hide it.
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var bridge = GetUiBridge(id);
            var show = bridge?.GetType().GetMethod("ShowCalendar", new[] { typeof(string) });
            if (show != null)
            {
                show.Invoke(bridge, new object?[] { null });
            }
            var targets = GetUiTargets(command);
            var visible = targets.FirstOrDefault(t => IsWindowVisible(t.Handle) && !IsIconic(t.Handle));
            if (visible != null) return visible;
            if (attempt == 0 && targets.Count > 0 && bridge == null)
                ShowWindow(targets[0].Handle, 9); // SW_RESTORE, for external-process windows only.
            await Task.Delay(100, token);
        }
        throw new UiApiException(422, "window_not_available");
    }

    private static async Task<CaptureInfo> TakeUiCaptureAsync(
        CommandItem command, UiTarget target, string mode, CancellationToken token)
    {
        await Task.Yield();
        if (mode is not ("auto" or "visual" or "window"))
            throw new UiApiException(400, "invalid_capture_mode");
        var visualAdapter = mode != "window" ? target.Bridge?.GetType().GetMethod(
            "CaptureSnapshot", new[] { typeof(string) }) : null;
        // Extension-owned visual adapters can render hidden flyouts without
        // stealing focus. Real HWND captures still require a visible window.
        if ((!IsWindowVisible(target.Handle) || IsIconic(target.Handle)) && visualAdapter == null)
            throw new UiApiException(409, "window_hidden_use_visual_capture_or_ui_open");
        Directory.CreateDirectory(UiCaptureRoot);
        PruneUiCaptures();
        var id = Guid.NewGuid();
        var file = Path.Combine(UiCaptureRoot, id.ToString("N") + ".png");
        var usedMode = "window";
        try
        {
            if (visualAdapter != null)
            {
                // Extension-provided WPF rendering remains on its own UI dispatcher.
                var result = visualAdapter.Invoke(target.Bridge, new object[] { file }) as string;
                if (result?.StartsWith("error:", StringComparison.OrdinalIgnoreCase) == true)
                    throw new UiApiException(422, "extension_capture_failed: " + result);
                usedMode = "visual";
            }
            else if (mode == "visual")
            {
                throw new UiApiException(422, "visual_capture_not_supported_by_extension");
            }
            else
            {
                if (!GetWindowRect(target.Handle, out var rect))
                    throw new UiApiException(422, "window_bounds_unavailable");
                var width = rect.Right - rect.Left;
                var height = rect.Bottom - rect.Top;
                if (width is < 1 or > 8192 || height is < 1 or > 8192)
                    throw new UiApiException(422, "window_size_out_of_range");
                using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                using var graphics = Graphics.FromImage(bitmap);
                var hdc = graphics.GetHdc();
                bool captured;
                try { captured = PrintWindow(target.Handle, hdc, 2) || PrintWindow(target.Handle, hdc, 0); }
                finally { graphics.ReleaseHdc(hdc); }
                if (!captured) throw new UiApiException(422, "window_capture_unavailable");
                bitmap.Save(file, ImageFormat.Png);
            }
            token.ThrowIfCancellationRequested();
            if (!File.Exists(file) || new FileInfo(file).Length is < 100 or > 20971520)
                throw new UiApiException(422, "invalid_capture_output");
            using var image = System.Drawing.Image.FromFile(file);
            var saved = new CaptureInfo(id, command.ExtensionId!, file, DateTimeOffset.UtcNow,
                image.Width, image.Height, usedMode);
            UiCaptures[id] = saved;
            return saved;
        }
        catch
        {
            try { File.Delete(file); } catch { }
            throw;
        }
    }

    private static object CaptureDto(CaptureInfo item) => new
    {
        captureId = item.Id.ToString("N"), extensionId = item.ExtensionId,
        createdAtUtc = item.CreatedAt, width = item.Width, height = item.Height,
        mode = item.Mode, imageUrl = $"/v1/captures/{item.Id:N}/image",
        expiresAtUtc = item.CreatedAt.AddHours(12)
    };

    private static void PruneUiCaptures()
    {
        // Also remove stale images left by previous host sessions; never touch other files.
        foreach (var file in Directory.EnumerateFiles(UiCaptureRoot, "*.png", SearchOption.TopDirectoryOnly))
        {
            try
            {
                if (File.GetCreationTimeUtc(file) < DateTime.UtcNow.AddHours(-12))
                    File.Delete(file);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        var expired = UiCaptures.Values.OrderByDescending(x => x.CreatedAt).Skip(64)
            .Concat(UiCaptures.Values.Where(x => x.CreatedAt < DateTimeOffset.UtcNow.AddHours(-12)))
            .DistinctBy(x => x.Id).ToArray();
        foreach (var item in expired)
        {
            if (UiCaptures.TryRemove(item.Id, out _))
                try { File.Delete(item.File); } catch { }
        }
    }


    private static AutomationElement UiRoot(UiTarget target)
    {
        if (!IsWindow(target.Handle) || !IsWindowVisible(target.Handle))
            throw new UiApiException(409, "window_not_visible");
        return AutomationElement.FromHandle(target.Handle)
            ?? throw new UiApiException(422, "automation_root_unavailable");
    }

    private static AutomationElement? FindUiElement(
        AutomationElement root, string automationId, string name, bool visibleOnly = true)
    {
        if (string.IsNullOrWhiteSpace(automationId) && string.IsNullOrWhiteSpace(name))
            throw new UiApiException(400, "automationId_or_name_required");
        var property = !string.IsNullOrWhiteSpace(automationId)
            ? AutomationElement.AutomationIdProperty : AutomationElement.NameProperty;
        var value = !string.IsNullOrWhiteSpace(automationId) ? automationId : name;
        var found = root.FindAll(TreeScope.Descendants, new PropertyCondition(property, value));
        var matches = found.Cast<AutomationElement>()
            .Where(x => !visibleOnly || !x.Current.IsOffscreen).Take(2).ToArray();
        if (matches.Length > 1)
            throw new UiApiException(409, "ambiguous_ui_selector");
        return matches.FirstOrDefault();
    }

    private static object[] InspectUiElements(UiTarget window, int maxDepth, int limit)
    {
        var results = new List<object>();
        var queue = new Queue<(AutomationElement Element, int Depth)>();
        queue.Enqueue((UiRoot(window), 0));
        while (queue.Count > 0 && results.Count < limit)
        {
            var (item, depth) = queue.Dequeue();
            try
            {
                var current = item.Current;
                if (depth > 0 && !current.IsOffscreen)
                    results.Add(new
                    {
                        automationId = current.AutomationId, name = current.Name,
                        type = current.ControlType.ProgrammaticName,
                        enabled = current.IsEnabled, depth
                    });
                if (depth >= maxDepth) continue;
                var children = item.FindAll(TreeScope.Children, Condition.TrueCondition);
                foreach (AutomationElement child in children)
                {
                    if (queue.Count + results.Count >= 3 * limit) break;
                    queue.Enqueue((child, depth + 1));
                }
            }
            catch (ElementNotAvailableException) { }
        }
        return results.ToArray();
    }

    private static object ExecuteUiAction(UiTarget window, JsonElement step, int index)
    {
        if (step.ValueKind != JsonValueKind.Object)
            throw new UiApiException(400, "invalid_ui_action");
        var kind = GetString(step, "type") ?? "";
        var automationId = GetString(step, "automationId") ?? "";
        var name = GetString(step, "name") ?? "";
        var element = FindUiElement(UiRoot(window), automationId, name)
            ?? throw new UiApiException(422, "ui_element_not_found");
        if (!element.Current.IsEnabled)
            throw new UiApiException(422, "ui_element_disabled");
        switch (kind)
        {
            case "invoke":
                if (!element.TryGetCurrentPattern(InvokePattern.Pattern, out var invoke))
                    throw new UiApiException(422, "invoke_not_supported");
                ((InvokePattern)invoke).Invoke();
                break;
            case "setValue":
                var value = GetString(step, "value");
                if (value == null || value.Length > 2000)
                    throw new UiApiException(400, "ui_value_required_or_too_long");
                if (!element.TryGetCurrentPattern(ValuePattern.Pattern, out var setValue) ||
                    ((ValuePattern)setValue).Current.IsReadOnly)
                    throw new UiApiException(422, "set_value_not_supported");
                ((ValuePattern)setValue).SetValue(value);
                break;
            case "focus":
                element.SetFocus();
                break;
            case "toggle":
                if (!element.TryGetCurrentPattern(TogglePattern.Pattern, out var toggle))
                    throw new UiApiException(422, "toggle_not_supported");
                ((TogglePattern)toggle).Toggle();
                break;
            case "select":
                if (!element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var select))
                    throw new UiApiException(422, "select_not_supported");
                ((SelectionItemPattern)select).Select();
                break;
            case "expand":
            case "collapse":
                if (!element.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var expand))
                    throw new UiApiException(422, "expand_collapse_not_supported");
                if (kind == "expand") ((ExpandCollapsePattern)expand).Expand();
                else ((ExpandCollapsePattern)expand).Collapse();
                break;
            default:
                throw new UiApiException(400, "unsupported_ui_action");
        }
        return new { index, type = kind, automationId, name, ok = true };
    }

    private static async Task WaitForUiElementAsync(
        UiTarget window, string automationId, CancellationToken token)
    {
        for (int i = 0; i < 30; i++)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if (FindUiElement(UiRoot(window), automationId, "") != null)
                    return;
            }
            catch (ElementNotAvailableException) { }
            await Task.Delay(100, token);
        }
        throw new UiApiException(422, "ui_wait_timeout: " + automationId);
    }

    private async Task<bool> TryHandleUiApiAsync(
        HttpListenerRequest request, HttpListenerResponse response, string path)
    {
        // Session control bypasses the per-extension UI gate so users can pause
        // or stop even while an automation request is running.
        if (await TryHandleUiSessionApiAsync(request, response, path))
            return true;
        const string capturesPrefix = "/v1/captures/";
        if (path.StartsWith(capturesPrefix, StringComparison.Ordinal))
        {
            if (request.HttpMethod != "GET")
                throw new UiApiException(405, "method_not_allowed");
            if (!IsTrustedDeploymentRequest(request))
                throw new UiApiException(403, "ui_origin_or_token_invalid");
            var segment = path[capturesPrefix.Length..];
            var isImage = segment.EndsWith("/image", StringComparison.Ordinal);
            if (isImage) segment = segment[..^"/image".Length];
            if (!Guid.TryParseExact(segment, "N", out var id) ||
                !UiCaptures.TryGetValue(id, out var capture) ||
                capture.CreatedAt < DateTimeOffset.UtcNow.AddHours(-12))
                throw new UiApiException(404, "capture_not_found_or_expired");
            if (isImage)
            {
                var bytes = await File.ReadAllBytesAsync(capture.File, _cts.Token);
                response.StatusCode = 200;
                response.ContentType = "image/png";
                response.Headers["Cache-Control"] = "private, no-store";
                response.ContentLength64 = bytes.Length;
                await response.OutputStream.WriteAsync(bytes, _cts.Token);
                response.Close();
            }
            else
            {
                await WriteJsonAsync(response, 200, new { ok = true, capture = CaptureDto(capture) });
            }
            return true;
        }

        const string extensionsPrefix = "/v1/extensions/";
        if (!path.StartsWith(extensionsPrefix, StringComparison.Ordinal))
            return false;
        var uiAt = path.IndexOf("/ui/", extensionsPrefix.Length, StringComparison.Ordinal);
        if (uiAt < 0) return false;
        var idText = Uri.UnescapeDataString(path[extensionsPrefix.Length..uiAt]);
        var route = path[(uiAt + "/ui/".Length)..];
        if (!IsTrustedDeploymentRequest(request))
            throw new UiApiException(403, "ui_origin_or_token_invalid");
        var command = RequireUiExtension(idText);
        var extensionId = command.ExtensionId!;
        if (_deployingExtensions.ContainsKey(extensionId))
            throw new UiApiException(409, "deployment_in_progress");
        var mutex = UiGates.GetOrAdd(extensionId, _ => new SemaphoreSlim(1, 1));
        if (!await mutex.WaitAsync(0, _cts.Token))
            throw new UiApiException(409, "ui_operation_in_progress");

        try
        {
            if (route == "windows" && request.HttpMethod == "GET")
            {
                var windows = GetUiTargets(command);
                await WriteJsonAsync(response, 200, new
                {
                    ok = true, id = extensionId, isRunning = RunningExtensionRegistry.IsRunning(extensionId),
                    windows = windows.Select((x, i) => GetUiWindowDto(x, i)).ToArray()
                });
            }
            else if (route == "open" && request.HttpMethod == "POST")
            {
                ValidateUiBody(request);
                var payload = await ReadJsonBodyAsync(request);
                var launch = !payload.TryGetProperty("launchIfNeeded", out var value) ||
                    value.ValueKind != JsonValueKind.False;
                var window = await EnsureUiOpenAsync(command, launch, _cts.Token);
                await WriteJsonAsync(response, 200, new { ok = true, id = extensionId,
                    window = GetUiWindowDto(window, 0) });
            }
            else if (route == "capture" && request.HttpMethod == "POST")
            {
                ValidateUiBody(request);
                var payload = await ReadJsonBodyAsync(request);
                var launch = payload.TryGetProperty("launchIfNeeded", out var value) &&
                    value.ValueKind == JsonValueKind.True;
                var mode = GetString(payload, "mode") ?? "auto";
                var selectedWindow = GetString(payload, "windowId");
                UiTarget window;
                try
                {
                    // A running flyout may have hidden itself when the browser
                    // regained focus. Prefer its offscreen visual snapshot rather
                    // than activating it again and racing with Deactivated.
                    window = RequireCaptureTarget(command, selectedWindow, mode);
                }
                catch (UiApiException ex) when (launch &&
                    (ex.Code == "window_not_initialized_call_ui_open_first" ||
                     ex.Code == "window_hidden_use_visual_capture_or_ui_open"))
                {
                    await EnsureUiOpenAsync(command, true, _cts.Token);
                    window = RequireCaptureTarget(command, selectedWindow, mode);
                }
                bool backgroundCapture = !IsWindowVisible(window.Handle) || IsIconic(window.Handle);
                if (backgroundCapture && string.IsNullOrWhiteSpace(GetString(payload, "sessionId")))
                {
                    // An invisible window needs no onscreen border or toolbar.
                    // This is a scoped visual render, not physical desktop input.
                    if (!UiSessions.IsEmpty)
                        throw new UiApiException(409, "ui_session_id_required_during_active_test");
                    var backgroundImage = await TakeUiCaptureAsync(command, window, mode, _cts.Token);
                    await WriteJsonAsync(response, 201, new
                    {
                        ok = true, backgroundCapture = true, windowVisible = false,
                        capture = CaptureDto(backgroundImage)
                    });
                    return true;
                }
                UiSession session;
                bool temporary;
                try
                {
                    (session, temporary) = await SelectUiSessionAsync(
                        command, window, payload, _cts.Token);
                }
                catch (UiApiException ex) when (ex.Code == "window_not_visible" &&
                    mode != "window" && !IsWindowVisible(window.Handle) &&
                    string.IsNullOrWhiteSpace(GetString(payload, "sessionId")) &&
                    UiSessions.IsEmpty &&
                    window.Bridge?.GetType().GetMethod(
                        "CaptureSnapshot", new[] { typeof(string) }) != null)
                {
                    // Focus can change between target lookup and overlay startup.
                    // In that case, fall back to the same non-activating visual render.
                    var backgroundImage = await TakeUiCaptureAsync(command, window, mode, _cts.Token);
                    await WriteJsonAsync(response, 201, new
                    {
                        ok = true, backgroundCapture = true, windowVisible = false,
                        capture = CaptureDto(backgroundImage)
                    });
                    return true;
                }
                try
                {
                    using var operation = CancellationTokenSource.CreateLinkedTokenSource(
                        _cts.Token, session.BeginOperation("正在截图"));
                    if (temporary) await Task.Delay(240, operation.Token);
                    var capture = await TakeUiCaptureAsync(command, window, mode, operation.Token);
                    operation.Token.ThrowIfCancellationRequested();
                    session.Touch("截图完成");
                    await WriteJsonAsync(response, 201, new
                    {
                        ok = true, sessionId = session.Id.ToString("N"),
                        backgroundCapture = backgroundCapture,
                        windowVisible = !backgroundCapture,
                        capture = CaptureDto(capture)
                    });
                }
                catch (OperationCanceledException) when (session.State == "paused")
                {
                    await WriteJsonAsync(response, 409, new { ok = false,
                        error = "ui_session_paused", sessionId = session.Id.ToString("N") });
                }
                finally
                {
                    if (temporary) EndUiSession(session, "capture_complete");
                }
            }
            else if (route == "elements" && request.HttpMethod == "GET")
            {
                var window = RequireUiTarget(command, request.QueryString["windowId"]);
                var depth = int.TryParse(request.QueryString["maxDepth"], out var d) ? Math.Clamp(d, 1, 5) : 4;
                var limit = int.TryParse(request.QueryString["limit"], out var n) ? Math.Clamp(n, 1, 150) : 80;
                await WriteJsonAsync(response, 200, new { ok = true, id = extensionId,
                    elements = InspectUiElements(window, depth, limit) });
            }
            else if (route == "actions" && request.HttpMethod == "POST")
            {
                ValidateUiBody(request);
                var payload = await ReadJsonBodyAsync(request);
                if (!payload.TryGetProperty("actions", out var steps) ||
                    steps.ValueKind != JsonValueKind.Array || steps.GetArrayLength() is < 1 or > 8)
                    throw new UiApiException(400, "actions_array_required_1_to_8");
                var selectedWindow = GetString(payload, "windowId");
                UiTarget window;
                try
                {
                    window = RequireUiTarget(command, selectedWindow);
                }
                catch (UiApiException ex) when (ex.Code == "window_not_visible_call_ui_open_first" &&
                    (!payload.TryGetProperty("showIfHidden", out var show) ||
                     show.ValueKind != JsonValueKind.False))
                {
                    // The browser's click often hides taskbar-style flyouts.
                    // Explicit UI actions may bring their own extension back to
                    // the foreground; passive capture never does.
                    var requestedId = GetString(payload, "sessionId");
                    if (!string.IsNullOrWhiteSpace(requestedId) &&
                        Guid.TryParseExact(requestedId, "N", out var parsedId) &&
                        FindUiSession(parsedId).State != "active")
                        throw new UiApiException(409, "ui_session_paused");
                    await EnsureUiOpenAsync(command, false, _cts.Token);
                    window = RequireUiTarget(command, selectedWindow);
                }
                var results = new List<object>();
                var captures = new List<object>();
                bool captureEach = payload.TryGetProperty("captureEach", out var ce) && ce.ValueKind == JsonValueKind.True;
                bool captureAfter = payload.TryGetProperty("captureAfter", out var ca) && ca.ValueKind == JsonValueKind.True;
                var (session, temporary) = await SelectUiSessionAsync(
                    command, window, payload, _cts.Token);
                int index = 0;
                try
                {
                    using var operation = CancellationTokenSource.CreateLinkedTokenSource(
                        _cts.Token, session.BeginOperation("正在执行 UI 测试"));
                    if (temporary) await Task.Delay(240, operation.Token);
                    foreach (var step in steps.EnumerateArray())
                    {
                        operation.Token.ThrowIfCancellationRequested();
                        session.Touch($"执行第 {index + 1}/{steps.GetArrayLength()} 步");
                        var result = ExecuteUiAction(window, step, index++);
                        results.Add(result);
                        operation.Token.ThrowIfCancellationRequested();
                        var waitFor = GetString(step, "waitFor");
                        if (!string.IsNullOrWhiteSpace(waitFor))
                            await WaitForUiElementAsync(window, waitFor, operation.Token);
                        if (captureEach)
                        {
                            await Task.Delay(100, operation.Token);
                            captures.Add(CaptureDto(await TakeUiCaptureAsync(
                                command, window, "auto", operation.Token)));
                        }
                    }
                    if (captureAfter && !captureEach)
                    {
                        await Task.Delay(100, operation.Token);
                        captures.Add(CaptureDto(await TakeUiCaptureAsync(
                            command, window, "auto", operation.Token)));
                    }
                    operation.Token.ThrowIfCancellationRequested();
                    session.Touch("操作完成");
                    HostAssets.AppendLog($"Agent UI actions: extension={extensionId}, steps={index}");
                    await WriteJsonAsync(response, 200, new { ok = true, id = extensionId,
                        sessionId = session.Id.ToString("N"), steps = results, captures });
                }
                catch (OperationCanceledException) when (session.State == "paused")
                {
                    await WriteJsonAsync(response, 409, new { ok = false, id = extensionId,
                        error = "ui_session_paused", sessionId = session.Id.ToString("N"),
                        completedSteps = results, captures });
                }
                finally
                {
                    if (temporary) EndUiSession(session, "actions_complete");
                }
            }
            else
            {
                throw new UiApiException(404, "ui_endpoint_not_found");
            }
        }
        finally { mutex.Release(); }
        return true;
    }
}
