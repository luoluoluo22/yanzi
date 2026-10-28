using System;
using System.Threading.Tasks;

namespace OpenQuickHost.Sync;

/// <summary>
/// 燕子 1.0.0+ 版本的 VIP 维护计划与版本权益管理服务
/// 遵循平滑过渡与维护期永续授权模式：
/// 1. 0.3.x 及开源基础核心功能永久免费保留，未开通 VIP 的老用户平稳过渡，绝不强制退出；
/// 2. 拥有在保 VIP 维护期的用户，享有全量高级权益及后续新版本的自动静默升级通道；
/// 3. 未开通或超出维护期的用户，运行于基础免费模式，且自动阻断后续更高版本（如 1.0.1+）的自动静默推送，彻底根除版本死循环。
/// </summary>
public static class VipGateService
{
    private static bool _isVerified;

    public static bool IsVerified => _isVerified;

    /// <summary>
    /// 检查并同步当前用户的维护期授权状态
    /// </summary>
    public static async Task<bool> EnsureVipEntitledAsync(MainWindow mainWindow, bool isStartup = true)
    {
        if (_isVerified)
        {
            return true;
        }

        var session = SyncSessionStore.Load();

        // 1. 本地缓存快速校验
        if (session != null && AppVersionInfo.IsVersionEntitled(session.IsVip, session.VipType, session.VipExpireAt))
        {
            _isVerified = true;
            HostAssets.AppendLog($"[VipGate] VIP maintenance plan active: userId={session.UserId}, type={session.VipType}, expireAt={session.VipExpireAt}");
            return true;
        }

        // 2. 本地缓存未满足，若已登录则向云端异步拉取最新状态（防止在其他设备核销后本地缓存未同步）
        if (mainWindow.CloudSyncClient != null && mainWindow.CloudSyncClient.HasCredential)
        {
            try
            {
                HostAssets.AppendLog("[VipGate] Querying latest VIP status from cloud...");
                var cloudStatus = await mainWindow.CloudSyncClient.GetVipStatusAsync();
                if (cloudStatus != null)
                {
                    session = SyncSessionStore.Load();
                    if (session != null && AppVersionInfo.IsVersionEntitled(session.IsVip, session.VipType, session.VipExpireAt))
                    {
                        _isVerified = true;
                        HostAssets.AppendLog($"[VipGate] VIP maintenance plan confirmed after cloud refresh: userId={session.UserId}, type={session.VipType}, expireAt={session.VipExpireAt}");
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                HostAssets.AppendLog($"[VipGate] Cloud status query skipped: {ex.Message}");
            }
        }

        // 3. 当前运行于基础免费模式（核心功能无缝可用，静默更新将对后续新版实施保护）
        _isVerified = false;
        HostAssets.AppendLog("[VipGate] App running in standard free mode (all core features remain permanently available). Future version auto-upgrades are protected.");
        return false;
    }
}
