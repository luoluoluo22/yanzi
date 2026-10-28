using System.Net;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using OpenQuickHost.Sync;

namespace OpenQuickHost;

public sealed partial class LocalAgentApiServer
{
    private async Task HandleSettingsRoute20Async(HttpListenerRequest request, HttpListenerResponse response, string path)
    {

        var settings = AppSettingsStore.Load();
        await WriteJsonAsync(response, 200, new {
            selectedGroupId = settings.SelectedQuickPanelGlobalGroupId,
            groups = settings.QuickPanelGlobalGroups
        });
        return;
    }

    private async Task HandleSettingsRoute21Async(HttpListenerRequest request, HttpListenerResponse response, string path)
    {

        var payload = await ReadJsonBodyAsync(request);
        var id = GetString(payload, "id") ?? Guid.NewGuid().ToString("N");
        var name = GetString(payload, "name");
        if (string.IsNullOrWhiteSpace(name))
        {
            await WriteJsonAsync(response, 400, new { error = "name_required" });
            return;
        }

        var settings = AppSettingsStore.Load();
        settings.QuickPanelGlobalGroups.Add(new QuickPanelGroupSettings
        {
            Id = id,
            Name = name,
            Slots = Enumerable.Repeat<string?>(null, 12).ToList(),
            SlotItems = Enumerable.Repeat<QuickPanelSlotItem?>(null, 12).ToList()
        });
        AppSettingsStore.Save(settings);
        _onMutated(null);
        await WriteJsonAsync(response, 200, new { ok = true, id });
        return;
    }

    private async Task HandleSettingsRoute22Async(HttpListenerRequest request, HttpListenerResponse response, string path)
    {

        var payload = await ReadJsonBodyAsync(request);
        var extensionId = GetString(payload, "extensionId");
        var groupId = GetString(payload, "groupId");
        if (string.IsNullOrWhiteSpace(extensionId))
        {
            await WriteJsonAsync(response, 400, new { error = "extensionId_required" });
            return;
        }

        var settings = AppSettingsStore.Load();
        var group = !string.IsNullOrWhiteSpace(groupId)
            ? settings.QuickPanelGlobalGroups.FirstOrDefault(item => string.Equals(item.Id, groupId, StringComparison.OrdinalIgnoreCase))
            : settings.QuickPanelGlobalGroups.FirstOrDefault(item => string.Equals(item.Id, settings.SelectedQuickPanelGlobalGroupId, StringComparison.OrdinalIgnoreCase))
              ?? settings.QuickPanelGlobalGroups.FirstOrDefault();

        if (group == null)
        {
            await WriteJsonAsync(response, 400, new { error = "no_quickpanel_group" });
            return;
        }

        group.SlotItems ??= new List<QuickPanelSlotItem?>();
        while (group.SlotItems.Count < 12)
        {
            group.SlotItems.Add(null);
        }

        if (group.SlotItems.Any(slot =>
                slot != null &&
                ((!slot.IsFolder && string.Equals(slot.ExtensionId, extensionId, StringComparison.OrdinalIgnoreCase)) ||
                 (slot.IsFolder && slot.FolderExtensionIds != null && slot.FolderExtensionIds.Any(id => string.Equals(id, extensionId, StringComparison.OrdinalIgnoreCase))))))
        {
            await WriteJsonAsync(response, 200, new { ok = true, message = "already_exists" });
            return;
        }

        var index = group.SlotItems.FindIndex(item => item == null);
        if (index >= 0)
        {
            group.SlotItems[index] = new QuickPanelSlotItem { ExtensionId = extensionId };
            group.Slots = group.SlotItems.Select(item => item != null && !item.IsFolder ? item.ExtensionId : null).ToList();

            if (string.Equals(group.Id, settings.SelectedQuickPanelGlobalGroupId, StringComparison.OrdinalIgnoreCase))
            {
                settings.QuickPanelSlots ??= new List<string?>();
                while (settings.QuickPanelSlots.Count <= index)
                {
                    settings.QuickPanelSlots.Add(null);
                }
                settings.QuickPanelSlots[index] = extensionId;
            }

            AppSettingsStore.Save(settings);
            _onMutated(null);
            await WriteJsonAsync(response, 200, new { ok = true, index, groupId = group.Id });
            return;
        }

        await WriteJsonAsync(response, 400, new { error = "quickpanel_full" });
        return;
    }

    private async Task HandleSettingsRoute54Async(HttpListenerRequest request, HttpListenerResponse response, string path)
    {

        var settings = AppSettingsStore.Load();
        await WriteJsonAsync(response, 200, new { ok = true, settings });
        return;
    }

    private async Task HandleSettingsRoute55Async(HttpListenerRequest request, HttpListenerResponse response, string path)
    {

        await WriteYanmStateAsync(response, AppSettingsStore.Load());
        return;
    }

