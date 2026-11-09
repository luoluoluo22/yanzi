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

    public bool IsStoreMode => false;

    public Visibility StoreVisibility => Visibility.Collapsed;

    private async Task LoadStoreExtensionsAsync()
    {
        // 市场已下线，直接返回
        await Task.CompletedTask;
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
