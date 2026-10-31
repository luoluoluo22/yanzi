using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using OpenQuickHost.Sync;

namespace OpenQuickHost;

/// <summary>Deterministic, fail-closed conjunction of optional host conditions.</summary>
public static class HostDispatchPolicy
{
    public sealed record Snapshot(bool? Online, bool? AcPower, double? CpuPercent, double? IdleMinutes, bool Fullscreen = false);
    public sealed record Result(bool Allowed, string Reason);

    public static Result Evaluate(LocalExtensionHostExecutionManifest? execution, Snapshot snapshot)
    {
        var c = execution?.Conditions;
        if (execution?.Priority is { Length: > 0 } priority &&
            priority is not ("low" or "normal" or "high"))
            return new(false, "invalid-priority");
        if (c == null) return new(true, "ready");

        if (c.MaxCpuPercent is { } cpu && (cpu < 1 || cpu > 100))
            return new(false, "invalid-max-cpu");
        if (c.MinIdleMinutes is { } idle && (idle < 0 || idle > 1440))
            return new(false, "invalid-min-idle");

        if (c.RequireOnline == true && snapshot.Online != true)
            return new(false, snapshot.Online == false ? "network-offline" : "network-unknown");
        if (c.RequireAcPower == true && snapshot.AcPower != true)
            return new(false, snapshot.AcPower == false ? "battery-power" : "power-unknown");
        if (c.MaxCpuPercent is { } threshold)
        {
            if (!snapshot.CpuPercent.HasValue) return new(false, "cpu-sampling");
            if (snapshot.CpuPercent.Value > threshold) return new(false, "cpu-high");
        }
        if (c.MinIdleMinutes is { } min)
        {
            if (snapshot.Fullscreen) return new(false, "fullscreen");
            if (!snapshot.IdleMinutes.HasValue) return new(false, "idle-unknown");
            if (snapshot.IdleMinutes.Value < min) return new(false, "waiting-idle-condition");
        }
        return new(true, "ready");
    }

    public static int Priority(LocalExtensionHostExecutionManifest? execution) =>
        execution?.Priority switch { "high" => 2, "low" => 0, _ => 1 };
}

/// <summary>Read-only machine sampler. CPU is delta sampled, never guessed from a single snapshot.</summary>
public static class HostResourceSampler
{
    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime { public uint Low; public uint High; public ulong Value => ((ulong)High << 32) | Low; }
    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo { public uint Size; public uint Tick; }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);

    private static readonly object Gate = new();
    private static (ulong idle, ulong kernel, ulong user, DateTimeOffset at)? _lastCpu;
    private static double? _cpuPercent;

    public static HostDispatchPolicy.Snapshot Read(LocalExtensionHostExecutionManifest? execution)
    {
        var c = execution?.Conditions;
        if (c is null) return new(null, null, null, null);
        bool? online = null;
        bool? ac = null;
        double? idle = null;
        double? cpu = null;

        if (c.RequireOnline == true)
        {
            try { online = NetworkInterface.GetIsNetworkAvailable(); }
            catch (Exception ex) when (ex is NetworkInformationException or InvalidOperationException) { }
        }
        if (c.RequireAcPower == true)
        {
            try
            {
                ac = SystemInformation.PowerStatus.PowerLineStatus switch
                {
                    PowerLineStatus.Online => true,
                    PowerLineStatus.Offline => false,
                    _ => null
                };
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        }
        var fullscreen = c.MinIdleMinutes.HasValue && ExtensionIdleTriggerService.IsForegroundWindowFullscreen();
        if (c.MinIdleMinutes.HasValue)
        {
            var input = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
            if (GetLastInputInfo(ref input))
            {
                var now = unchecked((uint)Environment.TickCount);
                idle = unchecked(now - input.Tick) / 60000d;
            }
        }
        if (c.MaxCpuPercent.HasValue)
        {
            lock (Gate)
            {
                if (GetSystemTimes(out var idleTime, out var kernel, out var user))
                {
                    var now = DateTimeOffset.UtcNow;
                    if (_lastCpu is { } previous)
                    {
                        if (now - previous.at > TimeSpan.FromSeconds(30))
                        {
                            _cpuPercent = null;
                            _lastCpu = (idleTime.Value, kernel.Value, user.Value, now);
                        }
                        else if (now - previous.at >= TimeSpan.FromSeconds(1))
                        {
                            var deltaTotal = (kernel.Value - previous.kernel) + (user.Value - previous.user);
                            var deltaIdle = idleTime.Value - previous.idle;
                            _cpuPercent = deltaTotal > 0 && deltaIdle <= deltaTotal
                                ? Math.Clamp(100d * (deltaTotal - deltaIdle) / deltaTotal, 0, 100)
                                : null;
                            _lastCpu = (idleTime.Value, kernel.Value, user.Value, now);
                        }
                    }
                    else _lastCpu = (idleTime.Value, kernel.Value, user.Value, now);
                }
                else _cpuPercent = null;
                cpu = _cpuPercent;
            }
        }
        return new(online, ac, cpu, idle, fullscreen);
    }
}
