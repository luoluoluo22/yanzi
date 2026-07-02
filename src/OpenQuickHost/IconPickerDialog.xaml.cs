using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace OpenQuickHost
{
    public partial class IconPickerDialog : Window
    {
        private readonly List<ExtensionIconOption> _allIcons;
        private List<ExtensionIconOption> _currentSource = new();
        private int _loadedCount = 0;
        private const int PageSize = 100;
        public string? SelectedIconReference { get; private set; }

        public IconPickerDialog(Window owner, string? initialSelection = null)
        {
            InitializeComponent();
            Owner = owner;

            // 获取全部内置图标
            _allIcons = ExtensionIconLibrary.GetAllMdiOptions().ToList();
            _currentSource = _allIcons;
            _loadedCount = PageSize;
            IconsListBox.ItemsSource = _currentSource.Take(_loadedCount).ToList();

            // 预选当前已设定的图标
            if (!string.IsNullOrWhiteSpace(initialSelection))
            {
                var target = _allIcons.FirstOrDefault(icon => string.Equals(icon.Reference, initialSelection, StringComparison.OrdinalIgnoreCase));
                if (target != null)
                {
                    var index = _allIcons.IndexOf(target);
                    if (index >= _loadedCount)
                    {
                        _loadedCount = ((index / PageSize) + 1) * PageSize;
                        IconsListBox.ItemsSource = _currentSource.Take(_loadedCount).ToList();
                    }
                    IconsListBox.SelectedItem = target;
                    IconsListBox.ScrollIntoView(target);
                }
            }

            // 支持双击直接确认
            IconsListBox.MouseDoubleClick += IconsListBox_MouseDoubleClick;
            SearchBox.Focus();

            this.Loaded += IconPickerDialog_Loaded;
        }

        private void IconPickerDialog_Loaded(object sender, RoutedEventArgs e)
        {
            var scrollViewer = FindScrollViewer(IconsListBox);
            if (scrollViewer != null)
            {
                scrollViewer.ScrollChanged += ScrollViewer_ScrollChanged;
            }
        }

        private ScrollViewer? FindScrollViewer(DependencyObject obj)
        {
            if (obj is ScrollViewer sv) return sv;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
            {
                var child = VisualTreeHelper.GetChild(obj, i);
                var result = FindScrollViewer(child);
                if (result != null) return result;
            }
            return null;
        }

        private void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer)
            {
                // 快滚到底部时自动增量加载下一页，实现滚动懒加载
                if (scrollViewer.ScrollableHeight > 0 && scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight - 50)
                {
                    LoadMoreIcons();
                }
            }
        }

        private void LoadMoreIcons()
        {
            if (_loadedCount >= _currentSource.Count) return;

            _loadedCount += PageSize;
            var selected = IconsListBox.SelectedItem;
            
            IconsListBox.ItemsSource = _currentSource.Take(_loadedCount).ToList();
            
            if (selected != null)
            {
                IconsListBox.SelectedItem = selected;
            }
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
                _currentSource = _allIcons;
            }
            else
            {
                _currentSource = _allIcons.Where(icon =>
                    icon.Label.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    icon.Reference.Contains(query, StringComparison.OrdinalIgnoreCase)
                ).ToList();
            }

            _loadedCount = PageSize;
            IconsListBox.ItemsSource = _currentSource.Take(_loadedCount).ToList();
            IconsListBox.SelectedIndex = -1;
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
            var count = IconsListBox.Items.Count;
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                if (IconsListBox.SelectedItem != null)
                {
                    ConfirmButton_Click(sender, e);
                }
                else if (count > 0)
                {
                    IconsListBox.SelectedIndex = 0;
                    var item = IconsListBox.SelectedItem;
                    if (item != null) IconsListBox.ScrollIntoView(item);
                }
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                CancelButton_Click(sender, e);
            }
            else if (e.Key == Key.Down)
            {
                e.Handled = true;
                if (count > 0)
                {
                    var nextIndex = IconsListBox.SelectedIndex == -1 ? 0 : IconsListBox.SelectedIndex + 6;
                    if (nextIndex < count)
                    {
                        IconsListBox.SelectedIndex = nextIndex;
                    }
                    else
                    {
                        IconsListBox.SelectedIndex = count - 1;
                    }
                    var item = IconsListBox.SelectedItem;
                    if (item != null) IconsListBox.ScrollIntoView(item);
                }
            }
            else if (e.Key == Key.Up)
            {
                e.Handled = true;
                if (count > 0)
                {
                    var prevIndex = IconsListBox.SelectedIndex == -1 ? 0 : IconsListBox.SelectedIndex - 6;
                    if (prevIndex >= 0)
                    {
                        IconsListBox.SelectedIndex = prevIndex;
                    }
                    else
                    {
                        IconsListBox.SelectedIndex = 0;
                    }
                    var item = IconsListBox.SelectedItem;
                    if (item != null) IconsListBox.ScrollIntoView(item);
                }
            }
            else if (e.Key == Key.Right && IconsListBox.SelectedIndex != -1)
            {
                if (count > 0)
                {
                    var nextIndex = IconsListBox.SelectedIndex + 1;
                    if (nextIndex < count)
                    {
                        e.Handled = true;
                        IconsListBox.SelectedIndex = nextIndex;
                        var item = IconsListBox.SelectedItem;
                        if (item != null) IconsListBox.ScrollIntoView(item);
                    }
                }
            }
            else if (e.Key == Key.Left && IconsListBox.SelectedIndex != -1)
            {
                if (count > 0)
                {
                    var prevIndex = IconsListBox.SelectedIndex - 1;
                    if (prevIndex >= 0)
                    {
                        e.Handled = true;
                        IconsListBox.SelectedIndex = prevIndex;
                        var item = IconsListBox.SelectedItem;
                        if (item != null) IconsListBox.ScrollIntoView(item);
                    }
                }
            }
        }
    }
}
