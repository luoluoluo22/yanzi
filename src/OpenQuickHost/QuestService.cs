using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenQuickHost;

public sealed class QuestDefinition
{
    public string Id { get; init; } = string.Empty;
    public string ShortPrompt { get; init; } = string.Empty;
    public int RewardPoints { get; init; } = 30;
    public string RewardBadge { get; init; } = string.Empty;
}

public static class QuestService
{
    public const string OpenBackpackQuestId = "quest_open_backpack";
    public const string CalculatorQuestId = "quest_calculator";
    public const string WebJumpQuestId = "quest_web_jump";
    public const string DnsCleanQuestId = "quest_dns_clean";
    public const string AiCreationQuestId = "quest_ai_creation";

    public static event Action? BackpackOpened;

    public static readonly QuestDefinition OpenBackpackQuest = new()
    {
        Id = OpenBackpackQuestId,
        ShortPrompt = "长按鼠标右键 召唤背包",
        RewardPoints = 30,
        RewardBadge = "🎒 初入江湖"
    };

    public static readonly QuestDefinition CalculatorQuest = new()
    {
        Id = CalculatorQuestId,
        ShortPrompt = "将计算器放入背包",
        RewardPoints = 50,
        RewardBadge = "🧮 指尖神算"
    };

    public static readonly QuestDefinition WebJumpQuest = new()
    {
        Id = WebJumpQuestId,
        ShortPrompt = "配置网页直达扩展放入背包",
        RewardPoints = 40,
        RewardBadge = "🚀 超光速跳跃"
    };

    public static readonly QuestDefinition DnsCleanQuest = new()
    {
        Id = DnsCleanQuestId,
        ShortPrompt = "编写一键刷新 DNS 脚本放入背包",
        RewardPoints = 50,
        RewardBadge = "🧹 系统清道夫"
    };

    public static readonly QuestDefinition AiCreationQuest = new()
    {
        Id = AiCreationQuestId,
        ShortPrompt = "用 AI 一句话全自动造物",
        RewardPoints = 100,
        RewardBadge = "🧙 智械先驱"
    };

    public static readonly IReadOnlyList<QuestDefinition> AllQuests =
    [
        OpenBackpackQuest,
        CalculatorQuest,
        WebJumpQuest,
        DnsCleanQuest,
        AiCreationQuest
    ];

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
            CalculatorQuestId => CheckCalculatorInBackpack(settings, allExtensions),
            WebJumpQuestId => CheckWebJumpInBackpack(settings, allExtensions),
            DnsCleanQuestId => CheckDnsCleanInBackpack(settings, allExtensions),
            AiCreationQuestId => CheckAiCreation(settings, allExtensions),
            _ => false
        };
    }

    private static bool CheckCalculatorInBackpack(AppSettings settings, IReadOnlyList<CommandItem> allExtensions)
    {
        var calcExt = allExtensions?.FirstOrDefault(ext =>
            (ext.OpenTarget?.Contains("calc", StringComparison.OrdinalIgnoreCase) ?? false) ||
            (ext.InlineScriptSource?.Contains("calc", StringComparison.OrdinalIgnoreCase) ?? false) ||
            (ext.EntryPoint?.Contains("calc", StringComparison.OrdinalIgnoreCase) ?? false) ||
            (ext.Title?.Contains("计算器", StringComparison.OrdinalIgnoreCase) ?? false) ||
            (ext.Title?.Contains("calculator", StringComparison.OrdinalIgnoreCase) ?? false));

        return calcExt != null && IsInQuickPanel(settings, calcExt.ExtensionId);
    }

    private static bool CheckWebJumpInBackpack(AppSettings settings, IReadOnlyList<CommandItem> allExtensions)
    {
        var webExt = allExtensions?.FirstOrDefault(ext =>
            ext.QueryPrefixes?.Count > 0 ||
            !string.IsNullOrWhiteSpace(ext.QueryTargetTemplate));

        return webExt != null && IsInQuickPanel(settings, webExt.ExtensionId);
    }

    private static bool CheckDnsCleanInBackpack(AppSettings settings, IReadOnlyList<CommandItem> allExtensions)
    {
        var scriptExt = allExtensions?.FirstOrDefault(ext =>
            (ext.InlineScriptSource?.Contains("flushdns", StringComparison.OrdinalIgnoreCase) ?? false) ||
            (ext.Title?.Contains("DNS", StringComparison.OrdinalIgnoreCase) ?? false));

        return scriptExt != null && IsInQuickPanel(settings, scriptExt.ExtensionId);
    }

    private static bool CheckAiCreation(AppSettings settings, IReadOnlyList<CommandItem> allExtensions)
    {
        var customCount = allExtensions?.Count(x => x.Source == CommandSource.LocalExtension) ?? 0;
        return customCount >= 2;
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
