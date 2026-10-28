using System.Reflection;
using OpenQuickHost;

internal static class WindowsEnvironmentVerification
{
    public static void Run()
    {
        var dataRoot = Path.Combine(Path.GetTempPath(), "yanzi-env-verify-" + Guid.NewGuid().ToString("N"));
        var key = "YANZI_GLOBAL_ENV_TEST_" + Guid.NewGuid().ToString("N");
        var collision = key + "_EXTERNAL";
        Directory.CreateDirectory(dataRoot);
        var scope = (IDisposable)typeof(HostAssets).GetMethod(
            "UseExistingDataRootForVerification", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [dataRoot])!;
        try
        {
            Environment.SetEnvironmentVariable(collision, "external-value", EnvironmentVariableTarget.User);
            AppEnvironmentVariableStore.Save([
                new AppEnvironmentVariableSettings { Name = key, Value = "v1" },
                new AppEnvironmentVariableSettings { Name = collision, Value = "do-not-clobber" },
                new AppEnvironmentVariableSettings { Name = "CNBLOGS_TOKEN", Value = "provider-only-fixture" }
            ]);
            Check(Environment.GetEnvironmentVariable(key, EnvironmentVariableTarget.User) == "v1",
                "User registry should contain new Yanzi variable");
            Check(Environment.GetEnvironmentVariable(collision, EnvironmentVariableTarget.User) == "external-value",
                "External user variable must not be overwritten");
            Check(Environment.GetEnvironmentVariable("CNBLOGS_TOKEN", EnvironmentVariableTarget.User) != "provider-only-fixture",
                "Provider-only secret must not be globally published");

            AppEnvironmentVariableStore.Save([
                new AppEnvironmentVariableSettings { Name = key, Value = "v2" }
            ]);
            Check(Environment.GetEnvironmentVariable(key, EnvironmentVariableTarget.User) == "v2",
                "Managed value should update when edited");
            Check(Environment.GetEnvironmentVariable(collision, EnvironmentVariableTarget.User) == "external-value",
                "Collision must remain intact after removing it from Yanzi settings");

            AppEnvironmentVariableStore.Save([]);
            Check(Environment.GetEnvironmentVariable(key, EnvironmentVariableTarget.User) == null,
                "Removed Yanzi variable should be removed from registry");
            Console.WriteLine("WINDOWS_USER_ENV_SYNC=PASSED; startup/save/update/delete/collision/provider-only behavior");
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, null, EnvironmentVariableTarget.User);
            Environment.SetEnvironmentVariable(collision, null, EnvironmentVariableTarget.User);
            Environment.SetEnvironmentVariable(key, null);
            scope.Dispose();
            try { Directory.Delete(dataRoot, true); } catch { }
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
