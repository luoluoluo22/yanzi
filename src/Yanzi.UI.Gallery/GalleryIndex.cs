using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Yanzi.UI.Wpf;

namespace Yanzi.UI.Gallery;

internal sealed partial class GalleryWindow
{
    private void ComponentsIndex()
    {
        var intro = Card("全部组件 / Component coverage",
            "严格按照 shadcn/ui 官网 64 个组件的原始排序。每项可以打开独立评估页、官方参考文档与核对清单；未实际验证的项目不标记通过。");
        intro.Children.Add(YanziPrimitives.Alert("状态定义",
            "可复用：具有可调用的 WPF 样式/类型/API，并不代表已达到网页版的完整行为或像素级一致。演示：仅在评估中心可交互；排期：尚未实现。"
            + " 双击任意可用条目可打开对应体验页。"));
        intro.Children.Add(Text("官网目录：64 项  ·  已有独立 WPF 预览："
            + IsolatedPreviewNames.Count + " 项  ·  待补独立预览："
            + (64 - IsolatedPreviewNames.Count) + " 项  ·  完整视觉验收：尚未开始",
            13, true, "Yanzi.Color.Foreground", new Thickness(0, 10, 0, 0)));
        var tools = Card("筛选组件", "支持通过名称、类别与公共 API 查找。");
        var filter = YanziUi.WithStyle(new TextBox { ToolTip = "按名称或 API 搜索组件", Margin = new Thickness(0, 0, 0, 12) },
            YanziUi.Styles.Input);
        tools.Children.Add(filter);
        var states = YanziUi.WithStyle(new ComboBox { Width = 220, HorizontalAlignment = HorizontalAlignment.Left },
            YanziUi.Styles.Select);
        states.Items.Add("全部状态");
        states.Items.Add("可复用");
        states.Items.Add("展示演示");
        states.Items.Add("待开发");
        states.SelectedIndex = 0;
        tools.Children.Add(states);

        var listSection = Card("组件覆盖清单", "列表覆盖官网全部 64 项，按官方排序排列。双击可打开对应的独立组件评估页，记录差异与结果。");
        var list = YanziUi.WithStyle(new DataGrid
        {
            Height = 400,
            AutoGenerateColumns = false,
            IsReadOnly = true,
            SelectionMode = DataGridSelectionMode.Single
        }, YanziUi.Styles.DataGrid);
        list.Columns.Add(new DataGridTextColumn { Header = "组件", Binding = new Binding(nameof(YanziComponentDescriptor.Name)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        list.Columns.Add(new DataGridTextColumn { Header = "API 状态", Binding = new Binding(nameof(YanziComponentDescriptor.Status)), Width = 92 });
        list.Columns.Add(new DataGridTextColumn { Header = "公共接口 / 适配方式", Binding = new Binding(nameof(YanziComponentDescriptor.Api)), Width = new DataGridLength(1.45, DataGridLengthUnitType.Star) });
        list.ItemsSource = YanziComponentRegistry.Components.ToList();
        list.MouseDoubleClick += (_, _) =>
        {
            if (list.SelectedItem is YanziComponentDescriptor item)
            {
                ShowComponent(item);
            }
        };
        listSection.Children.Add(list);
        var countText = Text("", 12, false, "Yanzi.Color.MutedForeground", new Thickness(0, 10, 0, 0));
        listSection.Children.Add(countText);

        void ApplyFilter()
        {
            var filtered = YanziComponentRegistry.Components.Where(x =>
                (string.IsNullOrWhiteSpace(filter.Text)
                    || x.Name.Contains(filter.Text.Trim(), StringComparison.OrdinalIgnoreCase)
                    || x.Api.Contains(filter.Text.Trim(), StringComparison.OrdinalIgnoreCase)
                    || x.Group.Contains(filter.Text.Trim(), StringComparison.OrdinalIgnoreCase))
                && (states.SelectedIndex == 0
                    || (states.SelectedIndex == 1 && x.Status == YanziComponentStatus.Ready)
                    || (states.SelectedIndex == 2 && x.Status == YanziComponentStatus.Preview)
                    || (states.SelectedIndex == 3 && x.Status == YanziComponentStatus.Planned))
            ).ToList();
            list.ItemsSource = filtered;
            countText.Text = $"官网组件共 64 项 · 当前显示 {filtered.Count} 项 · 双击进入该组件的独立对照与验收记录";
        }
        filter.TextChanged += (_, _) => ApplyFilter();
        states.SelectionChanged += (_, _) => ApplyFilter();
        ApplyFilter();
    }
}
