using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenQuickHost;

public sealed record QuestCheckResult(
    bool IsSuccess,
    bool Step1Completed,
    bool Step2Completed,
    string Message,
    string? ExtraData = null
);

public sealed class QuestDefinition
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Category { get; init; } = "新手试炼";
    public int RewardPoints { get; init; } = 50;
    public string RewardBadge { get; init; } = string.Empty;
    public string BadgeIcon { get; init; } = "🏅";
    public string Description { get; init; } = string.Empty;
    public string Step1Description { get; init; } = string.Empty;
    public string Step2Description { get; init; } = string.Empty;
    public bool HasStep2 => !string.IsNullOrWhiteSpace(Step2Description);
    public string InitialJsonTemplate { get; init; } = string.Empty;
    public string ActionButtonText { get; init; } = string.Empty;
}

public static class QuestService
{
    public const string OpenBackpackQuestId = "quest_open_backpack";
    public const string CalculatorQuestId = "quest_calculator";
    public const string WebJumpQuestId = "quest_web_jump";
    public const string DnsCleanQuestId = "quest_dns_clean";
    public const string AiCreationQuestId = "quest_ai_creation";

    public static readonly QuestDefinition OpenBackpackQuest = new()
    {
        Id = OpenBackpackQuestId,
        Title = "【探险家启程】召唤随身背包",
        Category = "新手试炼 · 1阶",
        RewardPoints = 30,
        RewardBadge = "🎒 初入江湖",
        BadgeIcon = "🎒",
        Description = "工欲善其事，必先利其器！在屏幕任意位置长按鼠标右键，唤醒属于你的随身背包（快捷轮盘与网格面板）。",
        Step1Description = "长按鼠标右键（或点击下方按钮）成功呼出一次随身背包",
        Step2Description = string.Empty,
        ActionButtonText = "🎒 立即呼出随身背包",
        InitialJsonTemplate = string.Empty
    };

    public static readonly QuestDefinition CalculatorQuest = new()
    {
        Id = CalculatorQuestId,
        Title = "【神算装备】将计算器放进随身背包",
        Category = "进阶试炼 · 2阶",
        RewardPoints = 50,
        RewardBadge = "🧮 指尖神算",
        BadgeIcon = "🧮",
        Description = "工作算账、临时算汇率，还要去开始菜单翻找？做一个专属扩展放入随身背包，鼠标一划就能在背包中秒开计算器！",
        Step1Description = "创建计算器扩展 (执行目标设为 calc.exe 或名称含计算器)",
        Step2Description = "将计算器扩展放入随身背包 (QuickPanel) 的任意格位",
        ActionButtonText = "⚡ 预填并创建计算器扩展",
        InitialJsonTemplate = """
        {
          "name": "打开计算器",
          "category": "系统工具",
          "description": "一键快速打开 Windows 计算器",
          "keywords": [
            "计算器",
            "calc",
            "jsq"
          ],
          "openTarget": "calc.exe",
          "icon": "calculator"
        }
        """
    };

    public static readonly QuestDefinition WebJumpQuest = new()
    {
        Id = WebJumpQuestId,
        Title = "【超光速跳跃】配置带参数的网页直达",
        Category = "快捷新星 · 3阶",
        RewardPoints = 40,
        RewardBadge = "🚀 超光速跳跃",
        BadgeIcon = "🚀",
        Description = "每天在浏览器翻收藏夹？为常用的 GitHub / B站配置一个 {query} 前缀直达扩展！",
        Step1Description = "创建一个 QueryPrefixes 为 gh 或 bil 的网页扩展",
        Step2Description = "将网页直达扩展放入随身背包或在主搜索框中体验一次",
        ActionButtonText = "⚡ 预填并创建 GitHub 直达",
        InitialJsonTemplate = """
        {
          "name": "GitHub 快捷搜索",
          "category": "网页搜索",
          "description": "输入 gh 关键词 快速搜索 GitHub 仓库",
          "queryPrefixes": ["gh"],
          "queryTargetTemplate": "https://github.com/search?q={query}",
          "icon": "globe"
        }
        """
    };

