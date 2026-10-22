using OpenQuickHost.Sync;

internal static class AccountExtensionPullPolicyVerification
{
    public static void Run()
    {
        static void Expect(AccountExtensionPullDecision expected, AccountExtensionPullDecision actual, string scenario)
        {
            if (actual != expected)
                throw new InvalidOperationException($"Account sync pull policy failed: {scenario}, expected={expected}, actual={actual}");
            Console.WriteLine($"PASS: account extension {scenario}");
        }

        Expect(AccountExtensionPullDecision.InstallMissing,
            AccountExtensionPullPolicy.Decide(false, null, "1.0.0"), "fresh machine installs cloud package");

        Expect(AccountExtensionPullDecision.SameVersionConflict,
            AccountExtensionPullPolicy.Decide(true, "1.0.0", "1.0.0", "LOCAL-EDIT", "OLD-CLOUD"),
            "local same-version edit cannot be overwritten by cloud");

        Expect(AccountExtensionPullDecision.KeepLocal,
            AccountExtensionPullPolicy.Decide(true, "1.0.0", "1.0.0", "abc123", "ABC123"),
            "identical packages are unchanged");

        Expect(AccountExtensionPullDecision.KeepLocal,
            AccountExtensionPullPolicy.Decide(true, "1.0.1", "1.0.0", "LOCAL-NEW", "CLOUD-OLD"),
            "lower cloud version cannot overwrite newer local");

        Expect(AccountExtensionPullDecision.InstallNewerVersion,
            AccountExtensionPullPolicy.Decide(true, "1.0.0", "1.0.1", "OLD", "NEW"),
            "strictly newer cloud version can install");

        Expect(AccountExtensionPullDecision.UnorderedVersionConflict,
            AccountExtensionPullPolicy.Decide(true, "dev-local", "1.0.1", "LOCAL", "CLOUD"),
            "unparseable local version must not be overwritten");

        if (AccountExtensionPullPolicy.IsRemoteVersionNewer("1.0.0", "1.0.0") ||
            AccountExtensionPullPolicy.IsRemoteVersionNewer("1.0.1", "1.0.0") ||
            !AccountExtensionPullPolicy.IsRemoteVersionNewer("v1.0.0", "1.0.1"))
            throw new InvalidOperationException("Account sync release ordering check failed.");

        Console.WriteLine("ACCOUNT_EXTENSION_PULL_POLICY_OK");
    }
}
