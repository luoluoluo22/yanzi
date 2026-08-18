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
    private int _currentQuestIndex = 0;

    public QuestWindow()
    {
        InitializeComponent();
        App.EnableSilentLoading(this);

        // 默认定位到第一个未完成的任务
        var settings = AppSettingsStore.Load();
        for (var i = 0; i < QuestService.AllQuests.Count; i++)
        {
            if (settings.CompletedQuestIds?.Contains(QuestService.AllQuests[i].Id) != true)
            {
                _currentQuestIndex = i;
                break;
            }
        }

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

        // 2. 刷新徽章墙点亮状态
        RefreshBadgesWall(settings);

        // 3. 边界保护
        if (_currentQuestIndex < 0) _currentQuestIndex = 0;
        if (_currentQuestIndex >= QuestService.AllQuests.Count) _currentQuestIndex = QuestService.AllQuests.Count - 1;

        var quest = QuestService.AllQuests[_currentQuestIndex];
        TxtQuestProgress.Text = $"第 {_currentQuestIndex + 1} / {QuestService.AllQuests.Count} 关";
        BtnPrevQuest.Visibility = _currentQuestIndex > 0 ? Visibility.Visible : Visibility.Collapsed;

        // 4. 填充当前任务信息
        TxtQuestCategory.Text = quest.Category;
        TxtQuestTitle.Text = quest.Title;
        TxtQuestDesc.Text = quest.Description;
        TxtStep1.Text = $"步骤 1：{quest.Step1Description}";
        TxtRewardPoints.Text = $"+{quest.RewardPoints} 成就分";
        TxtRewardBadge.Text = $"{quest.RewardBadge} 徽章";

        if (quest.HasStep2)
        {
            Step2Row.Visibility = Visibility.Visible;
            TxtStep2.Text = $"步骤 2：{quest.Step2Description}";
        }
        else
        {
            Step2Row.Visibility = Visibility.Collapsed;
        }

        BtnActionPrimary.Content = !string.IsNullOrWhiteSpace(quest.ActionButtonText) ? quest.ActionButtonText : "⚡ 快速配置";

        // 5. 检测是否已达成
        bool isAlreadyCompleted = settings.CompletedQuestIds?.Contains(quest.Id) == true;

        if (isAlreadyCompleted)
        {
            BadgeCompletedMark.Visibility = Visibility.Visible;
            IconStep1.Text = "🟢";
            IconStep2.Text = "🟢";
            TxtStatusFeedback.Text = $"🎉 本关已通关达成！已解锁徽章「{quest.RewardBadge}」并获得 +{quest.RewardPoints} 成就分！";
            TxtStatusFeedback.Foreground = new SolidColorBrush(MediaColor.FromRgb(0x34, 0xD3, 0x99));
            FeedbackBorder.Background = new SolidColorBrush(MediaColor.FromArgb(0x26, 0x34, 0xD3, 0x99));
            FeedbackBorder.BorderBrush = new SolidColorBrush(MediaColor.FromArgb(0x4D, 0x34, 0xD3, 0x99));
            BtnVerify.Content = "✓ 本关已通关";
            BtnVerify.IsEnabled = false;
            BtnVerify.Opacity = 0.6;

            // 下一个任务按钮状态：已解锁
            if (_currentQuestIndex < QuestService.AllQuests.Count - 1)
            {
                var nextQuest = QuestService.AllQuests[_currentQuestIndex + 1];
                BtnNextQuest.Content = $"➔ 前往下一关：{nextQuest.Title}";
                BtnNextQuest.IsEnabled = true;
                BtnNextQuest.Opacity = 1.0;
                BtnNextQuest.Background = new SolidColorBrush(MediaColor.FromRgb(0x25, 0x63, 0xEB));
            }
            else
            {
                BtnNextQuest.Content = "👑 已达成全部主线试炼！";
                BtnNextQuest.IsEnabled = false;
                BtnNextQuest.Opacity = 0.8;
                BtnNextQuest.Background = new SolidColorBrush(MediaColor.FromRgb(0x05, 0x96, 0x69));
            }
            return;
        }

        BadgeCompletedMark.Visibility = Visibility.Collapsed;
        BtnVerify.IsEnabled = true;
        BtnVerify.Opacity = 1.0;
        BtnVerify.Content = "🔍 立即检测完成情况";

        // 6. 实时检测当前任务进展
        var result = QuestService.CheckQuest(quest, settings, allExtensions);
        IconStep1.Text = result.Step1Completed ? "🟢" : "⚪";
        IconStep2.Text = result.Step2Completed ? "🟢" : "⚪";

        if (result.IsSuccess)
        {
            TxtStatusFeedback.Text = "🎉 任务步骤已达成！点击右侧「立即检测完成情况」领取通关奖励！";
            TxtStatusFeedback.Foreground = new SolidColorBrush(MediaColor.FromRgb(0x34, 0xD3, 0x99));
            FeedbackBorder.Background = new SolidColorBrush(MediaColor.FromArgb(0x26, 0x34, 0xD3, 0x99));
            FeedbackBorder.BorderBrush = new SolidColorBrush(MediaColor.FromArgb(0x4D, 0x34, 0xD3, 0x99));
        }
        else
        {
            TxtStatusFeedback.Text = $"👉 {result.Message}";
            TxtStatusFeedback.Foreground = new SolidColorBrush(MediaColor.FromRgb(0x93, 0xC5, 0xFD));
            FeedbackBorder.Background = new SolidColorBrush(MediaColor.FromArgb(0x26, 0x3B, 0x82, 0xF6));
            FeedbackBorder.BorderBrush = new SolidColorBrush(MediaColor.FromArgb(0x4D, 0x3B, 0x82, 0xF6));
        }

        // 下一个任务按钮：未完成时处于锁定提示状态
        if (_currentQuestIndex < QuestService.AllQuests.Count - 1)
        {
            var nextQuest = QuestService.AllQuests[_currentQuestIndex + 1];
            BtnNextQuest.Content = $"🔒 通关当前任务后解锁：{nextQuest.Title}";
            BtnNextQuest.IsEnabled = false;
            BtnNextQuest.Opacity = 0.5;
            BtnNextQuest.Background = new SolidColorBrush(MediaColor.FromRgb(0x37, 0x41, 0x51));
        }
        else
        {
            BtnNextQuest.Content = "👑 最后一关进行中";
            BtnNextQuest.IsEnabled = false;
            BtnNextQuest.Opacity = 0.5;
        }
    }

    private void RefreshBadgesWall(AppSettings settings)
    {
        var unlocked = settings.UnlockedBadges ?? new List<string>();

        SetBadgeState(Badge1Border, TxtBadge1, unlocked.Any(b => b.Contains("初入江湖")));
        SetBadgeState(Badge2Border, TxtBadge2, unlocked.Any(b => b.Contains("指尖神算")));
        SetBadgeState(Badge3Border, TxtBadge3, unlocked.Any(b => b.Contains("超光速跳跃")));
        SetBadgeState(Badge4Border, TxtBadge4, unlocked.Any(b => b.Contains("系统清道夫")));
        SetBadgeState(Badge5Border, TxtBadge5, unlocked.Any(b => b.Contains("智械先驱")));
    }

    private static void SetBadgeState(System.Windows.Controls.Border border, System.Windows.Controls.TextBlock text, bool isUnlocked)
    {
        if (isUnlocked)
        {
            border.BorderBrush = MediaBrushes.Gold;
            border.Background = new SolidColorBrush(MediaColor.FromArgb(0x33, 0xF5, 0x9E, 0x0B));
            text.Foreground = MediaBrushes.Gold;
            text.FontWeight = FontWeights.Bold;
        }
        else
        {
            border.BorderBrush = new SolidColorBrush(MediaColor.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
            border.Background = new SolidColorBrush(MediaColor.FromRgb(0x16, 0x16, 0x1B));
            text.Foreground = new SolidColorBrush(MediaColor.FromArgb(0x44, 0xFF, 0xFF, 0xFF));
            text.FontWeight = FontWeights.Normal;
        }
    }

    private void BtnActionPrimary_Click(object sender, RoutedEventArgs e)
    {
        var quest = QuestService.AllQuests[_currentQuestIndex];
        var mainWin = WpfApplication.Current.MainWindow as MainWindow;

        if (quest.Id == QuestService.OpenBackpackQuestId)
        {
            mainWin?.ShowMousePanel();
            RefreshStatus();
            return;
        }

        if (quest.Id == QuestService.AiCreationQuestId)
        {
            mainWin?.OpenAddExtensionWithInitialJson(string.Empty, this);
            RefreshStatus();
            return;
        }

        if (!string.IsNullOrWhiteSpace(quest.InitialJsonTemplate))
        {
            mainWin?.OpenAddExtensionWithInitialJson(quest.InitialJsonTemplate, this);
            RefreshStatus();
        }
    }

    private void BtnOpenBackpack_Click(object sender, RoutedEventArgs e)
    {
        var mainWin = WpfApplication.Current.MainWindow as MainWindow;
        mainWin?.ShowMousePanel();
        RefreshStatus();
    }

    private void BtnPrevQuest_Click(object sender, RoutedEventArgs e)
    {
        if (_currentQuestIndex > 0)
        {
            _currentQuestIndex--;
            RefreshStatus();
        }
    }

    private void BtnNextQuest_Click(object sender, RoutedEventArgs e)
    {
        if (_currentQuestIndex < QuestService.AllQuests.Count - 1)
        {
            _currentQuestIndex++;
            RefreshStatus();
        }
    }

    private void BtnVerify_Click(object sender, RoutedEventArgs e)
    {
        var settings = AppSettingsStore.Load();
        var mainWin = WpfApplication.Current.MainWindow as MainWindow;
        var allExtensions = mainWin?.GetExtensionsForSettings() ?? (IReadOnlyList<CommandItem>)Array.Empty<CommandItem>();

        var quest = QuestService.AllQuests[_currentQuestIndex];
        var result = QuestService.CheckQuest(quest, settings, allExtensions);

        if (result.IsSuccess)
        {
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

            RefreshStatus();

            WpfMessageBox.Show(
                this,
                $"🎉 恭喜通关！\n\n" +
                $"🌟 获得奖励: +{quest.RewardPoints} 成就分\n" +
                $"🏅 解锁勋章: {quest.RewardBadge}\n\n" +
                $"下一步请点击下方「➔ 前往下一关」继续探索更多效率技能！",
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
