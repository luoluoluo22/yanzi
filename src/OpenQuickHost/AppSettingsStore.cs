using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenQuickHost.Sync;

namespace OpenQuickHost;

public static class AppSettingsStore
{
    public const string DefaultAiSystemPrompt = "";

    public static string SettingsPath =>
        HostAssets.ResolveDataFilePath("appsettings.local.json");

    // 设置文件的所有读写串行化：调用方横跨 UI 线程、云同步后台与本地 HTTP API 线程，
    // 并发 WriteAllText 会互相抛 IOException 导致保存静默丢失。
    private static readonly object SettingsIoLock = new();
    private static long _writeVersion;
    internal static long WriteVersion => System.Threading.Interlocked.Read(ref _writeVersion);

    // 供低级钩子回调等高频路径使用的短 TTL 缓存；LL 钩子回调内直接读盘会超出
    // LowLevelHooksTimeout，Windows 会静默摘除钩子（表现为所有触发器集体失灵）。
    private const int SettingsCacheTtlMs = 1500;
    private static AppSettings? _cachedSettings;
    private static long _cachedSettingsTimestampTicks;

    /// <summary>
    /// 高频路径专用：短 TTL 缓存读取（≤1.5s 磁盘刷新一次）。
    /// 注意：可能返回共享实例，调用方只读，禁止修改返回对象。
    /// </summary>
    public static AppSettings LoadCached()
    {
        var cached = _cachedSettings;
        if (cached != null && Environment.TickCount64 - _cachedSettingsTimestampTicks < SettingsCacheTtlMs)
        {
            return cached;
        }

        lock (SettingsIoLock)
        {
            if (_cachedSettings != null && Environment.TickCount64 - _cachedSettingsTimestampTicks < SettingsCacheTtlMs)
            {
                return _cachedSettings;
            }

            var settings = Load();
            _cachedSettings = settings;
            _cachedSettingsTimestampTicks = Environment.TickCount64;
            return settings;
        }
    }

    private static void UpdateCache(AppSettings settings)
    {
        _cachedSettings = settings;
        _cachedSettingsTimestampTicks = Environment.TickCount64;
    }

    public static AppSettings Load()
    {
        using var processLock = SettingsProcessLock.Enter(HostAssets.DataRootPath);
        lock (SettingsIoLock)
        {
            if (!File.Exists(SettingsPath))
            {
                var defaults = Normalize(new AppSettings
                {
                    HasShownInitialWelcomePanel = false
                });
                TryImportPlaintextCredentials(defaults);
                ExtensionModelServiceRegistry.Reconcile(defaults);
                UpdateCache(defaults);
                return defaults;
            }

            try
            {
                var json = File.ReadAllText(SettingsPath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
                // Migration IO failures must never enter the corrupt-settings fallback.
                try
                {
                    if (AiAgentPrompt.IsLegacy(loaded.AiSystemPrompt))
                    {
                        AiAgentPrompt.BackupLegacy(loaded.AiSystemPrompt, HostAssets.ResolveDataDirectoryPath("PromptBackups"));
                        // Edit only this field before credential hydration; preserve all
                        // account fields (including unknown fields) exactly as JSON values.
                        var snapshot = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
                        snapshot["aiSystemPrompt"] = "";
                        SafeFile.AtomicWriteText(SettingsPath, snapshot.ToJsonString(JsonOptions));
                        System.Threading.Interlocked.Increment(ref _writeVersion);
                    }
                }
                catch (Exception ex)
                {
                    HostAssets.AppendLog($"[AppSettingsStore] Legacy prompt migration deferred: {ex.GetType().Name}");
                }
                var settings = Normalize(loaded);
                TryImportPlaintextCredentials(settings);
                ExtensionModelServiceRegistry.Reconcile(settings);
                UpdateCache(settings);
                return settings;
            }
            catch (Exception ex)
            {
                // 配置损坏（写一半崩溃/断电等）时保留坏文件供人工恢复，避免随后任何一次
                // Save 用默认值把它永久覆盖；随后返回默认配置让应用可用。
                HostAssets.AppendLog($"[AppSettingsStore] Load failed, settings reset to defaults: {ex.Message}");
                TryPreserveCorruptSettingsFile();
                var defaults = Normalize(new AppSettings());
                TryImportPlaintextCredentials(defaults);
                ExtensionModelServiceRegistry.Reconcile(defaults);
                UpdateCache(defaults);
                return defaults;
            }
        }
    }

    private static void TryImportPlaintextCredentials(AppSettings settings)
    {
        try
        {
            if (AiCredentialStore.ImportPlaintextAndHydrate(settings))
            {
                WriteSanitizedSettings(settings);
            }
        }
        catch (Exception ex)
        {
            // 凭据迁移/写凭据文件失败不应拖垮整个 Load（历史上会让 Load 返回默认配置）。
            HostAssets.AppendLog($"[AppSettingsStore] Credential hydration skipped: {ex.Message}");
        }
    }

    private static void TryPreserveCorruptSettingsFile()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return;
            }

            var backup = SettingsPath + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            File.Copy(SettingsPath, backup, overwrite: true);
            HostAssets.AppendLog($"[AppSettingsStore] Corrupt settings preserved: {backup}");
        }
        catch
        {
            // 保留失败也继续返回默认值，应用可用性优先。
        }
    }

    public static AppSettings Update(Func<AppSettings, AppSettings> edit)
    {
        using var processLock = SettingsProcessLock.Enter(HostAssets.DataRootPath);
        lock (SettingsIoLock)
        {
            var settings = Normalize(edit(Load()));
            Save(settings);
            return settings;
        }
    }

    public static void Save(AppSettings settings)
    {
        using var processLock = SettingsProcessLock.Enter(HostAssets.DataRootPath);
        lock (SettingsIoLock)
        {
            AiAgentPrompt.BackupLegacy(settings.AiSystemPrompt, HostAssets.ResolveDataDirectoryPath("PromptBackups"));
            settings = Normalize(settings);
            ExtensionModelServiceRegistry.Reconcile(settings);
            try
            {
                AiCredentialStore.Capture(settings);
            }
            catch (Exception ex)
            {
                HostAssets.AppendLog($"[AppSettingsStore] Credential capture failed: {ex.Message}");
            }

            WriteSanitizedSettings(settings);
            HostAssets.AppendLog($"[AppSettingsStore] Saved settings: EnableEverything={settings.EnableEverything}, UpdatedAt={settings.LauncherConfigUpdatedAtUtc}");
        }
    }

    private static void WriteSanitizedSettings(AppSettings settings)
    {
        // Redact only the persisted snapshot. Mutating the caller previously wiped
        // hydrated API keys in memory; an unrelated save could then clear the vault.
        var snapshot = JsonSerializer.Deserialize<AppSettings>(
            JsonSerializer.Serialize(settings, JsonOptions), JsonOptions) ?? new AppSettings();
        AiCredentialStore.RemovePlaintext(snapshot);
        SafeFile.AtomicWriteText(SettingsPath, JsonSerializer.Serialize(snapshot, JsonOptions));
        System.Threading.Interlocked.Increment(ref _writeVersion);
        UpdateCache(settings);
    }

    private static readonly JsonSerializerOptions JsonOptions = JsonDefaults.CamelCaseIndented;

    internal static int NormalizeLongPressMilliseconds(int milliseconds) => AppSettingsMigration.NormalizeLongPressMilliseconds(milliseconds);
    private static AppSettings Normalize(AppSettings settings) => HostRuntimeProfile.ApplySettings(AppSettingsMigration.Normalize(settings));
}
