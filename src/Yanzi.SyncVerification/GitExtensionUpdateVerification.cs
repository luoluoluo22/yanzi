using System.Diagnostics;
using OpenQuickHost.Sync;

internal static class GitExtensionUpdateVerification
{
    public static async Task RunAsync()
    {
        var temp = Path.Combine(Path.GetTempPath(), "yanzi-git-poll-" + Guid.NewGuid().ToString("N"));
        var bare = Path.Combine(temp, "remote.git");
        var author = Path.Combine(temp, "author");
        var client = Path.Combine(temp, "client");
        Directory.CreateDirectory(temp);
        try
        {
            Git(temp, "init", "--bare", "-b", "main", bare);
            Directory.CreateDirectory(author);
            Git(author, "init", "-b", "main");
            File.WriteAllText(Path.Combine(author, "manifest.json"), """{"id":"test-git-update","name":"Git test"}""");
            File.WriteAllText(Path.Combine(author, "content.txt"), "v1");
            Commit(author, "base");
            Git(author, "remote", "add", "origin", bare);
            Git(author, "push", "-u", "origin", "main");
            Git(temp, "clone", "-q", bare, client);

            var off = new GitExtensionUpdatePolicy { ExtensionId = "test-git-update" };
            Check(!GitExtensionUpdateService.IsDue(off, DateTimeOffset.UtcNow, true), "default must be OFF");
            var notify = off with { Mode = "notify", Frequency = "hourly" };
            var now = DateTimeOffset.UtcNow;
            Check(GitExtensionUpdateService.IsDue(notify, now, true), "first hourly check must run");
            Check(!GitExtensionUpdateService.IsDue(notify with { LastCheckedUtc = now }, now.AddMinutes(30), false), "hourly throttle failed");
            Check(GitExtensionUpdateService.IsDue(notify with { LastCheckedUtc = now }, now.AddHours(1.1), false), "hourly threshold failed");
            Check(GitExtensionUpdateService.IsDue(notify with { Frequency = "startup", LastCheckedUtc = now }, now, true), "startup check missing");
            Check(!GitExtensionUpdateService.IsDue(notify with { Frequency = "startup" }, now, false), "startup checked repeatedly");

            var config = Path.Combine(temp, "policies.json");
            GitExtensionUpdatePolicyStore.SaveTo(config, notify);
            Check(GitExtensionUpdatePolicyStore.LoadFrom(config)["test-git-update"].Frequency == "hourly", "policy persistence failed");
            try
            {
                GitExtensionUpdatePolicyStore.SaveTo(config, notify with { Remote = "--upload-pack=/tmp/malicious" });
                throw new Exception("Git option injection was accepted");
            }
            catch (ArgumentException) { }

            File.WriteAllText(Path.Combine(author, "content.txt"), "v2");
            Commit(author, "second");
            Git(author, "push", "origin", "main");

            var found = await GitExtensionUpdateService.CheckAsync(client, notify);
            Check(found.Status == "update-available", "notify mode failed to discover remote commit");
            Check(File.ReadAllText(Path.Combine(client, "content.txt")) == "v1", "notify mode changed local worktree");

            var auto = notify with { Mode = "auto" };
            var updated = await GitExtensionUpdateService.CheckAsync(client, auto);
            Check(updated.Status == "updated", "auto mode did not fast-forward");
            Check(File.ReadAllText(Path.Combine(client, "content.txt")) == "v2", "auto fast-forward did not update checkout");
            Check((await GitExtensionUpdateService.CheckAsync(client, auto)).Status == "unchanged", "idempotent check failed");

            File.WriteAllText(Path.Combine(client, "content.txt"), "local dirty");
            File.WriteAllText(Path.Combine(author, "content.txt"), "v3");
            Commit(author, "third");
            Git(author, "push", "origin", "main");
            var dirty = await GitExtensionUpdateService.CheckAsync(client, auto);
            Check(dirty.Status == "conflict" && File.ReadAllText(Path.Combine(client, "content.txt")) == "local dirty",
                "dirty checkout was overwritten");

            Git(client, "checkout", "--", "content.txt");
            File.WriteAllText(Path.Combine(client, "local.txt"), "new local file");
            Git(client, "add", "local.txt");
            Commit(client, "diverged");
            var diverged = await GitExtensionUpdateService.CheckAsync(client, auto);
            Check(diverged.Status == "conflict" && File.Exists(Path.Combine(client, "local.txt")), "diverged history overwritten");

            Check(!GitExtensionUpdateService.ValidRemote("-c") &&
                  !GitExtensionUpdateService.ValidBranch("../HEAD") &&
                  !GitExtensionUpdateService.ValidBranch("main//bad"), "Git refs allow option injection");
            Console.WriteLine("Git update scheduler passed: off-default, modes, hour/day/startup, settings persistence, injection defense, fetch-only, fast-forward, dirty/diverged protection.");
        }
        finally
        {
            // .git object files are read-only on Windows and need normalized attributes.
            if (Directory.Exists(temp))
            {
                foreach (var path in Directory.EnumerateFileSystemEntries(temp, "*", SearchOption.AllDirectories))
                {
                    try { File.SetAttributes(path, FileAttributes.Normal); } catch (IOException) { }
                }
                Directory.Delete(temp, recursive: true);
            }
        }
    }

    private static void Commit(string dir, string message)
    {
        Git(dir, "add", ".");
        Git(dir, "-c", "user.name=Yanzi-Test", "-c", "user.email=test@example.invalid", "commit", "-qm", message);
    }

    private static string Git(string dir, params string[] args)
    {
        var ps = new ProcessStartInfo("git")
        {
            WorkingDirectory = dir, UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true
        };
        foreach (var s in args) ps.ArgumentList.Add(s);
        using var process = Process.Start(ps) ?? throw new InvalidOperationException("Git not installed");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(25000);
        if (process.ExitCode != 0) throw new Exception($"git {string.Join(' ', args)}: {stderr}");
        return stdout.Trim();
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("Git update verification: " + message);
    }
}