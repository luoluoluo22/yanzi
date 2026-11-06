using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WpfColor = System.Windows.Media.Color;
using OpenQuickHost.Sync;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfPoint = System.Windows.Point;
using WpfVector = System.Windows.Vector;

namespace OpenQuickHost;

public partial class SettingsWindow
{
    private DispatcherTimer? _aiSaveTimer;
    private DispatcherTimer? _aiStatusHideTimer;
    private bool _isAiSaveStatusVisible;
    private bool _deferredAiCredentialRefresh;

    public bool IsAiSaveStatusVisible
    {
        get => _isAiSaveStatusVisible;
        private set
        {
            if (_isAiSaveStatusVisible == value) return;
            _isAiSaveStatusVisible = value;
            OnPropertyChanged();
        }
    }

    private void QueueAiSettingsSave(int delayMs = 500)
    {
        if (_aiSaveTimer == null)
        {
            _aiSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delayMs) };
            _aiSaveTimer.Tick += (s, e) => { _aiSaveTimer.Stop(); SaveAiSettings(); };
        }
        else
        {
            _aiSaveTimer.Stop();
            _aiSaveTimer.Interval = TimeSpan.FromMilliseconds(delayMs);
        }
        _aiSaveTimer.Start();
    }

    private void FlushAiSettingsSave()
    {
        if (_aiSaveTimer != null && _aiSaveTimer.IsEnabled)
        {
            _aiSaveTimer.Stop();
            SaveAiSettings();
        }
    }

    private void SaveAiSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        SaveAiSettings();
    }

    private void SaveAiSettings()
    {
        // 1. 同步服务商列表到 _settings
        _settings.AiServiceProviders = AiServiceProvidersList.Select(vm => {
            vm.RawSettings.Models = vm.Models.ToList();
            return vm.RawSettings;
        }).ToList();

        // 2. 同步提示词
        _settings.AiSystemPrompt = AiSystemPrompt;

        // A disabled or deleted provider must never remain the active chat channel.
        var activeProvider = SelectedServiceProvider?.IsEnabled == true
            ? SelectedServiceProvider
            : AiServiceProvidersList.FirstOrDefault(item => item.IsEnabled &&
                string.Equals(item.Id, _settings.ActiveServiceProviderId, StringComparison.OrdinalIgnoreCase))
              ?? AiServiceProvidersList.FirstOrDefault(item => item.IsEnabled);
        if (activeProvider != null)
        {
            _settings.ActiveServiceProviderId = activeProvider.Id;
            _settings.AiBaseUrl = activeProvider.BaseUrl;
            _settings.AiApiKey = activeProvider.ApiKey;
            _settings.AiModel = !string.IsNullOrWhiteSpace(activeProvider.SelectedModel)
                ? activeProvider.SelectedModel : activeProvider.Models.FirstOrDefault() ?? string.Empty;
        }
        else
        {
            _settings.ActiveServiceProviderId = string.Empty;
            _settings.AiBaseUrl = string.Empty;
            _settings.AiApiKey = string.Empty;
            _settings.AiModel = string.Empty;
        }

        // 4. 保存设置并同步至主窗口
        _settings = _settingsPersistence.Save(_settings);
        CloudSyncDiagnostics.Log(
            "SettingsWindow.Ai",
            "AI settings saved",
            ("providerCount", _settings.AiServiceProviders.Count),
            ("activeProviderId", _settings.ActiveServiceProviderId ?? string.Empty),
            ("providerNames", string.Join(", ", _settings.AiServiceProviders.Select(static provider => provider.Name ?? string.Empty))));
        _mainWindow.OnAiSettingsChanged();
        _mainWindow.NotifyQuickPanelSettingsChanged("ai-settings-saved", refreshYanmOverlay: false);

        // 5. 更新原始值
        _originalAiBaseUrl = _settings.AiBaseUrl;
        _originalAiApiKey = _settings.AiApiKey;
        _originalAiModel = _settings.AiModel;
        _originalAiSystemPrompt = _settings.AiSystemPrompt;

        AiBaseUrl = _settings.AiBaseUrl;
        AiApiKey = _settings.AiApiKey;
        AiModel = _settings.AiModel;
        AiSystemPrompt = _settings.AiSystemPrompt;
        AiSettingsStatusText = BuildAiSettingsSummary(_settings);

        // 6. 重置状态
        HasAiSettingsChanged = false;
        if (_deferredAiCredentialRefresh)
        {
            _deferredAiCredentialRefresh = false;
            RefreshAiConfigFromExternal();
        }
        _aiStatusHideTimer = ShowSaveStatusTemporarily(_aiStatusHideTimer, visible => IsAiSaveStatusVisible = visible);
    }

    private void AiProviderEnabledToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.CheckBox toggle ||
            toggle.DataContext is not SettingsAiProviderVM provider)
            return;

        // Both the list switch and details switch use the same activation flow.
        provider.IsEnabled = toggle.IsChecked == true;
        if (provider.IsEnabled)
        {
            SelectedServiceProvider = provider;
        }
        else if (ReferenceEquals(SelectedServiceProvider, provider) ||
                 string.Equals(_settings.ActiveServiceProviderId, provider.Id, StringComparison.OrdinalIgnoreCase))
        {
            SelectedServiceProvider = AiServiceProvidersList.FirstOrDefault(item => item.IsEnabled)
                                      ?? provider;
        }

        _aiSaveTimer?.Stop();
        HasAiSettingsChanged = true;
        SaveAiSettings();
    }

    private void AiProviderItem_MouseRightButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SettingsAiProviderVM provider } anchor)
            return;

        var menu = new Yanzi.UI.Wpf.YanziDropdownMenu();
        menu.UseStandaloneTheme(Yanzi.UI.Wpf.YanziUi.GetTheme(this));
        menu.UseContentWidth(182);
        menu.AddAction("设为当前渠道", () => ActivateAiProvider(provider));
        menu.AddSeparator();
        menu.AddAction("删除供应商", () => DeleteAiProvider(provider));
        var physical = anchor.PointToScreen(System.Windows.Input.Mouse.GetPosition(anchor));
        var source = System.Windows.PresentationSource.FromVisual(anchor);
        if (source?.CompositionTarget is null)
            return;
        var matrix = source.CompositionTarget.TransformFromDevice;
        var screen = System.Windows.Forms.Screen.FromPoint(
            new System.Drawing.Point((int)Math.Round(physical.X), (int)Math.Round(physical.Y))).WorkingArea;
        menu.ShowAtScreenPoint(matrix.Transform(physical),
            new System.Windows.Rect(matrix.Transform(new System.Windows.Point(screen.Left, screen.Top)),
                matrix.Transform(new System.Windows.Point(screen.Right, screen.Bottom))));
        e.Handled = true;
    }

    private void ActivateAiProvider(SettingsAiProviderVM provider)
    {
        if (!AiServiceProvidersList.Contains(provider))
            return;
        if (!provider.IsEnabled)
        {
            System.Windows.MessageBox.Show(this, "请先启用此供应商，再将它设为当前渠道。",
                "模型服务", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SelectedServiceProvider = provider;
        _aiSaveTimer?.Stop();
        HasAiSettingsChanged = true;
        SaveAiSettings();
    }

    private void DeleteAiProvider(SettingsAiProviderVM provider)
    {
        if (!AiServiceProvidersList.Contains(provider))
            return;

        // Extension-owned registrations reappear on manifest reconciliation.
        if (!string.IsNullOrWhiteSpace(provider.RawSettings.OwnerExtensionId) ||
            provider.Id.StartsWith("miniapp:", StringComparison.OrdinalIgnoreCase))
        {
            System.Windows.MessageBox.Show(this,
                "此供应商由小程序自动注册。请在对应小程序中卸载或移除模型服务注册，不能在这里直接删除。",
                "模型服务", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (System.Windows.MessageBox.Show(this,
                $"确定删除供应商「{provider.Name}」及其模型配置吗？此操作无法撤销。",
                "删除供应商", MessageBoxButton.YesNo, MessageBoxImage.Warning,
                MessageBoxResult.No) != MessageBoxResult.Yes)
            return;

        _aiSaveTimer?.Stop();
        var previouslyActiveId = _settings.ActiveServiceProviderId;
        AiServiceProvidersList.Remove(provider);
        if (ReferenceEquals(SelectedServiceProvider, provider) ||
            string.Equals(previouslyActiveId, provider.Id, StringComparison.OrdinalIgnoreCase))
        {
            SelectedServiceProvider = AiServiceProvidersList.FirstOrDefault(item =>
                item.IsEnabled && string.Equals(item.Id, previouslyActiveId, StringComparison.OrdinalIgnoreCase))
                ?? AiServiceProvidersList.FirstOrDefault(item => item.IsEnabled)
                ?? AiServiceProvidersList.FirstOrDefault();
        }

        OnPropertyChanged(nameof(FilteredProviders));
        HasAiSettingsChanged = true;
        SaveAiSettings();
    }

    private async void AddProviderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new AddProviderWindow();
            dialog.Owner = this;
            if (dialog.ShowDialog() == true)
            {
                if (dialog.ProviderType == YanziAgyModelBridge.ProviderType)
                    await YanziAgyManagedInstaller.EnsureInstalledAsync();
                var newProvider = new AiServiceProviderSettings
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = dialog.ProviderName,
                    ProviderType = dialog.ProviderType,
                    BaseUrl = BuildDefaultProviderBaseUrl(dialog.ProviderType),
                    ApiKey = dialog.ProviderType == YanziAgyModelBridge.ProviderType
                        ? YanziAgyModelBridge.GetLocalKey() : "",
                    IsEnabled = true,
                    Models = dialog.ProviderType == YanziAgyModelBridge.ProviderType
                        ? ["agy-default"] : [],
                    SelectedModel = dialog.ProviderType == YanziAgyModelBridge.ProviderType
                        ? "agy-default" : string.Empty
                };

                var vm = new SettingsAiProviderVM(newProvider);
                _aiServiceProvidersList.Add(vm);
                SelectedServiceProvider = vm;
                HasAiSettingsChanged = true;
                OnPropertyChanged(nameof(FilteredProviders));
                QueueAiSettingsSave();
            }
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"SettingsWindow.Ai: AddProviderWindow failed: {ex}");
            System.Windows.MessageBox.Show(this, $"打开添加提供商窗口失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DeleteModelButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is string modelName && SelectedServiceProvider != null)
        {
            SelectedServiceProvider.Models.Remove(modelName);
            if (SelectedServiceProvider.SelectedModel == modelName)
            {
                SelectedServiceProvider.SelectedModel = SelectedServiceProvider.Models.FirstOrDefault() ?? string.Empty;
            }
            HasAiSettingsChanged = true;
            QueueAiSettingsSave();
        }
    }

    private void AddNewModelButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedServiceProvider == null) return;

        var dialog = new SimpleTextInputWindow("添加模型", "请输入模型名称（如 gpt-4o, deepseek-chat）:", "");
        dialog.Owner = this;
        if (dialog.ShowDialog() == true)
        {
            var modelName = dialog.ValueText;
            if (!string.IsNullOrWhiteSpace(modelName))
            {
                if (!SelectedServiceProvider.Models.Contains(modelName))
                {
                    SelectedServiceProvider.Models.Add(modelName);
                    if (string.IsNullOrWhiteSpace(SelectedServiceProvider.SelectedModel))
                    {
                        SelectedServiceProvider.SelectedModel = modelName;
                    }
                    HasAiSettingsChanged = true;
                    QueueAiSettingsSave();
                }
            }
        }
    }

    private async void ManageModelsButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedServiceProvider == null)
        {
            System.Windows.MessageBox.Show(this, "请先选择一个提供商。", "管理模型", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedServiceProvider.BaseUrl) || string.IsNullOrWhiteSpace(SelectedServiceProvider.ApiKey))
        {
            System.Windows.MessageBox.Show(this, "请先填写当前提供商的 API 地址和 API 密钥。", "管理模型", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
            var availableModels = await FetchAvailableModelsAsync(SelectedServiceProvider);
            Mouse.OverrideCursor = null;
            if (availableModels.Count == 0)
            {
                System.Windows.MessageBox.Show(this, "没有读取到可用模型。请检查提供商接口是否支持读取模型列表。", "管理模型", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var picker = new AiModelPickerWindow(SelectedServiceProvider.Name, availableModels, SelectedServiceProvider.Models)
            {
                Owner = this
            };

            if (picker.ShowDialog() == true)
            {
                var addedCount = 0;
                foreach (var modelName in picker.SelectedModels)
                {
                    if (SelectedServiceProvider.Models.Contains(modelName))
                    {
                        continue;
                    }

                    SelectedServiceProvider.Models.Add(modelName);
                    addedCount++;
                }

                if (addedCount > 0)
                {
                    if (string.IsNullOrWhiteSpace(SelectedServiceProvider.SelectedModel))
                    {
                        SelectedServiceProvider.SelectedModel = SelectedServiceProvider.Models.FirstOrDefault() ?? string.Empty;
                    }

                    HasAiSettingsChanged = true;
                    QueueAiSettingsSave();
                    ShowToast($"已添加 {addedCount} 个模型");
                }
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, $"读取模型列表失败：{ex.Message}", "管理模型", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private async void CheckApiKeyButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedServiceProvider == null) return;
        var baseUrl = SelectedServiceProvider.BaseUrl;
        var apiKey = SelectedServiceProvider.ApiKey;

        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(apiKey))
        {
            System.Windows.MessageBox.Show("请先填写服务地址和 API 密钥。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        CheckApiKeyButtonText = "检测中...";

        try
        {
            if (string.Equals(SelectedServiceProvider.ProviderType, "Gemini", StringComparison.OrdinalIgnoreCase))
            {
                var availableModels = await FetchAvailableModelsAsync(SelectedServiceProvider);
                System.Windows.MessageBox.Show($"连接检测成功！已发现 {availableModels.Count} 个模型。", "检测成功", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) })
            {
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
                var requestUrl = $"{baseUrl.TrimEnd('/')}/models";
                var response = await client.GetAsync(requestUrl);
                if (response.IsSuccessStatusCode)
                {
                    System.Windows.MessageBox.Show("连接检测成功！API 地址和密钥连通正常。", "检测成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    var err = await response.Content.ReadAsStringAsync();
                    System.Windows.MessageBox.Show($"检测失败。服务器返回状态码: {(int)response.StatusCode}\n{err}", "检测失败", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"请求异常: {ex.Message}", "检测失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            CheckApiKeyButtonText = "检测";
        }
    }

    private static string BuildDefaultProviderBaseUrl(string providerType)
    {
        return providerType switch
        {
            "Ego ChatGPT" => "http://127.0.0.1:8765/v1",
            YanziAgyModelBridge.ProviderType => YanziAgyModelBridge.BaseUrl,
            "Gemini" => "https://generativelanguage.googleapis.com/v1beta",
            "Anthropic" => "https://api.anthropic.com/v1",
            "Ollama" => "http://localhost:11434/v1",
            _ => "https://api.openai.com/v1"
        };
    }

    private static Task<IReadOnlyList<string>> FetchAvailableModelsAsync(SettingsAiProviderVM provider) =>
        SettingsAiController.FetchAvailableModelsAsync(new AiProviderSnapshot(provider.ProviderType, provider.BaseUrl, provider.ApiKey));

    private void ResetSystemPromptButton_Click(object sender, RoutedEventArgs e)
    {
        AiSystemPrompt = AppSettingsStore.DefaultAiSystemPrompt;
        AiSystemPromptTextBox.Text = AppSettingsStore.DefaultAiSystemPrompt;
        HasAiSettingsChanged = true;
        ShowToast("提示词已重置（需要点击保存生效）");
    }

    private void SearchProviderTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        OnPropertyChanged(nameof(FilteredProviders));
    }

    private void AiModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshingSettingsFromDisk) return;
        HasAiSettingsChanged = true;
        QueueAiSettingsSave();
    }


    private void AiSettings_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoadingSettings || _isRefreshingSettingsFromDisk)
        {
            return;
        }

        CheckAiSettingsChanged();
        if (HasAiSettingsChanged)
        {
            QueueAiSettingsSave(500);
        }
    }

    public string ExtensionDataConflictActionStatusText
    {
        get => _extensionDataConflictActionStatusText;
        private set
        {
            if (value == _extensionDataConflictActionStatusText) return;
            _extensionDataConflictActionStatusText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasExtensionDataConflictActionStatus));
        }
    }

    public bool HasExtensionDataConflictActionStatus =>
        !string.IsNullOrWhiteSpace(ExtensionDataConflictActionStatusText);

    private void EditSystemPromptInNewWindow_Click(object sender, RoutedEventArgs e)
    {
        var editor = new SystemPromptEditorWindow(AiSystemPrompt)
        {
            Owner = this
        };
        if (editor.ShowDialog() == true)
        {
            AiSystemPrompt = editor.PromptText;
            AiSystemPromptTextBox.Text = editor.PromptText;
        }
    }

}
