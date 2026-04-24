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
    public AddJsonExtensionWindow(string initialJson, bool isEditMode = false)
    {
        InitializeComponent();
        Title = isEditMode ? "编辑 JSON 扩展" : "添加 JSON 扩展";
        TitleText.Text = isEditMode ? "编辑单文件 JSON 扩展" : "添加单文件 JSON 扩展";
        SaveButton.Content = isEditMode ? "保存修改" : "保存扩展";
        JsonEditor.Text = initialJson;
        TryPopulateFormFromJson(initialJson);
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
            UpdateScriptModeUi();
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

    private void InlineScriptModeCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        UpdateScriptModeUi();
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
            JsonEditor.Text = JsonSerializer.Serialize(manifest, CreateJsonOptions());
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private string NormalizeJsonForSave()
    {
        if (!string.IsNullOrWhiteSpace(JsonEditor.Text))
        {
            try
            {
                var normalizedJson = ExtractJsonPayload(JsonEditor.Text);
                var manifest = JsonSerializer.Deserialize<LocalExtensionManifest>(normalizedJson, CreateJsonOptions());
                if (manifest != null)
                {
                    ApplyManifestToForm(manifest);
                    return JsonSerializer.Serialize(manifest, CreateJsonOptions());
                }
            }
            catch (Exception ex)
            {
                HostAssets.AppendDevLog($"NormalizeJsonForSave editor path failed: {ex.Message}");
            }
        }

        var formManifest = BuildManifestFromForm();
        return JsonSerializer.Serialize(formManifest, CreateJsonOptions());
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

            JsonEditor.Text = JsonSerializer.Serialize(manifest, CreateJsonOptions());
            ApplyManifestToForm(manifest);
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
            OpenTarget = InlineScriptModeCheckBox.IsChecked == true ? null : NullIfEmpty(OpenTargetBox.Text),
            GlobalShortcut = NullIfEmpty(GlobalShortcutBox.Text),
            HotkeyBehavior = NullIfEmpty(HotkeyBehaviorBox.Text),
            Runtime = InlineScriptModeCheckBox.IsChecked == true ? "powershell" : NullIfEmpty(RuntimeBox.Text),
            EntryMode = InlineScriptModeCheckBox.IsChecked == true ? "inline" : null,
            Entry = InlineScriptModeCheckBox.IsChecked == true ? null : NullIfEmpty(EntryBox.Text),
            Permissions = SplitCsv(PermissionsBox.Text),
            Script = InlineScriptModeCheckBox.IsChecked == true
                ? new LocalExtensionInlineScriptManifest
                {
                    Source = string.IsNullOrWhiteSpace(ScriptSourceBox.Text)
                        ? GetDefaultInlineScript()
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
        OpenTargetBox.Text = manifest.OpenTarget ?? string.Empty;
        GlobalShortcutBox.Text = manifest.GlobalShortcut ?? string.Empty;
        HotkeyBehaviorBox.Text = manifest.HotkeyBehavior ?? string.Empty;
        RuntimeBox.Text = manifest.Runtime ?? string.Empty;
        EntryBox.Text = manifest.Entry ?? string.Empty;
        PermissionsBox.Text = manifest.Permissions == null ? string.Empty : string.Join(", ", manifest.Permissions);
        InlineScriptModeCheckBox.IsChecked =
            string.Equals(manifest.EntryMode, "inline", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrWhiteSpace(manifest.Script?.Source);
        ScriptSourceBox.Text = manifest.Script?.Source ?? string.Empty;
        UpdateScriptModeUi();
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
            "4. 常用字段说明：\n" +
            "   - id：扩展唯一标识，建议英文小写加连字符\n" +
            "   - name：扩展名称\n" +
            "   - version：版本号，例如 0.1.0\n" +
            "   - category：分类\n" +
            "   - description：扩展说明\n" +
            "   - keywords：关键词数组\n" +
            "   - openTarget：打开的文件、目录、URL 或系统协议\n" +
            "   - globalShortcut：可选，全局快捷键\n" +
            "   - hotkeyBehavior：可选，例如 show-view\n" +
            "   - runtime / permissions：脚本扩展需要时填写\n" +
            "   - entryMode：单 JSON 内联脚本时使用 inline\n" +
            "   - script.source：单 JSON 内联 PowerShell 脚本源码\n" +
            "5. 轻量脚本扩展优先使用 entryMode = inline + script.source。\n" +
            "6. 复杂扩展再使用目录脚本入口 entry。\n" +
            "7. 如果你认为某些字段更合理，可以调整值，但请保持结构简单清晰。\n\n" +
            "当前草稿如下，请在这个基础上完善并输出最终 JSON：\n" +
            manifestJson;
    }

    private void UpdateScriptModeUi()
    {
        var isInline = InlineScriptModeCheckBox.IsChecked == true;
        ScriptSourceLabel.Visibility = isInline ? Visibility.Visible : Visibility.Collapsed;
        ScriptSourceBox.Visibility = isInline ? Visibility.Visible : Visibility.Collapsed;
        TestInputLabel.Visibility = isInline ? Visibility.Visible : Visibility.Collapsed;
        TestInputBox.Visibility = isInline ? Visibility.Visible : Visibility.Collapsed;
        RuntimeBox.IsEnabled = !isInline;
        EntryBox.IsEnabled = !isInline;
        OpenTargetBox.IsEnabled = !isInline;
        if (isInline)
        {
            RuntimeBox.Text = "powershell";
            EntryBox.Text = string.Empty;
            if (string.IsNullOrWhiteSpace(ScriptSourceBox.Text))
            {
                ScriptSourceBox.Text = GetDefaultInlineScript();
            }
        }
    }

    private static string GetDefaultInlineScript()
    {
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
            glyph: string.IsNullOrWhiteSpace(manifest.Runtime) && manifest.Script == null ? "J" : "S",
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
            inlineScriptSource: manifest.Script?.Source);
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
