using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OpenQuickHost;

public partial class MainWindow
{
    private static ImageSource? _yanyuCreateIcon;
    private static ImageSource? _yanyuRuleLogoIcon;

    private void ApplyYanyuFilter(string query)
    {
        foreach (var command in _allCommands)
        {
            command.SetQueryPreview(null, null);
        }

        var items = BuildYanyuCommands(query).ToList();
        FilteredCommands.Clear();
        foreach (var item in items)
        {
            FilteredCommands.Add(item);
        }

        SelectedCommand = FilteredCommands.FirstOrDefault();
        CommandList.SelectedItem = SelectedCommand;
        OnPropertyChanged(nameof(VisibleCountText));
        OnPropertyChanged(nameof(FooterHint));
    }

    private int CountYanyuResults(string query) => BuildYanyuCommands(query).Count();

    private IEnumerable<CommandItem> BuildYanyuCommands(string query)
    {
        var trimmedQuery = (query ?? string.Empty).Trim();
        var rules = (_appSettings.YanyuRules ?? [])
            .OrderByDescending(static rule => rule.Enabled)
            .ThenBy(static rule => rule.TriggerText, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (trimmedQuery.Length == 0)
        {
            yield return CreateYanyuCreateCommand(string.Empty);
            foreach (var rule in rules)
            {
                yield return CreateYanyuRuleCommand(rule);
            }

            yield break;
        }

        var matches = rules
            .Select(rule => new
            {
                Rule = rule,
                Match = BuildCommandMatch(CreateYanyuRuleCommand(rule), trimmedQuery)
            })
            .Where(static item => item.Match.IsMatch)
            .OrderByDescending(static item => item.Match.Priority)
            .ThenBy(item => item.Rule.TriggerText, StringComparer.OrdinalIgnoreCase)
            .Select(static item => item.Rule)
            .ToList();

        if (matches.Count == 0)
        {
            yield return CreateYanyuCreateCommand(trimmedQuery);
            yield break;
        }

        foreach (var rule in matches)
        {
            yield return CreateYanyuRuleCommand(rule);
        }
    }

    private CommandItem CreateYanyuRuleCommand(YanyuRuleSettings rule)
    {
        var boundExtension = string.Equals(rule.ActionType, YanyuActionTypes.RunExtension, StringComparison.OrdinalIgnoreCase)
            ? ResolveYanyuBindableCommand(rule.ExtensionId)
            : null;
        var triggerPreview = $"{(rule.UseRegex ? "正则 " : string.Empty)}{rule.TriggerText} + {YanyuTriggerSuffix.ToDisplayText(rule.TriggerSuffix)}";
        var actionSummary = BuildYanyuActionSummary(rule);
        var statusLabel = rule.Enabled ? "已启用" : "已停用";
        var processLabel = string.IsNullOrWhiteSpace(rule.BoundProcessName) ? "所有应用" : $"仅 {rule.BoundProcessName}";
        var subtitle = string.IsNullOrWhiteSpace(rule.Description)
            ? $"{actionSummary}   ·   {processLabel}   ·   {statusLabel}"
            : $"{rule.Description}   ·   {actionSummary}   ·   {processLabel}   ·   {statusLabel}";

        return new CommandItem(
            glyph: boundExtension?.DisplayGlyph ?? string.Empty,
            title: triggerPreview,
            subtitle: subtitle,
            category: "燕语",
            accentHex: rule.Enabled ? "#FF22C55E" : "#FF64748B",
            openTarget: $"oqh://yanyu/edit/{Uri.EscapeDataString(rule.Id)}",
            keywords: BuildYanyuKeywords(rule, actionSummary),
            source: CommandSource.Local,
            extensionId: $"yanyu-rule-{rule.Id}",
            iconReference: boundExtension?.IconReference ?? "mdi:window",
            iconSourceOverride: boundExtension?.IconSource ?? GetYanyuRuleLogoIcon());
    }

    private CommandItem CreateYanyuCreateCommand(string triggerText)
    {
        var normalizedTrigger = triggerText.Trim();
        var subtitle = normalizedTrigger.Length == 0
            ? "新建一条燕语，统一编辑缩写词、后缀和动作。"
            : $"没有找到匹配项。按回车新建“{normalizedTrigger}”的燕语。";
        var title = normalizedTrigger.Length == 0
            ? "新建燕语"
            : $"新建燕语：{normalizedTrigger}";

        return new CommandItem(
            glyph: string.Empty,
            title: title,
            subtitle: subtitle,
            category: "燕语",
            accentHex: "#FF0EA5E9",
            openTarget: $"oqh://yanyu/new/{Uri.EscapeDataString(normalizedTrigger)}",
            keywords: ["燕语", "新建", "文本指令", "hotstring", normalizedTrigger],
            source: CommandSource.Local,
            extensionId: $"yanyu-create-{normalizedTrigger}",
            iconReference: "mdi:plus",
            iconSourceOverride: GetYanyuCreateIcon());
    }

    private IEnumerable<string> BuildYanyuKeywords(YanyuRuleSettings rule, string actionSummary)
    {
        yield return "燕语";
        yield return "文本指令";
        yield return "hotstring";
        yield return rule.TriggerText;
        yield return YanyuTriggerSuffix.ToDisplayText(rule.TriggerSuffix);
        yield return actionSummary;
        yield return rule.BoundProcessName;
        if (rule.UseRegex)
        {
            yield return "正则";
            yield return "regex";
        }

        if (!string.IsNullOrWhiteSpace(rule.Description))
        {
            yield return rule.Description;
        }

        if (string.Equals(rule.ActionType, YanyuActionTypes.RunExtension, StringComparison.OrdinalIgnoreCase))
        {
            yield return ResolveYanyuRuleExtensionTitle(rule);
        }
    }

    private static ImageSource? GetYanyuRuleLogoIcon()
    {
        try
        {
            var isLight = OpenQuickHost.AppSettingsStore.Load().ThemeMode == "Light";
            var logoUri = isLight ? "pack://application:,,,/logo-black.png" : "pack://application:,,,/logo-white.png";
            
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(logoUri, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            if (bitmap.CanFreeze)
            {
                bitmap.Freeze();
            }

            _yanyuRuleLogoIcon = bitmap;
        }
        catch
        {
            _yanyuRuleLogoIcon = null;
        }

        return _yanyuRuleLogoIcon;
    }

    private static ImageSource? GetYanyuCreateIcon()
    {
        try
        {
            var geometry = Geometry.Parse("M19,13H13V19H11V13H5V11H11V5H13V11H19V13Z");
            if (geometry.CanFreeze)
            {
                geometry.Freeze();
            }

            var brush = System.Windows.Application.Current.Resources["BrushTextMain"] as System.Windows.Media.SolidColorBrush ?? System.Windows.Media.Brushes.White;
            var drawing = new GeometryDrawing(
                brush,
                null,
                geometry);
            if (drawing.CanFreeze)
            {
                drawing.Freeze();
            }

            var image = new DrawingImage(drawing);
            if (image.CanFreeze)
            {
                image.Freeze();
            }

            _yanyuCreateIcon = image;
        }
        catch
        {
            _yanyuCreateIcon = null;
        }

        return _yanyuCreateIcon;
    }

    private string BuildYanyuActionSummary(YanyuRuleSettings rule)
    {
        return string.Equals(rule.ActionType, YanyuActionTypes.RunExtension, StringComparison.OrdinalIgnoreCase)
            ? $"运行扩展：{ResolveYanyuRuleExtensionTitle(rule)}"
            : $"粘贴文本：{BuildYanyuPreviewText(rule.TextContent)}";
    }

    private CommandItem? GetCurrentYanyuBindableExtension()
    {
        var source = SelectedCommand != null && !IsInternalCommand(SelectedCommand)
            ? SelectedCommand
            : _lastActionableCommand;
        if (source == null)
        {
            return null;
        }

        var command = ResolveRunnableCommand(source);
        return command.Source == CommandSource.LocalExtension ? command : null;
    }

    private string ResolveYanyuRuleExtensionTitle(YanyuRuleSettings rule)
    {
        if (string.IsNullOrWhiteSpace(rule.ExtensionId))
        {
            return "未绑定扩展";
        }

        var extension = ResolveYanyuBindableCommand(rule.ExtensionId);
        return extension != null
            ? extension.Title
            : rule.ExtensionId;
    }

    private static string BuildYanyuPreviewText(string text)
    {
        var normalized = (text ?? string.Empty).ReplaceLineEndings(" ").Trim();
        if (normalized.Length == 0)
        {
            return "空文本";
        }

        return normalized.Length <= 28 ? normalized : normalized[..28] + "...";
    }

    private bool HandleYanyuInternalCommand(CommandItem command)
    {
        var target = command.OpenTarget ?? string.Empty;
        const string newPrefix = "oqh://yanyu/new/";
        if (target.StartsWith(newPrefix, StringComparison.OrdinalIgnoreCase))
        {
            CreateYanyuRule(Uri.UnescapeDataString(target[newPrefix.Length..]));
            return true;
        }

        const string editPrefix = "oqh://yanyu/edit/";
        if (target.StartsWith(editPrefix, StringComparison.OrdinalIgnoreCase))
        {
            EditYanyuRule(Uri.UnescapeDataString(target[editPrefix.Length..]));
            return true;
        }

        return false;
    }

    private static bool TryGetYanyuRuleIdFromCommand(CommandItem? command, out string ruleId)
    {
        ruleId = string.Empty;
        var target = command?.OpenTarget ?? string.Empty;
        const string editPrefix = "oqh://yanyu/edit/";
        if (!target.StartsWith(editPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        ruleId = Uri.UnescapeDataString(target[editPrefix.Length..]);
        return ruleId.Length > 0;
    }

    private bool IsYanyuRuleCommand(CommandItem? command) => TryGetYanyuRuleIdFromCommand(command, out _);

    private bool IsYanyuRuleEnabled(CommandItem? command)
    {
        if (!TryGetYanyuRuleIdFromCommand(command, out var ruleId))
        {
            return false;
        }

        return (_appSettings.YanyuRules ?? [])
            .FirstOrDefault(item => item.Id.Equals(ruleId, StringComparison.OrdinalIgnoreCase))
            ?.Enabled == true;
    }

    private void CreateYanyuRule(string prefilledTriggerText)
    {
        var defaultExtension = GetCurrentYanyuBindableExtension();
        var defaultRule = new YanyuRuleSettings
        {
            TriggerText = prefilledTriggerText,
            TriggerSuffix = YanyuTriggerSuffix.Space,
            Enabled = true,
            ActionType = YanyuActionTypes.PasteText,
            ExtensionId = defaultExtension?.ExtensionId ?? string.Empty,
            Description = defaultExtension == null ? string.Empty : $"运行扩展：{defaultExtension.Title}"
        };

        var edited = ShowYanyuEditor(
            "新建燕语",
            "在一个界面里配置缩写词、触发后缀和动作。",
            defaultRule,
            isEditMode: false);
        if (edited is not { } createResult)
        {
            return;
        }

        var settings = AppSettingsStore.Load();
        settings.YanyuRules.Add(createResult.Rule);
        SaveYanyuSettings(settings, $"已添加燕语：{createResult.Rule.TriggerText}");
    }

    private void EditYanyuRule(string ruleId)
    {
        var settings = AppSettingsStore.Load();
        var index = settings.YanyuRules.FindIndex(item => item.Id.Equals(ruleId, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            SyncStatus = "没有找到对应燕语规则。";
            return;
        }

        var existing = settings.YanyuRules[index];
        var edited = ShowYanyuEditor(
            "编辑燕语",
            "直接修改这条燕语的缩写词、动作和说明。",
            existing,
            isEditMode: true,
            allowDelete: true);
        if (edited is not { } editResult)
        {
            return;
        }

        if (editResult.WasDeleted)
        {
            settings.YanyuRules.RemoveAt(index);
            SaveYanyuSettings(settings, $"已删除燕语：{existing.TriggerText}");
            return;
        }

        var updatedRule = editResult.Rule;
        updatedRule.Id = existing.Id;
        settings.YanyuRules[index] = updatedRule;
        SaveYanyuSettings(settings, $"已更新燕语：{updatedRule.TriggerText}");
    }

    private void ToggleYanyuRuleForCommand(CommandItem? command = null)
    {
        if (!TryGetYanyuRuleIdFromCommand(command ?? SelectedCommand, out var ruleId))
        {
            SyncStatus = "当前选中项不是燕语规则。";
            return;
        }

        var settings = AppSettingsStore.Load();
        var rule = settings.YanyuRules.FirstOrDefault(item => item.Id.Equals(ruleId, StringComparison.OrdinalIgnoreCase));
        if (rule == null)
        {
            SyncStatus = "没有找到对应燕语规则。";
            return;
        }

        rule.Enabled = !rule.Enabled;
        SaveYanyuSettings(settings, $"{(rule.Enabled ? "已启用" : "已停用")}燕语：{rule.TriggerText}");
    }

    private void EditYanyuRuleForCommand(CommandItem? command = null)
    {
        if (!TryGetYanyuRuleIdFromCommand(command ?? SelectedCommand, out var ruleId))
        {
            SyncStatus = "当前选中项不是燕语规则。";
            return;
        }

        EditYanyuRule(ruleId);
    }

    private void DeleteYanyuRuleForCommand(CommandItem? command = null)
    {
        if (!TryGetYanyuRuleIdFromCommand(command ?? SelectedCommand, out var ruleId))
        {
            SyncStatus = "当前选中项不是燕语规则。";
            return;
        }

        var settings = AppSettingsStore.Load();
        var index = settings.YanyuRules.FindIndex(item => item.Id.Equals(ruleId, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            SyncStatus = "没有找到对应燕语规则。";
            return;
        }

        var rule = settings.YanyuRules[index];
        var confirm = System.Windows.MessageBox.Show(
            this,
            $"确认删除燕语“{rule.TriggerText}”吗？",
            "删除燕语",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        settings.YanyuRules.RemoveAt(index);
        SaveYanyuSettings(settings, $"已删除燕语：{rule.TriggerText}");
    }

    private (bool WasDeleted, YanyuRuleSettings Rule)? ShowYanyuEditor(
        string title,
        string subtitle,
        YanyuRuleSettings rule,
        bool isEditMode,
        bool allowDelete = false)
    {
        var clonedRule = new YanyuRuleSettings
        {
            Id = rule.Id,
            Enabled = rule.Enabled,
            TriggerText = rule.TriggerText,
            TriggerSuffix = rule.TriggerSuffix,
            UseRegex = rule.UseRegex,
            BoundProcessName = rule.BoundProcessName,
            Description = rule.Description,
            ActionType = rule.ActionType,
            TextContent = rule.TextContent,
            ExtensionId = string.IsNullOrWhiteSpace(rule.ExtensionId)
                ? GetCurrentYanyuBindableExtension()?.ExtensionId ?? string.Empty
                : rule.ExtensionId
        };

        var dialog = new YanyuEditorWindow(
            title,
            subtitle,
            clonedRule,
            GetYanyuBindableExtensions(),
            isEditMode && allowDelete)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            return null;
        }

        var resultRule = dialog.EditedRule;
        resultRule.Id = clonedRule.Id;
        return (dialog.WasDeleted, resultRule);
    }

    private IReadOnlyList<CommandItem> GetYanyuBindableExtensions()
    {
        return _allCommands
            .Where(command =>
                !IsYanyuRuleCommand(command) &&
                !command.IsFileSystemResult &&
                !command.IsProviderResult &&
                (!IsInternalCommand(command) || command.Source == CommandSource.LocalExtension) &&
                (command.Source == CommandSource.LocalExtension ||
                 command.Source == CommandSource.Application ||
                 command.Source == CommandSource.WebSearch ||
                 command.Category.Contains("系统", StringComparison.OrdinalIgnoreCase)))
            .GroupBy(static command => command.ExtensionId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static command => command.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void SaveYanyuSettings(AppSettings settings, string successMessage)
    {
        AppSettingsStore.Save(settings);
        _appSettings = AppSettingsStore.Load();
        _windowBoundExtensionsService.Reload(_appSettings.WindowBindings);
        RefreshYanyuRules();
        ApplyFilter(SearchBox.Text);
        LastRunMessage = successMessage;
        SyncStatus = successMessage;
        NotifyQuickPanelSettingsChanged("yanyu-settings-saved");
    }

    private void RefreshYanyuRules()
    {
        YanyuTriggerService.UpdateRules(_appSettings.YanyuRules);
    }

    private void HandleYanyuRuleTriggered(YanyuTriggerEvent triggerEvent)
    {
        _ = ExecuteYanyuRuleAsync(triggerEvent);
    }

    private async Task ExecuteYanyuRuleAsync(YanyuTriggerEvent triggerEvent)
    {
        var rule = triggerEvent.Rule;
        try
        {
            if (string.Equals(rule.ActionType, YanyuActionTypes.RunExtension, StringComparison.OrdinalIgnoreCase))
            {
                var extension = ResolveYanyuBindableCommand(rule.ExtensionId);
                if (extension == null)
                {
                    SyncStatus = $"燕语绑定的扩展不存在：{rule.ExtensionId}";
                    return;
                }

                await ExecuteCommandAsync(extension, BuildYanyuExtensionInput(triggerEvent), "yanyu");
                LastRunMessage = $"已触发燕语：{rule.TriggerText} -> {extension.Title}";
                return;
            }

            YanyuTriggerService.PasteText(ApplyYanyuCaptureTemplate(rule.TextContent, triggerEvent));
            LastRunMessage = $"已触发燕语：{rule.TriggerText}";
        }
        catch (Exception ex)
        {
            SyncStatus = $"燕语执行失败：{FormatExceptionMessage(ex)}";
        }
    }

    private CommandItem? ResolveYanyuBindableCommand(string extensionId)
    {
        if (string.IsNullOrWhiteSpace(extensionId))
        {
            return null;
        }

        if (_localExtensionIndex.TryGetValue(extensionId, out var localExtension))
        {
            return localExtension;
        }

        return _allCommands.FirstOrDefault(command => command.ExtensionId.Equals(extensionId, StringComparison.OrdinalIgnoreCase));
    }

    private static string ApplyYanyuCaptureTemplate(string template, YanyuTriggerEvent triggerEvent)
    {
        var result = template ?? string.Empty;
        result = result.Replace("{0}", triggerEvent.MatchedText, StringComparison.Ordinal);
        for (var index = 0; index < triggerEvent.Groups.Count; index++)
        {
            result = result.Replace("{" + (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + "}", triggerEvent.Groups[index], StringComparison.Ordinal);
        }

        foreach (var pair in triggerEvent.NamedGroups)
        {
            result = result.Replace("{" + pair.Key + "}", pair.Value, StringComparison.Ordinal);
        }

        return result;
    }

    private static string BuildYanyuExtensionInput(YanyuTriggerEvent triggerEvent)
    {
        if (!triggerEvent.Rule.UseRegex)
        {
            return triggerEvent.MatchedText;
        }

        return JsonSerializer.Serialize(new
        {
            match = triggerEvent.MatchedText,
            process = triggerEvent.ForegroundProcessName,
            groups = triggerEvent.Groups,
            namedGroups = triggerEvent.NamedGroups
        });
    }
}
