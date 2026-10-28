using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace OpenQuickHost;

/// <summary>Serialize shared Stable/Runtime settings transactions across processes.</summary>
internal sealed class SettingsProcessLock : IDisposable
{
    private readonly Mutex _mutex;
    private SettingsProcessLock(string dataRoot)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(dataRoot.ToUpperInvariant())))[..24];
        _mutex = new Mutex(false, @"Local\Yanzi.Settings." + key);
        try
        {
            if (!_mutex.WaitOne(TimeSpan.FromSeconds(10))) throw new TimeoutException("设置正在由另一进程保存，请稍后重试。");
        }
        catch (AbandonedMutexException) { /* The previous writer exited; the atomic file remains authoritative. */ }
        catch { _mutex.Dispose(); throw; }
    }
    public static SettingsProcessLock Enter(string dataRoot) => new(dataRoot);
    public void Dispose() { _mutex.ReleaseMutex(); _mutex.Dispose(); }
}
