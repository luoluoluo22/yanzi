using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenQuickHost;

public sealed record QuestStep(int Index, string Title, bool IsCompleted);

public sealed record QuestCheckResult(
    bool IsSuccess,
    bool Step1Completed,
    bool Step2Completed,
    string Message,
    string? MatchedExtensionId = null
);

public sealed class QuestDefinition
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Category { get; init; } = "新手试炼";
    public int RewardPoints { get; init; } = 50;
    public string RewardBadge { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Step1Description { get; init; } = string.Empty;
    public string Step2Description { get; init; } = string.Empty;
    public string InitialJsonTemplate { get; init; } = string.Empty;
}

public static class QuestService
{
    public const string CalculatorQuestId = "quest_calculator";

    public static readonly QuestDefinition CalculatorQuest = new()
    {
        Id = CalculatorQuestId,
        Title = "【初试身手】将计算器放进鼠标面板",
        Category = "新手试炼 · 1阶",
        RewardPoints = 50,
        RewardBadge = "🏅 指尖神算",
        Description = "工作算账、临时算汇率，还要去开始菜单翻找？做一个专属扩展，鼠标一划就能在秒板中秒开计算器！",
        Step1Description = "创建计算器扩展 (执行目标设为 calc.exe 或名称含计算器)",
        Step2Description = "将计算器扩展放入鼠标面板 (QuickPanel) 的任意格位",
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

    public static readonly IReadOnlyList<QuestDefinition> AllQuests =
    [
        CalculatorQuest,
        new QuestDefinition
        {
            Id = "quest_web_jump",
            Title = "【超光速跳跃】配置一个带参数的网页直达扩展",
            Category = "快捷新星 · 2阶",
            RewardPoints = 30,
            RewardBadge = "🚀 超光速跳跃",
            Description = "每天在浏览器翻收藏夹？为常用的 GitHub / B站配置一个 {query} 前缀直达扩展！",
            Step1Description = "创建一个 QueryPrefixes 为 gh 或 bil 的网页扩展",
            Step2Description = "在主搜索框中输入前缀体验一次直达搜索",
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
        },
        new QuestDefinition
        {
            Id = "quest_dns_clean",
            Title = "【系统清道夫】编写一键刷新 DNS 缓存脚本",
            Category = "脚本工匠 · 3阶",
            RewardPoints = 40,
            RewardBadge = "🧹 系统清道夫",
            Description = "网络抽风或打不开某些网站？用 PowerShell 制作一键 ipconfig /flushdns 脚本扩展。",
            Step1Description = "创建包含 ipconfig /flushdns 命令的脚本扩展",
            Step2Description = "在鼠标面板或搜索栏中运行测试一次",
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
        },
        new QuestDefinition
        {
            Id = "quest_translate",
            Title = "【通天塔工匠】打造划词即时翻译扩展",
            Category = "数据织工 · 4阶",
            RewardPoints = 80,
            RewardBadge = "🌐 通天塔工匠",
            Description = "阅读外文不想切换窗口？结合剪贴板变量打造秒出翻译结果的效率神器。",
            Step1Description = "创建读取剪贴板并调用翻译接口的扩展",
            Step2Description = "绑定到鼠标面板或轮盘手势",
            InitialJsonTemplate = """
            {
              "name": "即时翻译",
              "category": "数据处理",
              "description": "读取剪贴板文本并快速翻译",
              "openTarget": "https://fanyi.baidu.com/#auto/zh/{query}",
              "icon": "translate"
            }
            """
        },
        new QuestDefinition
        {
            Id = "quest_ai_creation",
            Title = "【智械先驱】用 AI 一句话全自动造物",
            Category = "造物宗师 · 5阶",
            RewardPoints = 100,
            RewardBadge = "🧙 智械先驱",
            Description = "向燕子 AI 描述你的痛点，让 AI 全自动编写、调试并一键安装你的个性化专属工具。",
            Step1Description = "在 AI 对话框中描述并生成一个扩展",
            Step2Description = "点击一键安装并保存至本地扩展库",
            InitialJsonTemplate = string.Empty
        }
    ];

    public static QuestCheckResult CheckCalculatorQuest(AppSettings settings, IReadOnlyList<CommandItem> allExtensions)
    {
        if (allExtensions == null || allExtensions.Count == 0)
        {
            return new QuestCheckResult(false, false, false, "尚未检测到任何扩展，请先创建计算器扩展。");
        }

        // 1. 查找计算器相关扩展
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
                "未找到计算器扩展。请点击「帮我预填并创建扩展」，保存一个执行 calc.exe 的扩展。"
            );
        }

        var extId = calcExt.ExtensionId;

        // 2. 检查是否在鼠标面板 (QuickPanel) 中
        bool inQuickPanel = false;

        if (settings.QuickPanelGlobalGroups != null)
        {
            inQuickPanel = settings.QuickPanelGlobalGroups.Any(group =>
                group.SlotItems?.Any(s => string.Equals(s?.ExtensionId, extId, StringComparison.OrdinalIgnoreCase)) == true ||
                group.Slots?.Any(s => string.Equals(s, extId, StringComparison.OrdinalIgnoreCase)) == true);
        }

        if (!inQuickPanel && settings.QuickPanelContextGroups != null)
        {
            inQuickPanel = settings.QuickPanelContextGroups.Any(group =>
                group.SlotItems?.Any(s => string.Equals(s?.ExtensionId, extId, StringComparison.OrdinalIgnoreCase)) == true ||
                group.Slots?.Any(s => string.Equals(s, extId, StringComparison.OrdinalIgnoreCase)) == true);
        }

        if (!inQuickPanel && settings.QuickPanelSlots != null)
        {
            inQuickPanel = settings.QuickPanelSlots.Any(s => string.Equals(s, extId, StringComparison.OrdinalIgnoreCase));
        }

        if (!inQuickPanel)
        {
            return new QuestCheckResult(
                false,
                true,
                false,
                $"已找到扩展「{calcExt.Title}」，但尚未放入鼠标面板！请呼出鼠标面板将其添加至任意格子中。",
                extId
            );
        }

        return new QuestCheckResult(
            true,
            true,
            true,
            $"太棒了！已成功检测到计算器扩展「{calcExt.Title}」已配置在鼠标面板中！",
            extId
        );
    }

    public static (int Level, string Title, int CurrentExp, int MaxExp) GetPlayerLevel(int totalPoints)
    {
        if (totalPoints < 50)
        {
            return (1, "效率学徒", totalPoints, 50);
        }
        if (totalPoints < 150)
        {
            return (2, "快捷新星", totalPoints - 50, 100);
        }
        if (totalPoints < 350)
        {
            return (3, "脚本工匠", totalPoints - 150, 200);
        }
        if (totalPoints < 750)
        {
            return (4, "数据织工", totalPoints - 350, 400);
        }
        return (5, "造物之神", Math.Min(totalPoints - 750, 1000), 1000);
    }
}
