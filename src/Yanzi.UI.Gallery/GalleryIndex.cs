using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Yanzi.UI.Wpf;

namespace Yanzi.UI.Gallery;

internal sealed partial class GalleryWindow
{
    private void ComponentsIndex()
    {
        var intro = Card("全部组件 / Component coverage",
            "严格按照 shadcn/ui 官网 64 个组件的原始排序。每项可打开独立评估页、官网及核对清单；未验证的不标记通过。");
        intro.Children.Add(YanziPrimitives.Alert("状态定义",
            "可复用：具备公共 WPF API，不代表官网视觉或完整行为一致。展示演示：核心场景可操作，仍有高级能力待补。"));
        intro.Children.Add(Text("官网目录 64 项 · 独立 WPF 预览 " + IsolatedPreviewNames.Count
            + " 项 · 完整视觉验收：待逐项核对", 13, true, "Yanzi.Color.Foreground",
            new Thickness(0, 10, 0, 0)));

        var tools = Card("筛选组件", "按名称、类别、接口或状态检索。");
        var filter = YanziUi.WithStyle(new TextBox
        {
            ToolTip = "按名称或 API 搜索组件",
            Margin = new Thickness(0, 0, 0, 12)
        }, YanziUi.Styles.Input);
        tools.Children.Add(filter);
        var states = YanziUi.WithStyle(new ComboBox
        {
            Width = 220, HorizontalAlignment = HorizontalAlignment.Left
        }, YanziUi.Styles.Select);
        foreach (var name in new[] { "全部状态", "可复用", "展示演示", "待开发" })
            states.Items.Add(name);
        states.SelectedIndex = 0;
        tools.Children.Add(states);

        var section = Card("组件覆盖清单", "按官网原始顺序。双击表格行打开对应的独立评估页面。");
        var table = new YanziTable();
        var scroll = YanziLayoutPrimitives.ScrollArea(table, 400);
        section.Children.Add(scroll);
        var count = Text("", 12, false, "Yanzi.Color.MutedForeground", new Thickness(0, 10, 0, 0));
        section.Children.Add(count);
        YanziComponentDescriptor[] displayed = [];

        table.RowDoubleClicked += (_, index) =>
        {
            if (index >= 0 && index < displayed.Length)
                ShowComponent(YanziComponentRegistry.Components.First(x => x.Name == table.Rows[index][0]));
        };

        void ApplyFilter()
        {
            displayed = YanziComponentRegistry.Components
                .Where(x => (string.IsNullOrWhiteSpace(filter.Text)
                    || x.Name.Contains(filter.Text.Trim(), StringComparison.OrdinalIgnoreCase)
                    || x.Api.Contains(filter.Text.Trim(), StringComparison.OrdinalIgnoreCase)
                    || x.Group.Contains(filter.Text.Trim(), StringComparison.OrdinalIgnoreCase))
                    && (states.SelectedIndex == 0
                        || (states.SelectedIndex == 1 && x.Status == YanziComponentStatus.Ready)
                        || (states.SelectedIndex == 2 && x.Status == YanziComponentStatus.Preview)
                        || (states.SelectedIndex == 3 && x.Status == YanziComponentStatus.Planned)))
                .ToArray();
            table.SetData(new[] { "组件", "API 状态", "公共接口 / 适配方式" },
                displayed.Select(x => (IReadOnlyList<string>)new[]
                {
                    x.Name,
                    x.Status switch
                    {
                        YanziComponentStatus.Ready => "可复用",
                        YanziComponentStatus.Preview => "演示",
                        _ => "待开发"
                    },
                    x.Api
                }));
            count.Text = $"官网共 64 项 · 当前显示 {displayed.Length} 项 · 双击进入独立核对";
        }

        filter.TextChanged += (_, _) => ApplyFilter();
        states.SelectionChanged += (_, _) => ApplyFilter();
        ApplyFilter();
    }
}
