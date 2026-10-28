using System.Text.Json;
using OpenQuickHost.Sync;

internal static class GitHubPrivateBackupPolicyVerification
{
    public static void Run()
    {
        var accepted = 0;
        using (var repository = JsonDocument.Parse("""{"private":true,"archived":false}"""))
        {
            GitHubPersonalSyncBackend.AssertPrivateBackupRepository(repository.RootElement);
            accepted++;
        }
        foreach (var metadata in new[]
        {
            """{"private":false,"archived":false}""",
            """{"archived":false}""",
            """{"private":true,"archived":true}"""
        })
        {
            using var repository = JsonDocument.Parse(metadata);
            try
            {
                GitHubPersonalSyncBackend.AssertPrivateBackupRepository(repository.RootElement);
            }
            catch (InvalidOperationException)
            {
                accepted++;
                continue;
            }
            throw new Exception("Public, archived or unknown-visibility backup repository was accepted.");
        }
        if (accepted != 4) throw new Exception("Private GitHub policy verification incomplete.");
        Console.WriteLine("GITHUB_PRIVATE_BACKUP_POLICY_PASSED cases=4");
    }
}
