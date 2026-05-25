using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfColor = System.Windows.Media.Color;

namespace OpenQuickHost;

/// <summary>
/// 定时运行配置弹窗 - 直观的分段选择界面，支持分钟/小时/每天/每周/每月/Cron 六种模式
/// </summary>
public partial class ScheduleConfigWindow : Window
{
    // ─── State ───────────────────────────────────────────
    private bool _init = true;
    private bool _useCronMode = false;

    // Simple-mode state
    private string _freqMode = "daily";   // min | hour | daily | week | month
    private int    _minInterval  = 30;    // 5 10 15 30 45
    private int    _hourInterval = 1;     // 1 2 3 4 6 12
    private int    _dailyH = 9, _dailyM = 0;
    private readonly HashSet<int> _weekDays = new() { 1 }; // 1=Mon…7=Sun
    private int    _weeklyH = 9, _weeklyM = 0;
    private int    _monthDay = 1;
    private int    _monthlyH = 9, _monthlyM = 0;

    // Active selection visuals
    private static readonly SolidColorBrush AccentBg     = new(WpfColor.FromArgb(0x1A, 0x3B, 0x82, 0xF6));
    private static readonly SolidColorBrush AccentBorder = new(WpfColor.FromArgb(0xFF, 0x3B, 0x82, 0xF6));
    private static readonly SolidColorBrush AccentFg     = new(WpfColor.FromArgb(0xFF, 0x93, 0xC5, 0xFD));
    private static readonly SolidColorBrush NormalBg     = new(WpfColor.FromArgb(0x00, 0, 0, 0)); // transparent
    private static readonly SolidColorBrush NormalBorder = new(WpfColor.FromArgb(0xFF, 0x2E, 0x2E, 0x2E));
    private static readonly SolidColorBrush NormalFg     = new(WpfColor.FromArgb(0xFF, 0x8E, 0x8E, 0x8E));

    // ─── Constructor ─────────────────────────────────────
    public ScheduleConfigWindow(string currentSchedule)
    {
        InitializeComponent();

        PopulateTimeCombos();
        PopulateMonthDayChips();

        ApplyCurrentSchedule(currentSchedule);

        _init = false;
        RefreshUI();
    }

    /// <summary>Final cron result (empty = clear schedule).</summary>
    public string ResultSchedule { get; private set; } = string.Empty;

    // ─── Populate helpers ────────────────────────────────
    private void PopulateTimeCombos()
    {
        var hourBoxes = new[] { DailyHourBox, WeeklyHourBox, MonthlyHourBox };
        var minBoxes  = new[] { DailyMinBox,  WeeklyMinBox,  MonthlyMinBox  };

        foreach (var box in hourBoxes)
            for (int h = 0; h <= 23; h++) box.Items.Add(h.ToString("D2"));

        foreach (var box in minBoxes)
            for (int m = 0; m <= 59; m += 5) box.Items.Add(m.ToString("D2"));

        DailyHourBox.SelectedIndex   = 9;  DailyMinBox.SelectedIndex   = 0;
        WeeklyHourBox.SelectedIndex  = 9;  WeeklyMinBox.SelectedIndex  = 0;
        MonthlyHourBox.SelectedIndex = 9;  MonthlyMinBox.SelectedIndex = 0;
    }

    private void PopulateMonthDayChips()
    {
        for (int d = 1; d <= 31; d++)
        {
            var chip = new Border
            {
                Width = 36, Height = 36,
                Margin = new Thickness(0, 0, 6, 6),
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = d,
                Child = new TextBlock
                {
                    Text = d.ToString(),
                    FontSize = 11,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                }
            };
            chip.MouseLeftButtonDown += MonthDayChip_Click;
            MonthDayWrap.Children.Add(chip);
        }
    }

    // ─── Parse existing schedule ─────────────────────────
    private void ApplyCurrentSchedule(string schedule)
    {
        if (string.IsNullOrWhiteSpace(schedule))
        {
            EnableToggle.IsChecked = false;
            return;
        }

        EnableToggle.IsChecked = true;
        var s = schedule.Trim();

        // Minute intervals: */N * * * *
        if (TryParseEveryNMinutes(s, out int mins))
        {
            _freqMode = "min";
            _minInterval = mins;
            return;
        }
        // Hour intervals: 0 */N * * *
        if (TryParseEveryNHours(s, out int hrs))
        {
            _freqMode = "hour";
            _hourInterval = hrs;
            return;
        }
        // Weekly: M H * * DOW  (DOW is single digit, not comma-separated for simple mode)
        if (TryParseWeekly(s, out int wDow, out int wH, out int wM))
        {
            _freqMode = "week";
            _weekDays.Clear(); _weekDays.Add(wDow);
            _weeklyH = wH; _weeklyM = wM;
            WeeklyHourBox.SelectedIndex = wH;
            WeeklyMinBox.SelectedIndex  = MinIndexOf(WeeklyMinBox, wM);
            return;
        }
        // Monthly: M H D * *
        if (TryParseMonthly(s, out int mDay, out int mH, out int mM))
        {
            _freqMode = "month";
            _monthDay = mDay; _monthlyH = mH; _monthlyM = mM;
            MonthlyHourBox.SelectedIndex = mH;
            MonthlyMinBox.SelectedIndex  = MinIndexOf(MonthlyMinBox, mM);
            return;
        }
        // Daily: M H * * *
        if (TryParseDaily(s, out int dH, out int dM))
        {
            _freqMode = "daily";
            _dailyH = dH; _dailyM = dM;
            DailyHourBox.SelectedIndex = dH;
            DailyMinBox.SelectedIndex  = MinIndexOf(DailyMinBox, dM);
            return;
        }
        // Fallback: treat as raw cron
        _useCronMode = true;
        CronBox.Text = s;
    }

