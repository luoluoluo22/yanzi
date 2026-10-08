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
            "按 shadcn/ui 官方目录逐项登记 WPF 对应能力。每项都有一个可复用基础入口；高级行为、动画与视觉细节仍需分组件对照验收。");
        intro.Children.Add(YanziPrimitives.Alert("状态定义",
            "可复用：具有可调用的 WPF 样式/类型/API，并不代表已达到网页版的完整行为或像素级一致。演示：仅在评估中心可交互；排期：尚未实现。"
            + " 双击任意可用条目可打开对应体验页。"));
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

        var listSection = Card("组件覆盖清单", "列表包含官方组件名称、当前适配阶段、公共接口与所在演示页。双击可跳转。");
        var list = YanziUi.WithStyle(new DataGrid
        {
            Height = 400,
            AutoGenerateColumns = false,
            IsReadOnly = true,
            SelectionMode = DataGridSelectionMode.Single
        }, YanziUi.Styles.DataGrid);
        list.Columns.Add(new DataGridTextColumn { Header = "组件", Binding = new Binding(nameof(YanziComponentDescriptor.Name)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        list.Columns.Add(new DataGridTextColumn { Header = "状态", Binding = new Binding(nameof(YanziComponentDescriptor.Status)), Width = 92 });
        list.Columns.Add(new DataGridTextColumn { Header = "公共接口 / 适配方式", Binding = new Binding(nameof(YanziComponentDescriptor.Api)), Width = new DataGridLength(1.45, DataGridLengthUnitType.Star) });
        list.ItemsSource = YanziComponentRegistry.Components.ToList();
        list.MouseDoubleClick += (_, _) =>
        {
            if (list.SelectedItem is YanziComponentDescriptor item)
            {
                if (item.Status == YanziComponentStatus.Planned)
                {
                    Status("暂未实现：" + item.Name);
                    YanziToast.Show(this, "该组件仍待开发：" + item.Name);
                }
                else ShowPage(item.GalleryPage);
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
            ).OrderBy(x => x.Name).ToList();
            list.ItemsSource = filtered;
            countText.Text = $"当前显示 {filtered.Count} 项 · 可复用组件优先在真实小程序中使用并做回归验证";
        }
        filter.TextChanged += (_, _) => ApplyFilter();
        states.SelectionChanged += (_, _) => ApplyFilter();
        ApplyFilter();
    }
}
