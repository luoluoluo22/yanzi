using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;

namespace OpenQuickHost;

public partial class MainWindow
{
    private void RunButton_Click(object sender, RoutedEventArgs e)
    {
        RunSelectedCommand();
    }

    private void CloseHostedViewButton_Click(object sender, RoutedEventArgs e)
    {
        CloseHostedView();
    }

    private async void HostedViewRunButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshHostedViewOutputAsync();
    }

    private void HostedViewInputBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_activeHostedView == null)
        {
            return;
        }

        if (UsesScriptHostedView(_activeHostedView.Definition))
        {
            HostedViewStatus = "脚本视图已更新输入，点击右下角按钮执行。";
            return;
        }

        RefreshHostedViewOutput();
    }

    private void OpenHostedView(CommandItem command, string? initialInput = null)
    {
        if (command.HostedView == null)
        {
            return;
        }

        _activeHostedView = new HostedPluginSession(command, command.HostedView);
        ApplyHostedViewWindowMetrics(command.HostedView);
        HostedViewInput = (initialInput ?? string.Empty).Trim();
        InitializeHostedViewState(initialInput);
        HostedViewDynamicContent = command.HostedView.UsesDynamicLayout
            ? BuildHostedViewDynamicContent(_activeHostedView)
            : null;
        HostedViewOutput = command.HostedView.EmptyState ?? "等待插件输出。";
        HostedViewStatus = string.IsNullOrWhiteSpace(HostedViewInput)
            ? $"已进入 {command.Title}。输入内容后可直接在当前窗口完成操作。"
            : $"已进入 {command.Title}，并填入外部选中内容。";
        OnPropertyChanged(nameof(IsHostedViewOpen));
        OnPropertyChanged(nameof(IsHostedViewDynamic));
        OnPropertyChanged(nameof(HostedViewLegacyVisibility));
        OnPropertyChanged(nameof(HostedViewDynamicVisibility));
        OnPropertyChanged(nameof(HostedViewFooterActionVisibility));
        OnPropertyChanged(nameof(HostedViewTitle));
        OnPropertyChanged(nameof(HostedViewSubtitle));
        OnPropertyChanged(nameof(HostedViewCommandLabel));
        OnPropertyChanged(nameof(HostedViewInputLabel));
        OnPropertyChanged(nameof(HostedViewOutputLabel));
        OnPropertyChanged(nameof(HostedViewInputPlaceholder));
        OnPropertyChanged(nameof(HostedViewActionButtonText));
        LastRunMessage = $"已打开插件视图：{command.Title}";
        Dispatcher.BeginInvoke(() =>
        {
            if (_hostedViewPreferredFocusControl != null)
            {
                _hostedViewPreferredFocusControl.Focus();
                return;
            }

            HostedViewInputBox.Focus();
        });
    }

    private void CloseHostedView()
    {
        if (_activeHostedView == null)
        {
            return;
        }

        var title = _activeHostedView.Command.Title;
        _activeHostedView = null;
        _hostedViewStateBindings.Clear();
        _hostedViewPreferredFocusControl = null;
        HostedViewInput = string.Empty;
        HostedViewOutput = string.Empty;
        HostedViewDynamicContent = null;
        HostedViewStatus = "已关闭插件视图。";
        OnPropertyChanged(nameof(IsHostedViewOpen));
        OnPropertyChanged(nameof(IsHostedViewDynamic));
        OnPropertyChanged(nameof(HostedViewLegacyVisibility));
        OnPropertyChanged(nameof(HostedViewDynamicVisibility));
        OnPropertyChanged(nameof(HostedViewFooterActionVisibility));
        OnPropertyChanged(nameof(HostedViewTitle));
        OnPropertyChanged(nameof(HostedViewSubtitle));
        OnPropertyChanged(nameof(HostedViewCommandLabel));
        OnPropertyChanged(nameof(HostedViewInputLabel));
        OnPropertyChanged(nameof(HostedViewOutputLabel));
        OnPropertyChanged(nameof(HostedViewInputPlaceholder));
        OnPropertyChanged(nameof(HostedViewActionButtonText));
        RestoreHostedViewWindowMetrics();
        LastRunMessage = $"已返回命令列表：{title}";
        if (_hostedViewEditorWindowToRestore != null)
        {
            var editorWindow = _hostedViewEditorWindowToRestore;
            _hostedViewEditorWindowToRestore = null;
            if (editorWindow.Visibility != Visibility.Visible)
            {
                editorWindow.Show();
            }
            editorWindow.Activate();
            HostAssets.AppendLog("Hosted view preview closed: editor window restored.");
        }
        SearchBox.Focus();
    }

    public async Task PreviewHostedViewForTestAsync(
        CommandItem command,
        string initialInput = "测试输入",
        Window? editorWindowToRestore = null)
    {
        if (command.HostedView == null)
        {
            return;
        }

        _hostedViewEditorWindowToRestore = editorWindowToRestore;
        ShowPanel();
        Activate();
        OpenHostedView(command, initialInput);
        if (command.HostedView.UsesDynamicLayout)
        {
            return;
        }

        if (UsesScriptHostedView(command.HostedView))
        {
            await RefreshHostedViewOutputAsync();
        }
        else if (!string.IsNullOrWhiteSpace(initialInput))
        {
            RefreshHostedViewOutput();
        }
    }

    private void RefreshHostedViewOutput()
    {
        if (_activeHostedView == null)
        {
            return;
        }

        var input = HostedViewInput.Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            HostedViewOutput = _activeHostedView.Definition.EmptyState ?? "等待插件输出。";
            HostedViewStatus = "输入内容后即可执行插件动作。";
            return;
        }

        HostedViewOutput = ExecuteHostedView(_activeHostedView.Definition, input);
        HostedViewStatus = $"已更新 {_activeHostedView.Command.Title} 输出。";
    }

    private async Task RefreshHostedViewOutputAsync()
    {
        if (_activeHostedView == null)
        {
            return;
        }

        if (!UsesScriptHostedView(_activeHostedView.Definition))
        {
            RefreshHostedViewOutput();
            return;
        }

        var input = HostedViewInput.Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            HostedViewOutput = _activeHostedView.Definition.EmptyState ?? "等待插件输出。";
            HostedViewStatus = "输入内容后即可执行插件动作。";
            return;
        }

        if (!ScriptExtensionRunner.CanExecute(_activeHostedView.Command))
        {
            HostedViewOutput = "当前宿主视图声明为脚本模式，但扩展没有有效的脚本入口。";
            HostedViewStatus = "脚本入口缺失。";
            return;
        }

        HostedViewStatus = $"正在执行 {_activeHostedView.Command.Title} 脚本...";
        var result = await ScriptExtensionRunner.ExecuteAsync(_activeHostedView.Command, input, "hosted-view");
        HostedViewOutput = result.Success
            ? string.IsNullOrWhiteSpace(result.Output) ? "脚本执行完成，但没有返回输出。" : result.Output
            : $"脚本执行失败{Environment.NewLine}{Environment.NewLine}{result.Error}";
        HostedViewStatus = result.Success
            ? $"已更新 {_activeHostedView.Command.Title} 输出。"
            : $"脚本执行失败：{result.Error}";
    }

    private static string ExecuteHostedView(HostedPluginViewDefinition definition, string input)
    {
        return definition.ActionType switch
        {
            "template" when !string.IsNullOrWhiteSpace(definition.OutputTemplate)
                => definition.OutputTemplate.Replace("{input}", input, StringComparison.Ordinal),
            "uppercase" => input.ToUpperInvariant(),
            "reverse" => new string(input.Reverse().ToArray()),
            "mock-translate" => BuildMockTranslation(input),
            _ when !string.IsNullOrWhiteSpace(definition.OutputTemplate)
                => definition.OutputTemplate.Replace("{input}", input, StringComparison.Ordinal),
            _ => input
        };
    }

    private static bool UsesScriptHostedView(HostedPluginViewDefinition definition)
    {
        return string.Equals(definition.ActionType, "script", StringComparison.OrdinalIgnoreCase);
    }

    private void InitializeHostedViewState(string? initialInput)
    {
        if (_activeHostedView == null)
        {
            return;
        }

        _activeHostedView.State.Clear();
        _hostedViewStateBindings.Clear();
        _hostedViewPreferredFocusControl = null;

        foreach (var pair in _activeHostedView.Definition.InitialState)
        {
            _activeHostedView.State[pair.Key] = pair.Value ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(initialInput))
        {
            return;
        }

        if (_activeHostedView.State.ContainsKey("input"))
        {
            _activeHostedView.State["input"] = initialInput.Trim();
            return;
        }

        var firstBoundTextarea = _activeHostedView.Definition.Components
            .FirstOrDefault(component =>
                string.Equals(component.Type, "textarea", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(component.Bind));
        if (firstBoundTextarea != null && !string.IsNullOrWhiteSpace(firstBoundTextarea.Bind))
        {
            _activeHostedView.State[firstBoundTextarea.Bind] = initialInput.Trim();
        }
    }

    private UIElement BuildHostedViewDynamicContent(HostedPluginSession session)
    {
        if (!string.IsNullOrWhiteSpace(session.Definition.XamlTemplate))
        {
            return BuildHostedViewXamlContent(session);
        }

        return ResolveHostedViewLayout(session.Definition) switch
        {
            "split-horizontal" => BuildSplitHorizontalHostedView(session),
            _ => BuildSinglePaneHostedView(session)
        };
    }

    private UIElement BuildHostedViewXamlContent(HostedPluginSession session)
    {
        try
        {
            var parserContext = CreateHostedViewXamlParserContext();
            var xaml = NormalizeHostedViewXaml(session.Definition.XamlTemplate!);
            var root = XamlReader.Parse(xaml, parserContext) switch
            {
                Window window => ExtractHostedViewWindowContent(window),
                System.Windows.Controls.UserControl userControl => ExtractHostedViewUserControlContent(userControl),
                FrameworkElement element => element,
                _ => null
            };

            if (root == null)
            {
                return BuildHostedViewXamlError("XAML 根元素必须是 Window、UserControl 或 FrameworkElement。");
            }

            root.DataContext = session.BindingContext;
            AttachHostedViewActions(root, session);
            return root;
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"HostedViewXaml parse failed: {ex}");
            return BuildHostedViewXamlError(ex.Message);
        }
    }

    private static ParserContext CreateHostedViewXamlParserContext()
    {
        var assemblyName = typeof(HostedViewBridge).Assembly.GetName().Name ?? "Yanzi";
        var parserContext = new ParserContext
        {
            XamlTypeMapper = new XamlTypeMapper(Array.Empty<string>())
        };
        parserContext.XamlTypeMapper.AddMappingProcessingInstruction("oqh", "Yanzi", assemblyName);
        parserContext.XmlnsDictionary.Add(string.Empty, "http://schemas.microsoft.com/winfx/2006/xaml/presentation");
        parserContext.XmlnsDictionary.Add("x", "http://schemas.microsoft.com/winfx/2006/xaml");
        parserContext.XmlnsDictionary.Add("oqh", $"clr-namespace:Yanzi;assembly={assemblyName}");
        return parserContext;
    }

    private static string NormalizeHostedViewXaml(string xaml)
    {
        var assemblyName = typeof(HostedViewBridge).Assembly.GetName().Name ?? "Yanzi";
        const string plainYanziNamespace = "xmlns:oqh=\"clr-namespace:Yanzi\"";
        const string plainLegacyNamespace = "xmlns:oqh=\"clr-namespace:OpenQuickHost\"";
        var qualifiedYanziNamespace = $"xmlns:oqh=\"clr-namespace:Yanzi;assembly={assemblyName}\"";
        var qualifiedLegacyNamespace = $"xmlns:oqh=\"clr-namespace:OpenQuickHost;assembly={assemblyName}\"";
        var normalized = xaml;

        normalized = normalized.Replace(
            "xmlns=\"[http://schemas.microsoft.com/winfx/2006/xaml/presentation](http://schemas.microsoft.com/winfx/2006/xaml/presentation)\"",
            "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"",
            StringComparison.Ordinal);
        normalized = normalized.Replace(
            "xmlns:x=\"[http://schemas.microsoft.com/winfx/2006/xaml](http://schemas.microsoft.com/winfx/2006/xaml)\"",
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"",
            StringComparison.Ordinal);
        normalized = Regex.Replace(
            normalized,
            "\\s+LetterSpacing\\s*=\\s*\"[^\"]*\"",
            string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        normalized = Regex.Replace(
            normalized,
            "\\s+LineHeight\\s*=\\s*\"[^\"]*\"",
            string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (normalized.Contains(plainYanziNamespace, StringComparison.Ordinal))
        {
            return normalized.Replace(plainYanziNamespace, qualifiedYanziNamespace, StringComparison.Ordinal);
        }

        return normalized.Contains(plainLegacyNamespace, StringComparison.Ordinal)
            ? normalized.Replace(plainLegacyNamespace, qualifiedLegacyNamespace, StringComparison.Ordinal)
            : normalized;
    }

    private FrameworkElement ExtractHostedViewWindowContent(Window window)
    {
        if (window.Content is not FrameworkElement content)
        {
            throw new InvalidOperationException("Window 类型的 XAML 必须包含可视内容。");
        }

        if (!double.IsNaN(window.Width) && window.Width > 0)
        {
            Width = Math.Max(window.Width, MinWidth);
        }

        if (!double.IsNaN(window.Height) && window.Height > 0)
        {
            Height = Math.Max(window.Height, MinHeight);
        }

        if (!double.IsNaN(window.MinWidth) && window.MinWidth > 0)
        {
            MinWidth = Math.Max(window.MinWidth, 320);
        }

        if (!double.IsNaN(window.MinHeight) && window.MinHeight > 0)
        {
            MinHeight = Math.Max(window.MinHeight, 240);
        }

        window.Content = null;
        if (window.Resources.Count > 0)
        {
            content.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                MergedDictionaries = { window.Resources }
            });
        }

        return content;
    }

    private FrameworkElement ExtractHostedViewUserControlContent(System.Windows.Controls.UserControl userControl)
    {
        if (userControl.Content is not FrameworkElement content)
        {
            return userControl;
        }

        userControl.Content = null;
        return content;
    }

    private FrameworkElement BuildHostedViewXamlError(string errorMessage)
    {
        return new Border
        {
            Background = (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#331F2937")!,
            BorderBrush = (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#FFDC2626")!,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14),
            Child = new System.Windows.Controls.TextBox
            {
                Text = $"自定义 XAML 视图加载失败：{errorMessage}",
                IsReadOnly = true,
                AcceptsReturn = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Background = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = System.Windows.Media.Brushes.OrangeRed,
                TextWrapping = TextWrapping.Wrap,
                CaretBrush = (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#FFDC2626")!,
                Padding = new Thickness(0),
                FontSize = 13
            }
        };
    }

    private void AttachHostedViewActions(DependencyObject root, HostedPluginSession session)
    {
        foreach (var button in FindVisualChildren<System.Windows.Controls.Button>(root))
        {
            var actionText = HostedViewBridge.GetAction(button);
            if (string.IsNullOrWhiteSpace(actionText))
            {
                continue;
            }

            button.Click += async (_, _) =>
            {
                button.IsEnabled = false;
                try
                {
                    await ExecuteHostedViewActionsAsync(session, ParseHostedViewActionString(actionText));
                }
                finally
                {
                    button.IsEnabled = true;
                }
            };
        }

        var preferredFocusName = HostedViewBridge.GetPreferredFocus(root as DependencyObject);
        if (!string.IsNullOrWhiteSpace(preferredFocusName) && root is FrameworkElement frameworkRoot)
        {
            if (frameworkRoot.FindName(preferredFocusName) is System.Windows.Controls.Control control)
            {
                _hostedViewPreferredFocusControl = control;
            }
        }

        var loadedActionText = HostedViewBridge.GetLoadedAction(root);
        if (!string.IsNullOrWhiteSpace(loadedActionText) && root is FrameworkElement loadedRoot)
        {
            RoutedEventHandler? loadedHandler = null;
            loadedHandler = async (_, _) =>
            {
                loadedRoot.Loaded -= loadedHandler;
                await ExecuteHostedViewActionsAsync(session, ParseHostedViewActionString(loadedActionText));
            };
            loadedRoot.Loaded += loadedHandler;
        }
    }

    private UIElement BuildSinglePaneHostedView(HostedPluginSession session)
    {
        var panel = new StackPanel();
        foreach (var component in session.Definition.Components)
        {
            panel.Children.Add(BuildHostedViewComponent(component, session));
        }

        return WrapHostedViewRegion(panel);
    }

    private UIElement BuildSplitHorizontalHostedView(HostedPluginSession session)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var left = new StackPanel();
        var right = new StackPanel();
        foreach (var component in session.Definition.Components)
        {
            var region = string.IsNullOrWhiteSpace(component.Region) ? "left" : component.Region.Trim().ToLowerInvariant();
            var target = region == "right" ? right : left;
            target.Children.Add(BuildHostedViewComponent(component, session));
        }

        var leftBorder = WrapHostedViewRegion(left);
        var rightBorder = WrapHostedViewRegion(right);
        Grid.SetColumn(leftBorder, 0);
        Grid.SetColumn(rightBorder, 2);
        grid.Children.Add(leftBorder);
        grid.Children.Add(rightBorder);
        return grid;
    }

    private Border WrapHostedViewRegion(System.Windows.Controls.Panel content)
    {
        return new Border
        {
            Background = (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#FF202020")!,
            BorderBrush = (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#FF2E2E2E")!,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14),
            Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = content
            }
        };
    }

    private FrameworkElement BuildHostedViewComponent(HostedViewComponentDefinition component, HostedPluginSession session)
    {
        var wrapper = new StackPanel
        {
            Margin = new Thickness(0, 0, 0, 12)
        };

        if (!string.IsNullOrWhiteSpace(component.Label))
        {
            wrapper.Children.Add(new TextBlock
            {
                Text = component.Label,
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            });
        }

        FrameworkElement content = component.Type.Trim().ToLowerInvariant() switch
        {
            "text" => BuildHostedViewTextComponent(component, session, markdown: false),
            "markdown" => BuildHostedViewTextComponent(component, session, markdown: true),
            "textarea" => BuildHostedViewTextareaComponent(component, session),
            "button" => BuildHostedViewButtonComponent(component, session),
            _ => BuildHostedViewUnsupportedComponent(component)
        };

        wrapper.Children.Add(content);
        return wrapper;
    }

    private FrameworkElement BuildHostedViewTextComponent(HostedViewComponentDefinition component, HostedPluginSession session, bool markdown)
    {
        var textBox = new System.Windows.Controls.TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = System.Windows.Media.Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = markdown
                ? (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#FFE5E5E5")!
                : (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#FFD7D7D7")!,
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            Padding = new Thickness(0)
        };

        if (!string.IsNullOrWhiteSpace(component.Bind))
        {
            RegisterHostedViewStateBinding(component.Bind, value => textBox.Text = value);
        }
        else
        {
            textBox.Text = component.Text ?? string.Empty;
        }

        return new Border
        {
            Background = (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#FF171717")!,
            BorderBrush = (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#FF2E2E2E")!,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12),
            Child = textBox
        };
    }

    private FrameworkElement BuildHostedViewTextareaComponent(HostedViewComponentDefinition component, HostedPluginSession session)
    {
        var textBox = new System.Windows.Controls.TextBox
        {
            AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            TextWrapping = TextWrapping.Wrap,
            Background = (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#FF171717")!,
            BorderBrush = (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#FF2E2E2E")!,
            BorderThickness = new Thickness(1),
            Foreground = System.Windows.Media.Brushes.White,
            CaretBrush = (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#FF3B82F6")!,
            Padding = new Thickness(12),
            FontSize = 14,
            MinHeight = 180
        };

        if (_hostedViewPreferredFocusControl == null)
        {
            _hostedViewPreferredFocusControl = textBox;
        }

        if (!string.IsNullOrWhiteSpace(component.Bind))
        {
            var path = component.Bind;
            RegisterHostedViewStateBinding(path, value =>
            {
                if (!string.Equals(textBox.Text, value, StringComparison.Ordinal))
                {
                    textBox.Text = value;
                }
            });
            textBox.TextChanged += (_, _) => SetHostedViewState(path, textBox.Text);
        }

        var grid = new Grid();
        grid.Children.Add(textBox);
        if (!string.IsNullOrWhiteSpace(component.Placeholder))
        {
            var placeholder = new TextBlock
            {
                IsHitTestVisible = false,
                Margin = new Thickness(14, 12, 14, 0),
                VerticalAlignment = VerticalAlignment.Top,
                Foreground = (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#FF555555")!,
                TextWrapping = TextWrapping.Wrap,
                Text = component.Placeholder
            };
            textBox.TextChanged += (_, _) =>
            {
                placeholder.Visibility = string.IsNullOrWhiteSpace(textBox.Text) ? Visibility.Visible : Visibility.Collapsed;
            };
            placeholder.Visibility = string.IsNullOrWhiteSpace(textBox.Text) ? Visibility.Visible : Visibility.Collapsed;
            grid.Children.Add(placeholder);
        }

        return grid;
    }

    private FrameworkElement BuildHostedViewButtonComponent(HostedViewComponentDefinition component, HostedPluginSession session)
    {
        var button = new System.Windows.Controls.Button
        {
            Content = component.Text ?? component.Label ?? "执行",
            Background = (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#FF2563EB")!,
            Foreground = System.Windows.Media.Brushes.White,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(18, 10, 18, 10),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left
        };

        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            try
            {
                await ExecuteHostedViewActionsAsync(session, component.Actions);
            }
            finally
            {
                button.IsEnabled = true;
            }
        };
        return button;
    }

    private FrameworkElement BuildHostedViewUnsupportedComponent(HostedViewComponentDefinition component)
    {
        return new TextBlock
        {
            Text = $"暂不支持的组件类型：{component.Type}",
            Foreground = System.Windows.Media.Brushes.OrangeRed,
            FontSize = 12
        };
    }

    private void RegisterHostedViewStateBinding(string path, Action<string> updater)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (!_hostedViewStateBindings.TryGetValue(path, out var updaters))
        {
            updaters = [];
            _hostedViewStateBindings[path] = updaters;
        }

        updaters.Add(updater);
        updater(GetHostedViewState(path));
    }

    private string GetHostedViewState(string path)
    {
        if (_activeHostedView == null || string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return _activeHostedView.State.TryGetValue(path, out var value) ? value : string.Empty;
    }

    private void SetHostedViewState(string path, string? value)
    {
        if (_activeHostedView == null || string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var normalized = value ?? string.Empty;
        _activeHostedView.State[path] = normalized;
        _activeHostedView.BindingContext.NotifyChanged();
        if (_hostedViewStateBindings.TryGetValue(path, out var updaters))
        {
            foreach (var updater in updaters)
            {
                updater(normalized);
            }
        }
    }

    private async Task ExecuteHostedViewActionsAsync(
        HostedPluginSession session,
        IReadOnlyList<HostedViewActionDefinition> actions)
    {
        if (actions.Count == 0)
        {
            HostedViewStatus = "当前按钮没有配置动作。";
            return;
        }

        foreach (var action in actions)
        {
            switch (action.Type.Trim().ToLowerInvariant())
            {
                case "setstate":
                    var value = !string.IsNullOrWhiteSpace(action.ValueFrom)
                        ? GetHostedViewState(action.ValueFrom)
                        : action.Value ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(action.Path))
                    {
                        if (action.Append)
                        {
                            var existingValue = GetHostedViewState(action.Path);
                            if (!string.IsNullOrWhiteSpace(value))
                            {
                                value = string.IsNullOrWhiteSpace(existingValue)
                                    ? value
                                    : $"{existingValue}{action.Separator ?? Environment.NewLine}{value}";
                            }
                        }

                        SetHostedViewState(action.Path, value);
                    }
                    HostedViewStatus = string.IsNullOrWhiteSpace(action.SuccessMessage)
                        ? "已更新界面状态。"
                        : action.SuccessMessage;
                    break;
                case "runscript":
                    await ExecuteHostedViewScriptActionAsync(session, action);
                    break;
                case "loadstorage":
                    await ExecuteHostedViewLoadStorageActionAsync(session, action);
                    break;
                case "savestorage":
                    await ExecuteHostedViewSaveStorageActionAsync(session, action);
                    break;
                case "close":
                    HostedViewStatus = string.IsNullOrWhiteSpace(action.SuccessMessage)
                        ? "正在关闭视图。"
                        : action.SuccessMessage;
                    CloseHostedView();
                    return;
                default:
                    HostedViewStatus = $"暂不支持的动作类型：{action.Type}";
                    break;
            }
        }
    }

    private async Task ExecuteHostedViewScriptActionAsync(HostedPluginSession session, HostedViewActionDefinition action)
    {
        var scriptCommand = CreateHostedViewScriptCommand(session, action.Script);
        if (scriptCommand == null || !ScriptExtensionRunner.CanExecute(scriptCommand))
        {
            HostedViewStatus = "当前扩展没有可执行的脚本入口。";
            return;
        }

        var input = !string.IsNullOrWhiteSpace(action.InputFrom)
            ? GetHostedViewState(action.InputFrom)
            : ResolveDefaultHostedViewInput(session);
        HostedViewStatus = $"正在执行 {session.Command.Title} 脚本...";
        var result = await ScriptExtensionRunner.ExecuteAsync(scriptCommand, input, "hosted-view", session.State);
        ApplyHostedViewScriptStateUpdates(result.StateUpdates);
        var outputPath = string.IsNullOrWhiteSpace(action.OutputTo) ? "output" : action.OutputTo;
        SetHostedViewState(outputPath, result.Success ? result.Output : result.Error);
        HostedViewStatus = result.Success
            ? (string.IsNullOrWhiteSpace(action.SuccessMessage) ? "脚本执行完成。" : action.SuccessMessage)
            : $"脚本执行失败：{result.Error}";
    }

    private async Task ExecuteHostedViewLoadStorageActionAsync(HostedPluginSession session, HostedViewActionDefinition action)
    {
        var statePath = string.IsNullOrWhiteSpace(action.Path) ? action.ValueFrom : action.Path;
        if (string.IsNullOrWhiteSpace(statePath))
        {
            HostedViewStatus = "loadStorage 缺少 path。";
            return;
        }

        var storageKey = string.IsNullOrWhiteSpace(action.Key) ? $"{statePath}.txt" : action.Key;
        var readResult = await ExtensionStorageService.ReadTextAsync(session.Command.ExtensionId, storageKey, action.Scope);
        var value = readResult.Found ? readResult.Content ?? string.Empty : action.DefaultValue ?? string.Empty;
        SetHostedViewState(statePath, value);
        HostedViewStatus = string.IsNullOrWhiteSpace(action.SuccessMessage)
            ? (readResult.Found ? $"已从 {readResult.Source} 加载存储数据。" : "未找到存储数据，已使用默认值。")
            : action.SuccessMessage;
    }

    private async Task ExecuteHostedViewSaveStorageActionAsync(HostedPluginSession session, HostedViewActionDefinition action)
    {
        var statePath = string.IsNullOrWhiteSpace(action.Path) ? action.ValueFrom : action.Path;
        var storageKey = string.IsNullOrWhiteSpace(action.Key)
            ? (!string.IsNullOrWhiteSpace(statePath) ? $"{statePath}.txt" : null)
            : action.Key;
        if (string.IsNullOrWhiteSpace(storageKey))
        {
            HostedViewStatus = "saveStorage 缺少 key。";
            return;
        }

        var value = !string.IsNullOrWhiteSpace(action.ValueFrom)
            ? GetHostedViewState(action.ValueFrom)
            : !string.IsNullOrWhiteSpace(statePath)
                ? GetHostedViewState(statePath)
                : action.Value ?? string.Empty;
        var result = await ExtensionStorageService.WriteTextAsync(
            session.Command.ExtensionId,
            storageKey,
            value,
            action.Scope);
        HostedViewStatus = string.IsNullOrWhiteSpace(action.SuccessMessage)
            ? (result.CloudSaved ? "已保存到本地并同步到坚果云。" : "已保存到本地存储。")
            : action.SuccessMessage;
    }

    private CommandItem? CreateHostedViewScriptCommand(HostedPluginSession session, string? scriptName)
    {
        if (!string.IsNullOrWhiteSpace(scriptName) &&
            session.Definition.Scripts.TryGetValue(scriptName.Trim(), out var hostedScript))
        {
            return new CommandItem(
                glyph: session.Command.Glyph,
                title: session.Command.Title,
                subtitle: session.Command.Subtitle,
                category: session.Command.Category,
                accentHex: session.Command.AccentBrush.ToString(),
                openTarget: null,
                keywords: session.Command.Keywords,
                source: session.Command.Source,
                extensionId: session.Command.ExtensionId,
                declaredVersion: session.Command.DeclaredVersion,
                extensionDirectoryPath: session.Command.ExtensionDirectoryPath,
                runtime: hostedScript.Runtime,
                entryPoint: hostedScript.Entry,
                permissions: hostedScript.Permissions,
                entryMode: hostedScript.EntryMode,
                inlineScriptSource: hostedScript.Source,
                iconReference: session.Command.IconReference);
        }

        return session.Command;
    }

    private void ApplyHostedViewScriptStateUpdates(IReadOnlyDictionary<string, string>? updates)
    {
        if (updates == null || updates.Count == 0)
        {
            return;
        }

        foreach (var pair in updates)
        {
            SetHostedViewState(pair.Key, pair.Value);
        }
    }

    private static string ResolveHostedViewLayout(HostedPluginViewDefinition definition)
    {
        return string.IsNullOrWhiteSpace(definition.Type)
            ? "single-pane"
            : definition.Type.Trim().ToLowerInvariant();
    }

    private static IReadOnlyList<HostedViewActionDefinition> ParseHostedViewActionString(string actionText)
    {
        var actions = new List<HostedViewActionDefinition>();
        foreach (var rawAction in actionText.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var segments = rawAction.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length == 0)
            {
                continue;
            }

            string type;
            string? path = null;
            string? value = null;
            string? script = null;
            string? valueFrom = null;
            string? inputFrom = null;
            string? outputTo = null;
            string? successMessage = null;
            var append = false;
            string? separator = null;
            string? key = null;
            string? scope = null;
            string? defaultValue = null;

            if (segments[0].Contains('='))
            {
                type = "setState";
            }
            else
            {
                type = segments[0];
                segments = segments.Skip(1).ToArray();
            }

            foreach (var segment in segments)
            {
                var parts = segment.Split('=', 2);
                if (parts.Length != 2)
                {
                    continue;
                }

                var propertyKey = parts[0].Trim();
                var parsedValue = parts[1].Trim();
                switch (propertyKey.ToLowerInvariant())
                {
                    case "type":
                        type = parsedValue;
                        break;
                    case "path":
                        path = parsedValue;
                        break;
                    case "value":
                        value = parsedValue;
                        break;
                    case "script":
                        script = parsedValue;
                        break;
                    case "valuefrom":
                        valueFrom = parsedValue;
                        break;
                    case "inputfrom":
                        inputFrom = parsedValue;
                        break;
                    case "outputto":
                        outputTo = parsedValue;
                        break;
                    case "successmessage":
                        successMessage = parsedValue;
                        break;
                    case "append":
                        append = parsedValue.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                                 parsedValue.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                                 parsedValue.Equals("yes", StringComparison.OrdinalIgnoreCase);
                        break;
                    case "separator":
                        separator = parsedValue
                            .Replace("\\r\\n", "\r\n", StringComparison.Ordinal)
                            .Replace("\\n", "\n", StringComparison.Ordinal)
                            .Replace("\\t", "\t", StringComparison.Ordinal);
                        break;
                    case "key":
                        key = parsedValue;
                        break;
                    case "scope":
                        scope = parsedValue;
                        break;
                    case "defaultvalue":
                        defaultValue = parsedValue
                            .Replace("\\r\\n", "\r\n", StringComparison.Ordinal)
                            .Replace("\\n", "\n", StringComparison.Ordinal)
                            .Replace("\\t", "\t", StringComparison.Ordinal);
                        break;
                }
            }

            actions.Add(new HostedViewActionDefinition(type, path, value, script, valueFrom, inputFrom, outputTo, successMessage, append, separator, key, scope, defaultValue));
        }

        return actions;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent == null)
        {
            yield break;
        }

        var childrenCount = VisualTreeHelper.GetChildrenCount(parent);
        for (var index = 0; index < childrenCount; index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T typed)
            {
                yield return typed;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private string ResolveDefaultHostedViewInput(HostedPluginSession session)
    {
        if (!string.IsNullOrWhiteSpace(HostedViewInput))
        {
            return HostedViewInput.Trim();
        }

        if (session.State.TryGetValue("input", out var stateInput) && !string.IsNullOrWhiteSpace(stateInput))
        {
            return stateInput;
        }

        var firstBoundTextarea = session.Definition.Components
            .FirstOrDefault(component =>
                string.Equals(component.Type, "textarea", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(component.Bind));
        return firstBoundTextarea != null && !string.IsNullOrWhiteSpace(firstBoundTextarea.Bind)
            ? GetHostedViewState(firstBoundTextarea.Bind)
            : string.Empty;
    }

    private void ApplyHostedViewWindowMetrics(HostedPluginViewDefinition definition)
    {
        var minWidth = NormalizeHostedViewDimension(definition.MinWindowWidth, _defaultMinWindowWidth);
        var minHeight = NormalizeHostedViewDimension(definition.MinWindowHeight, _defaultMinWindowHeight);
        var width = NormalizeHostedViewDimension(definition.WindowWidth, _defaultWindowWidth);
        var height = NormalizeHostedViewDimension(definition.WindowHeight, _defaultWindowHeight);

        MinWidth = minWidth;
        MinHeight = minHeight;
        Width = Math.Max(width, minWidth);
        Height = Math.Max(height, minHeight);
    }

    private void RestoreHostedViewWindowMetrics()
    {
        MinWidth = _defaultMinWindowWidth;
        MinHeight = _defaultMinWindowHeight;
        Width = Math.Max(_defaultWindowWidth, MinWidth);
        Height = Math.Max(_defaultWindowHeight, MinHeight);
    }

    private static double NormalizeHostedViewDimension(double? value, double fallback)
    {
        return value is > 0 ? value.Value : fallback;
    }

    private static string BuildMockTranslation(string input)
    {
        var trimmed = input.Trim();
        return
            $"[译文预览]{Environment.NewLine}{Environment.NewLine}" +
            $"EN: {trimmed}{Environment.NewLine}{Environment.NewLine}" +
            $"说明：当前是宿主内置的示例翻译输出，用来验证“双栏插件界面”协议。" +
            $"{Environment.NewLine}后续你可以把这个 actionType 替换成真正的翻译服务或脚本执行器。";
    }
}