    public static readonly QuestDefinition DnsCleanQuest = new()
    {
        Id = DnsCleanQuestId,
        Title = "【系统清道夫】编写一键刷新 DNS 缓存脚本",
        Category = "脚本工匠 · 4阶",
        RewardPoints = 50,
        RewardBadge = "🧹 系统清道夫",
        BadgeIcon = "🧹",
        Description = "网络抽风或打不开某些网站？用 PowerShell 制作一键 ipconfig /flushdns 脚本扩展放入背包。",
        Step1Description = "创建包含 ipconfig /flushdns 命令的脚本扩展",
        Step2Description = "将脚本扩展放入随身背包任意格位",
        ActionButtonText = "⚡ 预填并创建 DNS 脚本",
        InitialJsonTemplate = """
        {
          "name": "刷新 DNS 缓存",
          "category": "系统维护",
          "description": "一键执行 ipconfig /flushdns 清理网络 DNS 缓存",
          "script": {
            "source": "ipconfig /flushdns\nWrite-Host 'DNS 缓存清理完毕！'"
          },
          "icon": "broom"
        }
        """
    };

    public static readonly QuestDefinition AiCreationQuest = new()
    {
        Id = AiCreationQuestId,
        Title = "【智械造物】用 AI 一句话全自动造物",
        Category = "造物宗师 · 5阶",
        RewardPoints = 100,
        RewardBadge = "🧙 智械先驱",
        BadgeIcon = "🧙",
        Description = "向燕子 AI 描述你的痛点，让 AI 全自动编写、调试并一键安装你的个性化专属工具。",
        Step1Description = "在 AI 扩展制作中生成并保存一个自定义工具",
        Step2Description = "将其放入随身背包中体验",
        ActionButtonText = "🤖 呼出 AI 扩展助手",
        InitialJsonTemplate = string.Empty
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
    }

    public static QuestCheckResult CheckQuest(QuestDefinition quest, AppSettings settings, IReadOnlyList<CommandItem> allExtensions)
    {
        return quest.Id switch
        {
            OpenBackpackQuestId => CheckOpenBackpackQuest(settings),
            CalculatorQuestId => CheckCalculatorQuest(settings, allExtensions),
            WebJumpQuestId => CheckWebJumpQuest(settings, allExtensions),
            DnsCleanQuestId => CheckDnsCleanQuest(settings, allExtensions),
            AiCreationQuestId => CheckAiCreationQuest(settings, allExtensions),
            _ => new QuestCheckResult(false, false, false, "未知任务。")
        };
    }

    public static QuestCheckResult CheckOpenBackpackQuest(AppSettings settings)
    {
        var opened = settings.HasOpenedBackpack;
        if (opened)
        {
            return new QuestCheckResult(
                true,
                true,
                true,
                "太棒了！已成功呼出随身背包！"
            );
        }

        return new QuestCheckResult(
            false,
            false,
            false,
            "尚未检测到随身背包呼出。请在桌面长按鼠标右键，或点击下方「🎒 立即呼出随身背包」体验一次！"
        );
    }

