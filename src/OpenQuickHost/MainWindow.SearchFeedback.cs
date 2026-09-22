namespace OpenQuickHost;

public partial class MainWindow
{
    private bool _resumeSearchOnShow;

    private void SuspendSearchForHide()
    {
        _resumeSearchOnShow = _searchDebounceTimer.IsEnabled || IsSearchResultsPending || IsFileSearching;
        _searchDebounceTimer.Stop();
        _searchPipelineManager.CancelActive();
        IsFileSearching = false;
    }

    private void ResumeSearchAfterShow()
    {
        if (!_resumeSearchOnShow) return;
        _resumeSearchOnShow = false;
        ApplyFilter(SearchBox.Text);
    }

    private bool _isSearchResultsPending;
    public bool IsSearchResultsPending
    {
        get => _isSearchResultsPending;
        private set
        {
            if (_isSearchResultsPending == value) return;
            _isSearchResultsPending = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanUseSearchResults));
        }
    }

    public bool CanUseSearchResults => !IsSearchResultsPending;

    private bool _isFileSearchSetupSuggested;
    public bool IsFileSearchSetupSuggested
    {
        get => _isFileSearchSetupSuggested;
        private set
        {
            if (_isFileSearchSetupSuggested == value) return;
            _isFileSearchSetupSuggested = value;
            OnPropertyChanged();
        }
    }

    private void OpenFileSearchSetup_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        try
        {
            SearchFeedbackText = EverythingRuntimeService.ShowInteractiveSetup()
                ? "已打开搜索服务，请完成授权后回来重新搜索。"
                : "无法打开搜索服务，请从托盘手动打开 Everything 后重试。";
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"File search setup failed: {ex.Message}");
            SearchFeedbackText = "无法打开搜索服务，请从托盘手动打开 Everything 后重试。";
        }
    }

    private string _searchFeedbackText = "没有找到匹配结果，试试更短的关键词或切换搜索范围。";
    public string SearchFeedbackText
    {
        get => _searchFeedbackText;
        private set
        {
            if (_searchFeedbackText == value) return;
            _searchFeedbackText = value;
            OnPropertyChanged();
        }
    }

    private async Task RunSearchTaskAsync(SearchSession session, Func<Task> work, string? loadingText = null)
    {
        var replacesResults = loadingText != null;
        if (replacesResults)
        {
            IsSearchResultsPending = true;
            FileSearchingText = loadingText!;
            IsFileSearching = true;
            FilteredCommands.ReplaceAll([]);
            SelectedCommand = null;
            CommandList.SelectedItem = null;
            OnPropertyChanged(nameof(VisibleCountText));
            OnPropertyChanged(nameof(FooterHint));
        }

        try { await work(); }
        catch (OperationCanceledException) when (session.Token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Search failed: scope={session.ScopeKey}, error={ex}");
            if (replacesResults && _searchPipelineManager.IsActive(session))
                SearchFeedbackText = "搜索暂时不可用，请修改关键词重试；持续失败时检查搜索服务。";
        }
        finally
        {
            // An older request must never dismiss the newer request's loading state.
            if (replacesResults && _searchPipelineManager.IsActive(session))
            {
                IsFileSearching = false;
                IsSearchResultsPending = false;
            }
        }
    }

    private void FlushPendingSearch()
    {
        if (!_searchDebounceTimer.IsEnabled) return;
        _searchDebounceTimer.Stop();
        ApplyFilter(SearchBox.Text);
    }
}
