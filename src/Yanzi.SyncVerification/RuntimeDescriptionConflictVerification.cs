using System.Text.Json;
using System.Text.Json.Nodes;
using OpenQuickHost.Sync;

internal static class RuntimeDescriptionConflictVerification
{
    internal static void Run()
    {
        var local = Envelope(new[] { Entry("MAIL_APP_PASSWORD", "fixed-test-placeholder", "") });
        var remote = Envelope(new[] { Entry("MAIL_APP_PASSWORD", "fixed-test-placeholder", "Gmail app password") });
        Expect(RuntimeDescriptionConflictPolicy.CanAdoptRemote(local, remote), "missing description can be adopted");
        Expect(!RuntimeDescriptionConflictPolicy.CanAdoptRemote(remote, local), "nonempty local description is never erased");

        var changedSecret = Envelope(new[] { Entry("MAIL_APP_PASSWORD", "different-test-placeholder", "Gmail app password") });
        Expect(!RuntimeDescriptionConflictPolicy.CanAdoptRemote(local, changedSecret), "changed credential must be a conflict");

        var changedName = Envelope(new[] { Entry("OTHER_MAIL", "fixed-test-placeholder", "Gmail app password") });
        Expect(!RuntimeDescriptionConflictPolicy.CanAdoptRemote(local, changedName), "different variable names must be a conflict");

        var changedSetting = Envelope(new[] { Entry("MAIL_APP_PASSWORD", "fixed-test-placeholder", "Gmail app password") }, enableAutoUpdate: false);
        Expect(!RuntimeDescriptionConflictPolicy.CanAdoptRemote(local, changedSetting), "unrelated runtime setting must be a conflict");

        var addedVariable = Envelope(new[] {
            Entry("MAIL_APP_PASSWORD", "fixed-test-placeholder", "Gmail app password"),
            Entry("MAIL_EMAIL", "example.invalid", "")
        });
        Expect(!RuntimeDescriptionConflictPolicy.CanAdoptRemote(local, addedVariable), "new variable requires independent merge");

        var identical = Envelope(new[] { Entry("MAIL_APP_PASSWORD", "fixed-test-placeholder", "") });
        Expect(!RuntimeDescriptionConflictPolicy.CanAdoptRemote(local, identical), "identical payload is handled by ordinary equivalence");

        var twoLocal = Envelope(new[] {
            Entry("MAIL_APP_PASSWORD", "fixed-test-placeholder", ""),
            Entry("CLOUDFLARE_API_TOKEN", "another-placeholder", "")
        });
        var twoRemoteReordered = Envelope(new[] {
            Entry("CLOUDFLARE_API_TOKEN", "another-placeholder", ""),
            Entry("MAIL_APP_PASSWORD", "fixed-test-placeholder", "Gmail app password")
        });
        Expect(RuntimeDescriptionConflictPolicy.CanAdoptRemote(twoLocal, twoRemoteReordered), "variable order does not affect safe description merge");

        Console.WriteLine("Runtime description conflict verification passed.");
    }

    private static JsonObject Entry(string name, string value, string description) =>
        new()
        {
            ["name"] = name,
            ["value"] = value,
            ["description"] = description
        };

    private static LauncherConfigObjectEnvelope Envelope(JsonObject[] entries, bool enableAutoUpdate = true)
    {
        var root = new JsonObject
        {
            ["enableAutoUpdate"] = enableAutoUpdate,
            ["environmentVariables"] = new JsonArray(entries.Select(e => (JsonNode?)e).ToArray())
        };
        return new LauncherConfigObjectEnvelope
        {
            ObjectId = "settings.runtime",
            Payload = JsonDocument.Parse(root.ToJsonString()).RootElement.Clone()
        };
    }

    private static void Expect(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
