using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using WpfApplication = System.Windows.Application;
using WpfBrush = System.Windows.Media.Brush;
using WpfButton = System.Windows.Controls.Button;
using WpfCursors = System.Windows.Input.Cursors;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfOrientation = System.Windows.Controls.Orientation;

namespace OpenQuickHost;

public partial class QuestWindow : Window
{
    private int _currentQuestIndex = 0;
    private int? _explicitQuestIndex = null;
    private bool _isCompleting = false;
    private bool _isWaitingUnpin = false;
    private bool _isDrawerOpen = false;
    private QuestMouseGuideWindow? _mouseGuideWindow;

    public QuestWindow()
    {
        InitializeComponent();

        // 浮窗初始定位在屏幕正上方居中
        Left = Math.Max(0, (SystemParameters.PrimaryScreenWidth - Width) / 2);
        Top = 32;

        QuestService.BackpackOpened += OnBackpackOpened;
        QuestService.BackpackPinChanged += OnBackpackPinChanged;
        QuestService.FileDroppedToBackpack += OnFileDroppedToBackpack;
        QuestService.BackpackItemClicked += OnBackpackItemClicked;

        Loaded += QuestWindow_Loaded;
        Closed += QuestWindow_Closed;
    }

    private void QuestWindow_Loaded(object sender, RoutedEventArgs e)
    {
        HostAssets.AppendLog("[QuestWindow] Loaded - activating floating quest HUD.");
        QuestService.IsQuestWindowActive = true;
        RefreshStatus();
    }

