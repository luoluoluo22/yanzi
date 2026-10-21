using System.Diagnostics;
using System.IO.Compression;
using OpenQuickHost;
using OpenQuickHost.Sync;

internal static class GitNativeExtensionVerification
{
    public static void Run()
    {
        var temp = Path.Combine(Path.GetTempPath(), "yanzi-git-native-" + Guid.NewGuid().ToString("N"));
        var local = Path.Combine(temp, "extension");
        var other = Path.Combine(temp, "other");
        var conflicts = Path.Combine(temp, "conflicts");
        Directory.CreateDirectory(local);
        try
        {
            Write(local, "manifest.json", """{"id":"test-git-native","name":"Git native","version":"1.0.0"}""");
            Write(local, ".gitignore", "build/\n*.tmp\n!important.tmp\n.env\n");
            Write(local, ".yanziignore", ".github/\nprivate/*.txt\n!private/public.txt\n");
            Write(local, "src/.gitignore", "*.cache\n");
            Write(local, "content.txt", "version-one");
            Write(local, "src/main.cs", "class Program {}");
            Write(local, "src/local.cache", "ignored nested");
            Write(local, "build/output.dll", "ignored");
            Write(local, "test.tmp", "ignored glob");
            Write(local, "important.tmp", "re-included");
            Write(local, ".env", "DO_NOT_UPLOAD");
            Write(local, "private/confidential.txt", "DO_NOT_UPLOAD");
            Write(local, "private/public.txt", "safe");
            Write(local, ".github/workflows/build.yml", "ignored CI");
            Git(local, "init", "-b", "main");
            Git(local, "add", ".");
            Git(local, "-c", "user.name=Yanzi-Test", "-c", "user.email=test@example.invalid",
                "commit", "-qm", "base");

            var original = Package(local);
            using (var zip = new ZipArchive(new MemoryStream(original), ZipArchiveMode.Read))
            {
                var names = zip.Entries.Select(e => e.FullName.Replace('\\', '/')).ToHashSet();
                Check(names.Contains("manifest.json") && names.Contains("content.txt") && names.Contains("important.tmp"), "main content/negation omitted");
                Check(names.Contains("private/public.txt") && names.Contains("src/main.cs"), "nested content/negation missing");
                Check(names.Contains(ExtensionGitSafety.ProvenanceEntry), "clean Git provenance missing");
                Check(!names.Any(n => n.StartsWith(".git/") || n == ".git" || n == ".env" ||
                    n.StartsWith("build/") || n.EndsWith(".cache") || n == "test.tmp" ||
                    n.StartsWith(".github/") || n == "private/confidential.txt"), "ignored file leaked into ZIP");
            }

            // Account/WebDAV extraction must never manufacture a .git folder,
            // but may carry provenance as disposable transport metadata.
            var standalone = Path.Combine(temp, "standalone");
            SyncPackageSafety.ReplaceDirectoryAsync(standalone, original, default).GetAwaiter().GetResult();
            Check(!ExtensionGitSafety.HasGitWorktree(standalone) &&
                  !File.Exists(Path.Combine(standalone, ExtensionGitSafety.ProvenanceEntry)),
                  "Git metadata escaped into normal extension install");

            using (var memory = new MemoryStream())
            {
                using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
                {
                    using var writer = new StreamWriter(zip.CreateEntry(".git/config").Open());
                    writer.Write("malicious");
                }
                using var archive = new ZipArchive(new MemoryStream(memory.ToArray()), ZipArchiveMode.Read);
                try
                {
                    ExtensionGitSafety.ValidateArchiveEntries(archive);
                    throw new Exception("Untrusted Git metadata accepted in ZIP");
                }
                catch (InvalidDataException) { }
            }

            Git(temp, "clone", "-q", local, other);
            Write(other, "content.txt", "version-two");
            Git(other, "add", "content.txt");
            Git(other, "-c", "user.name=Yanzi-Test", "-c", "user.email=test@example.invalid",
                "commit", "-qm", "next");
            var incoming = Package(other);
            var baseHead = Git(local, "rev-parse", "HEAD");
            try
            {
                ExtensionGitSafety.HandleIncoming(local, incoming, "test conflict", conflicts);
                throw new Exception("Unfetched cloud ZIP overwrote a Git working tree");
            }
            catch (IOException ex)
            {
                Check(ex.Message.Contains("Git"), "Git conflict error not reported");
            }
            Check(Git(local, "rev-parse", "HEAD") == baseHead && File.ReadAllText(Path.Combine(local, "content.txt")) == "version-one",
                "Git conflict changed local code or HEAD");
            Check(Directory.EnumerateFiles(conflicts, "*.zip", SearchOption.AllDirectories).Any(),
                "Incoming conflict snapshot not preserved");

            Git(local, "remote", "add", "origin", other);
            // No manual fetch: cloud commit provenance must cause a safe fetch
            // from the existing Git remote before fast-forwarding.
            Check(ExtensionGitSafety.HandleIncoming(local, incoming, "test fast forward", conflicts),
                "Git fast-forward was not accepted");
            Check(File.ReadAllText(Path.Combine(local, "content.txt")) == "version-two" &&
                  ExtensionGitSafety.HasGitWorktree(local), "Git fast-forward damaged checkout");
            Check(ExtensionGitSafety.HandleIncoming(local, incoming, "idempotent", conflicts),
                "Identical ZIP should be idempotent");

            Write(local, "content.txt", "dirty local version");
            Write(other, "content.txt", "version-three");
            Git(other, "add", "content.txt");
            Git(other, "-c", "user.name=Yanzi-Test", "-c", "user.email=test@example.invalid",
                "commit", "-qm", "third");
            var third = Package(other); // dirty source is false; other is clean
            Git(local, "fetch", "-q", other, "main");
            try { ExtensionGitSafety.HandleIncoming(local, third, "dirty conflict", conflicts); throw new Exception("Dirty Git tree was overwritten"); }
            catch (IOException) { }
            Check(File.ReadAllText(Path.Combine(local, "content.txt")) == "dirty local version", "Dirty local file lost");
            try { ExtensionGitSafety.GuardDeletion(local, "remote delete"); throw new Exception("Git deletion permitted"); }
            catch (IOException) { }
            Check(ExtensionGitSafety.HasGitWorktree(local), "Git metadata was deleted");
            Console.WriteLine("Git-native sync verified: nested ignore, negation, secrets, provenance, conflict archive, fast-forward, dirty tree, deletion guard.");
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                foreach (var path in Directory.EnumerateFileSystemEntries(temp, "*", SearchOption.AllDirectories))
                    File.SetAttributes(path, FileAttributes.Normal);
                Directory.Delete(temp, recursive: true);
            }
        }
    }

    private static byte[] Package(string directory)
    {
        var command = new CommandItem("T", "Git native", "", "小程序", "#000000", null, [],
            source: CommandSource.LocalExtension, extensionId: "test-git-native", extensionDirectoryPath: directory);
        return ExtensionPackageService.BuildPackage(command, "1.0.0");
    }

    private static void Write(string root, string path, string content)
    {
        var full = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private static string Git(string directory, params string[] arguments)
    {
        var info = new ProcessStartInfo("git")
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var arg in arguments) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new Exception("Git cannot start");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit(20000);
        if (process.ExitCode != 0) throw new Exception($"git {string.Join(' ', arguments)}: {error}");
        return output.Trim();
    }

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception("Git native verification: " + message);
    }
}