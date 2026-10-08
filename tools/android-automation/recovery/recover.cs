using System;
using System.Reflection;
using System.Threading.Tasks;
using OpenQuickHost.CSharpRuntime;

// One-shot recovery for the exact legacy Android service only. Never reads
// device or user data, and cannot stop unrelated services.
public static class YanziAction
{
    public static Task<string> RunAsync(YanziActionContext context)
    {
        const string key = "yanzi-android-automation-service";
        var old = context.GetRegisteredObject?.Invoke(key);
        if (old == null) return Task.FromResult("old_service_not_found");
        if (old.GetType().Name != "AndroidShoppingService")
            return Task.FromResult("type_mismatch_no_action");
        var method = old.GetType().GetMethod("Quit", BindingFlags.Instance | BindingFlags.Public,
            null, Type.EmptyTypes, null);
        if (method == null) return Task.FromResult("quit_not_exposed");
        method.Invoke(old, Array.Empty<object>());
        return Task.FromResult("quit_sent");
    }
}
