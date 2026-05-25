using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using OpenQuickHost.Sync;
using Microsoft.Win32;

namespace OpenQuickHost;

public partial class MainWindow
{
    private const uint InputKeyboard = 1;
    private const uint KeyeventfKeyup = 0x0002;
    private static readonly IntPtr SimulatedInputMarker = new(0x59414E5B);
    private static readonly SemaphoreSlim CSharpPrebuildGate = new(1, 1);

    private void UpsertLocalExtensionCommand(CommandItem command)
    {
        _allCommands.RemoveAll(x =>
            x.Source == CommandSource.LocalExtension &&
            x.ExtensionId.Equals(command.ExtensionId, StringComparison.OrdinalIgnoreCase));
        var insertIndex = GetPreferredLocalExtensionInsertIndex(command.ExtensionId);
        if (insertIndex >= 0 && insertIndex <= _allCommands.Count)
        {
            _allCommands.Insert(insertIndex, command);
        }
        else
        {
            _allCommands.Add(command);
        }

        _localExtensionIndex[command.ExtensionId] = command;
        ApplyNewExtensionState(command);
        RefreshExtensionHotkeys();
    }

    private void RemoveLocalExtensionCommand(string extensionId)
    {
        _allCommands.RemoveAll(x =>
            x.Source == CommandSource.LocalExtension &&
            x.ExtensionId.Equals(extensionId, StringComparison.OrdinalIgnoreCase));
        _localExtensionIndex.Remove(extensionId);
        RemoveExtensionUiTracking(extensionId);
        RefreshExtensionHotkeys();
    }

    public CommandItem PersistJsonExtensionFromDialog(string json, bool isEditMode)
    {
        HostAssets.AppendLog($"PersistJsonExtensionFromDialog: start, editMode={isEditMode}.");
        var command = LocalExtensionCatalog.SaveJsonExtension(json);
        QueueCSharpPrebuild(command, isEditMode ? "extension-edit" : "extension-add");

        if (!isEditMode)
        {
            TrackRecentlyAddedExtension(command.ExtensionId);
        }

        UpsertLocalExtensionCommand(command);
        StartMouseGestureService();
        ApplyFilter(SearchBox.Text);
        SelectedCommand = _allCommands.FirstOrDefault(x => x.ExtensionId.Equals(command.ExtensionId, StringComparison.OrdinalIgnoreCase));
        CommandList.SelectedItem = SelectedCommand;
        LastRunMessage = isEditMode
            ? $"已更新本地 JSON 扩展：{command.Title}"
            : $"已添加本地 JSON 扩展：{command.Title}";
        HostAssets.AppendLog($"PersistJsonExtensionFromDialog: success, extensionId={command.ExtensionId}.");
        return command;
    }

    public void ReloadLocalExtensionsFromExternal()
    {
        LocalExtensionCatalog.EnsureSampleExtension();
        ReplaceLocalExtensions(LocalExtensionCatalog.LoadEntries(), "已通过外部 Agent API 刷新本地扩展。");
    }

    private void ReloadLocalExtensionsFromWebDav()
    {
        ReplaceLocalExtensions(LocalExtensionCatalog.LoadEntries(), statusText: null);
    }

    public void ReloadLocalExtensionsFromEntries(IReadOnlyList<LocalExtensionCatalogEntry> entries, string? statusText = null)
    {
        ReplaceLocalExtensions(entries, statusText);
    }

    public void ReloadLocalExtensionsFromCommands(IReadOnlyList<CommandItem> commands, string? statusText = null)
    {
        ReplaceLocalExtensions(commands, statusText);
    }

    private int GetPreferredLocalExtensionInsertIndex(string extensionId)
    {
        var trackedIds = _appSettings.RecentlyAddedExtensionIds ?? [];
        if (!trackedIds.Contains(extensionId, StringComparer.OrdinalIgnoreCase))
        {
            return -1;
        }

        var orderedTrackedIds = trackedIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToList();

        var localCommands = _allCommands
            .Where(x => x.Source == CommandSource.LocalExtension)
            .ToList();

        var precedingTrackedCount = orderedTrackedIds
            .TakeWhile(id => !id.Equals(extensionId, StringComparison.OrdinalIgnoreCase))
            .Count(id => localCommands.Any(command => command.ExtensionId.Equals(id, StringComparison.OrdinalIgnoreCase)));

        if (precedingTrackedCount <= 0)
        {
            return 0;
        }

        var localInsertIndex = Math.Min(precedingTrackedCount, localCommands.Count);
        var localAtIndex = localCommands.ElementAtOrDefault(localInsertIndex);
        return localAtIndex == null ? _allCommands.Count : _allCommands.IndexOf(localAtIndex);
    }

    private int GetLocalExtensionDisplayOrder(CommandItem command)
    {
        if (command.Source != CommandSource.LocalExtension)
        {
            return int.MaxValue;
        }

        var index = (_appSettings.RecentlyAddedExtensionIds ?? []).FindIndex(id =>
            id.Equals(command.ExtensionId, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? int.MaxValue : index;
    }

    private void TrackRecentlyAddedExtension(string extensionId)
    {
        if (string.IsNullOrWhiteSpace(extensionId))
        {
            return;
        }

        var settings = AppSettingsStore.Load();
        settings.RecentlyAddedExtensionIds ??= [];
        settings.UnreadNewExtensionIds ??= [];
        settings.RecentlyAddedExtensionIds.RemoveAll(id => id.Equals(extensionId, StringComparison.OrdinalIgnoreCase));
        settings.UnreadNewExtensionIds.RemoveAll(id => id.Equals(extensionId, StringComparison.OrdinalIgnoreCase));
        settings.RecentlyAddedExtensionIds.Insert(0, extensionId);
        settings.UnreadNewExtensionIds.Insert(0, extensionId);
        if (settings.RecentlyAddedExtensionIds.Count > 50)
        {
            settings.RecentlyAddedExtensionIds = settings.RecentlyAddedExtensionIds.Take(50).ToList();
        }

        if (settings.UnreadNewExtensionIds.Count > 50)
        {
            settings.UnreadNewExtensionIds = settings.UnreadNewExtensionIds.Take(50).ToList();
        }

        AppSettingsStore.Save(settings);
        _appSettings = settings;
        _windowBoundExtensionsService.Reload(_appSettings.WindowBindings);
    }

    private void RemoveExtensionUiTracking(string extensionId)
    {
        if (string.IsNullOrWhiteSpace(extensionId))
        {
            return;
        }

        var settings = AppSettingsStore.Load();
        var changed = false;
        settings.RecentlyAddedExtensionIds ??= [];
        settings.UnreadNewExtensionIds ??= [];
        changed |= settings.RecentlyAddedExtensionIds.RemoveAll(id => id.Equals(extensionId, StringComparison.OrdinalIgnoreCase)) > 0;
        changed |= settings.UnreadNewExtensionIds.RemoveAll(id => id.Equals(extensionId, StringComparison.OrdinalIgnoreCase)) > 0;
        if (!changed)
        {
            return;
        }

        AppSettingsStore.Save(settings);
        _appSettings = settings;
        _windowBoundExtensionsService.Reload(_appSettings.WindowBindings);
    }

    private void ApplyNewExtensionState(CommandItem command)
    {
        command.SetHasNewBadge(
            command.Source == CommandSource.LocalExtension &&
            (_appSettings.UnreadNewExtensionIds ?? []).Contains(command.ExtensionId, StringComparer.OrdinalIgnoreCase));
    }

    private void MarkExtensionAsSeen(CommandItem? command)
    {
        if (command == null ||
            command.Source != CommandSource.LocalExtension ||
            !command.HasNewBadge)
        {
            return;
        }

        var settings = AppSettingsStore.Load();
        settings.UnreadNewExtensionIds ??= [];
        if (settings.UnreadNewExtensionIds.RemoveAll(id => id.Equals(command.ExtensionId, StringComparison.OrdinalIgnoreCase)) == 0)
        {
            return;
        }

        AppSettingsStore.Save(settings);
        _appSettings = settings;
        _windowBoundExtensionsService.Reload(_appSettings.WindowBindings);
        command.SetHasNewBadge(false);
    }

    private CommandItem? ShowJsonExtensionEditorAsync(string initialJson, bool isEditMode)
    {
        return ShowJsonExtensionEditorForOwner(initialJson, isEditMode, this);
    }

    private CommandItem? ShowJsonExtensionEditorForOwner(string initialJson, bool isEditMode, Window? owner)
    {
        var currentJson = initialJson;
        if (owner != null)
        {
            owner.Topmost = false;
        }

        while (true)
        {
            var dialog = new AddJsonExtensionWindow(currentJson, isEditMode)
            {
                Owner = owner,
                Topmost = false,
                ShowInTaskbar = true
            };
            dialog.ShowDialog();
            HostAssets.AppendLog($"ShowJsonExtensionEditorForOwner: dialog closed, accepted={dialog.WasAccepted}, persistedDirectly={dialog.PersistedCommand != null}.");
            if (!dialog.WasAccepted)
            {
                return null;
            }

            if (dialog.PersistedCommand != null)
            {
                return dialog.PersistedCommand;
            }

            try
            {
                HostAssets.AppendLog("ShowJsonExtensionEditorForOwner: persisting after dialog return.");
                var command = LocalExtensionCatalog.SaveJsonExtension(dialog.JsonContent);
                QueueCSharpPrebuild(command, isEditMode ? "extension-edit-fallback" : "extension-add-fallback");
                UpsertLocalExtensionCommand(command);
                StartMouseGestureService();
                ApplyFilter(SearchBox.Text);
                SelectedCommand = _allCommands.FirstOrDefault(x => x.ExtensionId.Equals(command.ExtensionId, StringComparison.OrdinalIgnoreCase));
                CommandList.SelectedItem = SelectedCommand;
                return command;
            }
            catch (Exception ex)
            {
                currentJson = dialog.JsonContent;
                var retryDialog = new AddJsonExtensionWindow(currentJson, isEditMode)
                {
                    Owner = owner,
                    Topmost = false,
                    ShowInTaskbar = true
                };
                retryDialog.ShowError(ex.Message);
                retryDialog.ShowDialog();
                if (!retryDialog.WasAccepted)
                {
                    return null;
                }

                currentJson = retryDialog.JsonContent;
            }
        }
    }

    private static bool IsCSharpRuntime(CommandItem command)
    {
        return string.Equals(command.Runtime, "csharp", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(command.Runtime, "cs", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(command.Runtime, "c#", StringComparison.OrdinalIgnoreCase);
    }

    private static void QueueCSharpPrebuild(CommandItem command, string reason)
    {
        if (!IsCSharpRuntime(command))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            HostAssets.AppendLog(
                $"CSharp prebuild queued: id={command.ExtensionId}, title={command.Title}, reason={reason}");
            SetCSharpPrebuildState(command, true);
            await CSharpPrebuildGate.WaitAsync().ConfigureAwait(false);
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var result = await ScriptExtensionRunner.PreparePortableAssetsAsync(command).ConfigureAwait(false);
                HostAssets.AppendLog(
                    $"CSharp prebuild completed: id={command.ExtensionId}, title={command.Title}, reason={reason}, success={result.Success}, elapsedMs={stopwatch.ElapsedMilliseconds}, exitCode={result.ExitCode}, error={result.Error}");
            }
            catch (Exception ex)
            {
                HostAssets.AppendLog(
                    $"CSharp prebuild failed: id={command.ExtensionId}, title={command.Title}, reason={reason}, elapsedMs={stopwatch.ElapsedMilliseconds}, error={ex}");
            }
            finally
            {
                CSharpPrebuildGate.Release();
                SetCSharpPrebuildState(command, false);
            }
        });
    }

    private static void SetCSharpPrebuildState(CommandItem command, bool isPrebuilding)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            command.SetCSharpPrebuildState(isPrebuilding);
            return;
        }

