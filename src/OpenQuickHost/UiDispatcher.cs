using System.Windows.Threading;

namespace OpenQuickHost;

/// <summary>
/// 统一的 UI 线程派发封装：所有"从后台线程/低级钩子回调触碰 UI"的代码都应经由这里，
/// 代替各服务手写的 Application.Current?.Dispatcher 判空 + CheckAccess + BeginInvoke 样板。
/// 统一约定：一律 BeginInvoke，绝不在调用线程内联执行（钩子回调内联执行完整 UI 操作
/// 会拖垮输入链路并可能导致系统静默摘钩）。
/// </summary>
public static class UiDispatcher
{
    /// <summary>当前 UI Dispatcher（应用退出阶段可能为 null）。</summary>
    public static Dispatcher? Current => System.Windows.Application.Current?.Dispatcher;

    /// <summary>
    /// 异步派发到 UI 线程；动作内的异常被捕获并上报（默认记日志），绝不逃逸出派发链路。
    /// </summary>
    public static void Post(Action action, DispatcherPriority priority = DispatcherPriority.Input, Action<Exception>? onError = null)
    {
        var dispatcher = Current;
        if (dispatcher == null)
        {
            return;
        }

        _ = dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                (onError ?? DefaultOnError)(ex);
            }
        }), priority);
    }

    private static void DefaultOnError(Exception ex)
    {
        HostAssets.AppendLog($"[UiDispatcher] posted action failed: {ex.Message}");
    }
}
