using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using WpfApplication = System.Windows.Application;

namespace OpenQuickHost;

public partial class QuestWindow : Window
{
    private int _currentQuestIndex = 0;
    private bool _isCompleting = false;

    private System.Windows.Threading.DispatcherTimer? _checkTimer;

    public QuestWindow()
    {
        InitializeComponent();
        App.EnableSilentLoading(this);

        Loaded += QuestWindow_Loaded;
        Closed += QuestWindow_Closed;
    }

    private void QuestWindow_Loaded(object sender, RoutedEventArgs e)
    {
        PositionAtTopRight();
        QuestService.BackpackOpened += OnBackpackOpened;
        RefreshStatus();

        _checkTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(1500)
        };
        _checkTimer.Tick += async (_, _) => await CheckCurrentQuestProgressAsync();
        _checkTimer.Start();
    }

    private void QuestWindow_Closed(object? sender, EventArgs e)
    {
        _checkTimer?.Stop();
        _checkTimer = null;
        QuestService.BackpackOpened -= OnBackpackOpened;
    }

    private void PositionAtTopRight()
    {
        try
        {
            var screen = System.Windows.Forms.Screen.PrimaryScreen;
            if (screen == null) return;

            var workingArea = screen.WorkingArea;
            var source = PresentationSource.FromVisual(this);
            double dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

            Left = (workingArea.Right / dpiX) - Width - 24;
            Top = (workingArea.Top / dpiY) + 24;
        }
        catch
        {
            Left = SystemParameters.WorkArea.Right - Width - 24;
            Top = SystemParameters.WorkArea.Top + 24;
        }
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try
            {
                DragMove();
            }
            catch
            {
                // Ignore fast click drag errors
            }
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async Task CheckCurrentQuestProgressAsync()
    {
        if (_isCompleting) return;

        var settings = AppSettingsStore.Load();
        if (_currentQuestIndex < 0 || _currentQuestIndex >= QuestService.AllQuests.Count) return;

        var currentQuest = QuestService.AllQuests[_currentQuestIndex];
        if (settings.CompletedQuestIds?.Contains(currentQuest.Id) == true) return;

        var mainWin = WpfApplication.Current.MainWindow as MainWindow;
        var allExtensions = mainWin?.GetExtensionsForSettings() ?? (IReadOnlyList<CommandItem>)Array.Empty<CommandItem>();

        if (QuestService.IsQuestCompleted(currentQuest, settings, allExtensions))
        {
            await TriggerQuestSuccessAsync(currentQuest, settings);
        }
    }

    private void OnBackpackOpened()
    {
        Dispatcher.InvokeAsync(async () =>
        {
            if (_isCompleting) return;

            var settings = AppSettingsStore.Load();
            if (_currentQuestIndex < 0 || _currentQuestIndex >= QuestService.AllQuests.Count) return;

            var currentQuest = QuestService.AllQuests[_currentQuestIndex];
            if (currentQuest.Id == QuestService.OpenBackpackQuestId &&
                settings.CompletedQuestIds?.Contains(currentQuest.Id) != true)
            {
                await TriggerQuestSuccessAsync(currentQuest, settings);
            }
        });
    }

    public void RefreshStatus()
    {
        if (_isCompleting) return;

        var settings = AppSettingsStore.Load();
        
        // 定位到第一个未完成的任务
        var nextIncomplete = -1;
        for (var i = 0; i < QuestService.AllQuests.Count; i++)
        {
            if (settings.CompletedQuestIds?.Contains(QuestService.AllQuests[i].Id) != true)
            {
                nextIncomplete = i;
                break;
            }
        }

        if (nextIncomplete == -1)
        {
            // 全部通关
            TxtStatusIcon.Text = "👑";
            TxtQuestPrompt.Text = "全部主线试炼已达成！";
            TxtQuestPrompt.Foreground = new SolidColorBrush(MediaColor.FromRgb(0x34, 0xD3, 0x99));
            CardBorder.BorderBrush = new SolidColorBrush(MediaColor.FromArgb(0x66, 0x34, 0xD3, 0x99));
            CardShadow.Color = MediaColor.FromRgb(0x05, 0x96, 0x69);
            return;
        }

        _currentQuestIndex = nextIncomplete;
        var quest = QuestService.AllQuests[_currentQuestIndex];

        // 默认显示任务提示语
        TxtStatusIcon.Text = _currentQuestIndex == 0 ? "🎒" : "🎯";
        TxtQuestPrompt.Text = quest.ShortPrompt;
        TxtQuestPrompt.Foreground = new SolidColorBrush(MediaColor.FromRgb(0xF0, 0xF0, 0xF5));
        CardBorder.BorderBrush = new SolidColorBrush(MediaColor.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
        CardShadow.Color = MediaColor.FromRgb(0x00, 0x00, 0x00);
    }

    private async Task TriggerQuestSuccessAsync(QuestDefinition quest, AppSettings settings)
    {
        _isCompleting = true;

        // 1. 记录完成
        settings.CompletedQuestIds ??= [];
        if (!settings.CompletedQuestIds.Contains(quest.Id))
        {
            settings.CompletedQuestIds.Add(quest.Id);
        }

        settings.UnlockedBadges ??= [];
        if (!settings.UnlockedBadges.Contains(quest.RewardBadge))
        {
            settings.UnlockedBadges.Add(quest.RewardBadge);
        }

        settings.AchievementPoints += quest.RewardPoints;
        AppSettingsStore.Save(settings);

        // 2. 加分视觉效果呈现
        TxtStatusIcon.Text = "✨";
        TxtQuestPrompt.Text = $"✓ 任务完成！ +{quest.RewardPoints}分";
        TxtQuestPrompt.Foreground = new SolidColorBrush(MediaColor.FromRgb(0x34, 0xD3, 0x99));
        CardBorder.BorderBrush = new SolidColorBrush(MediaColor.FromRgb(0x34, 0xD3, 0x99));
        CardShadow.Color = MediaColor.FromRgb(0x10, 0xB9, 0x81);
        CardShadow.Opacity = 0.9;
        CardShadow.BlurRadius = 26;

        // 3. 等待 3 秒后展示下一个任务
        await Task.Delay(3000);

        _isCompleting = false;
        RefreshStatus();
    }
}
