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

        // 3. 将当前选中的提供商设为 Active 默认提供商并同步参数
        if (SelectedServiceProvider != null)
        {
            _settings.ActiveServiceProviderId = SelectedServiceProvider.Id;
            _settings.AiBaseUrl = SelectedServiceProvider.BaseUrl;
            _settings.AiApiKey = SelectedServiceProvider.ApiKey;
            _settings.AiModel = SelectedServiceProvider.SelectedModel;
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
        _aiStatusHideTimer = ShowSaveStatusTemporarily(_aiStatusHideTimer, visible => IsAiSaveStatusVisible = visible);
    }

    private void AddProviderButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddProviderWindow();
        dialog.Owner = this;
        if (dialog.ShowDialog() == true)
        {
            var newProvider = new AiServiceProviderSettings
            {
                Id = Guid.NewGuid().ToString(),
                Name = dialog.ProviderName,
                ProviderType = dialog.ProviderType,
                BaseUrl = BuildDefaultProviderBaseUrl(dialog.ProviderType),
                ApiKey = "",
                IsEnabled = true,
                Models = [],
                SelectedModel = string.Empty
            };

            var vm = new SettingsAiProviderVM(newProvider);
            _aiServiceProvidersList.Add(vm);
            SelectedServiceProvider = vm;
            HasAiSettingsChanged = true;
            OnPropertyChanged(nameof(FilteredProviders));
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
        HasAiSettingsChanged = true;
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
