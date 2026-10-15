using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenQuickHost.Sync;

internal static class SyncUserText
{
    internal static string Device(string? id, string? name, string currentId) =>
        !string.IsNullOrWhiteSpace(id) && string.Equals(id, currentId, StringComparison.OrdinalIgnoreCase)
            ? "本机" : !string.IsNullOrWhiteSpace(name) ? name : "其他设备（名称未知）";

    internal static string Differences(CloudObjectConflictRecord local, CloudObjectSyncCacheEntry? remote)
    {
        if (remote == null) return "请先刷新同步，查看最新云端内容。";
        if (local.LocalDeleted != remote.Deleted)
            return local.LocalDeleted ? "本机选择删除，云端仍保留此项。" : "本机仍保留此项，云端已删除。";
        var names = new HashSet<string>();
        Compare(local.LocalPayload, remote.Payload, names);
        return names.Count == 0 ? "两份内容正在核对，请刷新同步。" : "不同的设置：" + string.Join("、", names.Take(6)) + (names.Count > 6 ? "等" : "") + "。";
    }

    private static void Compare(JsonElement left, JsonElement right, HashSet<string> names, string key = "")
    {
        if (left.ValueKind == JsonValueKind.Object && right.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in left.EnumerateObject().Select(p => p.Name).Union(right.EnumerateObject().Select(p => p.Name)))
            {
                left.TryGetProperty(name, out var a);
                right.TryGetProperty(name, out var b);
                Compare(a, b, names, name);
            }
            return;
        }
        if (left.ValueKind == JsonValueKind.Undefined || right.ValueKind == JsonValueKind.Undefined ||
            !JsonNode.DeepEquals(JsonNode.Parse(left.GetRawText()), JsonNode.Parse(right.GetRawText())))
            names.Add(key switch
            {
                "pageObjectIds" => "页面与顺序", "activationKey" => "呼出按键",
                "triggerRightButtonDrag" => "右键拖动", "mouseTriggerMode" => "鼠标呼出方式",
                "slots" => "菜单内容", "rightButtonLongPress" => "右键长按",
                "mouseGestureTriggerMode" => "鼠标手势", "quickPanelTrigger" => "快捷面板呼出方式",
                "name" => "名称", "enabled" => "启用状态", _ => "其他设置"
            });
    }
}
