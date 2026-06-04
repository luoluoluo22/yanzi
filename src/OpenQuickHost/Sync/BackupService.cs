using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;

namespace OpenQuickHost.Sync;

public static class BackupService
{
    public const string AutoBackupNamePrefix = "auto_backup_";
    public const string ManualBackupNamePrefix = "manual_backup_";

    public static string GetDefaultBackupsDirectory()
    {
        return Path.Combine(HostAssets.DataRootPath, "Backups");
    }

    /// <summary>
    /// 获取当前生效的备份保存目录（若用户配置了有效自定义目录则使用，否则返回默认）
    /// </summary>
    public static string GetActiveBackupsDirectory()
    {
        var settings = AppSettingsStore.Load();
        if (!string.IsNullOrWhiteSpace(settings.CustomBackupDirectory))
        {
            try
            {
                var dir = settings.CustomBackupDirectory.Trim();
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                return dir;
            }
            catch (Exception ex)
            {
                HostAssets.AppendLog($"BackupService: Failed to resolve custom backups directory, fallback to default. Error: {ex.Message}");
            }
        }
        
        var defaultDir = GetDefaultBackupsDirectory();
        Directory.CreateDirectory(defaultDir);
        return defaultDir;
    }

    /// <summary>
    /// 压缩打包完整数据，自动过滤无关和备份子目录
    /// </summary>
    public static void CreateBackup(string targetZipPath)
    {
        var sourcePath = HostAssets.DataRootPath;
        var tempZipPath = targetZipPath + ".tmp";

        // 确定自定义备份目录的相对路径，以防循环嵌套压缩
        string? customBackupDirName = null;
        var settings = AppSettingsStore.Load();
        if (!string.IsNullOrWhiteSpace(settings.CustomBackupDirectory))
        {
            try
            {
                var fullCustomPath = Path.GetFullPath(settings.CustomBackupDirectory.Trim());
                var fullRootPath = Path.GetFullPath(sourcePath);
                if (fullCustomPath.StartsWith(fullRootPath, StringComparison.OrdinalIgnoreCase))
                {
                    customBackupDirName = Path.GetRelativePath(fullRootPath, fullCustomPath);
                }
            }
            catch
            {
                // ignore path resolve errors
            }
        }

        try
        {
            if (File.Exists(tempZipPath))
            {
                File.Delete(tempZipPath);
            }

            var parentDir = Path.GetDirectoryName(targetZipPath);
            if (!string.IsNullOrWhiteSpace(parentDir))
            {
                Directory.CreateDirectory(parentDir);
            }

            using (var zipStream = new FileStream(tempZipPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
            {
                var files = Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    var relativePath = Path.GetRelativePath(sourcePath, file);

                    // 1. 过滤默认备份存放目录
                    if (relativePath.StartsWith("Backups" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                        relativePath.Equals("Backups", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // 2. 过滤用户配置的自定义备份存放目录（如果在数据根目录下）
                    if (!string.IsNullOrWhiteSpace(customBackupDirName))
                    {
                        if (relativePath.StartsWith(customBackupDirName + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                            relativePath.Equals(customBackupDirName, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                    }

                    // 3. 过滤 logs 目录
                    if (relativePath.StartsWith("logs" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                        relativePath.Equals("logs", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // 4. 过滤 WebView2 文件夹、Everything 运行时目录、回收站、图标缓存等无用/临时缓存文件
                    bool shouldExclude = false;
                    var pathParts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    foreach (var part in pathParts)
                    {
                        if (part.EndsWith("WebView2", StringComparison.OrdinalIgnoreCase) ||
                            part.Equals("icon-cache", StringComparison.OrdinalIgnoreCase) ||
                            part.Equals("ExtensionRecycleBin", StringComparison.OrdinalIgnoreCase) ||
                            part.Equals("EverythingRuntime", StringComparison.OrdinalIgnoreCase))
                        {
                            shouldExclude = true;
                            break;
                        }
                    }

                    if (shouldExclude)
                    {
                        continue;
                    }

                    // 5. 过滤当前的备份输出包本身和临时文件
                    if (file.Equals(targetZipPath, StringComparison.OrdinalIgnoreCase) ||
                        file.Equals(tempZipPath, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    archive.CreateEntryFromFile(file, relativePath);
                }
            }

            if (File.Exists(targetZipPath))
            {
                File.Delete(targetZipPath);
            }
            File.Move(tempZipPath, targetZipPath);
            HostAssets.AppendLog($"BackupService: successfully created backup at: {targetZipPath}");
        }
        catch (Exception ex)
        {
            if (File.Exists(tempZipPath))
            {
                try { File.Delete(tempZipPath); } catch { /* ignore */ }
            }
            throw new Exception($"打包备份数据时出错: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 解包覆盖数据，提供完整无损回滚灾备
    /// </summary>
    public static void RestoreBackup(string zipPath)
    {
        var targetPath = HostAssets.DataRootPath;
        var backupTempPath = targetPath + "_temp_restore_bak";

        try
        {
            if (Directory.Exists(backupTempPath))
            {
                Directory.Delete(backupTempPath, true);
            }

            // 1. 尝试打开 zip 进行自检，确认非损坏或非空压缩包
            using (var zipStream = new FileStream(zipPath, FileMode.Open, FileAccess.Read))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Read))
            {
                var _ = archive.Entries.Count;
            }

            // 2. 原地重命名现有的数据目录，做灾备
            Directory.Move(targetPath, backupTempPath);

            try
            {
                // 3. 解压缩覆盖
                Directory.CreateDirectory(targetPath);
                ZipFile.ExtractToDirectory(zipPath, targetPath);

                // 4. 保证 logs 等目录结构完整重建
                HostAssets.EnsureCreated();

                // 5. 还原顺利，彻底删除灾备目录
                Directory.Delete(backupTempPath, true);
                HostAssets.AppendLog("BackupService: backup restore completed successfully.");
            }
            catch (Exception exExtract)
            {
                // 发生解压异常，还原旧的配置目录进行回滚
                if (Directory.Exists(targetPath))
                {
                    try { Directory.Delete(targetPath, true); } catch { /* ignore */ }
                }
                Directory.Move(backupTempPath, targetPath);
                throw new Exception($"还原解包失败，数据已回滚复原。详细错误: {exExtract.Message}", exExtract);
            }
        }
        catch (Exception ex)
        {
            throw new Exception($"还原过程发生异常: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 静默检查自动备份逻辑并在需要时在后台触发备份
    /// </summary>
    public static void RunAutoBackupIfNeeded()
    {
        try
        {
            var settings = AppSettingsStore.Load();
            var freq = settings.AutoBackupFrequency;
            if (string.IsNullOrWhiteSpace(freq) || freq.Equals("None", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var now = DateTime.Now;
            bool shouldBackup = false;

            if (string.IsNullOrWhiteSpace(settings.LastAutoBackupTime))
            {
                shouldBackup = true;
            }
            else if (DateTime.TryParse(settings.LastAutoBackupTime, out var lastBackup))
            {
                var diff = now - lastBackup;
                if (freq.Equals("Daily", StringComparison.OrdinalIgnoreCase) && diff.TotalDays >= 1.0)
                {
                    shouldBackup = true;
                }
                else if (freq.Equals("Weekly", StringComparison.OrdinalIgnoreCase) && diff.TotalDays >= 7.0)
                {
                    shouldBackup = true;
                }
                else if (freq.Equals("Monthly", StringComparison.OrdinalIgnoreCase) && diff.TotalDays >= 30.0)
                {
                    shouldBackup = true;
                }
            }
            else
            {
                shouldBackup = true;
            }

            if (shouldBackup)
            {
                var backupsDir = GetActiveBackupsDirectory();
                var backupFileName = $"{AutoBackupNamePrefix}{now:yyyyMMdd_HHmmss}.zip";
                var targetPath = Path.Combine(backupsDir, backupFileName);

                HostAssets.AppendLog($"BackupService: triggering auto backup (freq={freq}) to {targetPath}");
                CreateBackup(targetPath);

                // 更新配置
                var updatedSettings = AppSettingsStore.Load();
                updatedSettings = updatedSettings with { LastAutoBackupTime = now.ToString("yyyy-MM-dd HH:mm:ss") };
                AppSettingsStore.Save(updatedSettings);

                // 清理旧自动备份，仅保留 5 个
                CleanupOldAutoBackups(backupsDir);
            }
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"BackupService: Running auto backup failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 只保留最近 5 个自动备份文件，避免磁盘爆满
    /// </summary>
    private static void CleanupOldAutoBackups(string backupsDir)
    {
        try
        {
            if (!Directory.Exists(backupsDir)) return;

            var dirInfo = new DirectoryInfo(backupsDir);
            var autoBackups = dirInfo.GetFiles(AutoBackupNamePrefix + "*.zip")
                                      .OrderByDescending(f => f.LastWriteTime)
                                      .ToList();

            if (autoBackups.Count > 5)
            {
                for (int i = 5; i < autoBackups.Count; i++)
                {
                    try
                    {
                        autoBackups[i].Delete();
                        HostAssets.AppendLog($"BackupService: Cleaned up old auto backup file: {autoBackups[i].Name}");
                    }
                    catch (Exception ex)
                    {
                        HostAssets.AppendLog($"BackupService: Failed to delete old auto backup {autoBackups[i].Name}: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"BackupService: Cleanup old auto backups failed: {ex.Message}");
        }
    }
}
