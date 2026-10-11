using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace OpenQuickHost;

public static class AppVersionInfo
{
    public static string Version
    {
        get
        {
            try
            {
                var processPath = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(processPath))
                {
                    var fileVersion = FileVersionInfo.GetVersionInfo(processPath);
                    if (!string.IsNullOrWhiteSpace(fileVersion.ProductVersion))
                    {
                        return fileVersion.ProductVersion!;
                    }

                    if (!string.IsNullOrWhiteSpace(fileVersion.FileVersion))
                    {
                        return fileVersion.FileVersion!;
                    }
                }
            }
            catch
            {
            }

            return Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
        }
    }

    public static string BuildStamp
    {
        get
        {
            try
            {
                var assemblyPath = typeof(AppVersionInfo).Assembly.Location;
                if (!string.IsNullOrWhiteSpace(assemblyPath) && File.Exists(assemblyPath))
                {
                    return File.GetLastWriteTime(assemblyPath).ToString("yyyy-MM-dd HH:mm:ss");
                }

                var processPath = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(processPath) && File.Exists(processPath))
                {
                    return File.GetLastWriteTime(processPath).ToString("yyyy-MM-dd HH:mm:ss");
                }
            }
            catch
            {
            }

            return "unknown";
        }
    }

    public static string DisplayText => $"燕子 · v{Version} · build {BuildStamp}";

    /// <summary>
    /// 当前版本的正式发布基准日期（UTC）
    /// 1.0.0 正式版定于 2026-09-07 发布
    /// </summary>
    public static readonly DateTime ReleaseDateUtc = new(2026, 9, 7, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// 0.x 和 1.0.0 为免费版；1.0.1 起需要覆盖版本发布日期的维护权益。
    /// 无法验证正式发布日期时不允许付费版本自动更新，避免回退到 1.0.0 日期导致越权。
    /// 已购买维护期内的版本可继续安装，即使 VIP 的云服务权益后来过期。
    /// </summary>
    public static bool IsUpdateEntitled(string targetVersion, string? vipType, string? vipExpireAt, DateTime? releaseDateUtc)
    {
        var versionText = (targetVersion ?? string.Empty).Trim().TrimStart('v', 'V');
        if (System.Version.TryParse(versionText, out var version) && version <= new System.Version(1, 0, 0))
            return true;
        if (string.Equals(vipType, "lifetime", StringComparison.OrdinalIgnoreCase))
            return true;
        if (!releaseDateUtc.HasValue)
            return false;
        if (!DateTimeOffset.TryParse(vipExpireAt, out var membershipEnd))
            return false;
        var releaseDate = releaseDateUtc.Value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(releaseDateUtc.Value, DateTimeKind.Utc)
            : releaseDateUtc.Value.ToUniversalTime();
        return membershipEnd >= new DateTimeOffset(releaseDate);
    }

    /// <summary>
    /// 判断给定的 VIP 信息是否有权使用指定发布日期的版本（基于维护期永续许可模式）
    /// </summary>
    public static bool IsVersionEntitled(bool isVip, string? vipType, string? vipExpireAt, DateTime? versionReleaseDate = null)
    {
        var targetReleaseDate = versionReleaseDate ?? ReleaseDateUtc;

        // 终身 VIP 拥有所有当前与未来版本的无限制使用权
        if (string.Equals(vipType, "lifetime", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 未开通或非 VIP
        if (!isVip)
        {
            return false;
        }

        // 无到期时间信息
        if (string.IsNullOrWhiteSpace(vipExpireAt))
        {
            return false;
        }

        if (DateTime.TryParse(vipExpireAt, out var expireTime))
        {
            // 只要用户的 VIP 到期时间在版本发布日期之后（或当天），即拥有该版本的永久使用权
            return expireTime.ToUniversalTime() >= targetReleaseDate;
        }

        return false;
    }
}
