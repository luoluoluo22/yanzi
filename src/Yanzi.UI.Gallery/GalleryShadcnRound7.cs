using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Yanzi.UI.Wpf;

namespace Yanzi.UI.Gallery;

/// <summary>Official registry examples built with native, reusable WPF controls.</summary>
internal sealed partial class GalleryWindow
{
    private UIElement ButtonShowcase()
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var heading = Text("Variants", 12, true, "Yanzi.Color.MutedForeground",
            new Thickness(0, 0, 0, 13));
        stack.Children.Add(heading);
        var firstRow = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var (name, style) in new[]
        {
            ("Default", YanziUi.Styles.DefaultButton),
            ("Secondary", YanziUi.Styles.SecondaryButton),
            ("Outline", YanziUi.Styles.OutlineButton),
            ("Destructive", YanziUi.Styles.DestructiveButton)
        })
        {
            var label = name;
            var b = Button(label, style, () => Status("Button · " + label));
            b.Margin = new Thickness(0, 0, 9, 10);
            firstRow.Children.Add(b);
        }
        stack.Children.Add(firstRow);
        var second = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var ghost = Button("Ghost", YanziUi.Styles.GhostButton, () => Status("Ghost"));
        ghost.Margin = new Thickness(0, 0, 12, 0);
        second.Children.Add(ghost);
        second.Children.Add(Button("Link", YanziUi.Styles.LinkButton, () => Status("Link")));
        stack.Children.Add(second);
        stack.Children.Add(Text("Button width follows its text, except explicitly fixed icon buttons.",
            12, false, "Yanzi.Color.MutedForeground", new Thickness(0, 16, 0, 0)));
        return stack;
    }

    private UIElement InputShowcase()
    {
        var stack = new StackPanel { Width = 340, HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(Text("Email", 14, true, "Yanzi.Color.Foreground",
            new Thickness(0, 0, 0, 8)));
        var email = YanziUi.WithStyle(new TextBox
        {
            Height = 34, FontSize = 16, Padding = new Thickness(10, 4, 10, 4)
        }, YanziUi.Styles.Input);
        System.Windows.Automation.AutomationProperties.SetName(email, "Email input");
        stack.Children.Add(email);
        stack.Children.Add(Text("Disabled", 14, true, "Yanzi.Color.Foreground",
            new Thickness(0, 20, 0, 8)));
        var disabled = YanziUi.WithStyle(new TextBox
        {
            Height = 34, Text = "Not available", IsEnabled = false
        }, YanziUi.Styles.Input);
        stack.Children.Add(disabled);
        return stack;
    }

    private UIElement SelectShowcase()
    {
        var select = new YanziSelect("Select a fruit")
        {
            Width = 245, HorizontalAlignment = HorizontalAlignment.Center
        };
        foreach (var fruit in new[] { "Apple", "Banana", "Blueberry", "Grapes", "Pineapple" })
            select.Add(fruit);
        select.SelectionChanged += (_, value) => Status("Select · " + value);
        return select;
    }

    private UIElement DialogShowcase()
    {
        var open = Button("Edit profile", YanziUi.Styles.OutlineButton, () =>
        {
            var fields = new StackPanel();
            fields.Children.Add(Text("Name", 14, true, "Yanzi.Color.Foreground",
                new Thickness(0, 0, 0, 8)));
            fields.Children.Add(YanziUi.WithStyle(new TextBox
            {
                Text = "Yanzi", Height = 34, Padding = new Thickness(10, 4, 10, 4)
            }, YanziUi.Styles.Input));
            fields.Children.Add(Text("Username", 14, true, "Yanzi.Color.Foreground",
                new Thickness(0, 12, 0, 8)));
            fields.Children.Add(YanziUi.WithStyle(new TextBox
            {
                Text = "@yanzi", Height = 34, Padding = new Thickness(10, 4, 10, 4)
            }, YanziUi.Styles.Input));
            var dialog = new YanziContentDialog(this, "Edit profile",
                "Make changes to your profile here. Click save when you're done.", fields);
            Status("Dialog · " + (dialog.ShowDialog() == true ? "Save" : "Cancel"));
        });
        open.HorizontalAlignment = HorizontalAlignment.Center;
        return open;
    }

    private UIElement DropdownMenuShowcase()
    {
        var trigger = Button("Open menu ▾", YanziUi.Styles.OutlineButton, () => { });
        var menu = new YanziDropdownMenu { PreferAbove = false };
        menu.AddLabel("My Account");
        menu.AddAction("Profile", () => Status("Dropdown: Profile"), "⇧⌘P");
        menu.AddAction("Billing", () => Status("Dropdown: Billing"), "⌘B");
        menu.AddSeparator();
        menu.AddCheck("Show status", true, value => Status("Show status: " + value));
        menu.AddSeparator();
        menu.AddRadioGroup("Theme", new[] { "Light", "Dark", "System" }, "Dark",
            value => Status("Theme: " + value));
        menu.AddSeparator();
        menu.AddSubmenu("Share", share =>
        {
            share.AddAction("Copy link", () => Status("Share: Copy link"));
            share.AddSubmenu("Export as", formats =>
            {
                formats.AddAction("PDF", () => Status("Export as PDF"));
                formats.AddAction("Markdown", () => Status("Export as Markdown"));
            });
        });
        menu.AddSeparator();
        menu.AddAction("Log out", () => Status("Log out · demo only"), destructive: true);
        menu.Attach(trigger);
        trigger.HorizontalAlignment = HorizontalAlignment.Center;
        return trigger;
    }
}
