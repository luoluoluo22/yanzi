using OpenQuickHost;

internal static class SingleProcessVerification
{
    public static void Run()
    {
        static void Check(bool ok, string label)
        {
            if (!ok) throw new InvalidOperationException("Single-process regression: " + label);
        }

        Check(!HostRuntimeProfile.IsRuntime, "verification must not be a production Runtime");
        Check(HostRuntimeProfile.IsDevelopment || HostRuntimeProfile.OwnsBackgroundServices,
            "stable Shell must own background services");
        Check(!HostRuntimeProfile.IsDevelopment || !HostRuntimeProfile.GlobalListenersEnabled,
            "development hooks must stay disabled");
        var root = Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData), "YanziRuntime");
        var managed = Path.Combine(root, "bundled", "test", "Yanzi.Runtime.exe");
        Check(LegacyRuntimeMigrationService.IsManagedRuntimeExecutable(managed), "owned legacy snapshot");
        Check(!LegacyRuntimeMigrationService.IsManagedRuntimeExecutable(Path.Combine(root, "runtime.json")),
            "runtime manifest is not executable");
        Check(!LegacyRuntimeMigrationService.IsManagedRuntimeExecutable(
            Path.Combine(root + "-other", "Yanzi.Runtime.exe")), "prefix must be directory-bound");
        Check(!LegacyRuntimeMigrationService.IsManagedRuntimeExecutable(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SomethingElse", "Yanzi.Runtime.exe")), "never terminate an unrelated process");
        Check(!RuntimeConnection.IsConnected, "single process must not depend on Runtime IPC");
        Console.WriteLine("SINGLE_PROCESS_ARCHITECTURE_PASSED");
    }
}