    public static QuestCheckResult CheckCalculatorQuest(AppSettings settings, IReadOnlyList<CommandItem> allExtensions)
    {
        if (allExtensions == null || allExtensions.Count == 0)
        {
            return new QuestCheckResult(false, false, false, "尚未检测到任何扩展，请先创建计算器扩展。");
        }

        var calcExt = allExtensions.FirstOrDefault(ext =>
            (ext.OpenTarget?.Contains("calc", StringComparison.OrdinalIgnoreCase) ?? false) ||
            (ext.InlineScriptSource?.Contains("calc", StringComparison.OrdinalIgnoreCase) ?? false) ||
            (ext.EntryPoint?.Contains("calc", StringComparison.OrdinalIgnoreCase) ?? false) ||
            (ext.Title?.Contains("计算器", StringComparison.OrdinalIgnoreCase) ?? false) ||
            (ext.Title?.Contains("calculator", StringComparison.OrdinalIgnoreCase) ?? false) ||
            (ext.Keywords?.Any(k => k.Contains("calc", StringComparison.OrdinalIgnoreCase) || k.Contains("计算器", StringComparison.OrdinalIgnoreCase)) ?? false));

        if (calcExt == null)
        {
            return new QuestCheckResult(
                false,
                false,
                false,
                "未找到计算器扩展。请点击「预填并创建计算器扩展」，保存一个执行 calc.exe 的扩展。"
            );
        }

        var extId = calcExt.ExtensionId;
        bool inQuickPanel = IsInQuickPanel(settings, extId);

        if (!inQuickPanel)
        {
            return new QuestCheckResult(
                false,
                true,
                false,
                $"已找到扩展「{calcExt.Title}」，但尚未放入随身背包！请长按右键呼出背包将其添加至任意格子中。",
                extId
            );
        }

        return new QuestCheckResult(
            true,
            true,
            true,
            $"太棒了！已成功检测到计算器扩展「{calcExt.Title}」已配置在随身背包中！",
            extId
        );
    }

    public static QuestCheckResult CheckWebJumpQuest(AppSettings settings, IReadOnlyList<CommandItem> allExtensions)
    {
        var webExt = allExtensions?.FirstOrDefault(ext =>
            ext.QueryPrefixes?.Count > 0 ||
            !string.IsNullOrWhiteSpace(ext.QueryTargetTemplate));

        if (webExt == null)
        {
            return new QuestCheckResult(
                false,
                false,
                false,
                "未找到带参数前缀的网页直达扩展。点击下方按钮即可一键预填创建 GitHub 搜索！"
            );
        }

        var inQuickPanel = IsInQuickPanel(settings, webExt.ExtensionId);
        return new QuestCheckResult(
            true,
            true,
            inQuickPanel,
            $"太棒了！已找到网页直达扩展「{webExt.Title}」！"
        );
    }

    public static QuestCheckResult CheckDnsCleanQuest(AppSettings settings, IReadOnlyList<CommandItem> allExtensions)
    {
        var scriptExt = allExtensions?.FirstOrDefault(ext =>
            (ext.InlineScriptSource?.Contains("flushdns", StringComparison.OrdinalIgnoreCase) ?? false) ||
            (ext.Title?.Contains("DNS", StringComparison.OrdinalIgnoreCase) ?? false));

        if (scriptExt == null)
        {
            return new QuestCheckResult(
                false,
                false,
                false,
                "未找到 DNS 清理脚本扩展。点击下方按钮一键创建！"
            );
        }

        var inQuickPanel = IsInQuickPanel(settings, scriptExt.ExtensionId);
        return new QuestCheckResult(
            true,
            true,
            inQuickPanel,
            $"太棒了！已检测到 DNS 清理脚本「{scriptExt.Title}」！"
        );
    }

    public static QuestCheckResult CheckAiCreationQuest(AppSettings settings, IReadOnlyList<CommandItem> allExtensions)
    {
        var customCount = allExtensions?.Count(x => x.Source == CommandSource.LocalExtension) ?? 0;
        if (customCount >= 2)
        {
            return new QuestCheckResult(
                true,
                true,
                true,
                "太棒了！你已成功打造并拥有丰富的个性化工具库！"
            );
        }

        return new QuestCheckResult(
            false,
            customCount > 0,
            false,
            "尝试使用 AI 或编辑器再打造一个专属的效率工具吧！"
        );
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

    public static (int Level, string Title, int CurrentExp, int MaxExp) GetPlayerLevel(int totalPoints)
    {
        if (totalPoints < 30)
        {
            return (1, "效率学徒", totalPoints, 30);
        }
        if (totalPoints < 80)
        {
            return (2, "快捷新星", totalPoints - 30, 50);
        }
        if (totalPoints < 170)
        {
            return (3, "脚本工匠", totalPoints - 80, 90);
        }
        if (totalPoints < 300)
        {
            return (4, "数据织工", totalPoints - 170, 130);
        }
        return (5, "造物宗师", Math.Min(totalPoints - 300, 500), 500);
    }
}
