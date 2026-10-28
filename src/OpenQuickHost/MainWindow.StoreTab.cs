using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using OpenQuickHost.Sync;

namespace OpenQuickHost;

public partial class MainWindow
{
    private const string SearchScopeStore = "store";
    private bool _isStoreLoading = false;

    public bool IsStoreMode => string.Equals(SelectedSearchScope?.Key, SearchScopeStore, StringComparison.OrdinalIgnoreCase);

    public Visibility StoreVisibility => Visibility.Collapsed;

    private async Task LoadStoreExtensionsAsync()
    {
        if (_isStoreLoading) return;
        _isStoreLoading = true;

        try
        {
            if (_cloudSyncClient == null) return;

            StoreLoadingOverlay.Visibility = Visibility.Visible;

            // 1. 后台工作线程仅抓取网络 API
            var extensions = await Task.Run(async () =>
            {
                return await _cloudSyncClient.GetExtensionsAsync();
            });

            // 2. 在 UI 线程构造 CommandItem，规避 DependencyObject 跨线程实例化限制
            var newCloudItems = new List<CommandItem>();
            foreach (var ext in extensions)
            {
                var installedLocally = _localExtensionIndex.ContainsKey(ext.ExtensionId);
                var category = string.IsNullOrWhiteSpace(ext.Category) ? "小程序商店" : ext.Category.Trim();
                var description = string.IsNullOrWhiteSpace(ext.Description) ? "暂无简介" : ext.Description.Trim();
                var subtitleParts = new List<string>();
                if (installedLocally)
                {
                    subtitleParts.Add("已安装");
                }

                subtitleParts.Add(description);
                if (!string.IsNullOrWhiteSpace(ext.PublisherUsername))
                {
                    subtitleParts.Add($"发布者：{ext.PublisherUsername}");
                }

                var command = new CommandItem(
                    glyph: "extension",
                    title: string.IsNullOrWhiteSpace(ext.DisplayName) ? "未知小程序" : ext.DisplayName,
                    subtitle: string.Join(" · ", subtitleParts),
                    category: category,
                    accentHex: string.IsNullOrWhiteSpace(ext.AccentHex) ? "#FF3B82F6" : ext.AccentHex, // 商店颜色
                    openTarget: null,
                    keywords: BuildStoreKeywords(ext),
                    source: CommandSource.Cloud,
                    extensionId: ext.ExtensionId,
                    declaredVersion: ext.LatestVersion,
                    iconReference: ext.Icon
                );
                double simulatedRating = 4.0 + (double)(Math.Abs(ext.ExtensionId.GetHashCode()) % 10) * 0.1;
                if (simulatedRating > 5.0 || simulatedRating < 4.0) simulatedRating = 4.8;
                command.ApplyCloudData(ext.DisplayName, ext.LatestVersion, true, installedLocally, ext.ArchiveKey, ext.InstallCount, simulatedRating);

                newCloudItems.Add(command);
            }

            _allCommands.RemoveAll(x => x.Source == CommandSource.Cloud);
            _allCommands.AddRange(newCloudItems);

            await Dispatcher.InvokeAsync(() =>
            {
                ApplyFilter(SearchBox.Text);
            });

            _ = Task.Run(async () =>
            {
                await CheckArchivesForItemsAsync(newCloudItems);
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Failed to load store extensions: " + ex.Message);
            HostAssets.AppendLog("Failed to load store extensions: " + ex.Message);
            LastRunMessage = "小程序商店加载失败：" + ex.Message;
        }
        finally
        {
            _isStoreLoading = false;
            StoreLoadingOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private async Task CheckArchivesForItemsAsync(List<CommandItem> items)
    {
        HostAssets.AppendLog($"[StoreTab] CheckArchivesForItemsAsync started. Checking {items.Count} items.");
        if (_cloudSyncClient == null)
        {
            HostAssets.AppendLog("[StoreTab] CheckArchivesForItemsAsync aborted: _cloudSyncClient is null.");
            return;
        }

        using var semaphore = new System.Threading.SemaphoreSlim(5);
        var tasks = items.Select(async item =>
        {
            await semaphore.WaitAsync();
            try
            {
                HostAssets.AppendLog($"[StoreTab] Checking archive for {item.ExtensionId}...");
                bool exists = await _cloudSyncClient.CheckExtensionArchiveExistsAsync(item.ExtensionId);
                HostAssets.AppendLog($"[StoreTab] Check result for {item.ExtensionId}: exists={exists}");
                if (exists)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        item.ApplyCloudData(
                            item.Title,
                            item.CloudVersion,
                            item.ExistsInCloud,
                            item.InstalledForUser,
                            "archive",
                            item.InstallCount,
                            item.Rating);
                        HostAssets.AppendLog($"[StoreTab] Dynamic UI update applied: {item.ExtensionId} (HasArchive=true)");
                    });
                }
            }
            catch (Exception ex)
            {
                HostAssets.AppendLog($"[StoreTab] Exception checking {item.ExtensionId}: {ex.Message}");
            }
            finally
            {
                semaphore.Release();
            }
        }).ToList();

        await Task.WhenAll(tasks);
        HostAssets.AppendLog("[StoreTab] CheckArchivesForItemsAsync finished all checks.");
    }

    private static IReadOnlyList<string> BuildStoreKeywords(CloudExtensionRecord ext)
    {
        var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Add(ext.ExtensionId);
        Add(ext.DisplayName);
        Add(ext.PublisherUsername);
        Add(ext.Category);
        foreach (var keyword in ext.Keywords)
        {
            Add(keyword);
        }

        return keywords.ToArray();

        void Add(string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                keywords.Add(value.Trim());
            }
        }
    }
}
