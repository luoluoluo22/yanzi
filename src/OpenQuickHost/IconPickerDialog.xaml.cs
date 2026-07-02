using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OpenQuickHost
{
    public partial class IconPickerDialog : Window
    {
        private readonly List<ExtensionIconOption> _allIcons;
        public string? SelectedIconReference { get; private set; }

        public IconPickerDialog(Window owner, string? initialSelection = null)
        {
            InitializeComponent();
            Owner = owner;

            // 获取全部内置图标
            _allIcons = ExtensionIconLibrary.GetBuiltInOptions().ToList();
            IconsListBox.ItemsSource = _allIcons;

            // 预选当前已设定的图标
            if (!string.IsNullOrWhiteSpace(initialSelection))
            {
                var target = _allIcons.FirstOrDefault(icon => string.Equals(icon.Reference, initialSelection, StringComparison.OrdinalIgnoreCase));
                if (target != null)
                {
                    IconsListBox.SelectedItem = target;
                    IconsListBox.ScrollIntoView(target);
                }
            }

            // 支持双击直接确认
            IconsListBox.MouseDoubleClick += IconsListBox_MouseDoubleClick;
            SearchBox.Focus();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            FilterIcons();
        }

        private void FilterIcons()
        {
            var query = (SearchBox.Text ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(query))
            {
                IconsListBox.ItemsSource = _allIcons;
                return;
            }

            // 根据标签文字或内部 Reference 过滤
            var filtered = _allIcons.Where(icon =>
                icon.Label.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                icon.Reference.Contains(query, StringComparison.OrdinalIgnoreCase)
            ).ToList();

            IconsListBox.ItemsSource = filtered;
        }

        private void IconsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 单击时暂存
            if (IconsListBox.SelectedItem is ExtensionIconOption option)
            {
                SelectedIconReference = option.Reference;
            }
        }

        private void IconsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (IconsListBox.SelectedItem is ExtensionIconOption)
            {
                ConfirmButton_Click(sender, e);
            }
        }

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            if (IconsListBox.SelectedItem is ExtensionIconOption option)
            {
                SelectedIconReference = option.Reference;
                DialogResult = true;
            }
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void SearchBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                // 如果回车，直接选择过滤出来的第一个，或者当前选中的那个
                if (IconsListBox.SelectedItem == null && IconsListBox.Items.Count > 0)
                {
                    IconsListBox.SelectedIndex = 0;
                }
                ConfirmButton_Click(sender, e);
            }
            else if (e.Key == Key.Escape)
            {
                CancelButton_Click(sender, e);
            }
        }
    }
}