    private async Task HandleSettingsRoute56Async(HttpListenerRequest request, HttpListenerResponse response, string path)
    {

        var payload = await ReadJsonBodyAsync(request);
        if (!payload.TryGetProperty("yanm", out var yanmElement) || yanmElement.ValueKind != JsonValueKind.Object)
        {
            await WriteJsonAsync(response, 400, new { error = "yanm_required" });
            return;
        }

        var yanm = JsonSerializer.Deserialize<YanmSettings>(yanmElement.GetRawText(), JsonOptions) ?? new YanmSettings();
        var settings = AppSettingsStore.Load();
        settings.Yanm = yanm;
        var updatedAtUtc = GetString(payload, "updatedAtUtc");
        settings.YanmStateUpdatedAtUtc = string.IsNullOrWhiteSpace(updatedAtUtc)
            ? DateTime.UtcNow.ToString("O")
            : updatedAtUtc;
        AppSettingsStore.Save(settings);
        _onSettingsChanged?.Invoke("api-yanm-state-updated", true);

        if (_onPushToMobile != null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _onPushToMobile("YanziSync", "yanm_updated");
                }
                catch {}
            });
        }

        await WriteYanmStateAsync(response, AppSettingsStore.Load());
        return;
    }

    private async Task HandleSettingsRoute57Async(HttpListenerRequest request, HttpListenerResponse response, string path)
    {

        var payload = await ReadJsonBodyAsync(request);
        var patch = ReadYanmComponentStatePatch(payload);
        if (patch.Count == 0)
        {
            await WriteJsonAsync(response, 400, new { error = "component_state_required" });
            return;
        }

        var settings = AppSettingsStore.Load();
        settings.Yanm ??= new YanmSettings();
        settings.Yanm.ComponentState ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in patch)
        {
            settings.Yanm.ComponentState[item.Key] = item.Value;
        }

        var updatedAtUtc = GetString(payload, "updatedAtUtc");
        settings.YanmStateUpdatedAtUtc = string.IsNullOrWhiteSpace(updatedAtUtc)
            ? DateTime.UtcNow.ToString("O")
            : updatedAtUtc;
        AppSettingsStore.Save(settings);
        _onSettingsChanged?.Invoke("api-yanm-component-state-updated", false);

        if (_onPushToMobile != null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _onPushToMobile("YanziSync", "yanm_updated");
                }
                catch {}
            });
        }

        await WriteYanmStateAsync(response, AppSettingsStore.Load());
        return;
    }

    private async Task HandleSettingsRoute58Async(HttpListenerRequest request, HttpListenerResponse response, string path)
    {

        var settings = AppSettingsStore.Load();
        var list = settings.MobileExtensionsJson ?? "[]";
        await WriteJsonAsync(response, 200, new { ok = true, extensions = list });
        return;
    }

    private async Task HandleSettingsRoute59Async(HttpListenerRequest request, HttpListenerResponse response, string path)
    {

        var payload = await ReadJsonBodyAsync(request);
        if (!payload.TryGetProperty("extensions", out var extElement) || extElement.ValueKind != JsonValueKind.String)
        {
            await WriteJsonAsync(response, 400, new { error = "extensions_string_required" });
            return;
        }
        var extensionsStr = extElement.GetString() ?? "[]";
        var settings = AppSettingsStore.Load();
        settings.MobileExtensionsJson = extensionsStr;
        AppSettingsStore.Save(settings);
        _onSettingsChanged?.Invoke("api-mobile-extensions-updated", true);
        await WriteJsonAsync(response, 200, new { ok = true });
        return;
    }

    private async Task HandleSettingsRoute60Async(HttpListenerRequest request, HttpListenerResponse response, string path)
    {

        var payload = await ReadJsonBodyAsync(request);
        var settings = AppSettingsStore.Load();
        if (payload.TryGetProperty("themeMode", out var themeEl) && themeEl.ValueKind == JsonValueKind.String)
        {
            settings.ThemeMode = themeEl.GetString()!;
        }
        if (payload.TryGetProperty("launchAtStartup", out var launchEl) && (launchEl.ValueKind == JsonValueKind.True || launchEl.ValueKind == JsonValueKind.False))
        {
            settings.LaunchAtStartup = launchEl.GetBoolean();
        }
        AppSettingsStore.Save(settings);
        _onMutated(null);
        await WriteJsonAsync(response, 200, new { ok = true, settings });
        return;
    }

    private async Task HandleSettingsRoute61Async(HttpListenerRequest request, HttpListenerResponse response, string path)
    {

        var extensionId = request.QueryString["extensionId"];
        if (string.IsNullOrWhiteSpace(extensionId))
        {
            await WriteJsonAsync(response, 400, new { error = "missing_extensionId" });
            return;
        }

        var settings = AppSettingsStore.Load();
        var removed = false;
        foreach (var group in settings.QuickPanelGlobalGroups)
        {
            if (group.Slots != null)
            {
                for (int i = 0; i < group.Slots.Count; i++)
                {
                    if (string.Equals(group.Slots[i], extensionId, StringComparison.OrdinalIgnoreCase))
                    {
                        group.Slots[i] = null;
                        if (group.SlotItems != null && i < group.SlotItems.Count)
                        {
                            group.SlotItems[i] = null;
                        }
                        removed = true;
                    }
                }
            }
        }
        if (removed)
        {
            AppSettingsStore.Save(settings);
            _onMutated(null);
        }
        await WriteJsonAsync(response, 200, new { ok = true, removed });
        return;
    }

    private async Task HandleSettingsRoute62Async(HttpListenerRequest request, HttpListenerResponse response, string path)
    {

        var payload = await ReadJsonBodyAsync(request);
        var groupId = GetString(payload, "groupId");
        var items = payload.TryGetProperty("items", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Array
            ? itemsEl.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => !string.IsNullOrEmpty(s)).ToList()
            : new List<string>();

        if (string.IsNullOrWhiteSpace(groupId))
        {
            await WriteJsonAsync(response, 400, new { error = "missing_groupId" });
            return;
        }

        var settings = AppSettingsStore.Load();
        var group = settings.QuickPanelGlobalGroups.FirstOrDefault(g => g.Id == groupId);
        if (group != null)
        {
            group.Slots = items.Select(x => string.IsNullOrEmpty(x) ? null : x).ToList();
            group.SlotItems = new List<QuickPanelSlotItem?>(new QuickPanelSlotItem?[group.Slots.Count]);
            AppSettingsStore.Save(settings);
            _onMutated(null);
            await WriteJsonAsync(response, 200, new { ok = true });
        }
        else
        {
            await WriteJsonAsync(response, 404, new { error = "group_not_found" });
        }
        return;
    }
}
