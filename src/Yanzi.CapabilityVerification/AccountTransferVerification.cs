using System.Reflection;
using System.Security.Cryptography;
using OpenQuickHost;

internal static class AccountTransferVerification
{
    internal static void Run()
    {
        var t = typeof(YanziBuiltinCapabilityRegistration).Assembly
            .GetType("OpenQuickHost.YanziAccountTransferBootstrap", true)!;
        var method = t.GetMethod("Derive", BindingFlags.Static | BindingFlags.NonPublic)!;
        var hostName = t.GetMethod("NetworkHostname", BindingFlags.Static | BindingFlags.NonPublic)!;
        var key = RandomNumberGenerator.GetBytes(32);
        var changedKey = RandomNumberGenerator.GetBytes(32);
        const string a = "desktop-bdf6c1e07f9649aa9f59";
        const string b = "desktop-91f6b2e33a4c47798b30";
        object Derive(string account, string own, string peer, byte[] seed) =>
            method.Invoke(null, [account, own, peer, seed])!;
        string Get(object plan, string keyName) =>
            (string)plan.GetType().GetProperty(keyName)!.GetValue(plan)!;
        void Assert(bool condition, string reason)
        {
            if (!condition) throw new InvalidOperationException(reason);
        }
        var left = Derive("account-1", a, b, key);
        var right = Derive("account-1", b, a, key);
        var repeat = Derive("account-1", a, b, key);
        Assert(Get(left, "NetworkName") == Get(right, "NetworkName"), "network differs across devices");
        Assert(Get(left, "NetworkSecret") == Get(right, "NetworkSecret"), "secrets differ across devices");
        Assert(Get(left, "LocalIp") == Get(right, "RemoteIp"), "device A IP mismatch");
        Assert(Get(left, "RemoteIp") == Get(right, "LocalIp"), "device B IP mismatch");
        Assert(Get(left, "NetworkName") == Get(repeat, "NetworkName"), "network not deterministic");
        Assert(Get(left, "NetworkName") != Get(Derive("account-2", a, b, key), "NetworkName"),
            "network leaked across accounts");
        Assert(Get(left, "NetworkSecret") != Get(Derive("account-1", a, b, changedKey), "NetworkSecret"),
            "network unchanged across keys");
        Assert(Get(left, "NetworkSecret").Length == 64, "secret invalid length");
        Assert(((string)hostName.Invoke(null, [a])!).Length <= 32, "EasyTier hostname truncation");
        Assert((string)hostName.Invoke(null, [a])! != (string)hostName.Invoke(null, [b])!,
            "hostnames collided");
        CryptographicOperations.ZeroMemory(key);
        CryptographicOperations.ZeroMemory(changedKey);
        Console.WriteLine("ACCOUNT_P2P_DERIVATION=PASSED: shared per-pair secret, mirrored IP, account/key isolation, short hostname");
    }
}