    // ─── Cron parsers ────────────────────────────────────
    private static bool TryParseEveryNMinutes(string s, out int n)
    {
        n = 30;
        var p = s.Split(' ');
        if (p.Length != 5) return false;
        if (!p[0].StartsWith("*/")) return false;
        if (p[1] != "*" || p[2] != "*" || p[3] != "*" || p[4] != "*") return false;
        return int.TryParse(p[0][2..], out n) && n > 0;
    }

    private static bool TryParseEveryNHours(string s, out int n)
    {
        n = 1;
        var p = s.Split(' ');
        if (p.Length != 5) return false;
        if (p[0] != "0") return false;
        if (!p[1].StartsWith("*/") && p[1] != "*") return false;
        if (p[2] != "*" || p[3] != "*" || p[4] != "*") return false;
        if (p[1] == "*") { n = 1; return true; }
        return int.TryParse(p[1][2..], out n) && n > 0;
    }

    private static bool TryParseDaily(string s, out int h, out int m)
    {
        h = 9; m = 0;
        var p = s.Split(' ');
        if (p.Length != 5) return false;
        if (p[2] != "*" || p[3] != "*" || p[4] != "*") return false;
        return int.TryParse(p[1], out h) && int.TryParse(p[0], out m);
    }

    private static bool TryParseWeekly(string s, out int dow, out int h, out int m)
    {
        dow = 1; h = 9; m = 0;
        var p = s.Split(' ');
        if (p.Length != 5) return false;
        if (p[2] != "*" || p[3] != "*") return false;
        if (!int.TryParse(p[4], out dow)) return false;
        return int.TryParse(p[1], out h) && int.TryParse(p[0], out m);
    }

    private static bool TryParseMonthly(string s, out int day, out int h, out int m)
    {
        day = 1; h = 9; m = 0;
        var p = s.Split(' ');
        if (p.Length != 5) return false;
        if (p[3] != "*" || p[4] != "*") return false;
        return int.TryParse(p[2], out day) && int.TryParse(p[1], out h) && int.TryParse(p[0], out m);
    }

    private static int MinIndexOf(WpfComboBox box, int value)
    {
        for (int i = 0; i < box.Items.Count; i++)
            if (box.Items[i] is string s && int.TryParse(s, out int v) && v == value) return i;
        return 0;
    }

    // ─── Build cron from state ────────────────────────────
    private string BuildCron()
    {
        if (EnableToggle.IsChecked != true) return string.Empty;
        if (_useCronMode) return CronBox.Text.Trim();

        return _freqMode switch
        {
            "min"   => $"*/{_minInterval} * * * *",
            "hour"  => _hourInterval == 1 ? "0 * * * *" : $"0 */{_hourInterval} * * *",
            "daily" => $"{_dailyM} {_dailyH} * * *",
            "week"  => BuildWeeklyCron(),
            "month" => $"{_monthlyM} {_monthlyH} {_monthDay} * *",
            _       => string.Empty
        };
    }

    private string BuildWeeklyCron()
    {
        if (_weekDays.Count == 0) return $"{_weeklyM} {_weeklyH} * * 1";
        var days = string.Join(",", _weekDays.OrderBy(d => d));
        return $"{_weeklyM} {_weeklyH} * * {days}";
    }

    private string BuildFriendly()
    {
        if (EnableToggle.IsChecked != true) return string.Empty;
        if (_useCronMode) return CronToFriendly(CronBox.Text.Trim());

        return _freqMode switch
        {
            "min"  => $"每 {_minInterval} 分钟运行一次",
            "hour" => _hourInterval == 1 ? "每小时运行一次" : $"每 {_hourInterval} 小时运行一次",
            "daily" => $"每天 {_dailyH:D2}:{_dailyM:D2} 运行",
            "week"  => BuildWeeklyFriendly(),
            "month" => $"每月 {_monthDay} 日 {_monthlyH:D2}:{_monthlyM:D2} 运行",
            _ => string.Empty
        };
    }

