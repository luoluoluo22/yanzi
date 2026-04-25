using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using System.Threading;
using System.Windows;
using OpenQuickHost.Sync;
using Forms = System.Windows.Forms;

namespace OpenQuickHost;

public partial class AddJsonExtensionWindow : Window
{
    private enum EditorSource
    {
        Form,
        Json
    }

    private readonly IReadOnlyList<ExtensionIconOption> _builtInIcons = ExtensionIconLibrary.GetBuiltInOptions();
    private bool _suppressEditorTracking;
    private EditorSource _lastEditedSource = EditorSource.Json;

    public AddJsonExtensionWindow(string initialJson, bool isEditMode = false)
    {
        InitializeComponent();
        Title = isEditMode ? "编辑 JSON 扩展" : "添加 JSON 扩展";
        TitleText.Text = isEditMode ? "编辑单文件 JSON 扩展" : "添加单文件 JSON 扩展";
        SaveButton.Content = isEditMode ? "保存修改" : "保存扩展";
        BuiltInIconsList.ItemsSource = _builtInIcons;
        RegisterEditorTracking();
        JsonEditor.Text = initialJson;
        TryPopulateFormFromJson(initialJson);
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
            UpdateScriptModeUi();
            RefreshIconPreview();
        };
    }

    public string JsonContent => JsonEditor.Text;

    public void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Foreground = System.Windows.Media.Brushes.IndianRed;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ErrorText.Visibility = Visibility.Collapsed;
            var normalizedJson = NormalizeJsonForSave();
            if (string.IsNullOrWhiteSpace(normalizedJson))
            {
                ShowError("JSON 内容不能为空。");
                return;
            }

            JsonEditor.Text = normalizedJson;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            HostAssets.AppendDevLog($"SaveButton_Click failed: {ex}");
            ShowError(ex.Message);
        }
    }

    private void ParseJsonButton_Click(object sender, RoutedEventArgs e)
    {
        TryPopulateFormFromJson(JsonEditor.Text);
    }

    private void GenerateJsonButton_Click(object sender, RoutedEventArgs e)
    {
        GenerateJsonFromForm();
    }

    private void JsonEditor_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_suppressEditorTracking)
        {
            return;
        }

        _lastEditedSource = EditorSource.Json;
    }

    private void IconBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        RefreshIconPreview();
    }

    private void IconPreviewContext_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        RefreshIconPreview();
    }

    private void BuiltInIconButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string iconReference })
        {
            return;
        }

        IconBox.Text = iconReference;
        RefreshIconPreview();
    }

    private void PickIconImageButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择扩展图标",
            Filter = "图片文件|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif;*.ico|所有文件|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        IconBox.Text = dialog.FileName;
        RefreshIconPreview();
    }

    private void ClearIconButton_Click(object sender, RoutedEventArgs e)
    {
        IconBox.Text = string.Empty;
        RefreshIconPreview();
    }

    private void InlineScriptModeCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        MarkFormEdited();
        UpdateScriptModeUi();
    }

    private void InlineRuntimeBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        MarkFormEdited();
        if (InlineScriptModeCheckBox.IsChecked == true)
        {
            RuntimeBox.Text = GetSelectedInlineRuntime();
            ScriptSourceBox.Text = GetDefaultInlineScript(GetSelectedInlineRuntime());
            RefreshIconPreview();
        }
    }

    private async void TestRunButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ErrorText.Visibility = Visibility.Collapsed;
            HostAssets.AppendDevLog("TestRunButton_Click started.");
            var normalizedJson = NormalizeJsonForSave();
            JsonEditor.Text = normalizedJson;
            var manifest = JsonSerializer.Deserialize<LocalExtensionManifest>(normalizedJson, CreateJsonOptions())
                ?? throw new InvalidOperationException("测试前解析扩展失败。");
            var testCommand = BuildTestCommand(manifest);

            if (ScriptExtensionRunner.CanExecute(testCommand))
            {
                var result = await ScriptExtensionRunner.ExecuteAsync(testCommand, TestInputBox.Text, "extension-editor-test");
                HostAssets.AppendDevLog($"TestRunButton_Click script completed. Success={result.Success} ExitCode={result.ExitCode}");
                ShowExecutionLogWindow(
                    manifest.Name,
                    result.Success,
                    result.Output,
                    result.Error,
                    result.ExitCode,
                    "来源：扩展编辑器测试执行");
                return;
            }

            if (!string.IsNullOrWhiteSpace(manifest.OpenTarget))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = manifest.OpenTarget,
                    UseShellExecute = true
                });
                HostAssets.AppendDevLog($"TestRunButton_Click launched openTarget: {manifest.OpenTarget}");
                ShowExecutionLogWindow(
                    manifest.Name,
                    true,
                    $"已触发目标：{manifest.OpenTarget}",
                    string.Empty,
                    0,
                    "来源：扩展编辑器测试执行");
                return;
            }

            throw new InvalidOperationException("当前草稿没有可测试的执行入口。脚本扩展请填写 runtime 和脚本内容；普通扩展请填写 openTarget。");
        }
        catch (Exception ex)
        {
            HostAssets.AppendDevLog($"TestRunButton_Click failed: {ex}");
            ShowError($"测试执行失败：{ex.Message}");
        }
    }

    private void OpenLogButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            HostAssets.EnsureCreated();
            var target = File.Exists(HostAssets.DevDebugLogPath)
                ? HostAssets.DevDebugLogPath
                : HostAssets.HostLogPath;
            Process.Start(new ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            HostAssets.AppendDevLog($"OpenLogButton_Click failed: {ex}");
            ShowError($"打开日志失败：{ex.Message}");
        }
    }

    private void CopyPromptButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ErrorText.Visibility = Visibility.Collapsed;
            var manifest = BuildManifestFromForm();
            var manifestJson = JsonSerializer.Serialize(manifest, CreateJsonOptions());
            var prompt = BuildAiPrompt(manifestJson);
            HostAssets.AppendDevLog($"CopyPromptButton_Click started. Length={prompt.Length}");
            CopyTextToClipboard(prompt);
            ErrorText.Text = "已复制扩展编写提示词，可直接发给 AI。";
            ErrorText.Foreground = System.Windows.Media.Brushes.LightGreen;
            ErrorText.Visibility = Visibility.Visible;
            HostAssets.AppendDevLog("CopyPromptButton_Click completed successfully.");
        }
        catch (Exception ex)
        {
            HostAssets.AppendDevLog($"CopyPromptButton_Click failed: {ex}");
            ShowError(ex.Message);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void GenerateJsonFromForm()
    {
        try
        {
            ErrorText.Visibility = Visibility.Collapsed;
            var manifest = BuildManifestFromForm();
            SetJsonEditorText(JsonSerializer.Serialize(manifest, CreateJsonOptions()));
            _lastEditedSource = EditorSource.Form;
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private string NormalizeJsonForSave()
    {
        if (_lastEditedSource == EditorSource.Form)
        {
            var formManifest = BuildManifestFromForm();
            return JsonSerializer.Serialize(formManifest, CreateJsonOptions());
        }

        if (string.IsNullOrWhiteSpace(JsonEditor.Text))
        {
            throw new InvalidOperationException("JSON 内容不能为空。");
        }

        var normalizedJson = ExtractJsonPayload(JsonEditor.Text);
        var manifest = JsonSerializer.Deserialize<LocalExtensionManifest>(normalizedJson, CreateJsonOptions())
            ?? throw new InvalidOperationException("JSON 解析失败。");
        ApplyManifestToForm(manifest);
        return JsonSerializer.Serialize(manifest, CreateJsonOptions());
    }

    private void TryPopulateFormFromJson(string json)
    {
        try
        {
            HostAssets.AppendDevLog($"TryPopulateFormFromJson entered. RawLength={json?.Length ?? 0}");
            var normalizedJson = ExtractJsonPayload(json);
            HostAssets.AppendDevLog($"TryPopulateFormFromJson normalized. Length={normalizedJson.Length}");
            var manifest = JsonSerializer.Deserialize<LocalExtensionManifest>(normalizedJson, CreateJsonOptions());
            if (manifest == null)
            {
                HostAssets.AppendDevLog("TryPopulateFormFromJson deserialized manifest was null.");
                return;
            }

            SetJsonEditorText(JsonSerializer.Serialize(manifest, CreateJsonOptions()));
            ApplyManifestToForm(manifest);
            _lastEditedSource = EditorSource.Json;
            ErrorText.Visibility = Visibility.Collapsed;
            HostAssets.AppendDevLog($"TryPopulateFormFromJson succeeded. Id={manifest.Id}");
        }
        catch (Exception ex)
        {
            HostAssets.AppendDevLog($"TryPopulateFormFromJson failed: {ex}");
            ShowError($"解析 JSON 失败：{ex.Message}");
        }
    }

    private LocalExtensionManifest BuildManifestFromForm()
    {
        if (string.IsNullOrWhiteSpace(IdBox.Text))
        {
            throw new InvalidOperationException("扩展 ID 不能为空。");
        }

        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            throw new InvalidOperationException("扩展名称不能为空。");
        }

        return new LocalExtensionManifest
        {
            Id = IdBox.Text.Trim(),
            Name = NameBox.Text.Trim(),
            Version = string.IsNullOrWhiteSpace(VersionBox.Text) ? "0.1.0" : VersionBox.Text.Trim(),
            Category = NullIfEmpty(CategoryBox.Text),
            Description = NullIfEmpty(DescriptionBox.Text),
            Keywords = SplitCsv(KeywordsBox.Text),
            Icon = NullIfEmpty(IconBox.Text),
            OpenTarget = InlineScriptModeCheckBox.IsChecked == true ? null : NullIfEmpty(OpenTargetBox.Text),
            GlobalShortcut = NullIfEmpty(GlobalShortcutBox.Text),
            HotkeyBehavior = NullIfEmpty(HotkeyBehaviorBox.Text),
            Runtime = InlineScriptModeCheckBox.IsChecked == true ? GetSelectedInlineRuntime() : NullIfEmpty(RuntimeBox.Text),
            EntryMode = InlineScriptModeCheckBox.IsChecked == true ? "inline" : null,
            Entry = InlineScriptModeCheckBox.IsChecked == true ? null : NullIfEmpty(EntryBox.Text),
            Permissions = SplitCsv(PermissionsBox.Text),
            Script = InlineScriptModeCheckBox.IsChecked == true
                ? new LocalExtensionInlineScriptManifest
                {
                    Source = string.IsNullOrWhiteSpace(ScriptSourceBox.Text)
                        ? GetDefaultInlineScript(GetSelectedInlineRuntime())
                        : ScriptSourceBox.Text.ReplaceLineEndings("\r\n")
                }
                : null
        };
    }

    private void ApplyManifestToForm(LocalExtensionManifest manifest)
    {
        IdBox.Text = manifest.Id;
        NameBox.Text = manifest.Name;
        VersionBox.Text = manifest.Version;
        CategoryBox.Text = manifest.Category ?? string.Empty;
        DescriptionBox.Text = manifest.Description ?? string.Empty;
        KeywordsBox.Text = manifest.Keywords == null ? string.Empty : string.Join(", ", manifest.Keywords);
        IconBox.Text = manifest.Icon ?? string.Empty;
        OpenTargetBox.Text = manifest.OpenTarget ?? string.Empty;
        GlobalShortcutBox.Text = manifest.GlobalShortcut ?? string.Empty;
        HotkeyBehaviorBox.Text = manifest.HotkeyBehavior ?? string.Empty;
        RuntimeBox.Text = manifest.Runtime ?? string.Empty;
        EntryBox.Text = manifest.Entry ?? string.Empty;
        PermissionsBox.Text = manifest.Permissions == null ? string.Empty : string.Join(", ", manifest.Permissions);
        InlineScriptModeCheckBox.IsChecked =
            string.Equals(manifest.EntryMode, "inline", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrWhiteSpace(manifest.Script?.Source);
        SetSelectedInlineRuntime(manifest.Runtime);
        ScriptSourceBox.Text = manifest.Script?.Source ?? string.Empty;
        UpdateScriptModeUi();
        RefreshIconPreview();
    }

    private void RegisterEditorTracking()
    {
        var manifestBoxes = new System.Windows.Controls.TextBox[]
        {
            IdBox,
            NameBox,
            VersionBox,
            CategoryBox,
            DescriptionBox,
            KeywordsBox,
            IconBox,
            OpenTargetBox,
            GlobalShortcutBox,
            HotkeyBehaviorBox,
            RuntimeBox,
            EntryBox,
            PermissionsBox,
            ScriptSourceBox
        };

        foreach (var box in manifestBoxes)
        {
            box.TextChanged += FormField_TextChanged;
        }

        JsonEditor.TextChanged += JsonEditor_TextChanged;
        InlineScriptModeCheckBox.Checked += FormField_CheckChanged;
        InlineScriptModeCheckBox.Unchecked += FormField_CheckChanged;
    }

    private void FormField_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        MarkFormEdited();
    }

    private void FormField_CheckChanged(object sender, RoutedEventArgs e)
    {
        MarkFormEdited();
    }

    private void MarkFormEdited()
    {
        if (_suppressEditorTracking)
        {
            return;
        }

        _lastEditedSource = EditorSource.Form;
    }

    private void SetJsonEditorText(string json)
    {
        _suppressEditorTracking = true;
        try
        {
            JsonEditor.Text = json;
        }
        finally
        {
            _suppressEditorTracking = false;
        }
    }

    private static string? NullIfEmpty(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string[]? SplitCsv(string? value)
    {
        var items = (value ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .ToArray();
        return items.Length == 0 ? null : items;
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
    }

    private static string ExtractJsonPayload(string? rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            throw new InvalidOperationException("没有检测到可解析的 JSON 内容。");
        }

        var trimmed = rawText.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var lines = trimmed.Split(["\r\n", "\n"], StringSplitOptions.None).ToList();
            if (lines.Count >= 3)
            {
                if (lines[0].StartsWith("```", StringComparison.Ordinal))
                {
                    lines.RemoveAt(0);
                }

                var fenceIndex = lines.FindLastIndex(static line => line.TrimStart().StartsWith("```", StringComparison.Ordinal));
                if (fenceIndex >= 0)
                {
                    lines.RemoveRange(fenceIndex, lines.Count - fenceIndex);
                }

                trimmed = string.Join(Environment.NewLine, lines).Trim();
            }
        }

        if (TrySliceJsonObject(trimmed, out var directJson))
        {
            return directJson;
        }

        throw new InvalidOperationException("没有在当前内容中找到合法的 JSON 对象，请确认 AI 返回的是 JSON。");
    }

    private static bool TrySliceJsonObject(string text, out string json)
    {
        json = string.Empty;
        var start = text.IndexOf('{');
        if (start < 0)
        {
            return false;
        }

        var depth = 0;
        var inString = false;
        var escaping = false;
        for (var index = start; index < text.Length; index++)
        {
            var ch = text[index];
            if (escaping)
            {
                escaping = false;
                continue;
            }

            if (ch == '\\' && inString)
            {
                escaping = true;
                continue;
            }

            if (ch == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString)
            {
                continue;
            }

            if (ch == '{')
            {
                depth++;
            }
            else if (ch == '}')
            {
                depth--;
                if (depth == 0)
                {
                    json = text[start..(index + 1)];
                    return true;
                }
            }
        }

        return false;
    }

    private static void CopyTextToClipboard(string text)
    {
        HostAssets.AppendDevLog("CopyTextToClipboard entered.");
        Exception? lastError = null;
        if (TryCopyViaStaClipboard(text, out var staError))
        {
            HostAssets.AppendDevLog("CopyTextToClipboard succeeded via STA clipboard.");
            return;
        }

        lastError = staError == null ? null : new InvalidOperationException(staError);
        HostAssets.AppendDevLog($"CopyTextToClipboard STA clipboard failed: {staError}");

        if (TryCopyViaClipExe(text, out var fallbackError))
        {
            HostAssets.AppendDevLog("CopyTextToClipboard succeeded via clip.exe fallback.");
            return;
        }

        HostAssets.AppendDevLog($"CopyTextToClipboard clip.exe fallback failed: {fallbackError}");
        throw new InvalidOperationException(
            $"复制到剪贴板失败：{fallbackError ?? lastError?.Message}",
            fallbackError == null ? lastError : new InvalidOperationException(fallbackError, lastError));
    }

    private static bool TryCopyViaStaClipboard(string text, out string? error)
    {
        error = null;
        Exception? threadError = null;
        var done = new ManualResetEventSlim(false);

        var thread = new Thread(() =>
        {
            try
            {
                for (var attempt = 0; attempt < 10; attempt++)
                {
                    try
                    {
                        Forms.Clipboard.SetText(text, Forms.TextDataFormat.UnicodeText);
                        threadError = null;
                        return;
                    }
                    catch (Exception ex)
                    {
                        threadError = ex;
                        Thread.Sleep(100 * (attempt + 1));
                    }
                }
            }
            finally
            {
                done.Set();
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        done.Wait(TimeSpan.FromSeconds(8));

        if (!done.IsSet)
        {
            error = "STA 剪贴板线程超时。";
            return false;
        }

        if (threadError == null)
        {
            return true;
        }

        error = threadError.Message;
        return false;
    }

    private static bool TryCopyViaClipExe(string text, out string? error)
    {
        error = null;
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "clip.exe",
                UseShellExecute = false,
                RedirectStandardInput = true,
                StandardInputEncoding = Encoding.Unicode,
                CreateNoWindow = true
            };

            process.Start();
            process.StandardInput.Write(text);
            process.StandardInput.Close();
            process.WaitForExit(5000);

            if (process.ExitCode == 0)
            {
                return true;
            }

            error = $"clip.exe 返回了退出码 {process.ExitCode}";
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string BuildAiPrompt(string manifestJson)
    {
        return
            "请帮我为燕子（Yanzi）编写一个单文件 JSON 扩展。\n\n" +
            "要求：\n" +
            "1. 输出必须是合法 JSON，不要附带 Markdown 代码块。\n" +
            "2. 只保留需要的字段，不要保留 null，也不要随意补充未知字段。\n" +
            "3. 这个扩展会保存为 manifest.json，供燕子直接加载。\n" +
            "4. 按需求选择扩展类型：\n" +
            "   - 只打开文件、目录、URL 或系统协议：使用 openTarget，不要写脚本。\n" +
            "   - 搜索或带参数打开 URL：使用 queryPrefixes + queryTargetTemplate，模板里用 {query}。\n" +
            "   - 处理选中文本、剪贴板、文件路径、调用 API：优先使用 runtime = csharp + entryMode = inline。\n" +
            "   - 需要宿主界面：使用 hostedView，actionType 优先使用 script。\n" +
            "   - Windows 系统自动化：可以使用 runtime = powershell。\n" +
            "5. 常用字段说明：\n" +
            "   - id：扩展唯一标识，建议英文小写加连字符\n" +
            "   - name：扩展名称\n" +
            "   - version：版本号，例如 0.1.0\n" +
            "   - category：分类\n" +
            "   - description：扩展说明\n" +
            "   - keywords：关键词数组\n" +
            "   - icon：可选，支持内置图标如 mdi:search / app:wechat，也支持扩展目录下的相对图片路径如 icons/logo.png\n" +
            "   - openTarget：打开的文件、目录、URL 或系统协议\n" +
            "   - queryPrefixes / queryTargetTemplate：参数化命令，queryTargetTemplate 中使用 {query}\n" +
            "   - globalShortcut：可选，全局快捷键\n" +
            "   - hotkeyBehavior：可选，例如 show-view\n" +
            "   - runtime / permissions：脚本扩展需要时填写，主力运行时建议 csharp，也支持 powershell\n" +
            "   - entryMode：单 JSON 内联动作时使用 inline\n" +
            "   - script.source：单 JSON 内联 C# 或 PowerShell 源码\n" +
            "   - hostedView：宿主界面配置，type 当前使用 split-workbench\n" +
            "6. C# 动作源码必须包含 public static class YanziAction，并实现 public static Task<string> RunAsync(YanziActionContext context)。\n" +
            "7. C# 源码可使用 context.InputText、context.ExtensionDirectory、context.LaunchSource、context.Now、context.Permissions。\n" +
            "8. 成功结果返回字符串；错误可以 throw。不要生成需要额外 NuGet 包的代码。\n" +
            "9. 复杂扩展再使用目录脚本入口 entry。\n" +
            "10. 如果你认为某些字段更合理，可以调整值，但请保持结构简单清晰。\n\n" +
            "C# 内联动作模板：\n" +
            "using OpenQuickHost.CSharpRuntime;\\n\\npublic static class YanziAction\\n{\\n    public static Task<string> RunAsync(YanziActionContext context)\\n    {\\n        return Task.FromResult(context.InputText);\\n    }\\n}\n\n" +
            "当前草稿如下，请在这个基础上完善并输出最终 JSON：\n" +
            manifestJson;
    }

    private void UpdateScriptModeUi()
    {
        var isInline = InlineScriptModeCheckBox.IsChecked == true;
        InlineRuntimeLabel.Visibility = isInline ? Visibility.Visible : Visibility.Collapsed;
        InlineRuntimeBox.Visibility = isInline ? Visibility.Visible : Visibility.Collapsed;
        ScriptSourceLabel.Visibility = isInline ? Visibility.Visible : Visibility.Collapsed;
        ScriptSourceBox.Visibility = isInline ? Visibility.Visible : Visibility.Collapsed;
        TestInputLabel.Visibility = isInline ? Visibility.Visible : Visibility.Collapsed;
        TestInputBox.Visibility = isInline ? Visibility.Visible : Visibility.Collapsed;
        RuntimeBox.IsEnabled = !isInline;
        EntryBox.IsEnabled = !isInline;
        OpenTargetBox.IsEnabled = !isInline;
        if (isInline)
        {
            RuntimeBox.Text = GetSelectedInlineRuntime();
            EntryBox.Text = string.Empty;
            if (string.IsNullOrWhiteSpace(ScriptSourceBox.Text))
            {
                ScriptSourceBox.Text = GetDefaultInlineScript(GetSelectedInlineRuntime());
            }
        }
    }

    private void RefreshIconPreview()
    {
        IconPreviewImage.Visibility = Visibility.Collapsed;
        IconPreviewImage.Source = null;
        IconPreviewVectorHost.Visibility = Visibility.Collapsed;
        IconPreviewVector.Data = null;
        IconPreviewGlyph.Visibility = Visibility.Collapsed;

        var iconReference = NullIfEmpty(IconBox.Text);
        var previewDirectory = string.IsNullOrWhiteSpace(IdBox.Text)
            ? HostAssets.ExtensionsPath
            : Path.Combine(HostAssets.ExtensionsPath, IdBox.Text.Trim());

        var imageSource = ExtensionIconLibrary.ResolveImageSource(iconReference, previewDirectory);
        if (imageSource != null)
        {
            IconPreviewImage.Source = imageSource;
            IconPreviewImage.Visibility = Visibility.Visible;
            IconPreviewHintText.Text = "当前使用图片图标。保存后会把这个值写入 manifest.json。";
            HighlightSelectedBuiltInButton(null);
            return;
        }

        var vectorIcon = ExtensionIconLibrary.ResolveVectorIcon(iconReference);
        if (vectorIcon != null)
        {
            IconPreviewVector.Data = vectorIcon;
            IconPreviewVectorHost.Visibility = Visibility.Visible;
            IconPreviewHintText.Text = $"当前使用内置图标：{iconReference}";
            HighlightSelectedBuiltInButton(iconReference);
            return;
        }

        IconPreviewGlyph.Text = InferFallbackGlyph();
        IconPreviewGlyph.Visibility = Visibility.Visible;
        IconPreviewHintText.Text = string.IsNullOrWhiteSpace(iconReference)
            ? "未设置图标时会回退为字母标识。"
            : $"当前 icon 值未解析成功：{iconReference}";
        HighlightSelectedBuiltInButton(null);
    }

    private void HighlightSelectedBuiltInButton(string? selectedReference)
    {
        foreach (var button in FindVisualChildren<System.Windows.Controls.Button>(BuiltInIconsList))
        {
            var isSelected = !string.IsNullOrWhiteSpace(selectedReference) &&
                             string.Equals(button.Tag as string, selectedReference, StringComparison.OrdinalIgnoreCase);
            button.BorderBrush = isSelected
                ? new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF3B82F6"))
                : new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF2A2A2A"));
            button.Background = isSelected
                ? new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF1E293B"))
                : new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF1E1E1E"));
        }
    }

    private string InferFallbackGlyph()
    {
        if (!string.IsNullOrWhiteSpace(NameBox.Text))
        {
            return NameBox.Text.Trim()[0].ToString().ToUpperInvariant();
        }

        if (InlineScriptModeCheckBox.IsChecked == true)
        {
            return string.Equals(GetSelectedInlineRuntime(), "csharp", StringComparison.OrdinalIgnoreCase) ? "C" : "S";
        }

        return "E";
    }

    private string GetSelectedInlineRuntime()
    {
        if (InlineRuntimeBox.SelectedItem is System.Windows.Controls.ComboBoxItem item &&
            item.Tag is string runtime &&
            !string.IsNullOrWhiteSpace(runtime))
        {
            return runtime;
        }

        return "csharp";
    }

    private void SetSelectedInlineRuntime(string? runtime)
    {
        var normalized = string.Equals(runtime, "powershell", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(runtime, "ps1", StringComparison.OrdinalIgnoreCase)
            ? "powershell"
            : "csharp";

        foreach (var item in InlineRuntimeBox.Items.OfType<System.Windows.Controls.ComboBoxItem>())
        {
            item.IsSelected = string.Equals(item.Tag as string, normalized, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(System.Windows.DependencyObject? parent) where T : System.Windows.DependencyObject
    {
        if (parent == null)
        {
            yield break;
        }

        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (var index = 0; index < count; index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, index);
            if (child is T typed)
            {
                yield return typed;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static string GetDefaultInlineScript(string runtime)
    {
        if (string.Equals(runtime, "csharp", StringComparison.OrdinalIgnoreCase))
        {
            return
"""
using OpenQuickHost.CSharpRuntime;

public static class YanziAction
{
    public static Task<string> RunAsync(YanziActionContext context)
    {
        var input = string.IsNullOrWhiteSpace(context.InputText)
            ? "你好，燕子。"
            : context.InputText.Trim();

        return Task.FromResult($"收到输入: {input}");
    }
}
""";
        }

        return
"""
param(
    [string]$InputText = "",
    [string]$ContextPath = ""
)

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

if ([string]::IsNullOrWhiteSpace($InputText)) {
    Write-Output "你好，燕子。"
} else {
    Write-Output ("收到输入: " + $InputText)
}
""";
    }

    private static CommandItem BuildTestCommand(LocalExtensionManifest manifest)
    {
        var extensionDirectory = Path.Combine(HostAssets.ExtensionsPath, manifest.Id);
        return new CommandItem(
            glyph: GetDefaultGlyph(manifest),
            title: manifest.Name,
            subtitle: manifest.Description ?? "来自扩展编辑器测试执行",
            category: manifest.Category ?? "扩展",
            accentHex: "#FF38BDF8",
            openTarget: manifest.OpenTarget,
            keywords: manifest.Keywords ?? [],
            source: CommandSource.LocalExtension,
            extensionId: manifest.Id,
            declaredVersion: manifest.Version ?? "0.1.0",
            extensionDirectoryPath: extensionDirectory,
            hostedView: manifest.HostedView?.ToDefinition(),
            globalShortcut: manifest.GlobalShortcut,
            hotkeyBehavior: manifest.HotkeyBehavior,
            runtime: manifest.Runtime,
            entryPoint: manifest.Entry,
            permissions: manifest.Permissions ?? [],
            entryMode: manifest.EntryMode,
            inlineScriptSource: manifest.Script?.Source,
            iconReference: manifest.Icon);
    }

    private static string GetDefaultGlyph(LocalExtensionManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.Runtime) && manifest.Script == null)
        {
            return "J";
        }

        return string.Equals(manifest.Runtime, "csharp", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(manifest.Runtime, "cs", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(manifest.Runtime, "c#", StringComparison.OrdinalIgnoreCase)
            ? "C"
            : "S";
    }

    private void ShowExecutionLogWindow(string title, bool success, string? output, string? error, int? exitCode, string? extraMeta)
    {
        var window = new ExecutionLogWindow(
            title,
            success,
            output ?? string.Empty,
            error ?? string.Empty,
            exitCode,
            extraMeta)
        {
            Owner = this
        };
        window.ShowDialog();
    }
}
