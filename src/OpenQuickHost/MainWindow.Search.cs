using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Collections.Specialized;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.VisualBasic.FileIO;
using Forms = System.Windows.Forms;
using OpenQuickHost.Sync;

namespace OpenQuickHost;

public partial class MainWindow
{
    private bool TryHandleSearchScopeTabNavigation(System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Tab || IsHostedViewOpen)
        {
            return false;
        }

        var delta = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift ? -1 : 1;
        CycleSearchScope(delta);
        e.Handled = true;

        if (!IsAiChatMode)
        {
            SearchBox.Focus();
            SearchBox.CaretIndex = SearchBox.Text.Length;
        }

        return true;
    }

    private void ApplyFilter(string? query)
    {
        var parsed = ParseSearchQuery(query);
        var normalizedQueryText = query ?? string.Empty;
        var preserveSelection = _hasAppliedFilterOnce &&
                                string.Equals(normalizedQueryText, _lastAppliedFilterText, StringComparison.Ordinal) &&
                                string.Equals(parsed.ScopeKey, _lastAppliedFilterScopeKey, StringComparison.OrdinalIgnoreCase);
        var previousSelectedCommand = SelectedCommand;
        _activeFilterScopeKey = parsed.ScopeKey;
        UpdateSearchScopeCounts(parsed);
        _activeQueryArgument = string.Empty;

        // 响应式会话调度：取消上一轮所有未完成的异步任务，创建本轮唯一 Session
        var session = _searchPipelineManager.CreateSession(normalizedQueryText, parsed.ScopeKey);

        var calculatorCommand = TryBuildCalculatorCommand(query, parsed);
        if (calculatorCommand != null)
        {
            foreach (var command in _allCommands)
            {
                if (command.HasQueryPreview)
                {
                    command.SetQueryPreview(null, null);
                }
            }

            FilteredCommands.Clear();
            FilteredCommands.Add(calculatorCommand);
            SelectedCommand = calculatorCommand;
            CommandList.SelectedItem = SelectedCommand;
            _hasAppliedFilterOnce = true;
            _lastAppliedFilterText = normalizedQueryText;
            _lastAppliedFilterScopeKey = parsed.ScopeKey;
            OnPropertyChanged(nameof(VisibleCountText));
            OnPropertyChanged(nameof(FooterHint));
            return;
        }

        if (string.Equals(parsed.ScopeKey, SearchScopeYanyu, StringComparison.OrdinalIgnoreCase))
        {
            ApplyYanyuFilter(parsed.Term);
            return;
        }

        if (string.Equals(parsed.ScopeKey, SearchScopeFile, StringComparison.OrdinalIgnoreCase))
        {
            if (!_appSettings.EnableEverything)
            {
                IsFileSearching = false;
                FilteredCommands.Clear();
                SelectedCommand = null;
                CommandList.SelectedItem = null;
                _hasAppliedFilterOnce = true;
                _lastAppliedFilterText = normalizedQueryText;
                _lastAppliedFilterScopeKey = parsed.ScopeKey;
                OnPropertyChanged(nameof(VisibleCountText));
                OnPropertyChanged(nameof(FooterHint));
                OnPropertyChanged(nameof(IsFileSearchScopeActive));
                OnPropertyChanged(nameof(IsFileSearchEnabledInHomeView));
                return;
            }

            _ = ApplyFileSearchResultsAsync(session, parsed.Term);
            return;
        }

        IsFileSearching = false;

        if (TryGetPinnedSearchProviderCommand(parsed.ScopeKey, out var providerCommand))
        {
            var providerTerm = NormalizePinnedSearchProviderInlineQuery(providerCommand, parsed.ScopeKey, parsed.Term);
            _ = ApplyExtensionSearchProviderResultsAsync(session, providerCommand, parsed.ScopeKey, providerTerm);
            return;
        }

        if (TryResolveInlineSearchProviderCommand(parsed, query, out var inlineProviderCommand, out var inlineProviderTerm))
        {
            _ = ApplyExtensionSearchProviderResultsAsync(session, inlineProviderCommand, parsed.ScopeKey, inlineProviderTerm);
            return;
        }

        var allowRawQueryArgumentInline = AllowsRawQueryArgument(parsed.ScopeKey);
        var trimmedQuery = parsed.Term ?? string.Empty;

        foreach (var command in _allCommands)
        {
            if (command.HasQueryPreview)
            {
                command.SetQueryPreview(null, null);
            }
        }

        // Tier 1 (极速首帧 Instant Tier，0~20ms)：本地内存匹配即时上屏
        var matches = ComputeFilterMatches(parsed, trimmedQuery, allowRawQueryArgumentInline);

        var allowRawQueryArgument = allowRawQueryArgumentInline;
        var leadingCommand = matches.Count > 0 && !string.IsNullOrWhiteSpace(trimmedQuery)
            ? FindFirstSupportingQueryArgument(matches)
            : null;
        if (leadingCommand != null)
        {
            _activeQueryArgument = ExtractQueryArgument(leadingCommand, trimmedQuery, allowRawQueryArgument);
            if (string.IsNullOrWhiteSpace(_activeQueryArgument) && allowRawQueryArgument)
            {
                _activeQueryArgument = trimmedQuery;
            }
        }

        if (!string.IsNullOrWhiteSpace(trimmedQuery))
        {
            for (var i = 0; i < matches.Count; i++)
            {
                var command = matches[i];
                if (command.SupportsQueryArgument)
                {
                    ApplyQueryPreview(command, trimmedQuery, allowRawQueryArgument);
                }
            }
        }

        FilteredCommands.ReplaceAll(matches);

        if (string.Equals(parsed.ScopeKey, SearchScopeApplication, StringComparison.OrdinalIgnoreCase))
        {
            HostAssets.AppendLog($"Application scope filter: totalCommands={_allCommands.Count}, appCommands={_allCommands.Count(x => x.Source == CommandSource.Application)}, visible={matches.Count}, query='{parsed.Term}'.");
        }

        SelectedCommand = preserveSelection
            ? TryRestoreSelection(previousSelectedCommand, FilteredCommands) ?? FilteredCommands.FirstOrDefault()
            : FilteredCommands.FirstOrDefault();
        CommandList.SelectedItem = SelectedCommand;
        _hasAppliedFilterOnce = true;
        _lastAppliedFilterText = normalizedQueryText;
        _lastAppliedFilterScopeKey = parsed.ScopeKey;
        OnPropertyChanged(nameof(VisibleCountText));
        OnPropertyChanged(nameof(FooterHint));
        OnPropertyChanged(nameof(IsFileSearchScopeActive));
        OnPropertyChanged(nameof(IsFileSearchEnabledInHomeView));

        // Tier 2 (异步流式 Async Tier)：全范围 Everything 检索流式追加，增量平滑合并
        if (string.Equals(parsed.ScopeKey, SearchScopeAll, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(parsed.Term) &&
            _appSettings.EnableEverything)
        {
            _ = StreamAllScopeFileResultsAsync(session, parsed.Term, matches, preserveSelection, previousSelectedCommand);
        }
    }

    private List<CommandItem> ComputeFilterMatches(SearchQueryState parsed, string trimmedQuery, bool allowRawQueryArgument)
    {
        var scope = parsed.ScopeKey;
        var isEmpty = parsed.IsEmpty;
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var scored = _filterMatchScratch;
        scored.Clear();

        foreach (var command in EnumerateScopeCommands(scope))
        {
            if (!IsSearchResultEnabled(command))
            {
                continue;
            }

            if (!isEmpty)
            {
                var match = BuildCommandMatch(command, trimmedQuery, allowRawQueryArgument);
                if (!match.IsMatch)
                {
                    continue;
                }
            }

            if (!seenIds.Add(command.ExtensionId))
            {
                continue;
            }

            var score = ScoreSearchResult(command, trimmedQuery) + GetRecentlyAddedOrderingBoost(command, parsed);
            scored.Add(new ScoredCommand(command, score));
        }

        scored.Sort(_filterMatchSorter);

        var result = new List<CommandItem>(scored.Count);
        for (var i = 0; i < scored.Count; i++)
        {
            result.Add(scored[i].Command);
        }
        return result;
    }

    private static CommandItem? FindFirstSupportingQueryArgument(List<CommandItem> commands)
    {
        for (var i = 0; i < commands.Count; i++)
        {
            if (commands[i].SupportsQueryArgument)
            {
                return commands[i];
            }
        }
        return null;
    }

    private readonly record struct ScoredCommand(CommandItem Command, int Score);

    private IEnumerable<CommandItem> EnumerateScopeCommands(string scopeKey)
    {
        var baseCommands = _allCommands.Where(command => SearchScopeAllows(command, scopeKey));
        if (!string.Equals(scopeKey, SearchScopeAll, StringComparison.OrdinalIgnoreCase))
        {
            return baseCommands;
        }

        return baseCommands.Concat(BuildYanyuCommands(string.Empty));
    }

    private static CommandItem? TryRestoreSelection(CommandItem? previousSelectedCommand, IEnumerable<CommandItem> candidates)
    {
        if (previousSelectedCommand == null)
        {
            return null;
        }

        return candidates.FirstOrDefault(candidate =>
            candidate.ExtensionId.Equals(previousSelectedCommand.ExtensionId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(candidate.OpenTarget, previousSelectedCommand.OpenTarget, StringComparison.OrdinalIgnoreCase));
    }

    private void CycleSearchScope(int delta)
    {
        if (SearchScopes.Count == 0)
        {
            return;
        }

        var currentIndex = SelectedSearchScope == null ? 0 : SearchScopes.IndexOf(SelectedSearchScope);
        if (currentIndex < 0)
        {
            currentIndex = 0;
        }

        var nextIndex = (currentIndex + delta) % SearchScopes.Count;
        if (nextIndex < 0)
        {
            nextIndex = SearchScopes.Count - 1;
        }

        SelectedSearchScope = SearchScopes[nextIndex];
    }

    private SearchQueryState ParseSearchQuery(string? query)
    {
        var raw = (query ?? string.Empty).Trim();
        var scope = SelectedSearchScope?.Key ?? SearchScopeAll;
        if (!raw.StartsWith('@') && !raw.StartsWith('＠'))
        {
            return new SearchQueryState(scope, raw, string.IsNullOrWhiteSpace(raw));
        }

        var withoutAt = raw[1..].TrimStart();
        if (string.IsNullOrWhiteSpace(withoutAt))
        {
            return new SearchQueryState(scope, string.Empty, true);
        }

        var separator = withoutAt.IndexOf(' ');
        var token = separator < 0 ? withoutAt : withoutAt[..separator];
        var term = separator < 0 ? string.Empty : withoutAt[(separator + 1)..].Trim();
        return TryResolveSearchScopeAlias(token, out var parsedScope)
            ? new SearchQueryState(parsedScope, term, string.IsNullOrWhiteSpace(term))
            : new SearchQueryState(scope, raw, false);
    }

    private bool TryResolveSearchScopeAlias(string token, out string scope)
    {
        if (TryResolveBuiltInSearchScopeAlias(token, out scope))
        {
            return true;
        }

        foreach (var command in GetPinnedSearchScopeCommands())
        {
            if (!IsPinnedSearchScopeAliasMatch(command, token))
            {
                continue;
            }

            scope = SearchScopeTab.CreatePinnedCommandKey(command.ExtensionId);
            return true;
        }

        scope = string.Empty;
        return false;
    }

    private static bool TryResolveBuiltInSearchScopeAlias(string token, out string scope)
    {
        scope = token.Trim().ToLowerInvariant() switch
        {
            "all" or "全部" => SearchScopeAll,
            "extension" or "ext" or "扩展" or "插件" => SearchScopeExtension,
            "application" or "app" or "应用" or "程序" => SearchScopeApplication,
            "file" or "files" or "everything" or "文件" => SearchScopeFile,
            "system" or "sys" or "设置" or "系统" => SearchScopeSystem,
            "yanyu" or "yan" or "燕语" or "文本指令" => SearchScopeYanyu,
            _ => string.Empty
        };

        if (scope.Length == 0 && token.StartsWith("pin:", StringComparison.OrdinalIgnoreCase))
        {
            scope = token;
        }

        return scope.Length > 0;
    }

    private static bool IsPinnedSearchScopeAliasMatch(CommandItem command, string token)
    {
        var normalizedToken = NormalizeSearchScopeAlias(token);
        if (normalizedToken.Length == 0)
        {
            return false;
        }

        if (NormalizeSearchScopeAlias(command.Title).Equals(normalizedToken, StringComparison.OrdinalIgnoreCase) ||
            NormalizeSearchScopeAlias(command.ExtensionId).Equals(normalizedToken, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (command.QueryPrefixes.Any(prefix =>
                NormalizeSearchScopeAlias(prefix).Equals(normalizedToken, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return ResolveSearchProviderForCommand(command)?.Aliases.Any(alias =>
            NormalizeSearchScopeAlias(alias).Equals(normalizedToken, StringComparison.OrdinalIgnoreCase)) == true;
    }

    private string NormalizePinnedSearchProviderInlineQuery(CommandItem command, string scopeKey, string term)
    {
        if (!string.Equals(SelectedSearchScope?.Key, scopeKey, StringComparison.OrdinalIgnoreCase))
        {
            return term;
        }

        var trimmedTerm = term.TrimStart();
        if (trimmedTerm.Length == 0)
        {
            return string.Empty;
        }

        foreach (var alias in EnumeratePinnedSearchProviderActivationAliases(command))
        {
            if (trimmedTerm.Equals(alias, StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            if (trimmedTerm.StartsWith(alias + " ", StringComparison.OrdinalIgnoreCase) ||
                trimmedTerm.StartsWith(alias + "　", StringComparison.OrdinalIgnoreCase))
            {
                return trimmedTerm[(alias.Length + 1)..].TrimStart();
            }
        }

        return term;
    }

    private static IEnumerable<string> EnumeratePinnedSearchProviderActivationAliases(CommandItem command)
    {
        yield return command.Title;
        yield return command.ExtensionId;

        foreach (var prefix in command.QueryPrefixes)
        {
            if (!string.IsNullOrWhiteSpace(prefix))
            {
                yield return prefix;
            }
        }

        if (ResolveSearchProviderForCommand(command) is { Aliases: var aliases })
        {
            foreach (var alias in aliases)
            {
                if (!string.IsNullOrWhiteSpace(alias))
                {
                    yield return alias;
                }
            }
        }
    }

    public void OpenSearchProviderInLauncher(CommandItem command, string? initialQuery = null)
    {
        var provider = ResolveSearchProviderForCommand(command);
        if (provider == null)
        {
            LastRunMessage = $"当前扩展没有搜索提供器：{command.Title}";
            return;
        }

        if (IsHostedViewOpen)
        {
            CloseHostedView();
        }

        ShowPanel();
        Activate();

        var extensionScope = SearchScopes.FirstOrDefault(scope =>
            scope.Key.Equals(SearchScopeExtension, StringComparison.OrdinalIgnoreCase));
        if (extensionScope != null)
        {
            SelectedSearchScope = extensionScope;
        }

        var alias = command.QueryPrefixes.FirstOrDefault(static item => !string.IsNullOrWhiteSpace(item) && !item.Contains(' '))
                    ?? provider.Aliases.FirstOrDefault(static item => !string.IsNullOrWhiteSpace(item) && !item.Contains(' '))
                    ?? command.ExtensionId;
        var query = string.IsNullOrWhiteSpace(initialQuery)
            ? $"{alias} "
            : $"{alias} {initialQuery.Trim()}";

        SearchBox.Text = query;
        SearchBox.CaretIndex = SearchBox.Text.Length;
        SearchBox.Focus();
        ApplyFilter(SearchBox.Text);
        LastRunMessage = $"已打开搜索：{command.Title}";
        HostAssets.AppendLog($"Opened search provider in launcher: id={command.ExtensionId}, title={command.Title}, alias={alias}");
    }

    private bool TryResolveInlineSearchProviderCommand(SearchQueryState parsed, string? rawQuery, out CommandItem command, out string providerTerm)
    {
        command = null!;
        providerTerm = string.Empty;

        if (!string.Equals(parsed.ScopeKey, SearchScopeExtension, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rawInput = rawQuery ?? string.Empty;
        if (rawInput.StartsWith('@') || rawInput.StartsWith('＠'))
        {
            return false;
        }

        var trimmedStartInput = rawInput.TrimStart();
        if (trimmedStartInput.Length == 0)
        {
            return false;
        }

        foreach (var candidate in _allCommands.Where(item =>
                     item.Source == CommandSource.LocalExtension &&
                     IsExtensionEnabled(item.ExtensionId) &&
                     ResolveSearchProviderForCommand(item) != null))
        {
            foreach (var alias in EnumeratePinnedSearchProviderActivationAliases(candidate))
            {
                if (string.IsNullOrWhiteSpace(alias))
                {
                    continue;
                }

                if (!trimmedStartInput.StartsWith(alias + " ", StringComparison.OrdinalIgnoreCase) &&
                    !trimmedStartInput.StartsWith(alias + "　", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                command = candidate;
                providerTerm = trimmedStartInput[(alias.Length + 1)..];
                if (providerTerm.Length == 0)
                {
                    return true;
                }

                providerTerm = providerTerm.TrimStart();
                return true;
            }
        }

        return false;
    }

    private CommandItem? TryBuildCalculatorCommand(string? rawQuery, SearchQueryState parsed)
    {
        if (!string.Equals(parsed.ScopeKey, SearchScopeAll, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(parsed.ScopeKey, SearchScopeExtension, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var input = (rawQuery ?? string.Empty).Trim();
        if (input.Length == 0 ||
            input.Contains('@') ||
            input.Contains(' ') ||
            !Regex.IsMatch(input, @"^[0-9\.\+\-\*/%\(\)]+$"))
        {
            return null;
        }

        try
        {
            var table = new DataTable();
            var result = table.Compute(input, string.Empty);
            var normalized = Convert.ToString(result, CultureInfo.InvariantCulture) ?? string.Empty;
            if (normalized.Length == 0)
            {
                return null;
            }

            return new CommandItem(
                glyph: "=",
                title: normalized,
                subtitle: $"计算结果   ·   {input}",
                category: "计算",
                accentHex: "#FF3B82F6",
                openTarget: null,
                keywords: [input, normalized, "计算", "calculator"],
                source: CommandSource.Local,
                extensionId: $"calc::{input}",
                resultKind: ResultItemKind.Record,
                resultProviderTitle: "计算器");
        }
        catch
        {
            return null;
        }
    }



    private static string NormalizeSearchScopeAlias(string value)
    {
        return (value ?? string.Empty)
            .Trim()
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("　", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
    }

    private static bool SearchScopeAllows(CommandItem command, string scope)
    {
        return scope switch
        {
            SearchScopeAll => true,
            SearchScopeExtension => command.Source == CommandSource.LocalExtension || command.Source == CommandSource.WebSearch,
            SearchScopeStore => command.Source == CommandSource.Cloud,
            SearchScopeApplication => command.Source == CommandSource.Application,
            SearchScopeFile => command.Source == CommandSource.File,
            SearchScopeSystem => command.Category.Contains("系统", StringComparison.OrdinalIgnoreCase),
            SearchScopeYanyu => command.Category.Contains("燕语", StringComparison.OrdinalIgnoreCase),
            _ when SearchScopeTab.TryParsePinnedCommandScope(scope, out var extensionId) =>
                command.ExtensionId.Equals(extensionId, StringComparison.OrdinalIgnoreCase),
            _ => true
        };
    }

    private int ScoreSearchResult(CommandItem command, string query)
    {
        var score = string.IsNullOrWhiteSpace(query)
            ? 0
            : BuildCommandMatch(command, query).Priority;
        score += command.Source == CommandSource.WebSearch ? 80 : 0;
        score += _searchUsageMemory.Score(command.ExtensionId);
        return score;
    }

    private int GetRecentlyAddedOrderingBoost(CommandItem command, SearchQueryState query)
    {
        if (command.Source != CommandSource.LocalExtension)
        {
            return 0;
        }

        if (!query.IsEmpty &&
            !string.Equals(query.ScopeKey, SearchScopeExtension, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(query.ScopeKey, SearchScopeAll, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var index = (_appSettings.RecentlyAddedExtensionIds ?? []).FindIndex(id =>
            id.Equals(command.ExtensionId, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? 0 : Math.Max(0, 5000 - index);
    }

    private void UpdateSearchScopeCounts(SearchQueryState parsed)
    {
        if (SearchScopes.Count == 0)
        {
            return;
        }

        var isFileScopeSelected = string.Equals(SelectedSearchScope?.Key, SearchScopeFile, StringComparison.OrdinalIgnoreCase);
        var currentPinnedProvider = string.IsNullOrEmpty(parsed.ScopeKey) ? null : (SelectedSearchScope?.Key ?? string.Empty);

        if (!parsed.IsEmpty)
        {
            var term = parsed.Term ?? string.Empty;
            var countsByKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            // 燕语数量单次计算，避免在命令遍历中重复执行数百次 LINQ
            countsByKey[SearchScopeYanyu] = CountYanyuResults(term);

            var allowRawLookup = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var scope in SearchScopes)
            {
                allowRawLookup[scope.Key] = AllowsRawQueryArgument(scope.Key);
            }

            foreach (var command in _allCommands)
            {
                if (!IsSearchResultEnabled(command))
                {
                    continue;
                }

                var defaultMatch = BuildCommandMatch(command, term, false);
                var rawAllowedMatch = defaultMatch;
                var hasComputedRaw = false;

                foreach (var scope in SearchScopes)
                {
                    if (string.Equals(scope.Key, SearchScopeFile, StringComparison.OrdinalIgnoreCase))
                    {
                        if (isFileScopeSelected && command.Source == CommandSource.File)
                        {
                            countsByKey[scope.Key] = (countsByKey.TryGetValue(scope.Key, out var existing) ? existing : 0) + 1;
                        }
                        continue;
                    }
                    if (string.Equals(scope.Key, SearchScopeYanyu, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    if (TryGetPinnedSearchProviderCommand(scope.Key, out _))
                    {
                        if (string.Equals(scope.Key, currentPinnedProvider, StringComparison.OrdinalIgnoreCase))
                        {
                            countsByKey[scope.Key] = (countsByKey.TryGetValue(scope.Key, out var p) ? p : 0) + 1;
                        }
                        continue;
                    }

                    if (!SearchScopeAllows(command, scope.Key))
                    {
                        continue;
                    }

                    var allowRaw = allowRawLookup.TryGetValue(scope.Key, out var ar) && ar;
                    bool isMatch;
                    if (!allowRaw)
                    {
                        isMatch = defaultMatch.IsMatch;
                    }
                    else
                    {
                        if (!hasComputedRaw)
                        {
                            rawAllowedMatch = BuildCommandMatch(command, term, true);
                            hasComputedRaw = true;
                        }
                        isMatch = rawAllowedMatch.IsMatch;
                    }

                    if (isMatch)
                    {
                        countsByKey[scope.Key] = (countsByKey.TryGetValue(scope.Key, out var v) ? v : 0) + 1;
                    }
                }
            }

            foreach (var scope in SearchScopes)
            {
                scope.Count = countsByKey.TryGetValue(scope.Key, out var count) ? count : 0;
            }
            return;
        }

        foreach (var scope in SearchScopes)
        {
            if (string.Equals(scope.Key, SearchScopeFile, StringComparison.OrdinalIgnoreCase))
            {
                scope.Count = isFileScopeSelected ? FilteredCommands.Count(command => command.Source == CommandSource.File) : 0;
                continue;
            }
            if (string.Equals(scope.Key, SearchScopeYanyu, StringComparison.OrdinalIgnoreCase))
            {
                scope.Count = CountYanyuResults(string.Empty);
                continue;
            }
            if (TryGetPinnedSearchProviderCommand(scope.Key, out _))
            {
                scope.Count = string.Equals(scope.Key, currentPinnedProvider, StringComparison.OrdinalIgnoreCase) ? FilteredCommands.Count : 0;
                continue;
            }
            scope.Count = 0;
        }
    }

    private bool IsSearchResultEnabled(CommandItem command) =>
        command.Source == CommandSource.Cloud || IsExtensionEnabled(command.ExtensionId);

    private static bool AllowsRawQueryArgument(string scopeKey) => false;

    private void ApplyQueryPreview(CommandItem command, string rawInput, bool allowRawQueryArgument)
    {
        var argument = ExtractQueryArgument(command, rawInput, allowRawQueryArgument);
        if (string.IsNullOrWhiteSpace(argument))
        {
            if (!allowRawQueryArgument)
            {
                command.SetQueryPreview(null, null);
                return;
            }

            argument = rawInput.Trim();
        }

        var compactArgument = argument.Length <= 18 ? argument : argument[..18] + "...";
        var subtitle = BuildQueryPreviewText(command, compactArgument);
        command.SetQueryPreview(subtitle, null);
    }

    private static string BuildQueryPreviewText(CommandItem command, string argument)
    {
        if (!string.IsNullOrWhiteSpace(command.QueryTargetTemplate))
        {
            var searchName = command.Title.Replace("搜索", string.Empty, StringComparison.Ordinal).Trim();
            if (string.IsNullOrWhiteSpace(searchName))
            {
                searchName = command.Title;
            }

            return $"用{searchName}搜索「{argument}」";
        }

        if (command.HasHostedView)
        {
            return $"打开{command.Title}并填入「{argument}」";
        }

        if (command.HasScriptEntry)
        {
            return $"将「{argument}」传给{command.Title}";
        }

        return $"使用{command.Title}处理「{argument}」";
    }

    private void MoveSelection(int delta)
    {
        if (FilteredCommands.Count == 0)
        {
            return;
        }

        var currentIndex = SelectedCommand == null ? -1 : FilteredCommands.IndexOf(SelectedCommand);
        var nextIndex = currentIndex + delta;
        if (nextIndex < 0)
        {
            nextIndex = 0;
        }
        else if (nextIndex >= FilteredCommands.Count)
        {
            nextIndex = FilteredCommands.Count - 1;
        }

        SelectedCommand = FilteredCommands[nextIndex];
        CommandList.SelectedItem = SelectedCommand;
        CommandList.ScrollIntoView(SelectedCommand);
    }

    private void AddCurrentCommandToPinnedSearchScopes()
    {
        var sourceCommand = SelectedCommand != null && !IsInternalCommand(SelectedCommand)
            ? SelectedCommand
            : _lastActionableCommand;
        if (sourceCommand == null)
        {
            SyncStatus = "没有可固定到顶部的扩展。";
            return;
        }

        var command = ResolveRunnableCommand(sourceCommand);
        if (command.IsFileSystemResult)
        {
            SyncStatus = "文件结果不支持固定为顶部扩展标签。";
            return;
        }

        var settings = AppSettingsStore.Load();
        settings.PinnedSearchScopeCommandIds ??= [];
        if (settings.PinnedSearchScopeCommandIds.Contains(command.ExtensionId, StringComparer.OrdinalIgnoreCase))
        {
            LastRunMessage = $"顶部已固定：{command.Title}";
            return;
        }

        settings.PinnedSearchScopeCommandIds.Add(command.ExtensionId);
        settings.SearchScopeConfigs ??= new();
        settings.SearchScopeConfigs.Add(new SearchScopeConfigItem
        {
            Key = $"pinned_{command.ExtensionId}",
            Label = command.Title,
            IsVisible = true,
            IsPinned = true
        });
        AppSettingsStore.Save(settings);
        NotifyQuickPanelSettingsChanged("search-scope-pinned", refreshYanmOverlay: false);
        _appSettings = AppSettingsStore.Load();
        _windowBoundExtensionsService.Reload(_appSettings.WindowBindings);
        ReloadSearchScopes();
        SelectedSearchScope = SearchScopes.FirstOrDefault(scope => scope.Key.Equals(SearchScopeTab.CreatePinnedCommandKey(command.ExtensionId), StringComparison.OrdinalIgnoreCase))
            ?? SelectedSearchScope;
        LastRunMessage = $"已固定到顶部：{command.Title}";
        SyncStatus = $"已固定到顶部标签：{command.Title}";
    }

    private void RemovePinnedSearchScope(SearchScopeTab scope)
    {
        if (!scope.IsPinnedCommand || string.IsNullOrWhiteSpace(scope.PinnedCommandId))
        {
            return;
        }

        var settings = AppSettingsStore.Load();
        settings.PinnedSearchScopeCommandIds ??= [];
        var removed = settings.PinnedSearchScopeCommandIds.RemoveAll(id => id.Equals(scope.PinnedCommandId, StringComparison.OrdinalIgnoreCase));
        if (settings.SearchScopeConfigs != null)
        {
            settings.SearchScopeConfigs.RemoveAll(c => c.IsPinned && string.Equals(c.Key, $"pinned_{scope.PinnedCommandId}", StringComparison.OrdinalIgnoreCase));
        }
        if (removed == 0)
        {
            return;
        }

        AppSettingsStore.Save(settings);
        NotifyQuickPanelSettingsChanged("search-scope-unpinned", refreshYanmOverlay: false);
        _appSettings = AppSettingsStore.Load();
        _windowBoundExtensionsService.Reload(_appSettings.WindowBindings);
        ReloadSearchScopes();
        SelectedSearchScope = SearchScopes.FirstOrDefault(item => item.Key.Equals(scope.Key, StringComparison.OrdinalIgnoreCase)) == null
            ? SearchScopes.FirstOrDefault(item => !item.IsPinnedCommand) ?? SearchScopes.FirstOrDefault()
            : SelectedSearchScope;
        LastRunMessage = $"已移除顶部标签：{scope.Label}";
        SyncStatus = $"已移除顶部标签：{scope.Label}";
    }

    private void AddCurrentCommandToQuickPanel()
    {
        var sourceCommand = SelectedCommand != null && !IsInternalCommand(SelectedCommand)
            ? SelectedCommand
            : _lastActionableCommand;
        
        if (sourceCommand == null) return;
        var command = ResolveRunnableCommand(sourceCommand);
        if (command.IsFileSystemResult)
        {
            SyncStatus = "文件结果不能加入背包。";
            return;
        }

        var settings = AppSettingsStore.Load();
        var group = settings.QuickPanelGlobalGroups
            .FirstOrDefault(item => string.Equals(item.Id, settings.SelectedQuickPanelGlobalGroupId, StringComparison.OrdinalIgnoreCase))
            ?? settings.QuickPanelGlobalGroups.FirstOrDefault();
        if (group == null)
        {
            SyncStatus = "背包分组未初始化，无法添加。";
            return;
        }

        group.SlotItems ??= [];
        while (group.SlotItems.Count < 12)
        {
            group.SlotItems.Add(null);
        }

        if (group.SlotItems.Any(slot =>
                slot != null &&
                ((!slot.IsFolder && string.Equals(slot.ExtensionId, command.ExtensionId, StringComparison.OrdinalIgnoreCase)) ||
                 (slot.IsFolder && slot.FolderExtensionIds.Any(id => string.Equals(id, command.ExtensionId, StringComparison.OrdinalIgnoreCase))))))
        {
            LastRunMessage = $"背包中已存在：{command.Title}";
            _quickPanel?.ReloadSlots();
            return;
        }

        var index = group.SlotItems.FindIndex(static item => item == null);
        if (index >= 0)
        {
            group.SlotItems[index] = new QuickPanelSlotItem
            {
                ExtensionId = command.ExtensionId
            };
            group.Slots = group.SlotItems.Select(static item => item != null && !item.IsFolder ? item.ExtensionId : null).ToList();
            if (settings.QuickPanelSlots.Count > index)
            {
                settings.QuickPanelSlots[index] = command.ExtensionId;
            }

            AppSettingsStore.Save(settings);
            _quickPanel?.ReloadSlots();
            LastRunMessage = $"已添加到背包「{group.Name}」第 {index + 1} 个槽位：{command.Title}";
        }
        else
        {
            SyncStatus = $"背包分组「{group.Name}」已满（12 个槽位），请先移除旧扩展。";
        }
    }

    private IEnumerable<CommandItem> GetPinnedSearchScopeCommands()
    {
        var pinnedIds = _appSettings.PinnedSearchScopeCommandIds ?? [];
        foreach (var pinnedId in pinnedIds)
        {
            var command = _allCommands.FirstOrDefault(item => item.ExtensionId.Equals(pinnedId, StringComparison.OrdinalIgnoreCase));
            if (command != null && !IsInternalCommand(command))
            {
                yield return command;
            }
        }
    }

    private bool TryGetPinnedSearchProviderCommand(string scopeKey, out CommandItem command)
    {
        command = null!;
        if (!SearchScopeTab.TryParsePinnedCommandScope(scopeKey, out var extensionId))
        {
            return false;
        }

        var matchedCommand = _allCommands.FirstOrDefault(item =>
            item.ExtensionId.Equals(extensionId, StringComparison.OrdinalIgnoreCase) &&
            ResolveSearchProviderForCommand(item) != null);
        if (matchedCommand == null)
        {
            return false;
        }

        command = matchedCommand;
        return true;
    }

    private static CommandSearchProviderDefinition? ResolveSearchProviderForCommand(CommandItem command)
    {
        if (command.SearchProvider != null)
        {
            return command.SearchProvider;
        }

        if (command.Source == CommandSource.LocalExtension &&
            command.OpenTarget is { Length: > 0 } openTarget &&
            Directory.Exists(openTarget))
        {
            return new CommandSearchProviderDefinition(
                "folder",
                openTarget,
                IncludeSubdirectories: true,
                IncludeFiles: true,
                IncludeDirectories: false,
                MaxResults: 128,
                Aliases: []);
        }

        return null;
    }

    private void ReloadSearchScopes()
    {
        var currentKey = SelectedSearchScope?.Key ?? SearchScopeAll;
        SearchScopes.Clear();
        foreach (var scope in BuildSearchScopes())
        {
            SearchScopes.Add(scope);
        }

        SelectedSearchScope = SearchScopes.FirstOrDefault(scope => scope.Key.Equals(currentKey, StringComparison.OrdinalIgnoreCase))
            ?? SearchScopes.FirstOrDefault();
    }

    private void CopyExtensionMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var sourceCommand = SelectedCommand != null && !IsInternalCommand(SelectedCommand)
            ? SelectedCommand
            : _lastActionableCommand;
        if (sourceCommand == null)
        {
            return;
        }

        var command = ResolveRunnableCommand(sourceCommand);
        if (command.IsFileSystemResult)
        {
            return;
        }

        SetQuickPanelClipboard(command, isCut: false, sourceSlot: null);
    }

    private void CutExtensionMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var sourceCommand = SelectedCommand != null && !IsInternalCommand(SelectedCommand)
            ? SelectedCommand
            : _lastActionableCommand;
        if (sourceCommand == null)
        {
            return;
        }

        var command = ResolveRunnableCommand(sourceCommand);
        if (command.IsFileSystemResult)
        {
            return;
        }

        SetQuickPanelClipboard(command, isCut: true, sourceSlot: null);
    }

    private void PasteExtensionMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (TryImportExtensionFromSystemClipboard(out var command, out var message) && command != null)
        {
            LastRunMessage = $"已从剪贴板导入扩展：{command.Title}";
            QueueBackgroundWebDavSync("extension-import-clipboard");
            return;
        }

        if (!string.IsNullOrWhiteSpace(message))
        {
            SyncStatus = message;
        }
    }

    private Task SetSelectedExtensionShortcutAsync()
    {
        var sourceCommand = SelectedCommand != null && !IsInternalCommand(SelectedCommand)
            ? SelectedCommand
            : _lastActionableCommand;
        if (sourceCommand == null)
        {
            SyncStatus = "没有可设置快捷键的扩展。";
            return Task.CompletedTask;
        }

        var extension = ResolveRunnableCommand(sourceCommand);
        if (extension.Source != CommandSource.LocalExtension)
        {
            SyncStatus = "当前选中项不是本地小程序，不能直接设置快捷键。";
            return Task.CompletedTask;
        }

        var dialog = new HotkeyCaptureWindow(
            "设置快捷键",
            "窗口激活后，直接按一次新的组合键即可完成录制。需要清除时可点“清空”。",
            extension.GlobalShortcut ?? string.Empty,
            allowEmpty: true)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return Task.CompletedTask;
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(dialog.ShortcutText) &&
                (!TryParseHotkey(dialog.ShortcutText, out _, out _) || IsDoubleTapShortcut(dialog.ShortcutText)))
            {
                SyncStatus = "快捷键格式无效。示例：Ctrl+Alt+T";
                return Task.CompletedTask;
            }

            var updated = LocalExtensionCatalog.SetGlobalShortcut(extension.ExtensionId, dialog.ShortcutText);
            UpsertLocalExtensionCommand(updated);
            ApplyFilter(SearchBox.Text);
            SelectedCommand = _allCommands.FirstOrDefault(x => x.ExtensionId.Equals(updated.ExtensionId, StringComparison.OrdinalIgnoreCase));
            CommandList.SelectedItem = SelectedCommand;
            LastRunMessage = string.IsNullOrWhiteSpace(updated.GlobalShortcut)
                ? $"已清除快捷键：{updated.Title}"
                : $"已设置快捷键：{updated.Title} -> {updated.GlobalShortcut}";
            QueueBackgroundWebDavSync("extension-shortcut");
        }
        catch (Exception ex)
        {
            SyncStatus = $"设置快捷键失败：{FormatExceptionMessage(ex)}";
        }

        return Task.CompletedTask;
    }

    private async Task ApplyFileSearchResultsAsync(SearchSession session, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            IsFileSearching = false;
            if (!_searchPipelineManager.IsActive(session))
            {
                return;
            }

            FilteredCommands.Clear();
            SelectedCommand = null;
            CommandList.SelectedItem = null;
            OnPropertyChanged(nameof(VisibleCountText));
            OnPropertyChanged(nameof(FooterHint));
            OnPropertyChanged(nameof(IsFileSearchScopeActive));
            OnPropertyChanged(nameof(IsFileSearchEnabledInHomeView));
            return;
        }

        try
        {
            // 防抖缓冲：给连续输入/中文拼音组字预留 120ms 缓冲，避免每个拼音字母都发起 Everything IPC
            await Task.Delay(120, session.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!_searchPipelineManager.IsActive(session) ||
            !string.Equals(_activeFilterScopeKey, SearchScopeFile, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        FileSearchingText = !EverythingSearchService.IsDatabaseLoaded()
            ? "Everything 正在初始化索引，请稍候..."
            : "搜索中...";
        IsFileSearching = true;

        var (response, fileCommands) = await Task.Run(() =>
        {
            if (session.Token.IsCancellationRequested)
            {
                return (new EverythingSearchResponse { Success = false }, (List<CommandItem>)[]);
            }

            if (!EverythingSearchService.IsIpcReachable())
            {
                EverythingRuntimeService.EnsureRunning();
            }

            var resp = EverythingSearchService.Search(query, 64, session.Token);
            if (!resp.Success || session.Token.IsCancellationRequested)
            {
                return (resp, (List<CommandItem>)[]);
            }

            var commands = resp.Results
                .Select(BuildResultItemFromEverythingResult)
                .Select(BuildCommandFromResultItem)
                .ToList();

            return (resp, commands);
        }, session.Token);

        if (!_searchPipelineManager.IsActive(session) ||
            !string.Equals(_activeFilterScopeKey, SearchScopeFile, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        IsFileSearching = false;

        if (!response.Success || session.Token.IsCancellationRequested)
        {
            if (!string.IsNullOrWhiteSpace(response.ErrorMessage))
            {
                SyncStatus = response.ErrorMessage;
            }

            MaybePromptEverythingManualInitialization(query);

            FilteredCommands.Clear();
            SelectedCommand = null;
            CommandList.SelectedItem = null;
            OnPropertyChanged(nameof(VisibleCountText));
            OnPropertyChanged(nameof(FooterHint));
            OnPropertyChanged(nameof(IsFileSearchScopeActive));
            OnPropertyChanged(nameof(IsFileSearchEnabledInHomeView));
            return;
        }

        FilteredCommands.ReplaceAll(fileCommands);

        SelectedCommand = FilteredCommands.FirstOrDefault();
        CommandList.SelectedItem = SelectedCommand;
        OnPropertyChanged(nameof(VisibleCountText));
        OnPropertyChanged(nameof(FooterHint));
        OnPropertyChanged(nameof(IsFileSearchScopeActive));
        OnPropertyChanged(nameof(IsFileSearchEnabledInHomeView));

        if (fileCommands.Count == 0)
        {
            MaybePromptEverythingManualInitialization(query);
        }

        _ = LoadFileIconsAsync(session, fileCommands);
    }

    private async Task StreamAllScopeFileResultsAsync(
        SearchSession session,
        string query,
        List<CommandItem> baseMatches,
        bool preserveSelection,
        CommandItem? previousSelectedCommand)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        try
        {
            // 防抖缓冲：全范围模式下给文件检索预留 150ms 缓冲，连续按键时即刻取消不抢占后台锁
            await Task.Delay(150, session.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var response = await Task.Run(() =>
        {
            if (session.Token.IsCancellationRequested)
            {
                return new EverythingSearchResponse { Success = false };
            }
            return EverythingSearchService.Search(query, 64, session.Token);
        }, session.Token);

        if (!_searchPipelineManager.IsActive(session) ||
            !string.Equals(_activeFilterScopeKey, SearchScopeAll, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(SearchBox.Text ?? string.Empty, session.Query, StringComparison.Ordinal))
        {
            return;
        }

        if (!response.Success || session.Token.IsCancellationRequested)
        {
            return;
        }

        var mergedScratch = _filterMatchScratch;
        mergedScratch.Clear();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allScopeQ = new SearchQueryState(SearchScopeAll, query, string.IsNullOrWhiteSpace(query));

        foreach (var command in baseMatches)
        {
            if (seenIds.Add(command.ExtensionId))
            {
                var score = ScoreSearchResult(command, query) + GetRecentlyAddedOrderingBoost(command, allScopeQ);
                mergedScratch.Add(new ScoredCommand(command, score));
            }
        }

        foreach (var result in response.Results)
        {
            var command = BuildCommandFromResultItem(BuildResultItemFromEverythingResult(result));
            if (seenIds.Add(command.ExtensionId))
            {
                var score = ScoreSearchResult(command, query) + GetRecentlyAddedOrderingBoost(command, allScopeQ);
                mergedScratch.Add(new ScoredCommand(command, score));
            }
        }

        mergedScratch.Sort(_filterMatchSorter);

        var merged = new List<CommandItem>(mergedScratch.Count);
        for (var i = 0; i < mergedScratch.Count; i++)
        {
            merged.Add(mergedScratch[i].Command);
        }

        if (!_searchPipelineManager.IsActive(session))
        {
            return;
        }

        var currentSelectedBeforeMerge = SelectedCommand ?? previousSelectedCommand;

        FilteredCommands.ReplaceAll(merged);

        SelectedCommand = TryRestoreSelection(currentSelectedBeforeMerge, FilteredCommands) ??
                          (preserveSelection ? TryRestoreSelection(previousSelectedCommand, FilteredCommands) : null) ??
                          FilteredCommands.FirstOrDefault();
        CommandList.SelectedItem = SelectedCommand;
        OnPropertyChanged(nameof(VisibleCountText));
        OnPropertyChanged(nameof(FooterHint));

        var fileCommands = merged.Where(static item => item.Source == CommandSource.File).ToList();
        if (fileCommands.Count > 0 && !session.Token.IsCancellationRequested)
        {
            _ = LoadFileIconsAsync(session, fileCommands);
        }
    }

    private async Task ApplyExtensionSearchProviderResultsAsync(
        SearchSession session,
        CommandItem providerCommand,
        string scopeKey,
        string query)
    {
        foreach (var command in _allCommands)
        {
            if (command.HasQueryPreview)
            {
                command.SetQueryPreview(null, null);
            }
        }

        var searchProvider = ResolveSearchProviderForCommand(providerCommand);
        if (searchProvider == null)
        {
            if (!_searchPipelineManager.IsActive(session))
            {
                return;
            }

            FilteredCommands.Clear();
            SelectedCommand = null;
            CommandList.SelectedItem = null;
            OnPropertyChanged(nameof(VisibleCountText));
            OnPropertyChanged(nameof(FooterHint));
            return;
        }

        try
        {
            // 防抖缓冲：扩展提供器预留 150ms 缓冲
            await Task.Delay(150, session.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        ResultProviderResponse response;
        try
        {
            response = await ExtensionSearchProviderService.SearchAsync(providerCommand, searchProvider, query, session.Token);
        }
        catch (OperationCanceledException)
        {
            // 新输入取消了本次搜索（含后台目录遍历中断），静默放弃本批结果
            return;
        }

        if (!_searchPipelineManager.IsActive(session) ||
            !string.Equals(_activeFilterScopeKey, scopeKey, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!response.Success || session.Token.IsCancellationRequested)
        {
            if (!string.IsNullOrWhiteSpace(response.ErrorMessage))
            {
                SyncStatus = response.ErrorMessage;
            }

            FilteredCommands.Clear();
            SelectedCommand = null;
            CommandList.SelectedItem = null;
            OnPropertyChanged(nameof(VisibleCountText));
            OnPropertyChanged(nameof(FooterHint));
            return;
        }

        var resultCommands = response.Results
            .Select(result => BuildCommandFromResultItem(result with
            {
                Subtitle = string.IsNullOrWhiteSpace(result.ProviderTitle)
                    ? $"{result.Subtitle}   ·   来源：{providerCommand.Title}"
                    : result.Subtitle
            }))
            .ToList();

        FilteredCommands.ReplaceAll(resultCommands);

        SelectedCommand = FilteredCommands.FirstOrDefault();
        CommandList.SelectedItem = SelectedCommand;
        OnPropertyChanged(nameof(VisibleCountText));
        OnPropertyChanged(nameof(FooterHint));

        if (resultCommands.Count == 0)
        {
            SyncStatus = $"{providerCommand.Title} 没有找到匹配结果。";
        }

        _ = LoadFileIconsAsync(session, resultCommands);
    }

    private static CommandItem BuildCommandFromResultItem(ResultProviderItem result)
    {
        return new CommandItem(
            glyph: string.Empty,
            title: result.Title,
            subtitle: result.Subtitle,
            category: result.Kind == ResultItemKind.Folder ? "文件夹" : result.Kind == ResultItemKind.File ? "文件" : "结果",
            accentHex: result.AccentHex,
            openTarget: result.OpenTarget,
            keywords: result.Keywords,
            source: CommandSource.File,
            extensionId: $"{ExtensionIdPrefixes.SearchResult}{result.Id}",
            resultKind: result.Kind,
            resultProviderTitle: result.ProviderTitle);
    }

    private static ResultProviderItem BuildResultItemFromEverythingResult(EverythingSearchResult result)
    {
        var subtitle = string.IsNullOrWhiteSpace(result.SizeText)
            ? result.DirectoryPath
            : $"{result.DirectoryPath}   ·   {result.SizeText}";

        return new ResultProviderItem(
            Id: result.FullPath,
            Title: result.Name,
            Subtitle: subtitle,
            Kind: result.IsFolder ? ResultItemKind.Folder : ResultItemKind.File,
            OpenTarget: result.FullPath,
            Keywords: [result.FullPath, result.DirectoryPath, result.Name],
            AccentHex: result.IsFolder ? "#FF3B82F6" : "#FF4B5563",
            ProviderTitle: "Everything 文件");
    }

    private bool IsFileIconLoadScopeActive()
    {
        return string.Equals(_activeFilterScopeKey, SearchScopeFile, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(_activeFilterScopeKey, SearchScopeAll, StringComparison.OrdinalIgnoreCase) ||
               TryGetPinnedSearchProviderCommand(_activeFilterScopeKey, out _);
    }

    private async Task LoadFileIconsAsync(SearchSession session, IReadOnlyList<CommandItem> commands)
    {
        if (commands.Count == 0) return;

        await Task.Run(() =>
        {
            var updates = new List<(CommandItem Command, ImageSource Icon)>();

            for (var i = 0; i < commands.Count; i++)
            {
                if (!_searchPipelineManager.IsActive(session) ||
                    session.Token.IsCancellationRequested ||
                    !IsFileIconLoadScopeActive())
                {
                    return;
                }

                var command = commands[i];
                if (string.IsNullOrWhiteSpace(command.OpenTarget) ||
                    command.OpenTarget.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var isFolder = command.ResultKind == ResultItemKind.Folder;
                var icon = NativeFileIconService.GetIcon(command.OpenTarget, isFolder);
                if (icon != null)
                {
                    updates.Add((command, icon));
                }

                // 前 8 个（首屏可视区）或每 16 个或结束时，批量推送到 UI 一次，使用 Background 优先级不抢占打字输入
                if (updates.Count > 0 && (i == 7 || updates.Count >= 16 || i == commands.Count - 1))
                {
                    var batch = updates.ToArray();
                    updates.Clear();

                    if (!_searchPipelineManager.IsActive(session) ||
                        session.Token.IsCancellationRequested ||
                        !IsFileIconLoadScopeActive())
                    {
                        return;
                    }

                    Dispatcher.InvokeAsync(() =>
                    {
                        if (!_searchPipelineManager.IsActive(session) ||
                            session.Token.IsCancellationRequested ||
                            !IsFileIconLoadScopeActive())
                        {
                            return;
                        }

                        foreach (var (cmd, ic) in batch)
                        {
                            cmd.SetIconSource(ic);
                        }
                    }, System.Windows.Threading.DispatcherPriority.Background);
                }
            }
        }, session.Token);
    }

    private void MaybePromptEverythingManualInitialization(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        if (EverythingSearchService.IsDatabaseLoaded())
        {
            return;
        }

        if (!EverythingRuntimeService.HasBundledRuntime())
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (now - _lastFileSearchManualInitPromptAt < TimeSpan.FromSeconds(10))
        {
            return;
        }

        _lastFileSearchManualInitPromptAt = now;
        SyncStatus = EverythingRuntimeService.IsProcessRunning()
            ? "Everything 已启动，但索引尚未完成初始化。"
            : "Everything 尚未完成初始化，文件搜索暂时不可用。";
        HostAssets.AppendLog("File search prompt: Everything runtime is not initialized; showing manual initialization hint.");
        var result = System.Windows.MessageBox.Show(
            "文件搜索引擎已启动，但当前还没有完成索引初始化。\n\n点击“是”后，燕子会直接打开 Everything，让它弹出原生授权/初始化提示。完成后回到燕子重新搜索即可。",
            "文件搜索需要初始化",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        if (EverythingRuntimeService.ShowInteractiveSetup())
        {
            SyncStatus = "已打开 Everything 初始化窗口，请按提示完成授权。";
        }
        else
        {
            SyncStatus = "无法打开 Everything 初始化窗口，请手动从托盘打开一次 Everything。";
        }
    }

    private bool TryShowNativeFileContextMenu()
    {
        var command = SelectedCommand;
        if (command?.IsFileSystemResult != true || string.IsNullOrWhiteSpace(command.OpenTarget))
        {
            return false;
        }

        return ShowFileResultContextMenu();
    }

    private bool ShowFileResultContextMenu(FrameworkElement? placementTarget = null, bool keyboardInvoked = false)
    {
        var command = SelectedCommand;
        if (command?.IsFileSystemResult != true || string.IsNullOrWhiteSpace(command.OpenTarget))
        {
            return false;
        }

        if (TryFindResource("FileResultContextMenu") is not System.Windows.Controls.ContextMenu menu)
        {
            return false;
        }

        menu.PlacementTarget = placementTarget ?? CommandList;
        menu.Placement = keyboardInvoked && placementTarget != null
            ? System.Windows.Controls.Primitives.PlacementMode.Right
            : System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen = true;
        return true;
    }

    private bool ShowGenericResultContextMenu(FrameworkElement? placementTarget = null, bool keyboardInvoked = false)
    {
        var command = SelectedCommand;
        if (command?.IsProviderResult != true || command.IsFileSystemResult)
        {
            return false;
        }

        if (TryFindResource("GenericResultContextMenu") is not System.Windows.Controls.ContextMenu menu)
        {
            return false;
        }

        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            if (item.Header is not string header)
            {
                continue;
            }

            if (header.Contains("复制目标", StringComparison.Ordinal))
            {
                item.IsEnabled = !string.IsNullOrWhiteSpace(command.OpenTarget);
            }
        }

        menu.PlacementTarget = placementTarget ?? CommandList;
        menu.Placement = keyboardInvoked && placementTarget != null
            ? System.Windows.Controls.Primitives.PlacementMode.Right
            : System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen = true;
        return true;
    }

    private async Task ExecuteGenericResultAsync(CommandItem command)
    {
        if (!string.IsNullOrWhiteSpace(command.OpenTarget))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = command.OpenTarget,
                    UseShellExecute = true
                });
                LastRunMessage = $"已运行结果：{command.Title}";
                return;
            }
            catch (Exception ex)
            {
                SyncStatus = $"执行结果失败：{FormatExceptionMessage(ex)}";
                return;
            }
        }

        ShowGenericResultDetails(command);
        await Task.CompletedTask;
    }

    private void ShowGenericResultDetails(CommandItem command)
    {
        var detailWindow = new Window
        {
            Title = command.Title,
            Owner = this,
            Width = 640,
            Height = 420,
            MinWidth = 480,
            MinHeight = 320,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(28, 28, 28)),
            Foreground = System.Windows.Media.Brushes.White
        };

        var contentBox = new System.Windows.Controls.TextBox
        {
            Text = BuildGenericResultDetailText(command),
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(28, 28, 28)),
            Foreground = System.Windows.Media.Brushes.White,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(16)
        };

        detailWindow.Content = contentBox;
        detailWindow.ShowDialog();
    }

    private static string BuildGenericResultDetailText(CommandItem command)
    {
        var lines = new List<string>
        {
            $"标题：{command.Title}",
            $"类型：{command.ResultKind}",
            $"摘要：{command.Subtitle}"
        };

        if (!string.IsNullOrWhiteSpace(command.OpenTarget))
        {
            lines.Add($"目标：{command.OpenTarget}");
        }

        if (command.Keywords.Count > 0)
        {
            lines.Add($"关键词：{string.Join(", ", command.Keywords)}");
        }

        if (!string.IsNullOrWhiteSpace(command.ResultProviderTitle))
        {
            lines.Add($"来源：{command.ResultProviderTitle}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private void CopyGenericResultText(string text, string successMessage)
    {
        try
        {
            ClipboardService.SetText(text ?? string.Empty);
            LastRunMessage = successMessage;
        }
        catch (Exception ex)
        {
            SyncStatus = $"复制失败：{FormatExceptionMessage(ex)}";
        }
    }

    private void OpenGenericResultMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedCommand is not { IsProviderResult: true } command)
        {
            return;
        }

        _ = ExecuteGenericResultAsync(command);
    }

    private void ShowGenericResultDetailsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedCommand is not { IsProviderResult: true } command)
        {
            return;
        }

        ShowGenericResultDetails(command);
    }

    private void CopyGenericResultTitleMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedCommand is not { IsProviderResult: true } command)
        {
            return;
        }

        CopyGenericResultText(command.Title, $"已复制标题：{command.Title}");
    }

    private void CopyGenericResultContentMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedCommand is not { IsProviderResult: true } command)
        {
            return;
        }

        CopyGenericResultText(BuildGenericResultDetailText(command), $"已复制结果内容：{command.Title}");
    }

    private void CopyGenericResultTargetMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedCommand is not { IsProviderResult: true } command || string.IsNullOrWhiteSpace(command.OpenTarget))
        {
            return;
        }

        CopyGenericResultText(command.OpenTarget, $"已复制目标：{command.OpenTarget}");
    }

    private void OpenFileResultMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var command = SelectedCommand;
        if (command?.IsFileSystemResult != true || string.IsNullOrWhiteSpace(command.OpenTarget))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = command.OpenTarget,
                UseShellExecute = true
            });
            LastRunMessage = $"已打开：{command.Title}";
        }
        catch (Exception ex)
        {
            SyncStatus = $"打开失败：{FormatExceptionMessage(ex)}";
        }
    }

    private void OpenFileResultDirectoryMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var command = SelectedCommand;
        if (command?.IsFileSystemResult != true || string.IsNullOrWhiteSpace(command.OpenTarget))
        {
            return;
        }

        try
        {
            if (Directory.Exists(command.OpenTarget))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = command.OpenTarget,
                    UseShellExecute = true
                });
            }
            else
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{command.OpenTarget}\"",
                    UseShellExecute = true
                });
            }

            LastRunMessage = $"已打开路径：{command.Title}";
        }
        catch (Exception ex)
        {
            SyncStatus = $"打开路径失败：{FormatExceptionMessage(ex)}";
        }
    }

    private void CopyFileResultMenuItem_Click(object sender, RoutedEventArgs e)
    {
        SetFileResultClipboard(isCut: false);
    }

    private void CopyFileResultFullPathMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var command = SelectedCommand;
        if (command?.IsFileSystemResult != true || string.IsNullOrWhiteSpace(command.OpenTarget))
        {
            return;
        }

        try
        {
            ClipboardService.SetText(command.OpenTarget);
            LastRunMessage = $"已复制完整路径：{command.OpenTarget}";
        }
        catch (Exception ex)
        {
            SyncStatus = $"复制完整路径失败：{FormatExceptionMessage(ex)}";
        }
    }

    private void CutFileResultMenuItem_Click(object sender, RoutedEventArgs e)
    {
        SetFileResultClipboard(isCut: true);
    }

    private void OpenFileResultTerminalMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var directory = GetSelectedFileResultDirectory();
        if (string.IsNullOrWhiteSpace(directory))
        {
            SyncStatus = "目标不存在，无法打开终端。";
            return;
        }

        try
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "wt.exe",
                    Arguments = $"-d \"{directory}\"",
                    UseShellExecute = true
                });
            }
            catch
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    WorkingDirectory = directory,
                    UseShellExecute = true
                });
            }

            LastRunMessage = $"已在终端打开：{directory}";
        }
        catch (Exception ex)
        {
            SyncStatus = $"打开终端失败：{FormatExceptionMessage(ex)}";
        }
    }

    private void PreviewFileResultMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var command = SelectedCommand;
        if (command?.IsFileSystemResult != true || string.IsNullOrWhiteSpace(command.OpenTarget))
        {
            return;
        }

        var targetPath = command.OpenTarget;
        try
        {
            ShowGenericTextWindow($"预览：{command.Title}", BuildFilePreviewText(targetPath));
        }
        catch (Exception ex)
        {
            SyncStatus = $"预览失败：{FormatExceptionMessage(ex)}";
        }
    }

    private void MoveFileResultMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var command = SelectedCommand;
        if (command?.IsFileSystemResult != true || string.IsNullOrWhiteSpace(command.OpenTarget))
        {
            return;
        }

        var sourcePath = command.OpenTarget;
        if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
        {
            SyncStatus = "目标不存在，无法移动。";
            return;
        }

        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "选择移动到的文件夹",
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog() != Forms.DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            return;
        }

        try
        {
            var destinationPath = Path.Combine(dialog.SelectedPath, Path.GetFileName(sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
            if (File.Exists(destinationPath) || Directory.Exists(destinationPath))
            {
                SyncStatus = $"目标位置已存在同名项目：{destinationPath}";
                return;
            }

            if (Directory.Exists(sourcePath))
            {
                Directory.Move(sourcePath, destinationPath);
            }
            else
            {
                File.Move(sourcePath, destinationPath);
            }

            LastRunMessage = $"已移动到：{destinationPath}";
            ApplyFilter(SearchBox.Text);
        }
        catch (Exception ex)
        {
            SyncStatus = $"移动失败：{FormatExceptionMessage(ex)}";
        }
    }

    private void FavoriteFileResultMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var command = SelectedCommand;
        if (command?.IsFileSystemResult != true || string.IsNullOrWhiteSpace(command.OpenTarget))
        {
            return;
        }

        try
        {
            var normalizedPath = Path.GetFullPath(command.OpenTarget);
            var effectiveId = !string.IsNullOrWhiteSpace(command.ExtensionId)
                ? command.ExtensionId
                : $"{ExtensionIdPrefixes.SearchResult}{normalizedPath}";
            var settings = AppSettingsStore.Load();
            settings.GlobalFavoriteExtensionIds ??= [];
            if (!settings.GlobalFavoriteExtensionIds.Contains(effectiveId, StringComparer.OrdinalIgnoreCase))
            {
                settings.GlobalFavoriteExtensionIds.Add(effectiveId);
            }

            AppSettingsStore.Save(settings);
            _appSettings = AppSettingsStore.Load();
            _windowBoundExtensionsService.Reload(_appSettings.WindowBindings);
            LastRunMessage = $"已收藏文件：{command.Title}";
        }
        catch (Exception ex)
        {
            SyncStatus = $"收藏失败：{FormatExceptionMessage(ex)}";
        }
    }

    private void DeleteFileResultMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var command = SelectedCommand;
        if (command?.IsFileSystemResult != true || string.IsNullOrWhiteSpace(command.OpenTarget))
        {
            return;
        }

        var targetPath = command.OpenTarget;
        var confirm = System.Windows.MessageBox.Show(
            this,
            $"确认删除“{command.Title}”吗？",
            "删除文件",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            if (Directory.Exists(targetPath))
            {
                FileSystem.DeleteDirectory(targetPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            }
            else if (File.Exists(targetPath))
            {
                FileSystem.DeleteFile(targetPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            }
            else
            {
                SyncStatus = "目标不存在，无法删除。";
                return;
            }

            LastRunMessage = $"已删除：{command.Title}";
            ApplyFilter(SearchBox.Text);
        }
        catch (Exception ex)
        {
            SyncStatus = $"删除失败：{FormatExceptionMessage(ex)}";
        }
    }

    private void SetFileResultClipboard(bool isCut)
    {
        var command = SelectedCommand;
        if (command?.IsFileSystemResult != true || string.IsNullOrWhiteSpace(command.OpenTarget))
        {
            return;
        }

        try
        {
            var files = new StringCollection
            {
                command.OpenTarget
            };
            var dataObject = new System.Windows.DataObject();
            dataObject.SetFileDropList(files);

            var dropEffect = isCut ? 2u : 5u;
            using var stream = new MemoryStream(new byte[] { (byte)dropEffect, 0, 0, 0 });
            dataObject.SetData("Preferred DropEffect", stream);
            ClipboardService.SetDataObject(dataObject, true);
            LastRunMessage = isCut ? $"已剪切：{command.Title}" : $"已复制：{command.Title}";
        }
        catch (Exception ex)
        {
            SyncStatus = $"{(isCut ? "剪切" : "复制")}失败：{FormatExceptionMessage(ex)}";
        }
    }

    private string GetSelectedFileResultDirectory()
    {
        var target = SelectedCommand?.OpenTarget ?? string.Empty;
        if (Directory.Exists(target))
        {
            return target;
        }

        return File.Exists(target) ? Path.GetDirectoryName(target) ?? string.Empty : string.Empty;
    }

    private static string BuildFilePreviewText(string targetPath)
    {
        var lines = new List<string>
        {
            $"路径：{targetPath}",
            $"类型：{(Directory.Exists(targetPath) ? "文件夹" : "文件")}"
        };

        if (Directory.Exists(targetPath))
        {
            var directory = new DirectoryInfo(targetPath);
            lines.Add($"修改时间：{directory.LastWriteTime}");
            lines.Add(string.Empty);
            lines.Add("子项：");
            foreach (var entry in directory.EnumerateFileSystemInfos().Take(80))
            {
                lines.Add($"{(entry.Attributes.HasFlag(FileAttributes.Directory) ? "[目录]" : "[文件]")} {entry.Name}");
            }

            return string.Join(Environment.NewLine, lines);
        }

        if (!File.Exists(targetPath))
        {
            lines.Add("目标不存在。");
            return string.Join(Environment.NewLine, lines);
        }

        var file = new FileInfo(targetPath);
        lines.Add($"大小：{file.Length:N0} 字节");
        lines.Add($"修改时间：{file.LastWriteTime}");

        if (file.Length <= 512 * 1024 && IsLikelyTextFile(targetPath))
        {
            lines.Add(string.Empty);
            lines.Add("内容预览：");
            lines.Add(LimitPreviewText(File.ReadAllText(targetPath), 12000));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static bool IsLikelyTextFile(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension is ".txt" or ".md" or ".json" or ".xml" or ".csv" or ".log" or ".ini" or ".yaml" or ".yml" or ".cs" or ".xaml" or ".js" or ".ts" or ".css" or ".html" or ".py" or ".ps1";
    }

    private static string LimitPreviewText(string text, int maxLength)
    {
        if (text.Length <= maxLength)
        {
            return text;
        }

        return text[..maxLength] + Environment.NewLine + $"... 已截断，原始长度 {text.Length:N0} 字符";
    }

    private void ShowGenericTextWindow(string title, string text)
    {
        var detailWindow = new Window
        {
            Title = title,
            Owner = this,
            Width = 720,
            Height = 520,
            MinWidth = 520,
            MinHeight = 360,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(28, 28, 28)),
            Foreground = System.Windows.Media.Brushes.White
        };

        var contentBox = new System.Windows.Controls.TextBox
        {
            Text = text,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(28, 28, 28)),
            Foreground = System.Windows.Media.Brushes.White,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(16)
        };

        detailWindow.Content = contentBox;
        detailWindow.ShowDialog();
    }

    private bool HandleInternalCommand(CommandItem command)
    {
        if (HandleYanyuInternalCommand(command))
        {
            return true;
        }

        switch (command.OpenTarget)
        {
            case "oqh://settings":
                if (System.Windows.Application.Current is App app)
                {
                    app.OpenSettingsWindow();
                    LastRunMessage = "已打开设置。";
                }

                return true;
            case "oqh://add-extension":
                _ = AddJsonExtensionAsync();
                return true;
            case "oqh://edit-extension":
                _ = EditSelectedExtensionAsync();
                return true;
            case "oqh://delete-extension":
                _ = DeleteSelectedExtensionAsync();
                return true;
            case "scope:file":
                ShowPanel();
                SelectedSearchScope = SearchScopes.FirstOrDefault(s => s.Key.Equals(SearchScopeFile, StringComparison.OrdinalIgnoreCase)) ?? SelectedSearchScope;
                SearchBox.Focus();
                LastRunMessage = "已打开文件搜索。";
                return true;
            default:
                return false;
        }
    }

    private static bool IsInternalCommand(CommandItem command)
    {
        return command.OpenTarget?.StartsWith("oqh://", StringComparison.OrdinalIgnoreCase) == true ||
               command.OpenTarget?.StartsWith("scope:", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static CommandMatch BuildCommandMatch(CommandItem command, string query, bool allowRawQueryArgument = false)
    {
        var argument = ExtractQueryArgument(command, query, allowRawQueryArgument);
        if (command.SupportsQueryArgument && argument.Length > 0)
        {
            return new CommandMatch(true, 300);
        }

        if (command.SupportsQueryArgument && command.QueryPrefixes.Any(prefix =>
                prefix.Contains(query, StringComparison.OrdinalIgnoreCase)))
        {
            return new CommandMatch(true, 260);
        }

        if (command.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return new CommandMatch(true, 220);
        }

        if (command.ExtensionId.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return new CommandMatch(true, 210);
        }

        if (command.Subtitle.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return new CommandMatch(true, 160);
        }

        if (command.Category.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return new CommandMatch(true, 120);
        }

        if (command.Keywords.Any(k => k.Contains(query, StringComparison.OrdinalIgnoreCase)))
        {
            return new CommandMatch(true, 140);
        }

        return new CommandMatch(false, 0);
    }

    private static string ExtractQueryArgument(CommandItem command, string rawInput, bool allowRawQuery = false)
    {
        if (!command.SupportsQueryArgument || string.IsNullOrWhiteSpace(rawInput))
        {
            return string.Empty;
        }

        var input = rawInput.Trim();
        foreach (var prefix in command.QueryPrefixes)
        {
            if (input.Equals(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            if (input.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
            {
                return input[(prefix.Length + 1)..].Trim();
            }

            if (input.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && input.Length > prefix.Length)
            {
                return input[prefix.Length..].Trim();
            }
        }

        if (allowRawQuery)
        {
            return input;
        }

        return string.Empty;
    }

    private static string? BuildExecutionTarget(CommandItem command, string? rawInput, bool allowRawQuery = false)
    {
        if (command.SupportsQueryArgument)
        {
            var argument = ExtractQueryArgument(command, rawInput ?? string.Empty, allowRawQuery);
            if (!string.IsNullOrWhiteSpace(argument))
            {
                return command.QueryTargetTemplate!.Replace("{query}", Uri.EscapeDataString(argument), StringComparison.Ordinal);
            }
        }

        return command.OpenTarget;
    }

    private static string BuildScriptInput(CommandItem command, string? rawInput)
    {
        if (command.SupportsQueryArgument)
        {
            var argument = ExtractQueryArgument(command, rawInput ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(argument))
            {
                return argument;
            }
        }

        return (rawInput ?? string.Empty).Trim();
    }

    private async Task ExecuteScriptCommandAsync(CommandItem runnable, string? input, string launchSource)
    {
        if (string.Equals(launchSource, "launcher", StringComparison.OrdinalIgnoreCase))
        {
            HideToTray();
            await Task.Delay(100);
        }

        var executionStopwatch = Stopwatch.StartNew();
        HostAssets.AppendLog(
            $"Main execute script start: id={runnable.ExtensionId}, title={runnable.Title}, launchSource={launchSource}, nativeWindow={runnable.UsesNativeWindowUi}, inputLength={(input ?? string.Empty).Length}");
        SyncStatus = $"正在执行脚本：{runnable.Title}";
        var result = await ScriptExtensionRunner.ExecuteAsync(runnable, input, launchSource);
        if (result.Success)
        {
            RecordCommandUsage(runnable);
            HostAssets.AppendRecent(runnable.Title);
            HostAssets.AppendLog($"Executed script extension: {runnable.Title} -> {runnable.EntryPoint}");
            var nativeWindowStarted = string.Equals(result.Output, "native-window-started", StringComparison.Ordinal);
            var summary = string.IsNullOrWhiteSpace(result.Output)
                ? "脚本执行完成。"
                : result.Output.ReplaceLineEndings(" ").Trim();
            if (nativeWindowStarted)
            {
                summary = "原生窗口已启动。";
            }

            if (summary.Length > 180)
            {
                summary = summary[..180] + "...";
            }

            LastRunMessage = $"已执行脚本：{runnable.Title} -> {summary}";
            SyncStatus = nativeWindowStarted ? "原生窗口已启动。" : "脚本执行完成。";
            HostAssets.AppendLog(
                $"Main execute script success: id={runnable.ExtensionId}, title={runnable.Title}, launchSource={launchSource}, nativeWindowStarted={nativeWindowStarted}, elapsedMs={executionStopwatch.ElapsedMilliseconds}, outputLength={result.Output.Length}, errorLength={result.Error.Length}");
            AppendScriptExecutionDetailLog(runnable, success: true, result.Output, result.Error, result.ExitCode, launchSource);
            return;
        }

        HostAssets.AppendLog($"Script extension failed: {runnable.Title} -> {result.Error}");
        HostAssets.AppendLog(
            $"Main execute script failed: id={runnable.ExtensionId}, title={runnable.Title}, launchSource={launchSource}, elapsedMs={executionStopwatch.ElapsedMilliseconds}, exitCode={result.ExitCode}");
        AppendScriptExecutionDetailLog(runnable, success: false, result.Output, result.Error, result.ExitCode, launchSource);
        LastRunMessage = $"脚本执行失败：{runnable.Title}";
        SyncStatus = $"脚本执行失败：{result.Error}";
        var errorWindow = new ExecutionLogWindow(
            runnable.Title,
            success: false,
            output: result.Output,
            error: string.IsNullOrWhiteSpace(result.Error) ? "脚本执行失败。" : result.Error,
            exitCode: result.ExitCode,
            extraMeta: $"来源：{launchSource}")
        {
            Owner = this
        };
        errorWindow.Show();
        errorWindow.Activate();
    }

    private static void AppendScriptExecutionDetailLog(
        CommandItem command,
        bool success,
        string output,
        string error,
        int exitCode,
        string launchSource)
    {
        var normalizedOutput = string.IsNullOrWhiteSpace(output)
            ? "(empty)"
            : output.Trim().ReplaceLineEndings(" | ");
        var normalizedError = string.IsNullOrWhiteSpace(error)
            ? "(empty)"
            : error.Trim().ReplaceLineEndings(" | ");
        HostAssets.AppendLog(
            $"Script execution detail: id={command.ExtensionId}, title={command.Title}, success={success}, exitCode={exitCode}, launchSource={launchSource}, output={normalizedOutput}, error={normalizedError}");
    }

    // --- Quick Panel Support ---

}