    private string BuildWeeklyFriendly()
    {
        var names = new[] { "", "周一", "周二", "周三", "周四", "周五", "周六", "周日" };
        var days  = string.Join("、", _weekDays.OrderBy(d => d).Select(d => names[d]));
        return $"每{days} {_weeklyH:D2}:{_weeklyM:D2} 运行";
    }

    // ─── Refresh all visuals ────────────────────────────
    private void RefreshUI()
    {
        if (_init) return;

        bool enabled = EnableToggle.IsChecked == true;
        ConfigPanel.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;

        // Mode segment buttons
        SetSegActive(ModeSimpBtn, ModeSimpText, !_useCronMode);
        SetSegActive(ModeCronBtn, ModeCronText,  _useCronMode);
        SimpleModePanel.Visibility = _useCronMode ? Visibility.Collapsed : Visibility.Visible;
        CronModePanel.Visibility   = _useCronMode ? Visibility.Visible   : Visibility.Collapsed;

        if (!_useCronMode)
        {
            // Freq chips
            SetChipActive(FreqMinBtn,   _freqMode == "min");
            SetChipActive(FreqHourBtn,  _freqMode == "hour");
            SetChipActive(FreqDailyBtn, _freqMode == "daily");
            SetChipActive(FreqWeekBtn,  _freqMode == "week");
            SetChipActive(FreqMonthBtn, _freqMode == "month");

            // Sub-panels
            MinutePanel.Visibility  = _freqMode == "min"   ? Visibility.Visible : Visibility.Collapsed;
            HourPanel.Visibility    = _freqMode == "hour"  ? Visibility.Visible : Visibility.Collapsed;
            DailyPanel.Visibility   = _freqMode == "daily" ? Visibility.Visible : Visibility.Collapsed;
            WeeklyPanel.Visibility  = _freqMode == "week"  ? Visibility.Visible : Visibility.Collapsed;
            MonthlyPanel.Visibility = _freqMode == "month" ? Visibility.Visible : Visibility.Collapsed;

            // Minute chips
            RefreshMinuteChips();
            // Hour chips
            RefreshHourChips();
            // Day-of-week chips
            RefreshDowChips();
            // Month-day chips
            RefreshMonthDayChips();
        }

        // Preview
        ResultSchedule = BuildCron();
        var friendly   = BuildFriendly();
        if (!string.IsNullOrEmpty(friendly) && enabled)
        {
            PreviewBorder.Visibility = Visibility.Visible;
            PreviewText.Text = friendly;
        }
        else
        {
            PreviewBorder.Visibility = Visibility.Collapsed;
        }
    }

    // ─── Chip active-state helpers ────────────────────────
    private static void SetSegActive(Border border, TextBlock text, bool active)
    {
        border.Background   = active ? AccentBg     : NormalBg;
        border.BorderBrush  = active ? AccentBorder : NormalBorder;
        text.Foreground     = active ? AccentFg     : NormalFg;
        text.FontWeight     = active ? FontWeights.SemiBold : FontWeights.Normal;
    }

    private static void SetChipActive(Border chip, bool active)
    {
        chip.Background  = active ? AccentBg     : NormalBg;
        chip.BorderBrush = active ? AccentBorder : NormalBorder;
        if (chip.Child is TextBlock tb)
        {
            tb.Foreground  = active ? AccentFg  : NormalFg;
            tb.FontWeight  = active ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    private void RefreshMinuteChips()
    {
        foreach (Border chip in new[] { Min5Btn, Min10Btn, Min15Btn, Min30Btn, Min45Btn })
            SetChipActive(chip, chip.Tag is string s && int.TryParse(s, out int sv) && sv == _minInterval);

        // Sync custom input box
        var presetValues = new[] { 5, 10, 15, 30, 45 };
        if (!presetValues.Contains(_minInterval))
        {
            CustomMinuteBox.Text = _minInterval.ToString();
        }
        else if (string.IsNullOrWhiteSpace(CustomMinuteBox.Text) || 
                 (int.TryParse(CustomMinuteBox.Text, out int cv) && presetValues.Contains(cv)))
        {
            CustomMinuteBox.Text = string.Empty;
        }
    }

    private void RefreshHourChips()
    {
        foreach (Border chip in new[] { Hr1Btn, Hr2Btn, Hr3Btn, Hr4Btn, Hr6Btn, Hr12Btn })
            SetChipActive(chip, chip.Tag is int v && v == _hourInterval
                              || chip.Tag is string s && int.TryParse(s, out int sv) && sv == _hourInterval);
    }

    private void RefreshDowChips()
    {
        foreach (Border chip in new[] { DowMon, DowTue, DowWed, DowThu, DowFri, DowSat, DowSun })
        {
            if (chip.Tag is string st && int.TryParse(st, out int day))
                SetChipActive(chip, _weekDays.Contains(day));
        }
    }

    private void RefreshMonthDayChips()
    {
        foreach (UIElement el in MonthDayWrap.Children)
        {
            if (el is Border chip && chip.Tag is int d)
                SetChipActive(chip, d == _monthDay);
        }
    }

    // ─── Event handlers ──────────────────────────────────
    private void EnableToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_init) return;
        ConfigPanel.Visibility = EnableToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        RefreshUI();
    }