    private void QuestWindow_Closed(object? sender, EventArgs e)
    {
        HostAssets.AppendLog("[QuestWindow] Closed - deactivating floating quest HUD.");
        QuestService.IsQuestWindowActive = false;
        QuestService.ActiveQuestId = null;

        CloseMouseGuideWindow();
        QuestService.BackpackOpened -= OnBackpackOpened;
        QuestService.BackpackPinChanged -= OnBackpackPinChanged;
        QuestService.FileDroppedToBackpack -= OnFileDroppedToBackpack;
        QuestService.BackpackItemClicked -= OnBackpackItemClicked;
        QuestService.NotifyQuestStateChanged();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try
            {
                if (IsLoaded && IsVisible)
                {
                    DragMove();
                }
            }
            catch (Exception ex)
            {
                HostAssets.AppendLog($"[QuestWindow] DragMove exception ignored: {ex.Message}");
            }
        }
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            HostAssets.AppendLog("[QuestWindow] ESC pressed - closing floating HUD.");
            Close();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        HostAssets.AppendLog("[QuestWindow] Close button clicked.");
        Close();
    }

    private void BtnToggleDrawer_Click(object sender, RoutedEventArgs e)
    {
        _isDrawerOpen = !_isDrawerOpen;
        DrawerPanel.Visibility = _isDrawerOpen ? Visibility.Visible : Visibility.Collapsed;
        if (_isDrawerOpen)
        {
            UpdateDrawerContent();
        }
    }

    /// <summary>
    /// 重置当前正在进行的关卡 (方便反复调试与练习当前任务)
    /// </summary>
    private void BtnResetCurrentQuest_Click(object sender, RoutedEventArgs e)
    {
        HostAssets.AppendLog($"[QuestWindow] Reset current quest clicked: index={_currentQuestIndex}.");
        var settings = AppSettingsStore.Load();
        if (_currentQuestIndex >= 0 && _currentQuestIndex < QuestService.AllQuests.Count)
        {
            var quest = QuestService.AllQuests[_currentQuestIndex];
            settings.CompletedQuestIds?.Remove(quest.Id);
            settings.UnlockedBadges?.Remove(quest.RewardBadge);
            AppSettingsStore.Save(settings);
        }

        _isCompleting = false;
        RefreshStatus();
    }

    /// <summary>
    /// 重置整个任务系统 (清空全部进度与积分)
    /// </summary>
    private void BtnResetAllQuests_Click(object sender, RoutedEventArgs e)
    {
        HostAssets.AppendLog("[QuestWindow] Reset all quests clicked.");
        var settings = AppSettingsStore.Load();
        settings.CompletedQuestIds?.Clear();
        settings.UnlockedBadges?.Clear();
        settings.AchievementPoints = 0;
        settings.HasOpenedBackpack = false;
        AppSettingsStore.Save(settings);

        _currentQuestIndex = 0;
        _explicitQuestIndex = 0;
        _isCompleting = false;
        _isWaitingUnpin = false;
        QuestService.IsWaitingUnpin = false;
        RefreshStatus();
    }

    private void BtnNextQuest_Click(object sender, RoutedEventArgs e)
    {
        HostAssets.AppendLog($"[QuestWindow] Next quest clicked. CurrentIndex was {_currentQuestIndex}.");
        _isCompleting = false;
        _isWaitingUnpin = false;
        QuestService.IsWaitingUnpin = false;
        _explicitQuestIndex = null; // 解锁自动寻关
        if (_currentQuestIndex < QuestService.AllQuests.Count - 1)
        {
            _currentQuestIndex++;
            _explicitQuestIndex = _currentQuestIndex;
        }
        RefreshStatus();
    }

    private void OnBackpackOpened()
    {
        Dispatcher.InvokeAsync(async () =>
        {
            HostAssets.AppendLog($"[QuestWindow] OnBackpackOpened received. _currentQuestIndex={_currentQuestIndex}, _isCompleting={_isCompleting}.");
            if (_isCompleting) return;

            var settings = AppSettingsStore.Load();
            if (_currentQuestIndex < 0 || _currentQuestIndex >= QuestService.AllQuests.Count) return;

            var currentQuest = QuestService.AllQuests[_currentQuestIndex];
            
            // 第一关：现场收到呼出背包事件即通关
            if (currentQuest.Id == QuestService.OpenBackpackQuestId)
            {
                HostAssets.AppendLog("[QuestWindow] Quest 1 completed via BackpackOpened!");
                await TriggerQuestSuccessAsync(currentQuest, settings);
                return;
            }

            // 第二关：呼出背包后关闭中央鼠标引导，进入图钉置顶引导
            if (currentQuest.Id == QuestService.DragFileQuestId)
            {
                HostAssets.AppendLog("[QuestWindow] Quest 2 backpack opened -> close mouse guide and guide pin.");
                CloseMouseGuideWindow();
                if (!_isWaitingUnpin)
                {
                    SetHighlightedPrompt(
                        ("点击背包右上角 ", "#FFF0F0F5", false),
                        ("📌 图钉", "#FFFDE047", true),
                        (" 置顶", "#FFF0F0F5", false));
                }
                QuestService.NotifyQuestStateChanged();
            }

            // 第三关：呼出背包后关闭中央鼠标引导，进入点击打开示例文件引导
            if (currentQuest.Id == QuestService.OpenSampleFileQuestId)
            {
                HostAssets.AppendLog("[QuestWindow] Quest 3 backpack opened -> guide click sample file.");
                CloseMouseGuideWindow();
                SetHighlightedPrompt(
                    ("找到并点击背包中的 ", "#FFF0F0F5", false),
                    ("【示例文件】", "#FFFDE047", true),
                    (" 打开它", "#FFF0F0F5", false));
            }
        });
    }

    private void OnBackpackItemClicked(string itemName)
    {
        Dispatcher.InvokeAsync(async () =>
        {
            HostAssets.AppendLog($"[QuestWindow] OnBackpackItemClicked: itemName='{itemName}', _currentQuestIndex={_currentQuestIndex}, _isCompleting={_isCompleting}.");
            if (_isCompleting) return;

            var settings = AppSettingsStore.Load();
            if (_currentQuestIndex < 0 || _currentQuestIndex >= QuestService.AllQuests.Count) return;

            var currentQuest = QuestService.AllQuests[_currentQuestIndex];
            if (currentQuest.Id == QuestService.OpenSampleFileQuestId &&
                itemName.Contains("示例文件", StringComparison.OrdinalIgnoreCase))
            {
                HostAssets.AppendLog("[QuestWindow] Quest 3 completed via clicking sample file in backpack!");
                await TriggerQuestSuccessAsync(currentQuest, settings);
            }
        });
    }

    private void OnBackpackPinChanged(bool isPinned)
    {
        Dispatcher.InvokeAsync(async () =>
        {
            HostAssets.AppendLog($"[QuestWindow] OnBackpackPinChanged: isPinned={isPinned}, _currentQuestIndex={_currentQuestIndex}, _isWaitingUnpin={_isWaitingUnpin}.");
            if (_isCompleting) return;

            if (_currentQuestIndex == 1)
            {
                if (isPinned && !_isWaitingUnpin)
                {
                    SetHighlightedPrompt(
                        ("将桌面 ", "#FFF0F0F5", false),
                        ("【示例文件】", "#FFFDE047", true),
                        (" 拖入发光的 ", "#FFF0F0F5", false),
                        ("空格子", "#FFFDE047", true));
                }
                else if (!isPinned && _isWaitingUnpin)
                {
                    // 第二关最后一步：用户成功取消置顶 -> 正式达成通关！
                    _isWaitingUnpin = false;
                    QuestService.IsWaitingUnpin = false;
                    var settings = AppSettingsStore.Load();
                    var dragQuest = QuestService.DragFileQuest;
                    HostAssets.AppendLog("[QuestWindow] Quest 2 completed after unpinning!");
                    await TriggerQuestSuccessAsync(dragQuest, settings);
                }
            }
        });
    }

    private void OnFileDroppedToBackpack()
    {
        Dispatcher.InvokeAsync(() =>
        {
            HostAssets.AppendLog($"[QuestWindow] OnFileDroppedToBackpack received! _currentQuestIndex={_currentQuestIndex}, _isCompleting={_isCompleting}.");
            if (_isCompleting) return;

            if (_currentQuestIndex == 1)
            {
                // 第二关：文件已成功拖入背包，引导用户再次点击图钉取消置顶以完成闭环！
                _isWaitingUnpin = true;
                QuestService.IsWaitingUnpin = true;
                HostAssets.AppendLog("[QuestWindow] Quest 2 file dropped -> now guide user to unpin.");

                SetHighlightedPrompt(
                    ("太棒了！再次点击右上角 ", "#FFF0F0F5", false),
                    ("📌 图钉", "#FFFDE047", true),
                    (" 取消置顶，恢复自动隐藏", "#FFF0F0F5", false));

                QuestService.NotifyQuestStateChanged();
            }
        });
    }

    public void RefreshStatus()
    {
        if (_isCompleting) return;

        var settings = AppSettingsStore.Load();
        
        if (_explicitQuestIndex.HasValue)
        {
            _currentQuestIndex = Math.Clamp(_explicitQuestIndex.Value, 0, QuestService.AllQuests.Count - 1);
        }
        else
        {
            // 查找第一个未完成的任务
            var nextIncomplete = -1;
            for (var i = 0; i < QuestService.AllQuests.Count; i++)
            {
                if (settings.CompletedQuestIds?.Contains(QuestService.AllQuests[i].Id) != true)
                {
                    nextIncomplete = i;
                    break;
                }
            }

            HostAssets.AppendLog($"[QuestWindow] RefreshStatus: nextIncomplete={nextIncomplete}, currentQuestIndex={_currentQuestIndex}.");

            if (nextIncomplete == -1)
            {
                // 全部通关状态
                CloseMouseGuideWindow();
                ImgYanziMascot.Visibility = Visibility.Visible;
                SuccessCheckBadge.Visibility = Visibility.Collapsed;
                BtnNextQuest.Visibility = Visibility.Collapsed;

                SetHighlightedPrompt(("👑 全部主线试炼已达成！", "#FFFFFFFF", true));

                CardBorder.Background = new SolidColorBrush(MediaColor.FromRgb(0x05, 0x96, 0x69));
                CardBorder.BorderBrush = new SolidColorBrush(MediaColor.FromRgb(0x34, 0xD3, 0x99));
                CardShadow.Color = MediaColor.FromRgb(0x10, 0xB9, 0x81);
                if (_isDrawerOpen) UpdateDrawerContent();
                return;
            }

            _currentQuestIndex = nextIncomplete;
        }

        var quest = QuestService.AllQuests[_currentQuestIndex];
        QuestService.ActiveQuestId = quest.Id;

        // 检查当前任务是否已满足完成条件 (例如背包中已存在拖入的文件) - 仅在非显式练习模式下自动完成
        var mainWin = WpfApplication.Current.MainWindow as MainWindow;
        var allExtensions = mainWin?.GetExtensionsForSettings() ?? (IReadOnlyList<CommandItem>)Array.Empty<CommandItem>();
        if (!_explicitQuestIndex.HasValue && quest.Id != QuestService.OpenBackpackQuestId && QuestService.IsQuestCompleted(quest, settings, allExtensions))
        {
            HostAssets.AppendLog($"[QuestWindow] Quest {quest.Id} fulfilled on RefreshStatus -> trigger success!");
            _ = TriggerQuestSuccessAsync(quest, settings);
            if (_isDrawerOpen) UpdateDrawerContent();
            return;
        }

        // 复原未完成状态 UI
        ImgYanziMascot.Visibility = Visibility.Visible;
        SuccessCheckBadge.Visibility = Visibility.Collapsed;
        BtnNextQuest.Visibility = Visibility.Collapsed;

        if (_currentQuestIndex == 0)
        {
            // 第一关：启动屏幕中央鼠标右键长按引导窗口，黄色强调【右键】和【背包】
            EnsureMouseGuideWindow();
            SetHighlightedPrompt(
                ("长按鼠标 ", "#FFF0F0F5", false),
                ("右键", "#FFFDE047", true),
                (" 召唤 ", "#FFF0F0F5", false),
                ("背包", "#FFFDE047", true));
        }
        else if (_currentQuestIndex == 1)
        {
            // 第二关第一步：退到桌面、准备示例文件并启动屏幕中央鼠标引导
            MinimizeAllWindowsAndShowDesktop();
            EnsureSampleFileOnDesktop();
            EnsureMouseGuideWindow();
            SetHighlightedPrompt(
                ("长按鼠标 ", "#FFF0F0F5", false),
                ("右键", "#FFFDE047", true),
                (" 召唤 ", "#FFF0F0F5", false),
                ("背包", "#FFFDE047", true));
        }
        else if (_currentQuestIndex == 2)
        {
            // 第三关第一步：启动屏幕中央鼠标长按引导，呼出背包
            EnsureMouseGuideWindow();
            SetHighlightedPrompt(
                ("长按鼠标 ", "#FFF0F0F5", false),
                ("右键", "#FFFDE047", true),
                (" 召唤 ", "#FFF0F0F5", false),
                ("背包", "#FFFDE047", true));
        }
        else
        {
            CloseMouseGuideWindow();
            SetHighlightedPrompt((quest.ShortPrompt, "#FFF0F0F5", false));
        }

        CardBorder.Background = new SolidColorBrush(MediaColor.FromArgb(0xF0, 0x14, 0x14, 0x1E));
        CardBorder.BorderBrush = new SolidColorBrush(MediaColor.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
        CardShadow.Color = MediaColor.FromRgb(0x00, 0x00, 0x00);
        CardShadow.Opacity = 0.7;

        QuestService.NotifyQuestStateChanged();
        if (_isDrawerOpen) UpdateDrawerContent();
    }

    /// <summary>
    /// 设置带有黄色高亮强调的富文本提示
    /// </summary>
    private void SetHighlightedPrompt(params (string text, string hexColor, bool isBold)[] spans)
    {
        TxtQuestPrompt.Inlines.Clear();
        foreach (var (text, hexColor, isBold) in spans)
        {
            var run = new Run(text)
            {
                Foreground = new BrushConverter().ConvertFromString(hexColor) as WpfBrush ?? MediaBrushes.White,
                FontWeight = isBold ? FontWeights.Bold : FontWeights.SemiBold
            };
            TxtQuestPrompt.Inlines.Add(run);
        }
    }

    /// <summary>
    /// 更新清单抽屉总览数据 (总分、等级称号、进度条与关卡条目)
    /// </summary>
    private void UpdateDrawerContent()
    {
        var settings = AppSettingsStore.Load();
        var totalPoints = settings.AchievementPoints;
        const int maxPoints = 320;

        // 1. 等级称号计算
        var levelTitle = totalPoints switch
        {
            >= 250 => "Lv.5 燕子传奇宗师",
            >= 170 => "Lv.4 极客先锋",
            >= 100 => "Lv.3 效率大师",
            >= 50 => "Lv.2 随身行者",
            _ => "Lv.1 探索学徒"
        };

        TxtUserLevelTitle.Text = levelTitle;
        TxtTotalScoreSummary.Text = $"🌟 {totalPoints} / {maxPoints} 分";

        // 2. 进度条填充宽度 (最大 528px)
        var targetWidth = Math.Max(16.0, Math.Min(528.0, 528.0 * totalPoints / maxPoints));
        ProgressBarFill.Width = targetWidth;

        // 3. 动态构建关卡清单条目
        QuestItemsListContainer.Children.Clear();
        for (var i = 0; i < QuestService.AllQuests.Count; i++)
        {
            var quest = QuestService.AllQuests[i];
            var isCompleted = settings.CompletedQuestIds?.Contains(quest.Id) == true;
            var isCurrent = (i == _currentQuestIndex);
            var itemIndex = i;

            var itemBtn = new WpfButton
            {
                Margin = new Thickness(0, 0, 0, 6),
                Padding = new Thickness(0),
                Background = MediaBrushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = WpfCursors.Hand,
                ToolTip = "点击直接跳转练习本关",
                Tag = itemIndex,
                HorizontalContentAlignment = WpfHorizontalAlignment.Stretch
            };

            var template = new ControlTemplate(typeof(WpfButton));
            var factory = new FrameworkElementFactory(typeof(ContentPresenter));
            template.VisualTree = factory;
            itemBtn.Template = template;

            var itemBorder = new Border
            {
                Padding = new Thickness(12, 8, 12, 8),
                CornerRadius = new CornerRadius(10),
                Background = isCurrent
                    ? new SolidColorBrush(MediaColor.FromArgb(0x33, 0xF5, 0x9E, 0x0B))
                    : (isCompleted ? new SolidColorBrush(MediaColor.FromArgb(0x22, 0x10, 0xB9, 0x81)) : new SolidColorBrush(MediaColor.FromArgb(0x14, 0xFF, 0xFF, 0xFF))),
                BorderBrush = isCurrent
                    ? new SolidColorBrush(MediaColor.FromArgb(0x80, 0xF5, 0x9E, 0x0B))
                    : (isCompleted ? new SolidColorBrush(MediaColor.FromArgb(0x40, 0x10, 0xB9, 0x81)) : new SolidColorBrush(MediaColor.FromArgb(0x1A, 0xFF, 0xFF, 0xFF))),
                BorderThickness = new Thickness(1)
            };

            itemBtn.Click += (s, _) =>
            {
                if (s is WpfButton b && b.Tag is int targetIdx)
                {
                    HostAssets.AppendLog($"[QuestWindow] Drawer quest item clicked: targetIdx={targetIdx}.");
                    _explicitQuestIndex = targetIdx;
                    _currentQuestIndex = targetIdx;
                    _isCompleting = false;
                    _isWaitingUnpin = false;
                    QuestService.IsWaitingUnpin = false;
                    _isDrawerOpen = false;
                    DrawerPanel.Visibility = Visibility.Collapsed;
                    RefreshStatus();
                }
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // 左侧：序号 + 徽章称号 + 任务简述
            var leftStack = new StackPanel { Orientation = WpfOrientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            leftStack.Children.Add(new TextBlock
            {
                Text = $"{i + 1}. {quest.RewardBadge}",
                Foreground = isCurrent ? new SolidColorBrush(MediaColor.FromRgb(0xFD, 0xE0, 0x47)) : (isCompleted ? new SolidColorBrush(MediaColor.FromRgb(0x34, 0xD3, 0x99)) : new SolidColorBrush(MediaColor.FromRgb(0xD0, 0xD0, 0xDF))),
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                Margin = new Thickness(0, 0, 10, 0)
            });
            leftStack.Children.Add(new TextBlock
            {
                Text = quest.ShortPrompt,
                Foreground = new SolidColorBrush(MediaColor.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            });

            Grid.SetColumn(leftStack, 0);
            grid.Children.Add(leftStack);

            // 右侧：状态指示 (已达成 ✓ / 进行中 ⚡ / 待挑战) + 奖励分数
            var rightStack = new StackPanel { Orientation = WpfOrientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (isCompleted)
            {
                rightStack.Children.Add(new TextBlock
                {
                    Text = "✓ 已达成",
                    Foreground = new SolidColorBrush(MediaColor.FromRgb(0x34, 0xD3, 0x99)),
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 8, 0)
                });
            }
            else if (isCurrent)
            {
                rightStack.Children.Add(new TextBlock
                {
                    Text = "⚡ 进行中",
                    Foreground = new SolidColorBrush(MediaColor.FromRgb(0xFD, 0xE0, 0x47)),
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 8, 0)
                });
            }

            rightStack.Children.Add(new TextBlock
            {
                Text = $"+{quest.RewardPoints}分",
                Foreground = new SolidColorBrush(MediaColor.FromRgb(0xF5, 0x9E, 0x0B)),
                FontSize = 12,
                FontWeight = FontWeights.Bold
            });

            Grid.SetColumn(rightStack, 1);
            grid.Children.Add(rightStack);

            itemBorder.Child = grid;
            itemBtn.Content = itemBorder;
            QuestItemsListContainer.Children.Add(itemBtn);
        }
    }

    private void EnsureMouseGuideWindow()
    {
        try
        {
            if (_mouseGuideWindow == null)
            {
                _mouseGuideWindow = new QuestMouseGuideWindow();
                _mouseGuideWindow.Show();
                HostAssets.AppendLog("[QuestWindow] Opened screen-center MouseGuideWindow.");
            }
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"[QuestWindow] EnsureMouseGuideWindow failed: {ex.Message}");
        }
    }

    private void CloseMouseGuideWindow()
    {
        try
        {
            if (_mouseGuideWindow != null)
            {
                _mouseGuideWindow.Close();
                _mouseGuideWindow = null;
                HostAssets.AppendLog("[QuestWindow] Closed screen-center MouseGuideWindow.");
            }
        }
        catch
        {
            // Ignore
        }
    }

    private static void MinimizeAllWindowsAndShowDesktop()
    {
        try
        {
            HostAssets.AppendLog("[QuestWindow] Minimizing all windows to show desktop.");
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType != null)
            {
                dynamic? shell = Activator.CreateInstance(shellType);
                shell?.MinimizeAll();
            }
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"[QuestWindow] MinimizeAll failed: {ex.Message}");
        }
    }

    private static void EnsureSampleFileOnDesktop()
    {
        try
        {
            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (!Directory.Exists(desktopPath)) return;

            var filePath = Path.Combine(desktopPath, "【示例文件】拖我进背包.txt");
            if (!File.Exists(filePath))
            {
                File.WriteAllText(filePath, "恭喜你！将任意常用文件直接拖入背包，即可秒级一键唤醒打开！", System.Text.Encoding.UTF8);
                HostAssets.AppendLog($"[QuestWindow] Created sample file at: {filePath}");
            }
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"[QuestWindow] EnsureSampleFile failed: {ex.Message}");
        }
    }

    private Task TriggerQuestSuccessAsync(QuestDefinition quest, AppSettings settings)
    {
        _isCompleting = true;

        // 1. 记录完成与积分 (只在首次通关时增加积分)
        settings.CompletedQuestIds ??= [];
        if (!settings.CompletedQuestIds.Contains(quest.Id))
        {
            settings.CompletedQuestIds.Add(quest.Id);
            settings.AchievementPoints += quest.RewardPoints;
        }

        settings.UnlockedBadges ??= [];
        if (!settings.UnlockedBadges.Contains(quest.RewardBadge))
        {
            settings.UnlockedBadges.Add(quest.RewardBadge);
        }

        AppSettingsStore.Save(settings);

        // 2. 视觉通关动效：关闭中央鼠标引导 + 绿底白字 + 打勾徽标内置 + 下一关按钮显现
        CloseMouseGuideWindow();

        ImgYanziMascot.Visibility = Visibility.Collapsed;
        SuccessCheckBadge.Visibility = Visibility.Visible;
        BtnNextQuest.Visibility = Visibility.Visible;

        // 纯正文本：任务完成！ (白色) + +30分 (亮黄色高亮)，无多余勾
        SetHighlightedPrompt(
            ("任务完成！", "#FFFFFFFF", true),
            ($" +{quest.RewardPoints}分", "#FFFDE047", true));

        CardBorder.Background = new SolidColorBrush(MediaColor.FromRgb(0x05, 0x96, 0x69));
        CardBorder.BorderBrush = new SolidColorBrush(MediaColor.FromRgb(0x34, 0xD3, 0x99));
        CardShadow.Color = MediaColor.FromRgb(0x10, 0xB9, 0x81);
        CardShadow.Opacity = 0.95;
        CardShadow.BlurRadius = 24;

        QuestService.NotifyQuestStateChanged();
        if (_isDrawerOpen) UpdateDrawerContent();
        return Task.CompletedTask;
    }
}