        _ = dispatcher.BeginInvoke(() => command.SetCSharpPrebuildState(isPrebuilding));
    }

    private static string CreateWebSearchTemplateJson()
    {
        var id = $"web-search-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        var manifest = new LocalExtensionManifest
        {
            Id = id,
            Name = "自定义网页搜索",
            Version = "1.0.0",
            Category = "网页搜索",
            Description = "输入关键词后打开指定网站的搜索结果。",
            Keywords = ["网页", "搜索", "自定义"],
            QueryPrefixes = ["搜索", "web"],
            QueryTargetTemplate = "https://www.example.com/search?q={query}",
            Icon = "mdi:magnify"
        };

        return JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });
    }

    private CommandItem ResolveRunnableCommand(CommandItem command)
    {
        if (command.Source != CommandSource.Cloud)
        {
            return command;
        }

        return _localExtensionIndex.TryGetValue(command.ExtensionId, out var localExtension)
            ? localExtension
            : command;
    }

    public CommandItem? OpenAddExtensionForSlot(Window? owner = null)
    {
        return ShowJsonExtensionEditorForOwner(string.Empty, false, owner ?? this);
    }

    public CommandItem CreateQuickOpenExtensionFromPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("路径为空，无法创建扩展。");
        }

        var fullPath = Path.GetFullPath(path);
        var isDirectory = Directory.Exists(fullPath);
        var isFile = File.Exists(fullPath);
        if (!isDirectory && !isFile)
        {
            throw new FileNotFoundException("目标文件或目录不存在。", fullPath);
        }

        var fileName = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = fullPath;
        }

        var extensionId = $"quick-open-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}";
        var manifest = new LocalExtensionManifest
        {
            Id = extensionId,
            Name = fileName,
            Version = "1.0.0",
            Category = isDirectory ? "目录" : "文件",
            Description = isDirectory ? $"打开目录：{fullPath}" : $"打开文件：{fullPath}",
            Keywords = new[]
            {
                fileName,
                Path.GetFileNameWithoutExtension(fileName),
                Path.GetExtension(fullPath),
                fullPath,
                isDirectory ? "目录" : "文件",
                "拖拽导入"
            }.Where(static value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            OpenTarget = fullPath,
            Icon = fullPath
        };

        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });

        var command = PersistJsonExtensionFromDialog(json, isEditMode: false);
        QueueBackgroundWebDavSync("extension-drag-import");
        return command;
    }

    public void MarkExtensionAsNewFromQuickPanel(CommandItem? command)
    {
        if (command == null || command.Source != CommandSource.LocalExtension)
        {
            return;
        }

        TrackRecentlyAddedExtension(command.ExtensionId);
        if (_localExtensionIndex.TryGetValue(command.ExtensionId, out var existing))
        {
            ApplyNewExtensionState(existing);
        }

        ApplyNewExtensionState(command);
        ApplyFilter(SearchBox.Text);
    }

    public void ShowPanel()
    {
        ShowInTaskbar = true;
        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        RefreshWindowDpiIfNeeded(_dpiRefreshRequested);

        Topmost = true;
        BringMainWindowToFront();
        SetSearchScopePopupOpen(true);

        Dispatcher.BeginInvoke(() =>
        {
            BringMainWindowToFront();
            SearchBox.SelectAll();
            RepositionSearchScopePopup();
        }, DispatcherPriority.ApplicationIdle);
    }

    public void ShowMousePanel()
    {
        _quickPanel?.ShowAtMouse();
    }

    private void BringMainWindowToFront()
    {
        Activate();
        Focus();

        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
        {
            NativeMethods.SetForegroundWindow(handle);
        }

        Topmost = true;
        Topmost = false;
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
    }

    private void StartStartupExtensions()
    {
        _ = Task.Run(async () =>
        {
            // 给软件一点初始化时间
            await Task.Delay(3000);
            
            var startupCommands = _allCommands
                .Where(x => x.Startup?.Mode == "on_app_launch")
                .ToList();

            foreach (var command in startupCommands)
            {
                await Dispatcher.InvokeAsync(async () => 
                {
                    try
                    {
                        HostAssets.AppendLog($"Starting startup extension: {command.Title} ({command.ExtensionId})");
                        await ExecuteCommandAsync(command, launchSource: "app-startup");
                    }
                    catch (Exception ex)
                    {
                        HostAssets.AppendLog($"Failed to start extension {command.Title}: {ex.Message}");
                    }
                });
                
                // 多个自启动扩展之间稍微间隔下，避免瞬间压力过大
                await Task.Delay(500);
            }

            // 启动定时调度服务
            await Dispatcher.InvokeAsync(() => StartExtensionScheduler());
        });
    }

    private ExtensionSchedulerService? _extensionScheduler;

    private void StartExtensionScheduler()
    {
        _extensionScheduler ??= new ExtensionSchedulerService(this);
        _extensionScheduler.Start();
    }

    /// <summary>
    /// 供 ExtensionSchedulerService 调用，执行定时触发的扩展
    /// </summary>
    public async Task ExecuteScheduledExtensionAsync(CommandItem command)
    {
        await ExecuteCommandAsync(command, launchSource: "scheduler");
    }

    /// <summary>
    /// 通知调度服务某个扩展的 Schedule 已变更
    /// </summary>
    public void NotifyScheduleChanged(string extensionId, string? schedule)
    {
        _extensionScheduler?.RefreshExtension(extensionId, schedule);
    }

    public void StartMousePanelService()
    {
        if (_listenerServicesPaused)
        {
            return;
        }

        InputHookService.Start(
            () => _quickPanel?.ShowAtMouse(),
            () => _quickPanel?.ExecuteHoveredSlotFromHoldRelease(),
            () => _radialMenu?.ShowAtMouse(),
            () => _radialMenu?.ExecuteSelectedFromHoldRelease(),
            () => _yanmOverlay?.ShowTemporary(),
            () => _yanmOverlay?.HideTemporary(),
            () => _windowSnapAssistService.BeginMouseDragSelectionAtMouse(),
            () => _windowSnapAssistService.UpdateMouseDragSelectionAtMouse(),
            () => _windowSnapAssistService.CompleteMouseDragSelectionAtMouse());
    }

    public void StopMousePanelService()
    {
        InputHookService.Stop();
    }

    public bool IsMousePanelServiceRunning => InputHookService.IsRunning;

    public void SetWindowSnapAssistEnabled(bool enabled)
    {
        if (enabled)
        {
            _windowSnapAssistService.Start();
            RefreshWindowSnapAssistHotkeyRegistration();
        }
        else
        {
            _windowSnapAssistService.Stop();
            UnregisterWindowSnapAssistHotkey();
        }
    }

    public void TriggerWindowSnapAssist()
    {
        _windowSnapAssistService.TriggerAtForegroundWindow();
    }

    public IReadOnlyList<RadialMenuRuntimeItem> GetRadialMenuItems(string? pageId = null)
    {
        var allCommands = GetAllCommands();
        var settings = AppSettingsStore.Load();
        var radial = settings.RadialMenu ?? new RadialMenuSettings();
        var page = radial.Pages.FirstOrDefault(item => item.Id.Equals(pageId ?? radial.SelectedPageId, StringComparison.OrdinalIgnoreCase))
            ?? radial.Pages.FirstOrDefault();
        var slots = page?.Slots ?? [];
        var slotTitles = page?.SlotTitles ?? [];
        var childPages = page?.ChildPageIds ?? [];
        var result = new List<RadialMenuRuntimeItem>();
        for (var index = 0; index < RadialMenuSettings.TotalSlotCount; index++)
        {
            var extensionId = slots.ElementAtOrDefault(index);
            var displayTitle = slotTitles.ElementAtOrDefault(index);
            var command = string.IsNullOrWhiteSpace(extensionId)
                ? null
                : ResolveRadialCommand(extensionId, allCommands, displayTitle);
            var childPageId = childPages.ElementAtOrDefault(index) ?? string.Empty;
            result.Add(new RadialMenuRuntimeItem(command, childPageId));
        }

        return result;
    }

    private static CommandItem? ResolveRadialCommand(string extensionId, IReadOnlyList<CommandItem> allCommands, string? displayTitle = null)
    {
        var command = allCommands.FirstOrDefault(command => command.ExtensionId.Equals(extensionId, StringComparison.OrdinalIgnoreCase));
        if (command != null)
        {
            return command;
        }

        const string simulatedKeyPrefix = "keysim::";
        if (extensionId.StartsWith(simulatedKeyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var shortcut = extensionId[simulatedKeyPrefix.Length..].Trim();
            if (string.IsNullOrWhiteSpace(shortcut))
            {
                return null;
            }

            var effectiveTitle = string.IsNullOrWhiteSpace(displayTitle) ? shortcut : displayTitle.Trim();
            return new CommandItem(
                glyph: "键",
                title: effectiveTitle,
                subtitle: "向前台程序发送这个按键",
                category: "模拟按键",
                accentHex: "#FF2563EB",
                openTarget: null,
                keywords: [shortcut, "模拟按键", "快捷键"],
                source: CommandSource.Local,
                extensionId: extensionId,
                iconReference: "mdi:shortcut");
        }

        const string filePrefix = "result::";
        if (!extensionId.StartsWith(filePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var path = extensionId[filePrefix.Length..];
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var isFolder = Directory.Exists(path);
        var exists = isFolder || File.Exists(path);
        var title = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(title))
        {
            title = path;
        }

        return new CommandItem(
            glyph: isFolder ? "夹" : "文",
            title: title,
            subtitle: exists ? Path.GetDirectoryName(path) ?? path : "文件不存在",
            category: isFolder ? "文件夹" : "文件",
            accentHex: isFolder ? "#FF3B82F6" : "#FF4B5563",
            openTarget: path,
            keywords: [path, title],
            source: CommandSource.File,
            extensionId: extensionId,
            resultKind: isFolder ? ResultItemKind.Folder : ResultItemKind.File,
            resultProviderTitle: "文件",
            iconSourceOverride: NativeFileIconService.GetIcon(path, isFolder));
    }

    private bool TryExecuteSimulatedKeystroke(CommandItem runnable)
    {
        const string simulatedKeyPrefix = "keysim::";
        if (!runnable.ExtensionId.StartsWith(simulatedKeyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var shortcut = runnable.ExtensionId[simulatedKeyPrefix.Length..].Trim();
        if (!TryParseSimulatedShortcut(shortcut, out var modifiers, out var key))
        {
            HostAssets.AppendLog($"Simulated keystroke parse failed: {shortcut}");
            LastRunMessage = $"模拟按键无效：{shortcut}";
            return true;
        }

        try
        {
            var sent = SendSimulatedKeystroke(modifiers, key, out var inputCount, out var lastError);
            HostAssets.AppendLog($"Simulated keystroke SendInput: shortcut={shortcut}, sent={sent}/{inputCount}, inputSize={Marshal.SizeOf<INPUT>()}, lastError={lastError}.");
            if (sent != inputCount)
            {
                throw new Win32Exception(lastError == 0 ? Marshal.GetLastWin32Error() : lastError, "SendInput 未完整发送按键。");
            }

            RecordCommandUsage(runnable);
            HostAssets.AppendRecent(runnable.Title);
            HostAssets.AppendLog($"Executed simulated keystroke: {shortcut}");
            LastRunMessage = $"已模拟按键：{shortcut}";
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Simulated keystroke failed: {shortcut} -> {ex.Message}");
            LastRunMessage = $"模拟按键失败：{shortcut}，{ex.Message}";
        }

        return true;
    }

    private static bool TryParseSimulatedShortcut(string shortcut, out uint modifiers, out Key key)
    {
        modifiers = 0;
        key = Key.None;
        if (string.IsNullOrWhiteSpace(shortcut) || IsDoubleTapShortcut(shortcut))
        {
            return false;
        }

        var segments = shortcut
            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            return false;
        }

        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index];
            var isLast = index == segments.Length - 1;
            if (!isLast)
            {
                switch (segment.ToLowerInvariant())
                {
                    case "ctrl":
                    case "control":
                        modifiers |= ModControl;
                        continue;
                    case "alt":
                        modifiers |= ModAlt;
                        continue;
                    case "shift":
                        modifiers |= ModShift;
                        continue;
                    case "win":
                    case "windows":
                        modifiers |= ModWin;
                        continue;
                    default:
                        return false;
                }
            }

            try
            {
                key = segment.ToLowerInvariant() switch
                {
                    "space" => Key.Space,
                    "enter" => Key.Enter,
                    "tab" => Key.Tab,
                    "esc" or "escape" => Key.Escape,
                    "backspace" => Key.Back,
                    "pagedown" => Key.Next,
                    "pageup" => Key.Prior,
                    "capslock" => Key.Capital,
                    _ => (Key)new KeyConverter().ConvertFromInvariantString(segment)!
                };
            }
            catch
            {
                return false;
            }
        }

        return key != Key.None;
    }

    private static uint SendSimulatedKeystroke(uint modifiers, Key key, out int inputCount, out int lastError)
    {
        Thread.Sleep(60);

        var virtualKeys = new List<ushort>(5);
        if ((modifiers & ModControl) != 0)
        {
            virtualKeys.Add(0x11);
        }

        if ((modifiers & ModAlt) != 0)
        {
            virtualKeys.Add(0x12);
        }

        if ((modifiers & ModShift) != 0)
        {
            virtualKeys.Add(0x10);
        }

        if ((modifiers & ModWin) != 0)
        {
            virtualKeys.Add(0x5B);
        }

        virtualKeys.Add((ushort)KeyInterop.VirtualKeyFromKey(key));

        var inputs = new List<INPUT>(virtualKeys.Count * 2);
        for (var index = 0; index < virtualKeys.Count - 1; index++)
        {
            inputs.Add(CreateKeyInput(virtualKeys[index], keyUp: false));
        }

        var mainKey = virtualKeys[^1];
        inputs.Add(CreateKeyInput(mainKey, keyUp: false));
        inputs.Add(CreateKeyInput(mainKey, keyUp: true));

        for (var index = virtualKeys.Count - 2; index >= 0; index--)
        {
            inputs.Add(CreateKeyInput(virtualKeys[index], keyUp: true));
        }

        inputCount = inputs.Count;
        var sent = SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
        lastError = Marshal.GetLastWin32Error();
        return sent;
    }

    private static INPUT CreateKeyInput(ushort virtualKey, bool keyUp)
    {
        return new INPUT
        {
            type = InputKeyboard,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = virtualKey,
                    dwFlags = keyUp ? KeyeventfKeyup : 0,
                    dwExtraInfo = SimulatedInputMarker
                }
            }
        };
    }

    public IReadOnlyList<RadialMenuPageSettings> GetRadialMenuPages() =>
        AppSettingsStore.Load().RadialMenu?.Pages ?? [];

    public IReadOnlyList<CommandItem> GetRadialMenuCommandCandidates(string? keyword)
    {
        keyword = (keyword ?? string.Empty).Trim();
        var candidates = GetAllCommands()
            .Where(static command => command.Source is CommandSource.LocalExtension or CommandSource.WebSearch or CommandSource.Application or CommandSource.Local)
            .Where(command =>
                string.IsNullOrWhiteSpace(keyword) ||
                command.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                command.ExtensionId.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                command.Subtitle.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                command.Category.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(command.OpenTarget) && command.OpenTarget.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(GetLocalExtensionDisplayOrder)
            .ThenBy(static command => command.Title, StringComparer.OrdinalIgnoreCase)
            .Take(string.IsNullOrWhiteSpace(keyword) ? 60 : 40)
            .ToList();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var fileResults = EverythingSearchService.Search(keyword, 20);
            if (fileResults.Success)
            {
                foreach (var result in fileResults.Results)
                {
                    var command = BuildRadialFileCommand(result);
                    if (candidates.Any(existing => existing.ExtensionId.Equals(command.ExtensionId, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    candidates.Add(command);
                }
            }
        }

        return candidates;
    }

    private static CommandItem BuildRadialFileCommand(EverythingSearchResult result)
    {
        var subtitle = string.IsNullOrWhiteSpace(result.SizeText)
            ? result.DirectoryPath
            : $"{result.DirectoryPath}   ·   {result.SizeText}";
        return new CommandItem(
            glyph: result.IsFolder ? "夹" : "文",
            title: result.Name,
            subtitle: subtitle,
            category: result.IsFolder ? "文件夹" : "文件",
            accentHex: result.IsFolder ? "#FF3B82F6" : "#FF4B5563",
            openTarget: result.FullPath,
            keywords: [result.FullPath, result.DirectoryPath, result.Name],
            source: CommandSource.File,
            extensionId: $"result::{result.FullPath}",
            resultKind: result.IsFolder ? ResultItemKind.Folder : ResultItemKind.File,
            resultProviderTitle: "Everything 文件",
            iconSourceOverride: NativeFileIconService.GetIcon(result.FullPath, result.IsFolder));
    }

    public bool AreListenerServicesPaused => _listenerServicesPaused;

    private void HandleYarnSelectAction(YarnSelectActionRequest request)
    {
        var text = (request.Text ?? string.Empty).Trim();
        if (string.Equals(request.ActionType, YarnSelectActionTypes.Search, StringComparison.OrdinalIgnoreCase))
        {
            ShowPanel();
            SelectedSearchScope = SearchScopes.FirstOrDefault(static scope => scope.Key == SearchScopeAll) ?? SelectedSearchScope;
            SearchBox.Text = text;
            SearchBox.CaretIndex = SearchBox.Text.Length;
            SearchBox.Focus();
            LastRunMessage = string.IsNullOrWhiteSpace(text) ? "燕选搜索：空输入" : $"燕选搜索：{text}";
            return;
        }

        if (string.Equals(request.ActionType, YarnSelectActionTypes.Run, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                LastRunMessage = "燕选运行：空输入，已忽略。";
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = text,
                    UseShellExecute = true
                });
                LastRunMessage = $"燕选运行：{text}";
            }
            catch (Exception ex)
            {
                LastRunMessage = $"燕选运行失败：{FormatExceptionMessage(ex)}";
                HostAssets.AppendLog($"YarnSelect run failed: target={text}, error={ex.Message}");
            }
            return;
        }

        if (string.Equals(request.ActionType, YarnSelectActionTypes.RunExtension, StringComparison.OrdinalIgnoreCase))
        {
            var command = _allCommands.FirstOrDefault(item =>
                item.ExtensionId.Equals(request.ExtensionId ?? string.Empty, StringComparison.OrdinalIgnoreCase));
            if (command == null)
            {
                LastRunMessage = $"燕选运行扩展失败：没有找到扩展 {request.ExtensionId}";
                return;
            }

            _ = ExecuteCommandAsync(ResolveRunnableCommand(command), text, "yarnselect");
            LastRunMessage = $"燕选运行扩展：{command.Title}";
        }
    }

    public void PauseListenerServices()
    {
        _listenerServicesPaused = true;
        StopMousePanelService();
        KeyboardDoubleTapService.Stop();
        YanyuTriggerService.Stop();
        YarnSelectService.Stop();
        MouseGestureService.Stop();
        _windowBoundExtensionsService.Stop();
        _windowSnapAssistService.Stop();
        UnregisterLauncherHotkey();
        UnregisterYanmHotkey();
        UnregisterWindowSnapAssistHotkey();
        UnregisterExtensionHotkeys();
        SyncStatus = "已暂停快捷键、扩展快捷键、燕选和鼠标面板监听。";
    }

    public void ResumeListenerServices()
    {
        _listenerServicesPaused = false;
        InputHookService.ReloadSettings();
        StartMousePanelService();
        StartKeyboardTriggerService();
        YanyuTriggerService.Start(HandleYanyuRuleTriggered);
        YarnSelectService.Start(HandleYarnSelectAction);
        StartMouseGestureService();
        RefreshYanyuRules();
        _windowBoundExtensionsService.Start(_appSettings.WindowBindings);
        _windowSnapAssistService.Start();
        RefreshLauncherHotkeyRegistration();
        RefreshYanmHotkeyRegistration();
        RefreshRadialHotkeyRegistration();
        RefreshWindowSnapAssistHotkeyRegistration();
        RefreshExtensionHotkeys();
        SyncStatus = "已恢复快捷键、扩展快捷键、燕选和鼠标面板监听。";
    }

    /// <summary>启动全局鼠标手势服务并扫描所有扩展中的 MouseGesture 注册到服务里。</summary>
    public void StartMouseGestureService()
    {
        if (!MouseGestureService.IsRunning)
        {
            MouseGestureService.Start((level, msg) => HostAssets.AppendLog(msg));
        }
        var count = MouseGestureService.ReloadFromCatalog(HandleMouseGestureExecute);
        HostAssets.AppendLog($"MouseGesture: catalog reloaded, registrations={count}.");
    }

    /// <summary>扩展保存后调用，重新扫描注册表。</summary>
    public void ReloadMouseGestureRegistrations()
    {
        if (!MouseGestureService.IsRunning) return;
        var count = MouseGestureService.ReloadFromCatalog(HandleMouseGestureExecute);
        HostAssets.AppendLog($"MouseGesture: registrations refreshed -> {count}.");
    }

    private void HandleMouseGestureExecute(RegisteredGesture gesture)
    {
        // 钩子线程上回调，UI 线程执行扩展
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var command = _allCommands.FirstOrDefault(c =>
                string.Equals(c.ExtensionId, gesture.ExtensionId, StringComparison.OrdinalIgnoreCase));
            if (command == null)
            {
                HostAssets.AppendLog($"MouseGesture: extension not found in commands, id={gesture.ExtensionId}.");
                LastRunMessage = $"鼠标手势：未找到扩展 {gesture.ExtensionName}";
                return;
            }
            ExecuteCommandExternally(command, explicitInput: null, launchSource: "mouse-gesture");
            LastRunMessage = $"鼠标手势 {gesture.Sequence} 触发：{command.Title}";
        }));
    }

    private void PinAutoHideButton_Click(object sender, RoutedEventArgs e)
    {
        _isPinned = !_isPinned;
        OnPropertyChanged(nameof(PinButtonBrush));
        OnPropertyChanged(nameof(PinButtonTooltip));
        LastRunMessage = _isPinned ? "已固定窗口，失去焦点不会自动关闭。" : "已取消固定，失去焦点将自动关闭。";
    }

    public void HideToTray()
    {
        ShowInTaskbar = false;
        SetSearchScopePopupOpen(false);
        Hide();
    }

    public void TogglePanelVisibility()
    {
        if (IsVisible)
        {
            HideToTray();
        }
        else
        {
            ShowPanel();
        }
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        _source = (HwndSource?)PresentationSource.FromVisual(this);
        if (_source == null)
        {
            return;
        }

        _lastKnownWindowDpi = GetCurrentWindowDpi();
        _source.AddHook(WndProc);
        if (!_listenerServicesPaused)
        {
            StartKeyboardTriggerService();
            YanyuTriggerService.Start(HandleYanyuRuleTriggered);
            YarnSelectService.Start(HandleYarnSelectAction);
            StartMouseGestureService();
            RefreshYanyuRules();
            RefreshLauncherHotkeyRegistration();
            RefreshYanmHotkeyRegistration();
            RefreshRadialHotkeyRegistration();
            RefreshWindowSnapAssistHotkeyRegistration();
            RefreshExtensionHotkeys();
        }
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (AllowClose)
        {
            NetworkChange.NetworkAvailabilityChanged -= NetworkChange_NetworkAvailabilityChanged;
            NetworkChange.NetworkAddressChanged -= NetworkChange_NetworkAddressChanged;
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged;
            _cloudReconnectTimer.Stop();

            if (_source != null)
            {
                UnregisterExtensionHotkeys();
                UnregisterLauncherHotkey();
                UnregisterYanmHotkey();
                UnregisterRadialHotkey();
                UnregisterWindowSnapAssistHotkey();
                KeyboardDoubleTapService.Stop();
                YanyuTriggerService.Stop();
                YarnSelectService.Stop();
                _source.RemoveHook(WndProc);
            }

            return;
        }

        if (!AppSettingsStore.Load().CloseToTray)
        {
            AllowClose = true;
            System.Windows.Application.Current.Shutdown();
            return;
        }

        e.Cancel = true;
        HideToTray();
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            SetSearchScopePopupOpen(false);
            HideToTray();
        }
        else if (IsActive)
        {
            SetSearchScopePopupOpen(true);
        }
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        SetSearchScopePopupOpen(false);

        if (_isPinned || !IsVisible)
        {
            return;
        }

        if (IsAutoHideSuppressed())
        {
            return;
        }

        if (OwnedWindows.OfType<Window>().Any(static window => window.IsVisible))
        {
            return;
        }

        if (_quickPanel?.IsVisible == true)
        {
            return;
        }

        if (FooterQuickMenuPopup.IsOpen || CommandList.ContextMenu?.IsOpen == true)
        {
            return;
        }

        HideToTray();
    }

    private void MainWindow_Activated(object? sender, EventArgs e)
    {
        SetSearchScopePopupOpen(true);
    }

    private void MainWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        SetSearchScopePopupOpen(IsVisible && IsActive);
    }

    private void MainWindow_PositionChanged(object? sender, EventArgs e)
    {
        RepositionSearchScopePopup();
    }

    private void SetSearchScopePopupOpen(bool isOpen)
    {
        if (!IsInitialized)
        {
            return;
        }

        var shouldOpen = isOpen && IsVisible && WindowState != WindowState.Minimized;
        SearchScopePopup.IsOpen = shouldOpen;
        if (shouldOpen)
        {
            RepositionSearchScopePopup();
        }
    }

    private void RepositionSearchScopePopup()
    {
        if (!IsInitialized || SearchScopePopup?.IsOpen != true)
        {
            return;
        }

        // WPF Popup uses its own native window and can keep an old screen position
        // after DPI changes, DragMove, or hide/show. Nudging the offset forces a
        // placement recomputation without closing the popup.
        var offset = SearchScopePopup.HorizontalOffset;
        SearchScopePopup.HorizontalOffset = offset + 0.1;
        SearchScopePopup.HorizontalOffset = offset;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotKey && wParam.ToInt32() == HotKeyId)
        {
            TogglePanelVisibility();
            handled = true;
        }
        else if (msg == WmHotKey && wParam.ToInt32() == YanmHotKeyId)
        {
            _yanmOverlay?.ToggleFromShortcut();
            handled = true;
        }
        else if (msg == WmHotKey && wParam.ToInt32() == RadialHotKeyId)
        {
            if (IsRadialAllowedForForegroundProcess())
            {
                _radialMenu?.ShowAtMouse();
            }

            handled = true;
        }
        else if (msg == WmHotKey && wParam.ToInt32() == WindowSnapAssistHotKeyId)
        {
            var settings = AppSettingsStore.Load();
            if (TryParseHotkey(settings.WindowSnapAssistHotkey, out var modifiers, out var key))
            {
                _windowSnapAssistService.BeginShortcutSelectionAtMouse(
                    modifiers,
                    KeyInterop.VirtualKeyFromKey(key));
            }
            else
            {
                TriggerWindowSnapAssist();
            }

            handled = true;
        }
        else if (msg == WmHotKey && _registeredExtensionHotkeys.TryGetValue(wParam.ToInt32(), out var command))
        {
            ExecuteCommandFromGlobalHotkey(command);
            handled = true;
        }
        else if (msg == WmDpiChanged)
        {
            HandleWindowDpiChanged(wParam, lParam);
            handled = true;
        }

        return IntPtr.Zero;
    }

    private void SystemEvents_DisplaySettingsChanged(object? sender, EventArgs e)
    {
        _dpiRefreshRequested = true;
        Dispatcher.BeginInvoke(() =>
        {
            if (IsVisible)
            {
                RefreshWindowDpiIfNeeded(force: true);
            }
        }, DispatcherPriority.ApplicationIdle);
    }

    private void RefreshWindowDpiIfNeeded(bool force = false)
    {
        if (_source == null)
        {
            return;
        }

        var currentDpi = GetCurrentWindowDpi();
        if (currentDpi == 0)
        {
            return;
        }

        if (!force && currentDpi == _lastKnownWindowDpi)
        {
            return;
        }

        ApplyWindowDpiScale(currentDpi, suggestedRect: null);
    }

    private void HandleWindowDpiChanged(IntPtr wParam, IntPtr lParam)
    {
        var newDpi = (uint)(wParam.ToInt32() & 0xFFFF);
        if (newDpi == 0)
        {
            newDpi = GetCurrentWindowDpi();
        }

        NativeRect? suggestedRect = null;
        if (lParam != IntPtr.Zero)
        {
            suggestedRect = Marshal.PtrToStructure<NativeRect>(lParam);
        }

        ApplyWindowDpiScale(newDpi, suggestedRect);
    }

    private void ApplyWindowDpiScale(uint newDpi, NativeRect? suggestedRect)
    {
        var previousDpi = _lastKnownWindowDpi == 0 ? newDpi : _lastKnownWindowDpi;
        _lastKnownWindowDpi = newDpi;
        _dpiRefreshRequested = false;

        if (suggestedRect is { } rect)
        {
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget != null)
            {
                var transform = source.CompositionTarget.TransformFromDevice;
                var topLeft = transform.Transform(new System.Windows.Point(rect.Left, rect.Top));
                var bottomRight = transform.Transform(new System.Windows.Point(rect.Right, rect.Bottom));
                Left = topLeft.X;
                Top = topLeft.Y;
                Width = Math.Max(MinWidth, bottomRight.X - topLeft.X);
                Height = Math.Max(MinHeight, bottomRight.Y - topLeft.Y);
            }
        }
        else if (previousDpi != 0 && previousDpi != newDpi)
        {
            var scaleRatio = previousDpi / (double)newDpi;
            Width = Math.Max(MinWidth, Width * scaleRatio);
            Height = Math.Max(MinHeight, Height * scaleRatio);
        }

        InvalidateMeasure();
        InvalidateArrange();
        InvalidateVisual();
        UpdateLayout();
        CommandList.Items.Refresh();
    }

    private uint GetCurrentWindowDpi()
    {
        var handle = new WindowInteropHelper(this).Handle;
        return handle == IntPtr.Zero ? 96u : GetDpiForWindow(handle);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private void ExecuteCommandFromGlobalHotkey(CommandItem command)
    {
        var runnable = ResolveRunnableCommand(command);
        if (runnable.HasHostedView || string.Equals(runnable.HotkeyBehavior, "show-view", StringComparison.OrdinalIgnoreCase))
        {
            ShowPanel();
        }

        _ = ExecuteCommandAsync(runnable, string.Empty, "hotkey");
    }

    private void HandleKeyboardDoubleTap(string keyName)
    {
        var configuredShortcut = AppSettingsStore.Load().LauncherHotkey;
        var expectedShortcut = keyName switch
        {
            "Control" => "DoubleCtrl",
            "Alt" => "DoubleAlt",
            _ => string.Empty
        };

        if (!string.Equals(configuredShortcut, expectedShortcut, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        HostAssets.AppendLog($"Launcher keyboard double tap invoked: {keyName}.");
        TogglePanelVisibility();
    }

    private void StartKeyboardTriggerService()
    {
        KeyboardDoubleTapService.Start(
            HandleKeyboardDoubleTap,
            ShowYanmOverlayHold,
            HideYanmOverlayHold,
            ToggleYanmOverlayPinned);
    }

    private void ShowYanmOverlayHold()
    {
        var settings = _appSettings.Yanm ?? new YanmSettings();
        if (!settings.Enabled || !settings.TriggerWinHold)
        {
            return;
        }

        _yanmOverlay?.ShowTemporary();
    }

    private void HideYanmOverlayHold()
    {
        _yanmOverlay?.HideTemporary();
    }

    private void ToggleYanmOverlayPinned()
    {
        var settings = _appSettings.Yanm ?? new YanmSettings();
        if (!settings.Enabled || !settings.TriggerWinDoubleTap)
        {
            return;
        }

        _yanmOverlay?.TogglePinned();
    }

    private void RefreshExtensionHotkeys()
    {
        if (_source == null)
        {
            return;
        }

        UnregisterExtensionHotkeys();
        _nextExtensionHotkeyId = 0x5400;

        foreach (var command in _localExtensionIndex.Values
                     .Where(command => IsExtensionEnabled(command.ExtensionId))
                     .Where(static x => !string.IsNullOrWhiteSpace(x.GlobalShortcut))
                     .OrderBy(static x => x.Title, StringComparer.OrdinalIgnoreCase))
        {
            if (!TryParseHotkey(command.GlobalShortcut!, out var modifiers, out var key))
            {
                HostAssets.AppendLog($"Invalid global shortcut skipped: {command.Title} -> {command.GlobalShortcut}");
                continue;
            }

            if (key == Key.None || modifiers == 0)
            {
                HostAssets.AppendLog($"Unsupported extension shortcut skipped: {command.Title} -> {command.GlobalShortcut}");
                continue;
            }

            if (command.SupportsQueryArgument && !command.HasHostedView)
            {
                HostAssets.AppendLog($"Query shortcut skipped without hosted view: {command.Title} -> {command.GlobalShortcut}");
                continue;
            }

            var id = _nextExtensionHotkeyId++;
            var success = RegisterHotKey(
                _source.Handle,
                id,
                modifiers | ModNoRepeat,
                (uint)KeyInterop.VirtualKeyFromKey(key));
            if (!success)
            {
                HostAssets.AppendLog($"Failed to register global shortcut: {command.Title} -> {command.GlobalShortcut}");
                continue;
            }

            _registeredExtensionHotkeys[id] = command;
        }
    }

    private void UnregisterExtensionHotkeys()
    {
        if (_source == null)
        {
            _registeredExtensionHotkeys.Clear();
            return;
        }

        foreach (var hotkey in _registeredExtensionHotkeys.Keys.ToArray())
        {
            UnregisterHotKey(_source.Handle, hotkey);
        }

        _registeredExtensionHotkeys.Clear();
    }

    private static bool TryParseHotkey(string shortcut, out uint modifiers, out Key key)
    {
        modifiers = 0;
        key = Key.None;
        if (string.IsNullOrWhiteSpace(shortcut))
        {
            return false;
        }

        if (IsDoubleTapShortcut(shortcut))
        {
            return true;
        }

        var segments = shortcut
            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length < 2)
        {
            return false;
        }

        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index];
            var isLast = index == segments.Length - 1;
            if (!isLast)
            {
                switch (segment.ToLowerInvariant())
                {
                    case "ctrl":
                    case "control":
                        modifiers |= ModControl;
                        continue;
                    case "alt":
                        modifiers |= ModAlt;
                        continue;
                    case "shift":
                        modifiers |= ModShift;
                        continue;
                    case "win":
                    case "windows":
                        modifiers |= ModWin;
                        continue;
                    default:
                        return false;
                }
            }

            try
            {
                key = segment.ToLowerInvariant() switch
                {
                    "space" => Key.Space,
                    "enter" => Key.Enter,
                    "tab" => Key.Tab,
                    "esc" or "escape" => Key.Escape,
                    _ => (Key)new KeyConverter().ConvertFromInvariantString(segment)!
                };
            }
            catch
            {
                return false;
            }
        }

        return modifiers != 0 && key != Key.None;
    }

    private static bool IsDoubleTapShortcut(string shortcut)
    {
        return string.Equals(shortcut, "DoubleCtrl", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(shortcut, "DoubleAlt", StringComparison.OrdinalIgnoreCase);
    }

    private bool RefreshLauncherHotkeyRegistration()
    {
        if (_source == null)
        {
            return false;
        }

        UnregisterLauncherHotkey();
        var shortcut = AppSettingsStore.Load().LauncherHotkey;
        KeyboardDoubleTapService.ApplyConfiguredShortcut(shortcut);
        if (IsDoubleTapShortcut(shortcut))
        {
            HostAssets.AppendLog($"Launcher hotkey registered as double tap: {shortcut}");
            return true;
        }

        if (!TryParseHotkey(shortcut, out var modifiers, out var key))
        {
            HostAssets.AppendLog($"Invalid launcher hotkey skipped: {shortcut}");
            return false;
        }

        var success = RegisterHotKey(
            _source.Handle,
            HotKeyId,
            modifiers | ModNoRepeat,
            (uint)KeyInterop.VirtualKeyFromKey(key));
        if (!success)
        {
            HostAssets.AppendLog($"Failed to register launcher hotkey: {shortcut}");
        }

        return success;
    }

    private bool RefreshYanmHotkeyRegistration()
    {
        if (_source == null)
        {
            return false;
        }

        UnregisterYanmHotkey();
        var yanm = AppSettingsStore.Load().Yanm ?? new YanmSettings();
        if (!yanm.Enabled || !string.Equals(YanmActivationKeys.Normalize(yanm.ActivationKey), YanmActivationKeys.Custom, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!TryParseHotkey(yanm.CustomShortcut, out var modifiers, out var key))
        {
            HostAssets.AppendLog($"Invalid yanm hotkey skipped: {yanm.CustomShortcut}");
            return false;
        }

        var success = RegisterHotKey(
            _source.Handle,
            YanmHotKeyId,
            modifiers | ModNoRepeat,
            (uint)KeyInterop.VirtualKeyFromKey(key));
        if (!success)
        {
            HostAssets.AppendLog($"Failed to register yanm hotkey: {yanm.CustomShortcut}");
        }

        return success;
    }

    private bool RefreshRadialHotkeyRegistration()
    {
        if (_source == null)
        {
            return false;
        }

        UnregisterRadialHotkey();
        var radial = AppSettingsStore.Load().RadialMenu ?? new RadialMenuSettings();
        if (!radial.Enabled || !string.Equals(RadialActivationKeys.Normalize(radial.ActivationKey), RadialActivationKeys.Custom, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!TryParseHotkey(radial.CustomShortcut, out var modifiers, out var key))
        {
            HostAssets.AppendLog($"Invalid radial hotkey skipped: {radial.CustomShortcut}");
            return false;
        }

        var success = RegisterHotKey(
            _source.Handle,
            RadialHotKeyId,
            modifiers | ModNoRepeat,
            (uint)KeyInterop.VirtualKeyFromKey(key));
        if (!success)
        {
            HostAssets.AppendLog($"Failed to register radial hotkey: {radial.CustomShortcut}");
        }

        return success;
    }

    private bool RefreshWindowSnapAssistHotkeyRegistration()
    {
        if (_source == null)
        {
            return false;
        }

        UnregisterWindowSnapAssistHotkey();
        var settings = AppSettingsStore.Load();
        if (!settings.EnableWindowSnapAssist || string.IsNullOrWhiteSpace(settings.WindowSnapAssistHotkey))
        {
            return true;
        }

        if (!TryParseHotkey(settings.WindowSnapAssistHotkey, out var modifiers, out var key) ||
            IsDoubleTapShortcut(settings.WindowSnapAssistHotkey))
        {
            HostAssets.AppendLog($"Invalid window snap assist hotkey skipped: {settings.WindowSnapAssistHotkey}");
            return false;
        }

        var success = RegisterHotKey(
            _source.Handle,
            WindowSnapAssistHotKeyId,
            modifiers | ModNoRepeat,
            (uint)KeyInterop.VirtualKeyFromKey(key));
        if (!success)
        {
            HostAssets.AppendLog($"Failed to register window snap assist hotkey: {settings.WindowSnapAssistHotkey}");
        }

        return success;
    }

    private static bool IsRadialAllowedForForegroundProcess()
    {
        var radial = AppSettingsStore.Load().RadialMenu ?? new RadialMenuSettings();
        var whitelist = radial.WhitelistedProcesses ?? [];
        var blacklist = radial.BlacklistedProcesses ?? [];
        if (whitelist.Count == 0 && blacklist.Count == 0)
        {
            return true;
        }

        var processName = GetForegroundProcessName();
        if (string.IsNullOrWhiteSpace(processName))
        {
            return whitelist.Count == 0;
        }

        if (whitelist.Count > 0)
        {
            var allowed = whitelist.Any(item => ProcessNameMatches(processName, item));
            if (!allowed)
            {
                HostAssets.AppendLog($"Radial hotkey blocked by whitelist, process={processName}.");
            }

            return allowed;
        }

        var blocked = blacklist.Any(item => ProcessNameMatches(processName, item));
        if (blocked)
        {
            HostAssets.AppendLog($"Radial hotkey blocked by blacklist, process={processName}.");
        }

        return !blocked;
    }

    private static bool ProcessNameMatches(string processName, string pattern)
    {
        var normalizedProcess = NormalizeProcessName(processName);
        var normalizedPattern = NormalizeProcessName(pattern);
        if (string.IsNullOrWhiteSpace(normalizedPattern))
        {
            return false;
        }

        if (normalizedPattern.Contains('*', StringComparison.Ordinal))
        {
            var parts = normalizedPattern.Split('*', StringSplitOptions.RemoveEmptyEntries);
            var index = 0;
            foreach (var part in parts)
            {
                var found = normalizedProcess.IndexOf(part, index, StringComparison.OrdinalIgnoreCase);
                if (found < 0)
                {
                    return false;
                }

                index = found + part.Length;
            }

            return true;
        }

        return normalizedProcess.Equals(normalizedPattern, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeProcessName(string value)
    {
        value = (value ?? string.Empty).Trim();
        return value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? value[..^4]
            : value;
    }

    private static string GetForegroundProcessName()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
            {
                return string.Empty;
            }

            _ = GetWindowThreadProcessId(hwnd, out var processId);
            return processId == 0 ? string.Empty : Process.GetProcessById((int)processId).ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }

    private void UnregisterLauncherHotkey()
    {
        if (_source == null)
        {
            return;
        }

        UnregisterHotKey(_source.Handle, HotKeyId);
    }

    private void UnregisterYanmHotkey()
    {
        if (_source == null)
        {
            return;
        }

        UnregisterHotKey(_source.Handle, YanmHotKeyId);
    }

    private void UnregisterRadialHotkey()
    {
        if (_source == null)
        {
            return;
        }

        UnregisterHotKey(_source.Handle, RadialHotKeyId);
    }

    private void UnregisterWindowSnapAssistHotkey()
    {
        if (_source == null)
        {
            return;
        }

        UnregisterHotKey(_source.Handle, WindowSnapAssistHotKeyId);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MOUSEINPUT mi;

        [FieldOffset(0)]
        public KEYBDINPUT ki;

        [FieldOffset(0)]
        public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    public IReadOnlyList<CommandItem> GetLocalExtensionsForSettings()
    {
        return _localExtensionIndex.Values
            .OrderBy(GetLocalExtensionDisplayOrder)
            .ThenBy(static x => x.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<CommandItem> GetExtensionsForSettings()
    {
        return _localExtensionIndex.Values
            .OrderBy(GetLocalExtensionDisplayOrder)
            .ThenBy(static x => x.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static x => x.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public bool IsExtensionEnabled(string extensionId) =>
        !_appSettings.DisabledExtensionIds.Contains(extensionId, StringComparer.OrdinalIgnoreCase);

    public bool TryResolveExtensionCommand(string extensionId, out CommandItem command)
    {
        var resolved = _allCommands.FirstOrDefault(item =>
            item.Source is CommandSource.LocalExtension or CommandSource.Cloud &&
            item.ExtensionId.Equals(extensionId ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
            !item.IsProviderResult);
        if (resolved == null)
        {
            command = null!;
            return false;
        }

        if (!IsExtensionEnabled(resolved.ExtensionId))
        {
            command = null!;
            return false;
        }

        command = resolved;
        return true;
    }

    public void ExecuteExtensionFromWindowBinding(string extensionId)
    {
        if (_listenerServicesPaused)
        {
            return;
        }

        if (!TryResolveExtensionCommand(extensionId, out var command))
        {
            return;
        }

        MarkExtensionAsSeen(command);
        _ = ExecuteCommandAsync(ResolveRunnableCommand(command), explicitInput: null, launchSource: "window-binding");
        LastRunMessage = $"窗口绑定触发：{command.Title}";
    }

    public void SetExtensionEnabled(string extensionId, bool enabled)
    {
        var settings = AppSettingsStore.Load();
        settings.DisabledExtensionIds ??= [];
        settings.DisabledExtensionIds.RemoveAll(id => id.Equals(extensionId, StringComparison.OrdinalIgnoreCase));
        if (!enabled)
        {
            settings.DisabledExtensionIds.Add(extensionId);
        }

        AppSettingsStore.Save(settings);
        _appSettings = settings;
        _windowBoundExtensionsService.Reload(_appSettings.WindowBindings);
        RefreshExtensionHotkeys();
        ApplyFilter(SearchBox.Text);
    }

    private void ReplaceLocalExtensions(IReadOnlyList<LocalExtensionCatalogEntry> entries, string? statusText)
    {
        ReplaceLocalExtensions(entries.Select(LocalExtensionCatalog.CreateCommand).ToList(), statusText);
    }

    private void ReplaceLocalExtensions(IReadOnlyList<CommandItem> commands, string? statusText)
    {
        _allCommands.RemoveAll(x => x.Source == CommandSource.LocalExtension);
        _localExtensionIndex.Clear();
        foreach (var command in commands.OrderBy(GetLocalExtensionDisplayOrder).ThenBy(static x => x.Title, StringComparer.OrdinalIgnoreCase))
        {
            UpsertLocalExtensionCommand(command);
        }

        StartMouseGestureService();
        ApplyFilter(SearchBox.Text);
        if (!string.IsNullOrWhiteSpace(statusText))
        {
            SyncStatus = statusText;
        }
    }

    public IReadOnlyList<CommandItem> GetQuickPanelRecommendedCommands(ForegroundAppContext? context, IEnumerable<string> excludeExtensionIds, int maxCount = 8)
    {
        if (context == null || string.IsNullOrWhiteSpace(context.ProcessName))
        {
            return [];
        }

        var exclude = new HashSet<string>(excludeExtensionIds, StringComparer.OrdinalIgnoreCase);
        var aliases = BuildContextAliases(context.ProcessName, context.WindowTitle);

        return _allCommands
            .Where(static command => !IsInternalCommand(command))
            .Where(command => IsExtensionEnabled(command.ExtensionId))
            .Where(command => !exclude.Contains(command.ExtensionId))
            .Select(command => new
            {
                Command = command,
                Score = ScoreQuickPanelRecommendation(command, aliases, context.WindowTitle)
            })
            .Where(static item => item.Score > 0)
            .OrderByDescending(static item => item.Score)
            .ThenBy(item => item.Command.Title, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(0, maxCount))
            .Select(static item => item.Command)
            .ToList();
    }

    public Task<(bool ok, string message)> EditExtensionFromSettingsAsync(string extensionId, Window? owner = null)
    {
        try
        {
            if (!_localExtensionIndex.TryGetValue(extensionId, out var editable))
            {
                return Task.FromResult((false, "没有找到对应扩展。"));
            }

            var manifestJson = LocalExtensionCatalog.LoadManifestJson(editable.ExtensionId);
            var updated = ShowJsonExtensionEditorForOwner(manifestJson, isEditMode: true, owner);
            if (updated == null)
            {
                return Task.FromResult((false, string.Empty));
            }

            ReplaceEditedExtensionReferences(extensionId, updated.ExtensionId);
            LastRunMessage = $"已更新本地 JSON 扩展：{updated.Title}";
            QueueBackgroundWebDavSync("extension-edit-settings");
            return Task.FromResult((true, $"已更新扩展：{updated.Title}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult((false, $"编辑失败：{FormatExceptionMessage(ex)}"));
        }
    }

    public Task<(bool ok, string message)> EditExtensionFromQuickPanelAsync(string extensionId, Window? owner = null)
    {
        return EditExtensionFromSettingsAsync(extensionId, owner);
    }

    private void ReplaceEditedExtensionReferences(string oldExtensionId, string newExtensionId)
    {
        if (string.IsNullOrWhiteSpace(oldExtensionId) ||
            string.IsNullOrWhiteSpace(newExtensionId) ||
            string.Equals(oldExtensionId, newExtensionId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var settings = AppSettingsStore.Load();
        var changed = false;

        changed |= ReplaceExtensionId(settings.FavoriteExtensionIds, oldExtensionId, newExtensionId);
        changed |= ReplaceExtensionId(settings.GlobalFavoriteExtensionIds, oldExtensionId, newExtensionId);
        changed |= ReplaceExtensionId(settings.ContextFavoriteExtensionIds, oldExtensionId, newExtensionId);
        changed |= ReplaceExtensionId(settings.DisabledExtensionIds, oldExtensionId, newExtensionId);
        changed |= ReplaceExtensionId(settings.PinnedSearchScopeCommandIds, oldExtensionId, newExtensionId);
        changed |= ReplaceExtensionId(settings.RecentlyAddedExtensionIds, oldExtensionId, newExtensionId);
        changed |= ReplaceExtensionId(settings.UnreadNewExtensionIds, oldExtensionId, newExtensionId);
        changed |= ReplaceNullableExtensionId(settings.QuickPanelSlots, oldExtensionId, newExtensionId);

        foreach (var group in settings.QuickPanelGlobalGroups.Concat(settings.QuickPanelContextGroups))
        {
            changed |= ReplaceNullableExtensionId(group.Slots, oldExtensionId, newExtensionId);
            group.SlotItems ??= [];
            for (var index = 0; index < group.SlotItems.Count; index++)
            {
                var item = group.SlotItems[index];
                if (ReplaceExtensionInQuickPanelSlotItem(item, oldExtensionId, newExtensionId))
                {
                    changed = true;
                }
            }

            group.Slots = group.SlotItems
                .Take(12)
                .Select(static item => item != null && !item.IsFolder ? item.ExtensionId : null)
                .ToList();
        }

        if (settings.RadialMenu != null)
        {
            changed |= ReplaceNullableExtensionId(settings.RadialMenu.Slots, oldExtensionId, newExtensionId);
            foreach (var page in settings.RadialMenu.Pages)
            {
                changed |= ReplaceNullableExtensionId(page.Slots, oldExtensionId, newExtensionId);
            }
        }

        if (!changed)
        {
            return;
        }

        AppSettingsStore.Save(settings);
        _appSettings = settings;
        _windowBoundExtensionsService.Reload(_appSettings.WindowBindings);
        _quickPanel?.ReloadSlots();
        NotifyQuickPanelSettingsChanged("extension-edit-id-replace");
        HostAssets.AppendLog($"Extension edit references replaced: oldId={oldExtensionId}, newId={newExtensionId}.");
    }

    private static bool ReplaceExtensionId(List<string> values, string oldExtensionId, string newExtensionId)
    {
        var changed = false;
        for (var index = 0; index < values.Count; index++)
        {
            if (!values[index].Equals(oldExtensionId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            values[index] = newExtensionId;
            changed = true;
        }

        return changed;
    }

    private static bool ReplaceNullableExtensionId(List<string?> values, string oldExtensionId, string newExtensionId)
    {
        var changed = false;
        for (var index = 0; index < values.Count; index++)
        {
            if (!string.Equals(values[index], oldExtensionId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            values[index] = newExtensionId;
            changed = true;
        }

        return changed;
    }

    private static bool ReplaceExtensionInQuickPanelSlotItem(QuickPanelSlotItem? item, string oldExtensionId, string newExtensionId)
    {
        if (item == null)
        {
            return false;
        }

        if (!item.IsFolder)
        {
            if (!string.Equals(item.ExtensionId, oldExtensionId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            item.ExtensionId = newExtensionId;
            return true;
        }

        var changed = ReplaceExtensionId(item.FolderExtensionIds, oldExtensionId, newExtensionId);
        item.FolderSlotItems ??= [];
        foreach (var child in item.FolderSlotItems)
        {
            changed |= ReplaceExtensionInQuickPanelSlotItem(child, oldExtensionId, newExtensionId);
        }

        item.FolderExtensionIds = item.FolderSlotItems
            .Where(static slot => slot != null && !slot.IsFolder && !string.IsNullOrWhiteSpace(slot.ExtensionId))
            .Select(static slot => slot!.ExtensionId!)
            .ToList();

        return changed;
    }

    public Task<(bool ok, string message)> DeleteExtensionFromSettingsAsync(string extensionId, Window? owner = null)
    {
        try
        {
            if (!_localExtensionIndex.TryGetValue(extensionId, out var deletable))
            {
                return Task.FromResult((false, "没有找到对应扩展。"));
            }

            var confirm = System.Windows.MessageBox.Show(
                owner ?? this,
                $"确认删除“{deletable.Title}”吗？",
                "删除扩展",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes)
            {
                return Task.FromResult((false, string.Empty));
            }

            WebDavSyncService.MarkExtensionDeletedLocally(deletable.ExtensionId, deletable.DeclaredVersion);
            ExtensionRecycleBinService.MoveToRecycleBin(deletable.ExtensionId);
            RemoveExtensionFromQuickPanelSettings(deletable.ExtensionId);
            RemoveLocalExtensionCommand(deletable.ExtensionId);
            ApplyFilter(SearchBox.Text);
            SelectedCommand = FilteredCommands.FirstOrDefault();
            CommandList.SelectedItem = SelectedCommand;

            LastRunMessage = $"已将扩展移入回收站：{deletable.Title}";
            SyncStatus = $"已将扩展移入回收站：{deletable.Title}";
            QueueBackgroundWebDavSync("extension-delete-settings");
            return Task.FromResult((true, $"已将扩展移入回收站：{deletable.Title}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult((false, $"删除失败：{FormatExceptionMessage(ex)}"));
        }
    }

    public Task<(bool ok, string message)> DeleteExtensionFromQuickPanelAsync(string extensionId, Window? owner = null)
    {
        return DeleteExtensionFromSettingsAsync(extensionId, owner);
    }

    private void RemoveExtensionFromQuickPanelSettings(string extensionId)
    {
        if (string.IsNullOrWhiteSpace(extensionId))
        {
            return;
        }

        var settings = AppSettingsStore.Load();
        var changed = false;

        settings.GlobalFavoriteExtensionIds.RemoveAll(id => id.Equals(extensionId, StringComparison.OrdinalIgnoreCase));
        settings.ContextFavoriteExtensionIds.RemoveAll(id => id.Equals(extensionId, StringComparison.OrdinalIgnoreCase));
        settings.DisabledExtensionIds.RemoveAll(id => id.Equals(extensionId, StringComparison.OrdinalIgnoreCase));
        settings.PinnedSearchScopeCommandIds.RemoveAll(id => id.Equals(extensionId, StringComparison.OrdinalIgnoreCase));

        foreach (var group in settings.QuickPanelGlobalGroups.Concat(settings.QuickPanelContextGroups))
        {
            group.SlotItems ??= [];
            while (group.SlotItems.Count < 12)
            {
                group.SlotItems.Add(null);
            }

            for (var index = 0; index < group.SlotItems.Count; index++)
            {
                var item = group.SlotItems[index];
                if (!RemoveExtensionFromQuickPanelSlotItem(ref item, extensionId))
                {
                    continue;
                }

                group.SlotItems[index] = item;
                changed = true;
            }

            group.Slots = group.SlotItems
                .Take(12)
                .Select(static item => item != null && !item.IsFolder ? item.ExtensionId : null)
                .ToList();
        }

        if (!changed)
        {
            return;
        }

        AppSettingsStore.Save(settings);
        _appSettings = settings;
        _windowBoundExtensionsService.Reload(_appSettings.WindowBindings);
        _quickPanel?.ReloadSlots();
        NotifyQuickPanelSettingsChanged("extension-delete-cleanup");
    }

    private static bool RemoveExtensionFromQuickPanelSlotItem(ref QuickPanelSlotItem? item, string extensionId)
    {
        if (item == null)
        {
            return false;
        }

        if (!item.IsFolder)
        {
            if (!string.Equals(item.ExtensionId, extensionId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            item = null;
            return true;
        }

        var changed = item.FolderExtensionIds.RemoveAll(id => id.Equals(extensionId, StringComparison.OrdinalIgnoreCase)) > 0;
        item.FolderSlotItems ??= [];
        for (var index = 0; index < item.FolderSlotItems.Count; index++)
        {
            var child = item.FolderSlotItems[index];
            if (RemoveExtensionFromQuickPanelSlotItem(ref child, extensionId))
            {
                item.FolderSlotItems[index] = child;
                changed = true;
            }
        }

        item.FolderExtensionIds = item.FolderSlotItems
            .Where(static slot => slot != null && !slot.IsFolder && !string.IsNullOrWhiteSpace(slot.ExtensionId))
            .Select(static slot => slot!.ExtensionId!)
            .ToList();
        if (!item.FolderSlotItems.Any(static slot => slot != null))
        {
            item = null;
        }

        return changed;
    }

    public IReadOnlyList<RecycledExtensionEntry> GetRecycleBinEntriesForSettings()
    {
        return ExtensionRecycleBinService.LoadEntries();
    }

    public Task<(bool ok, string message)> RestoreExtensionFromRecycleBinAsync(string itemId)
    {
        try
        {
            var restored = ExtensionRecycleBinService.RestoreFromRecycleBin(itemId);
            var command = LocalExtensionCatalog.LoadCommands()
                .FirstOrDefault(item => item.ExtensionId.Equals(restored.ExtensionId, StringComparison.OrdinalIgnoreCase));
            if (command == null)
            {
                throw new InvalidOperationException("恢复后的扩展清单无效。");
            }

            WebDavSyncService.MarkExtensionRestoredLocally(command.ExtensionId, command.DeclaredVersion);
            UpsertLocalExtensionCommand(command);
            ApplyFilter(SearchBox.Text);
            SelectedCommand = _allCommands.FirstOrDefault(item =>
                item.ExtensionId.Equals(command.ExtensionId, StringComparison.OrdinalIgnoreCase));
            CommandList.SelectedItem = SelectedCommand;
            LastRunMessage = $"已从回收站恢复扩展：{command.Title}";
            SyncStatus = $"已恢复扩展：{command.Title}";
            QueueBackgroundWebDavSync("extension-restore-settings");
            return Task.FromResult((true, $"已恢复扩展：{command.Title}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult((false, $"恢复失败：{FormatExceptionMessage(ex)}"));
        }
    }

    public Task<(bool ok, string message)> PurgeExtensionFromRecycleBinAsync(string itemId)
    {
        try
        {
            var deleted = ExtensionRecycleBinService.DeletePermanently(itemId);
            WebDavSyncService.MarkExtensionPurgedLocally(deleted.ExtensionId, deleted.Version);
            RemoveLocalExtensionCommand(deleted.ExtensionId);
            ApplyFilter(SearchBox.Text);
            SelectedCommand = FilteredCommands.FirstOrDefault();
            CommandList.SelectedItem = SelectedCommand;
            LastRunMessage = $"已彻底删除扩展：{deleted.Title}";
            SyncStatus = $"已彻底删除扩展：{deleted.Title}";
            QueueBackgroundWebDavSync("extension-purge-settings");
            return Task.FromResult((true, $"已彻底删除扩展：{deleted.Title}，会同步到其他设备。"));
        }
        catch (Exception ex)
        {
            return Task.FromResult((false, $"彻底删除失败：{FormatExceptionMessage(ex)}"));
        }
    }

    private static Dictionary<string, string> ParseProtocolQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(query))
        {
            return result;
        }

        var segments = query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var segment in segments)
        {
            var pair = segment.Split('=', 2);
            var key = Uri.UnescapeDataString(pair[0]);
            var value = pair.Length > 1 ? Uri.UnescapeDataString(pair[1]) : string.Empty;
            if (!string.IsNullOrWhiteSpace(key))
            {
                result[key] = value;
            }
        }

        return result;
    }

    private static string? GetProtocolValue(IReadOnlyDictionary<string, string> parameters, string key)
    {
        return parameters.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
    }

    public bool TryOpenExtensionDirectory(string extensionId, out string message)
    {
        message = string.Empty;
        if (!_localExtensionIndex.TryGetValue(extensionId, out var command) ||
            string.IsNullOrWhiteSpace(command.ExtensionDirectoryPath) ||
            !Directory.Exists(command.ExtensionDirectoryPath))
        {
            message = "扩展目录不存在。";
            return false;
        }

        var directoryPath = command.ExtensionDirectoryPath!;
        Process.Start(new ProcessStartInfo
        {
            FileName = directoryPath,
            UseShellExecute = true
        });
        return true;
    }

    public Task<(bool ok, string message)> UpdateExtensionShortcutFromSettingsAsync(string extensionId, string? shortcut)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(shortcut) &&
                (!TryParseHotkey(shortcut, out _, out _) || IsDoubleTapShortcut(shortcut)))
            {
                return Task.FromResult((false, "快捷键格式无效。示例：Ctrl+Alt+T"));
            }

            var updated = LocalExtensionCatalog.SetGlobalShortcut(extensionId, shortcut);
            UpsertLocalExtensionCommand(updated);
            ApplyFilter(SearchBox.Text);
            QueueBackgroundWebDavSync("extension-shortcut-settings");

            var message = string.IsNullOrWhiteSpace(updated.GlobalShortcut)
                ? $"已清除快捷键：{updated.Title}"
                : $"已设置快捷键：{updated.Title} -> {updated.GlobalShortcut}";
            return Task.FromResult((true, message));
        }
        catch (Exception ex)
        {
            return Task.FromResult((false, $"设置快捷键失败：{FormatExceptionMessage(ex)}"));
        }
    }

    public Task<(bool ok, string message, CommandItem? updated)> UpdateExtensionStartupFromSettingsAsync(string extensionId, string? mode, string? schedule)
    {
        try
        {
            var updated = LocalExtensionCatalog.SetStartup(extensionId, mode, schedule);
            UpsertLocalExtensionCommand(updated);
            ApplyFilter(SearchBox.Text);
            QueueBackgroundWebDavSync("extension-startup-settings");

            // 通知调度服务更新
            NotifyScheduleChanged(extensionId, updated.Startup?.Schedule);

            var message = updated.Startup == null
                ? $"已关闭自动运行：{updated.Title}"
                : $"已更新自动运行：{updated.Title}";
            return Task.FromResult<(bool ok, string message, CommandItem? updated)>((true, message, updated));
        }
        catch (Exception ex)
        {
            return Task.FromResult<(bool ok, string message, CommandItem? updated)>((false, $"更新自动运行失败：{FormatExceptionMessage(ex)}", null));
        }
    }

    public Task<(bool ok, string message, CommandItem? updated)> UpdateExtensionMouseGestureFromSettingsAsync(
        string extensionId,
        LocalExtensionMouseGestureManifest? mouseGesture)
    {
        try
        {
            var updated = LocalExtensionCatalog.SetMouseGesture(extensionId, mouseGesture);
            UpsertLocalExtensionCommand(updated);
            ApplyFilter(SearchBox.Text);
            ReloadMouseGestureRegistrations();
            QueueBackgroundWebDavSync("extension-mouse-gesture-settings");

            var message = mouseGesture == null || string.IsNullOrWhiteSpace(mouseGesture.Sequence)
                ? $"已清除鼠标手势：{updated.Title}"
                : $"已绑定鼠标手势：{updated.Title} -> {mouseGesture.Sign ?? mouseGesture.Sequence}";
            return Task.FromResult<(bool ok, string message, CommandItem? updated)>((true, message, updated));
        }
        catch (Exception ex)
        {
            return Task.FromResult<(bool ok, string message, CommandItem? updated)>((false, $"更新鼠标手势失败：{FormatExceptionMessage(ex)}", null));
        }
    }

    private void OpenCommandActionsMenu(CommandActionsMenuOrigin origin = CommandActionsMenuOrigin.ResultsList)
    {
        _commandActionsMenuOrigin = origin;
        if (origin == CommandActionsMenuOrigin.ResultsList)
        {
            CommandList.Focus();
        }

        var selectedItem = CommandList.ItemContainerGenerator.ContainerFromItem(SelectedCommand) as FrameworkElement;
        var selectedCommand = SelectedCommand;
        if (selectedCommand?.IsFileSystemResult == true)
        {
            ShowFileResultContextMenu(selectedItem, keyboardInvoked: true);
            return;
        }

        if (selectedCommand?.IsProviderResult == true)
        {
            ShowGenericResultContextMenu(selectedItem, keyboardInvoked: true);
            return;
        }

        if (!UpdateCommandContextMenuState() || CommandList.ContextMenu == null || !CommandList.ContextMenu.HasItems)
        {
            return;
        }

        if (selectedItem != null)
        {
            CommandList.ContextMenu.PlacementTarget = selectedItem;
            CommandList.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Right;
        }
        else
        {
            CommandList.ContextMenu.PlacementTarget = CommandList;
            CommandList.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Center;
        }
        
        CommandList.ContextMenu.IsOpen = true;
    }

    public void OpenSettingsWindow(string? sectionKey = null)
    {
        if (System.Windows.Application.Current is App app)
        {
            app.OpenSettingsWindow(sectionKey);
            return;
        }

        var settingsWindow = new SettingsWindow(this);
        if (sectionKey != null) settingsWindow.NavigateTo(sectionKey);
        settingsWindow.Show();
    }

    private async Task CreateDesktopShortcutAsync()
    {
        var sourceCommand = SelectedCommand != null && !IsInternalCommand(SelectedCommand)
            ? SelectedCommand
            : _lastActionableCommand;
        if (sourceCommand == null)
        {
            SyncStatus = "没有可创建快捷方式的命令。";
            return;
        }

        var command = ResolveRunnableCommand(sourceCommand);
        if (string.IsNullOrWhiteSpace(command.OpenTarget) || IsInternalCommand(command))
        {
            SyncStatus = "当前命令不支持创建桌面快捷方式。";
            return;
        }

        try
        {
            var path = DesktopShortcutService.CreateShortcut(command.Title, command.OpenTarget);
            LastRunMessage = $"已创建桌面快捷方式：{path}";
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            SyncStatus = $"创建快捷方式失败：{FormatExceptionMessage(ex)}";
        }
    }

    private Task RenameSelectedExtensionAsync()
    {
        var sourceCommand = SelectedCommand != null && !IsInternalCommand(SelectedCommand)
            ? SelectedCommand
            : _lastActionableCommand;
        if (sourceCommand == null)
        {
            SyncStatus = "没有可重命名的扩展。";
            return Task.CompletedTask;
        }

        var extension = ResolveRunnableCommand(sourceCommand);
        if (extension.Source != CommandSource.LocalExtension)
        {
            SyncStatus = "当前选中项不是本地扩展，不能直接重命名。";
            return Task.CompletedTask;
        }

        var dialog = new SimpleTextInputWindow("重命名扩展", "输入新的扩展名称。", extension.Title)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return Task.CompletedTask;
        }

        try
        {
            var renamed = LocalExtensionCatalog.RenameExtension(extension.ExtensionId, dialog.ValueText);
            UpsertLocalExtensionCommand(renamed);
            ApplyFilter(SearchBox.Text);
            SelectedCommand = _allCommands.FirstOrDefault(x => x.ExtensionId.Equals(renamed.ExtensionId, StringComparison.OrdinalIgnoreCase));
            CommandList.SelectedItem = SelectedCommand;
            LastRunMessage = $"已重命名扩展：{renamed.Title}";
            QueueBackgroundWebDavSync("extension-rename");
        }
        catch (Exception ex)
        {
            SyncStatus = $"重命名失败：{FormatExceptionMessage(ex)}";
        }

        return Task.CompletedTask;
    }

    private void AddToQuickPanelMenuItem_Click(object sender, RoutedEventArgs e)
    {
        AddCurrentCommandToQuickPanel();
    }

}