    private void ModeSimpBtn_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    { _useCronMode = false; RefreshUI(); }

    private void ModeCronBtn_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    { _useCronMode = true; RefreshUI(); }

    private void FreqBtn_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is Border { Tag: string tag }) { _freqMode = tag; RefreshUI(); }
    }

    private void MinChip_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is Border b && b.Tag is string st && int.TryParse(st, out int v))
        { _minInterval = v; RefreshUI(); }
    }

    private void HrChip_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is Border b && b.Tag is string st && int.TryParse(st, out int v))
        { _hourInterval = v; RefreshUI(); }
    }

    private void DowChip_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is Border b && b.Tag is string st && int.TryParse(st, out int day))
        {
            // Toggle day selection (must keep at least one)
            if (_weekDays.Contains(day))
            {
                if (_weekDays.Count > 1) _weekDays.Remove(day);
            }
            else
            {
                _weekDays.Add(day);
            }
            RefreshUI();
        }
    }

    private void MonthDayChip_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is Border { Tag: int d }) { _monthDay = d; RefreshUI(); }
    }

    private void TimeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_init) return;
        SyncTimeFromCombos();
        RefreshUI();
    }

    private void EditableCombo_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_init) return;
        SyncTimeFromCombos();
        RefreshUI();
    }

    private void SyncTimeFromCombos()
    {
        _dailyH  = ParseComboValue(DailyHourBox, 0, 23, _dailyH);
        _dailyM  = ParseComboValue(DailyMinBox, 0, 59, _dailyM);
        _weeklyH = ParseComboValue(WeeklyHourBox, 0, 23, _weeklyH);
        _weeklyM = ParseComboValue(WeeklyMinBox, 0, 59, _weeklyM);
        _monthlyH = ParseComboValue(MonthlyHourBox, 0, 23, _monthlyH);
        _monthlyM = ParseComboValue(MonthlyMinBox, 0, 59, _monthlyM);
    }

    private static int ParseComboValue(WpfComboBox box, int min, int max, int fallback)
    {
        // Try editable text first, then selected item
        var text = box.Text?.Trim() ?? (box.SelectedItem as string ?? string.Empty);
        if (int.TryParse(text, out int val) && val >= min && val <= max)
            return val;
        return fallback;
    }

    private void CustomMinuteBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_init) return;
        if (int.TryParse(CustomMinuteBox.Text.Trim(), out int val) && val >= 1 && val <= 59)
        {
            _minInterval = val;
            RefreshUI();
        }
    }

    private void CronBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_init) return;
        RefreshUI();
    }

    private void CronExample_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is Border { Tag: string cron }) CronBox.Text = cron;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (EnableToggle.IsChecked == true && _useCronMode && string.IsNullOrWhiteSpace(CronBox.Text))
        {
            ErrorText.Text = "请填写 Cron 表达式，或关闭定时运行。";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }
        ErrorText.Visibility = Visibility.Collapsed;
        ResultSchedule = BuildCron();
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    // ─── Public static: cron → friendly (used by card labels) ─
    public static string CronToFriendly(string? cron)
    {
        if (string.IsNullOrWhiteSpace(cron)) return "未设置";
        var s = cron.Trim();

        if (TryParseEveryNMinutes(s, out int mins))
            return mins == 1 ? "每分钟" : $"每 {mins} 分钟";

        if (TryParseEveryNHours(s, out int hrs))
            return hrs == 1 ? "每小时" : $"每 {hrs} 小时";

        if (TryParseMonthly(s, out int mDay, out int mH, out int mM))
            return $"每月{mDay}日 {mH:D2}:{mM:D2}";

        if (TryParseWeekly(s, out int wDow, out int wH, out int wM))
        {
            var names = new[] { "", "周一", "周二", "周三", "周四", "周五", "周六", "周日" };
            var label = wDow >= 1 && wDow <= 7 ? names[wDow] : $"DOW{wDow}";
            return $"每{label} {wH:D2}:{wM:D2}";
        }

        if (TryParseDaily(s, out int dH, out int dM))
            return $"每天 {dH:D2}:{dM:D2}";

        return "定时";
    }
}
