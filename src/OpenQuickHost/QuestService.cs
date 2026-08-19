using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenQuickHost;

public sealed class QuestDefinition
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string ShortPrompt { get; init; } = string.Empty;
    public int RewardPoints { get; init; } = 30;
    public string RewardBadge { get; init; } = string.Empty;
}

public static class QuestService
{
    public const string OpenBackpackQuestId = "quest_open_backpack";
    public const string DragFileQuestId = "quest_drag_file_to_backpack";
    public const string OpenSampleFileQuestId = "quest_open_sample_file";

    public const string MasterRewardBadge = "🎒 随身行者";
    public const int TotalMaxPoints = 130;

    public static bool IsQuestWindowActive { get; set; } = false;
    public static string? ActiveQuestId { get; set; } = null;
    public static bool IsWaitingUnpin { get; set; } = false;

    public static event Action? BackpackOpened;
    public static event Action? FileDroppedToBackpack;
    public static event Action<bool>? BackpackPinChanged;
    public static event Action<string>? BackpackItemClicked;
    public static event Action? QuestStateChanged;

    public static void NotifyQuestStateChanged()
    {
        try
        {
            QuestStateChanged?.Invoke();
        }
        catch
        {
            // Ignore failure
        }
    }

    public static void OnBackpackPinned(bool isPinned)
    {
        try
        {
            BackpackPinChanged?.Invoke(isPinned);
        }
        catch
        {
            // Ignore failure
        }
    }

    public static void OnBackpackItemClicked(string itemName)
    {
        try
        {
            BackpackItemClicked?.Invoke(itemName);
        }
        catch
        {
            // Ignore failure
        }
    }

    public static readonly QuestDefinition OpenBackpackQuest = new()
    {
        Id = OpenBackpackQuestId,
        Title = "召唤背包",
        ShortPrompt = "长按鼠标右键 召唤背包",
        RewardPoints = 30,
        RewardBadge = string.Empty
    };

    public static readonly QuestDefinition DragFileQuest = new()
    {
        Id = DragFileQuestId,
        Title = "收纳文件",
        ShortPrompt = "将桌面文件拖拽进背包",
        RewardPoints = 50,
        RewardBadge = string.Empty
    };

    public static readonly QuestDefinition OpenSampleFileQuest = new()
    {
        Id = OpenSampleFileQuestId,
        Title = "极速打开",
        ShortPrompt = "长按右键呼出背包，点击打开【示例文件】",
        RewardPoints = 50,
        RewardBadge = string.Empty
    };

    public static readonly IReadOnlyList<QuestDefinition> AllQuests =
    [
        OpenBackpackQuest,
        DragFileQuest,
        OpenSampleFileQuest
    ];

    public static void OnFileDroppedToBackpack()
    {
        try
        {
            FileDroppedToBackpack?.Invoke();
        }
        catch
        {
            // Ignore failure
        }
    }

    public static void OnBackpackOpened()
    {
        try
        {
            var settings = AppSettingsStore.Load();
            if (!settings.HasOpenedBackpack)
            {
                settings.HasOpenedBackpack = true;
                AppSettingsStore.Save(settings);
            }
        }
        catch
        {
            // Ignore failure to record
        }

        try
        {
            BackpackOpened?.Invoke();
        }
        catch
        {
            // Ignore failure to dispatch
        }
    }

    public static bool IsQuestCompleted(QuestDefinition quest, AppSettings settings, IReadOnlyList<CommandItem> allExtensions)
    {
        if (settings.CompletedQuestIds?.Contains(quest.Id) == true)
        {
            return true;
        }

        return quest.Id switch
        {
            OpenBackpackQuestId => settings.HasOpenedBackpack,
            DragFileQuestId => CheckFileInBackpack(settings, allExtensions),
            _ => false
        };
    }

    private static bool CheckFileInBackpack(AppSettings settings, IReadOnlyList<CommandItem> allExtensions)
    {
        if (allExtensions == null || allExtensions.Count == 0) return false;

        var fileExt = allExtensions.FirstOrDefault(ext =>
            (!string.IsNullOrWhiteSpace(ext.OpenTarget) && (File.Exists(ext.OpenTarget) || Directory.Exists(ext.OpenTarget))) ||
            (ext.Title?.Contains("【示例文件】", StringComparison.OrdinalIgnoreCase) ?? false) ||
            (ext.Title?.Contains("示例文件", StringComparison.OrdinalIgnoreCase) ?? false) ||
            ext.Source == CommandSource.File);

        return fileExt != null && IsInQuickPanel(settings, fileExt.ExtensionId);
    }

    private static bool IsInQuickPanel(AppSettings settings, string? extId)
    {
        if (string.IsNullOrWhiteSpace(extId)) return false;

        if (settings.QuickPanelGlobalGroups != null)
        {
            if (settings.QuickPanelGlobalGroups.Any(group =>
                group.SlotItems?.Any(s => string.Equals(s?.ExtensionId, extId, StringComparison.OrdinalIgnoreCase)) == true ||
                group.Slots?.Any(s => string.Equals(s, extId, StringComparison.OrdinalIgnoreCase)) == true))
            {
                return true;
            }
        }

        if (settings.QuickPanelContextGroups != null)
        {
            if (settings.QuickPanelContextGroups.Any(group =>
                group.SlotItems?.Any(s => string.Equals(s?.ExtensionId, extId, StringComparison.OrdinalIgnoreCase)) == true ||
                group.Slots?.Any(s => string.Equals(s, extId, StringComparison.OrdinalIgnoreCase)) == true))
            {
                return true;
            }
        }

        if (settings.QuickPanelSlots != null)
        {
            if (settings.QuickPanelSlots.Any(s => string.Equals(s, extId, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }
}
