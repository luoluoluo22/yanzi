using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenQuickHost.Sync;

public sealed record SyncFieldDifference(
    string Path, string LocalValue, string RemoteValue,
    string JsonPointer = "", string ObjectId = "", long Revision = 0)
{
    public bool CanChooseField => JsonPointer.Length > 0 && ObjectId.Length > 0;
}

internal static class SyncUserText
{
    private const int MaxDifferences = 18;
    private const int MaxPreviewLength = 180;

    internal static string Device(string? id, string? name, string currentId) =>
        !string.IsNullOrWhiteSpace(id) && string.Equals(id, currentId, StringComparison.OrdinalIgnoreCase)
            ? "本机" : !string.IsNullOrWhiteSpace(name) ? name : "其他设备（名称未知）";

    internal static string Differences(CloudObjectConflictRecord local, CloudObjectSyncCacheEntry? remote)
    {
        if (remote == null) return "请先刷新同步，查看最新云端内容。";
        var rows = DetailDifferences(local.LocalPayload, remote.Payload, local.LocalDeleted, remote.Deleted);
        return rows.Count == 0 ? "两份内容正在核对，请刷新同步。"
            : "存在差异：" + string.Join("、", rows.Take(5).Select(static row => row.Path)) +
                (rows.Count > 5 ? "等" : "") + "。";
    }

    internal static IReadOnlyList<SyncFieldDifference> DetailDifferences(
        JsonElement local, JsonElement remote, bool localDeleted = false, bool remoteDeleted = false)
    {
        if (localDeleted || remoteDeleted)
        {
            if (localDeleted == remoteDeleted) return [];
            return [new SyncFieldDifference("数据状态", localDeleted ? "已删除" : "保留", remoteDeleted ? "已删除" : "保留")];
        }

        var rows = new List<SyncFieldDifference>();
        Compare(local, remote, rows, "", "", 0);
        return rows;
    }

    internal static IReadOnlyList<SyncFieldDifference> ContentDifferences(
        string? local, string? remote, bool localDeleted = false, bool remoteDeleted = false)
    {
        if (localDeleted || remoteDeleted)
        {
            if (localDeleted == remoteDeleted) return [];
            return [new SyncFieldDifference("文件状态", localDeleted ? "已删除" : "保留", remoteDeleted ? "已删除" : "保留")];
        }
        if (string.Equals(local, remote, StringComparison.Ordinal)) return [];
        try
        {
            using var localJson = JsonDocument.Parse(local ?? "");
            using var remoteJson = JsonDocument.Parse(remote ?? "");
            return DetailDifferences(localJson.RootElement, remoteJson.RootElement);
        }
        catch (JsonException)
        {
            return [new SyncFieldDifference("文本内容", PreviewText(local), PreviewText(remote))];
        }
    }

    private static void Compare(JsonElement local, JsonElement remote,
        List<SyncFieldDifference> rows, string path, string pointer, int depth)
    {
        if (rows.Count >= MaxDifferences) return;
        if (Equivalent(local, remote)) return;
        if (depth < 8 && local.ValueKind == JsonValueKind.Object && remote.ValueKind == JsonValueKind.Object)
        {
            var keys = local.EnumerateObject().Select(static p => p.Name)
                .Union(remote.EnumerateObject().Select(static p => p.Name), StringComparer.Ordinal)
                .OrderBy(static key => key, StringComparer.Ordinal);
            foreach (var key in keys)
            {
                local.TryGetProperty(key, out var a);
                remote.TryGetProperty(key, out var b);
                Compare(a, b, rows, string.IsNullOrEmpty(path) ? key : path + "." + key,
                    pointer + "/" + key.Replace("~", "~0").Replace("/", "~1"), depth + 1);
                if (rows.Count >= MaxDifferences) break;
            }
            return;
        }
        var label = string.IsNullOrEmpty(path) ? "内容" : FriendlyPath(path);
        var sensitive = IsSensitivePath(path);
        rows.Add(new SyncFieldDifference(label,
            sensitive ? "••••（已隐藏敏感字段）" : Preview(local),
            sensitive ? "••••（已隐藏敏感字段）" : Preview(remote), pointer));
    }

    private static bool Equivalent(JsonElement left, JsonElement right)
    {
        if (left.ValueKind == JsonValueKind.Undefined || right.ValueKind == JsonValueKind.Undefined)
            return left.ValueKind == right.ValueKind;
        try { return JsonNode.DeepEquals(JsonNode.Parse(left.GetRawText()), JsonNode.Parse(right.GetRawText())); }
        catch (JsonException) { return string.Equals(left.GetRawText(), right.GetRawText(), StringComparison.Ordinal); }
    }

    private static string FriendlyPath(string path)
    {
        var segments = path.Split('.');
        var name = segments[^1] switch
        {
            "pageObjectIds" => "页面与顺序", "activationKey" => "呼出按键",
            "triggerRightButtonDrag" => "右键拖动", "mouseTriggerMode" => "鼠标呼出方式",
            "slots" => "菜单内容", "rightButtonLongPress" => "右键长按",
            "mouseGestureTriggerMode" => "鼠标手势", "quickPanelTrigger" => "快捷面板呼出方式",
            "name" => "名称", "enabled" => "启用状态",
            _ => segments[^1]
        };
        return segments.Length > 1 ? path[..(path.Length - segments[^1].Length)] + name : name;
    }

    private static bool IsSensitivePath(string path)
    {
        var last = path.Split('.', '[', ']')[^1];
        return new[] { "password", "secret", "token", "apikey", "apiauth", "authorization",
            "credential", "privatekey", "clientsecret", "accesskey" }
            .Any(word => last.Replace("_", "", StringComparison.Ordinal)
                .Equals(word, StringComparison.OrdinalIgnoreCase));
    }

    private static string Preview(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Undefined => "（不存在）",
        JsonValueKind.Null => "（空值）",
        JsonValueKind.True => "开启",
        JsonValueKind.False => "关闭",
        JsonValueKind.String => PreviewText(value.GetString()),
        JsonValueKind.Array => PreviewText(value.GetRawText()),
        JsonValueKind.Object => PreviewText(value.GetRawText()),
        _ => PreviewText(value.GetRawText())
    };

    private static string PreviewText(string? text)
    {
        if (text == null) return "（不存在）";
        if (text.Length == 0) return "（空）";
        var normalized = text.Replace("\r", "↵").Replace("\n", "↵");
        return normalized.Length <= MaxPreviewLength ? normalized
            : normalized[..MaxPreviewLength] + "…（已截断）";
    }
}
