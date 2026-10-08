using System.Collections.Generic;
namespace Yanzi.UI.Wpf;

public enum YanziComponentStatus { Ready, Preview, Planned }
public sealed record YanziComponentDescriptor(string Name, YanziComponentStatus Status, string Api, string Group, int GalleryPage)
{
    /// <summary>Exact path convention verified against the official /docs/components page (64 base entries).</summary>
    public string OfficialUrl => "https://ui.shadcn.com/docs/components/base/"
        + Name.ToLowerInvariant().Replace(' ', '-');
}

/// <summary>Truthful capability registry. Ready = reusable public API; Preview = gallery only; Planned = not implemented.</summary>
public static class YanziComponentRegistry
{
    public static IReadOnlyList<YanziComponentDescriptor> Components { get; } = new YanziComponentDescriptor[]
    {
        new("Accordion", YanziComponentStatus.Ready, "YanziAccordion", "导航与布局", 10),
        new("Alert", YanziComponentStatus.Ready, "YanziPrimitives.Alert", "基础组件", 8),
        new("Alert Dialog", YanziComponentStatus.Ready, "YanziDialog.Confirm", "弹层与反馈", 12),
        new("Aspect Ratio", YanziComponentStatus.Ready, "YanziLayoutPrimitives.AspectRatio", "基础组件", 8),
        new("Attachment", YanziComponentStatus.Ready, "YanziContentPrimitives.Attachment", "数据展示", 11),
        new("Avatar", YanziComponentStatus.Ready, "YanziPrimitives.Avatar", "基础组件", 8),
        new("Badge", YanziComponentStatus.Ready, "YanziBadge", "徽标 Badge", 7),
        new("Breadcrumb", YanziComponentStatus.Ready, "YanziLayoutPrimitives.Breadcrumb", "导航与布局", 10),
        new("Bubble", YanziComponentStatus.Ready, "YanziContentPrimitives.MessageBubble", "数据展示", 11),
        new("Button", YanziComponentStatus.Ready, "Yanzi.Button.*", "按钮", 1),
        new("Button Group", YanziComponentStatus.Ready, "YanziButtonGroup", "表单控件", 9),
        new("Calendar", YanziComponentStatus.Ready, "Yanzi.Calendar", "表单控件", 9),
        new("Card", YanziComponentStatus.Ready, "Yanzi.Card", "总览", 0),
        new("Carousel", YanziComponentStatus.Ready, "YanziCarousel", "数据展示", 11),
        new("Chart", YanziComponentStatus.Ready, "YanziBarChart", "数据展示", 11),
        new("Checkbox", YanziComponentStatus.Ready, "Yanzi.CheckBox.Preview", "选择", 3),
        new("Collapsible", YanziComponentStatus.Ready, "YanziLayoutPrimitives.Collapsible", "导航与布局", 10),
        new("Combobox", YanziComponentStatus.Ready, "Yanzi.Select (IsEditable=true)", "表单控件", 9),
        new("Command", YanziComponentStatus.Ready, "YanziCommandPalette", "弹层与反馈", 12),
        new("Context Menu", YanziComponentStatus.Ready, "Yanzi.Menu / Yanzi.MenuItem", "弹层与反馈", 12),
        new("Data Table", YanziComponentStatus.Ready, "Yanzi.DataGrid", "数据展示", 11),
        new("Date Picker", YanziComponentStatus.Ready, "Yanzi.DatePicker", "表单控件", 9),
        new("Dialog", YanziComponentStatus.Ready, "YanziDialog.Confirm", "弹层与反馈", 12),
        new("Direction", YanziComponentStatus.Ready, "YanziContentPrimitives.Direction", "弹层与反馈", 12),
        new("Drawer", YanziComponentStatus.Ready, "YanziSheet.Show", "弹层与反馈", 12),
        new("Dropdown Menu", YanziComponentStatus.Ready, "YanziDropdownMenu", "弹层与反馈", 12),
        new("Empty", YanziComponentStatus.Ready, "YanziPrimitives.EmptyState", "基础组件", 8),
        new("Field", YanziComponentStatus.Ready, "YanziPrimitives.Field", "表单控件", 9),
        new("Hover Card", YanziComponentStatus.Ready, "YanziHoverCard.Attach", "弹层与反馈", 12),
        new("Input", YanziComponentStatus.Ready, "Yanzi.Input", "输入", 2),
        new("Input Group", YanziComponentStatus.Ready, "YanziInputGroup", "表单控件", 9),
        new("Input OTP", YanziComponentStatus.Ready, "YanziOtpInput", "表单控件", 9),
        new("Item", YanziComponentStatus.Ready, "YanziItem", "数据展示", 11),
        new("Kbd", YanziComponentStatus.Ready, "YanziPrimitives.Kbd", "基础组件", 8),
        new("Label", YanziComponentStatus.Ready, "YanziPrimitives.Field", "基础组件", 8),
        new("Marker", YanziComponentStatus.Ready, "YanziContentPrimitives.Marker", "数据展示", 11),
        new("Menubar", YanziComponentStatus.Ready, "Yanzi.Menubar", "弹层与反馈", 12),
        new("Message", YanziComponentStatus.Ready, "YanziContentPrimitives.MessageBubble", "数据展示", 11),
        new("Message Scroller", YanziComponentStatus.Ready, "YanziMessageScroller", "数据展示", 11),
        new("Native Select", YanziComponentStatus.Ready, "Yanzi.Select", "表单控件", 9),
        new("Navigation Menu", YanziComponentStatus.Ready, "Yanzi.Menubar", "导航与布局", 10),
        new("Pagination", YanziComponentStatus.Ready, "YanziPagination", "导航与布局", 10),
        new("Popover", YanziComponentStatus.Ready, "YanziPopover.Attach", "弹层与反馈", 12),
        new("Progress", YanziComponentStatus.Ready, "Yanzi.Progress", "数据展示", 11),
        new("Questionnaire", YanziComponentStatus.Ready, "YanziQuestionnaire", "表单控件", 9),
        new("Radio Group", YanziComponentStatus.Ready, "YanziRadio / YanziRadioGroup", "表单控件", 9),
        new("Resizable", YanziComponentStatus.Ready, "YanziLayoutPrimitives.Resizable", "导航与布局", 10),
        new("Scroll Area", YanziComponentStatus.Ready, "YanziLayoutPrimitives.ScrollArea", "导航与布局", 10),
        new("Select", YanziComponentStatus.Ready, "Yanzi.Select", "表单控件", 9),
        new("Separator", YanziComponentStatus.Ready, "YanziPrimitives.Separator", "基础组件", 8),
        new("Sheet", YanziComponentStatus.Ready, "YanziSheet.Show", "弹层与反馈", 12),
        new("Sidebar", YanziComponentStatus.Ready, "YanziSidebar", "导航与布局", 10),
        new("Skeleton", YanziComponentStatus.Ready, "YanziPrimitives.Skeleton", "基础组件", 8),
        new("Slider", YanziComponentStatus.Ready, "Yanzi.Slider", "表单控件", 9),
        new("Spinner", YanziComponentStatus.Ready, "YanziLoadingRing", "反馈", 5),
        new("Switch", YanziComponentStatus.Ready, "Yanzi.Switch.Shadcn", "选择", 3),
        new("Table", YanziComponentStatus.Ready, "Yanzi.DataGrid", "数据展示", 11),
        new("Tabs", YanziComponentStatus.Ready, "Yanzi.Tabs", "导航与布局", 10),
        new("Textarea", YanziComponentStatus.Ready, "Yanzi.Textarea", "表单控件", 9),
        new("Toast", YanziComponentStatus.Ready, "YanziToast.Show", "弹层与反馈", 12),
        new("Toggle", YanziComponentStatus.Ready, "Yanzi.ToggleButton", "选择", 3),
        new("Toggle Group", YanziComponentStatus.Ready, "YanziToggleGroup", "表单控件", 9),
        new("Tooltip", YanziComponentStatus.Ready, "Yanzi.Tooltip", "弹层与反馈", 12),
        new("Typography", YanziComponentStatus.Ready, "TextBlock / tokens", "基础组件", 8),
    };
}
