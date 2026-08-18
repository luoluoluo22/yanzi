using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using WpfApplication = System.Windows.Application;
using WpfMessageBox = System.Windows.MessageBox;

namespace OpenQuickHost;

public partial class QuestWindow : Window
{
    public QuestWindow()
    {
        InitializeComponent();
        App.EnableSilentLoading(this);
        Loaded += (_, _) => RefreshStatus();
        Activated += (_, _) => RefreshStatus();
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
                // Ignore drag exceptions during fast clicks
            }
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    public void RefreshStatus()
    {
        var settings = AppSettingsStore.Load();
        var mainWin = WpfApplication.Current.MainWindow as MainWindow;
        var allExtensions = mainWin?.GetExtensionsForSettings() ?? (IReadOnlyList<CommandItem>)Array.Empty<CommandItem>();

        // 1. 刷新等级与成就分
        var (level, title, curExp, maxExp) = QuestService.GetPlayerLevel(settings.AchievementPoints);
        TxtLevelName.Text = $"Lv.{level} {title}";
        TxtPoints.Text = $"{settings.AchievementPoints} 分";

        // 2. 检查是否已通关该任务
        bool isAlreadyCompleted = settings.CompletedQuestIds?.Contains(QuestService.CalculatorQuestId) == true;

        if (isAlreadyCompleted)
        {
            BadgeCompletedMark.Visibility = Visibility.Visible;
            IconStep1.Text = "🟢";
            IconStep2.Text = "🟢";
            TxtBadgeCalc.Foreground = MediaBrushes.Gold;
            BadgeCalcBorder.BorderBrush = MediaBrushes.Gold;
            TxtStatusFeedback.Text = "🎉 本关已通关达成！已成功将计算器放进秒板，并获得「指尖神算」徽章与 +50 成就分！";
            TxtStatusFeedback.Foreground = new SolidColorBrush(MediaColor.FromRgb(0x34, 0xD3, 0x99));
            FeedbackBorder.Background = new SolidColorBrush(MediaColor.FromArgb(0x26, 0x34, 0xD3, 0x99));
            FeedbackBorder.BorderBrush = new SolidColorBrush(MediaColor.FromArgb(0x4D, 0x34, 0xD3, 0x99));
            BtnVerify.Content = "✓ 已达成通关";
            BtnVerify.IsEnabled = false;
            BtnVerify.Opacity = 0.6;
            return;
        }

        // 3. 实时检测当前任务进展
        var result = QuestService.CheckCalculatorQuest(settings, allExtensions);
        IconStep1.Text = result.Step1Completed ? "🟢" : "⚪";
        IconStep2.Text = result.Step2Completed ? "🟢" : "⚪";

        if (result.Step1Completed && result.Step2Completed)
        {
            TxtStatusFeedback.Text = "🎉 任务步骤已全部达成！点击右下角「立即检测完成情况」领取通关奖励！";
            TxtStatusFeedback.Foreground = new SolidColorBrush(MediaColor.FromRgb(0x34, 0xD3, 0x99));
            FeedbackBorder.Background = new SolidColorBrush(MediaColor.FromArgb(0x26, 0x34, 0xD3, 0x99));
            FeedbackBorder.BorderBrush = new SolidColorBrush(MediaColor.FromArgb(0x4D, 0x34, 0xD3, 0x99));
        }
        else if (result.Step1Completed)
        {
            TxtStatusFeedback.Text = "👉 计算器扩展已就绪！请点击「打开鼠标面板」，把计算器拖拽或添加进任意格子。";
            TxtStatusFeedback.Foreground = new SolidColorBrush(MediaColor.FromRgb(0xF5, 0x9E, 0x0B));
            FeedbackBorder.Background = new SolidColorBrush(MediaColor.FromArgb(0x26, 0xF5, 0x9E, 0x0B));
            FeedbackBorder.BorderBrush = new SolidColorBrush(MediaColor.FromArgb(0x4D, 0xF5, 0x9E, 0x0B));
        }
        else
        {
            TxtStatusFeedback.Text = "👉 点击「帮我预填并创建扩展」，快速生成你的第一个自动化工具！";
            TxtStatusFeedback.Foreground = new SolidColorBrush(MediaColor.FromRgb(0x93, 0xC5, 0xFD));
            FeedbackBorder.Background = new SolidColorBrush(MediaColor.FromArgb(0x26, 0x3B, 0x82, 0xF6));
            FeedbackBorder.BorderBrush = new SolidColorBrush(MediaColor.FromArgb(0x4D, 0x3B, 0x82, 0xF6));
        }
    }

    private void BtnAutoCreate_Click(object sender, RoutedEventArgs e)
    {
        var mainWin = WpfApplication.Current.MainWindow as MainWindow;
        if (mainWin != null)
        {
            mainWin.OpenAddExtensionWithInitialJson(QuestService.CalculatorQuest.InitialJsonTemplate, this);
            RefreshStatus();
        }
    }

    private void BtnOpenPanel_Click(object sender, RoutedEventArgs e)
    {
        var mainWin = WpfApplication.Current.MainWindow as MainWindow;
        mainWin?.ShowMousePanel();
    }

    private void BtnVerify_Click(object sender, RoutedEventArgs e)
    {
        var settings = AppSettingsStore.Load();
        var mainWin = WpfApplication.Current.MainWindow as MainWindow;
        var allExtensions = mainWin?.GetExtensionsForSettings() ?? (IReadOnlyList<CommandItem>)Array.Empty<CommandItem>();

        var result = QuestService.CheckCalculatorQuest(settings, allExtensions);
        if (result.IsSuccess)
        {
            settings.CompletedQuestIds ??= [];
            if (!settings.CompletedQuestIds.Contains(QuestService.CalculatorQuestId))
            {
                settings.CompletedQuestIds.Add(QuestService.CalculatorQuestId);
            }

            settings.UnlockedBadges ??= [];
            if (!settings.UnlockedBadges.Contains(QuestService.CalculatorQuest.RewardBadge))
            {
                settings.UnlockedBadges.Add(QuestService.CalculatorQuest.RewardBadge);
            }

            settings.AchievementPoints += QuestService.CalculatorQuest.RewardPoints;
            AppSettingsStore.Save(settings);

            RefreshStatus();

            WpfMessageBox.Show(
                this,
                $"🎉 恭喜通关新手任务！\n\n" +
                $"🌟 获得奖励: +{QuestService.CalculatorQuest.RewardPoints} 成就分\n" +
                $"🏅 解锁勋章: {QuestService.CalculatorQuest.RewardBadge}\n\n" +
                $"你已成功制作并配置了计算器秒板工具，日常工作按住鼠标右键即可秒开计算器！",
                "任务达成 · 燕子工坊",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }
        else
        {
            RefreshStatus();
            WpfMessageBox.Show(
                this,
                result.Message,
                "任务检测提示",
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
        }
    }
}
