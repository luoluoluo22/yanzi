using System.Text;
using System.Text.Json;
using OpenQuickHost;
using OpenQuickHost.Sync;

internal static class AccountExtensionBridgeVerification
{
    public static async Task RunAsync(string[] args)
    {
        var operation = Require(args, "--operation").ToLowerInvariant();
        var existingRoot = Optional(args, "--existing-root");
        var root = Path.GetFullPath(
            string.IsNullOrWhiteSpace(existingRoot)
                ? Require(args, "--root")
                : existingRoot);
        if (string.IsNullOrWhiteSpace(existingRoot))
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
            Directory.CreateDirectory(root);
        }
        else if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException(
                $"Existing Yanzi data root was not found: {root}");
        }

        using var scope = string.IsNullOrWhiteSpace(existingRoot)
            ? HostAssets.UseIsolatedDataRootForVerification(root)
            : HostAssets.UseExistingDataRootForVerification(root);

        if (string.IsNullOrWhiteSpace(existingRoot))
        {
            var baseUrl = Require(args, "--base-url");
            var token = Require(args, "--token");
            var userId = Require(args, "--user-id");
            WriteSyncConfig(baseUrl);
            WriteSession(token, userId);
        }

        if (operation == "list-test")
        {
            var client = new CloudSyncClient(SyncConfigLoader.Load());
            var response = await client.GetSyncObjectsAsync();
            var prefixes = new[]
            {
                "diag-readback-",
                "diag-primary-read-",
                "real-account-roundtrip-",
                "real-account-storage-diag-"
            };

            var items = response.Objects
                .Select(record =>
                {
                    var extensionId = record.Payload.TryGetProperty("extensionId", out var extensionIdValue)
                        ? extensionIdValue.GetString() ?? string.Empty
                        : string.Empty;
                    var key = record.Payload.TryGetProperty("key", out var keyValue)
                        ? keyValue.GetString() ?? string.Empty
                        : string.Empty;
                    return new
                    {
                        record.ObjectId,
                        record.Revision,
                        record.Deleted,
                        ExtensionId = extensionId,
                        Key = key
                    };
                })
                .Where(item =>
                    prefixes.Any(prefix =>
                        item.ExtensionId.StartsWith(
                            prefix,
                            StringComparison.Ordinal)))
                .ToArray();

            Print(new
            {
                operation,
                count = items.Length,
                items
            });
            return;
        }

        var extensionId = Require(args, "--extension-id");
        var key = Require(args, "--key");

        if (operation == "write")
        {
            var contentFile = Optional(args, "--content-file");
            var content = contentFile == null ? Require(args, "--content") : await File.ReadAllTextAsync(contentFile);
            var expectedText = Optional(args, "--expected-revision");
            long? expectedRevision = string.IsNullOrWhiteSpace(expectedText)
                ? null
                : long.Parse(expectedText);

            var result = await AccountExtensionDataStore.WriteAsync(
                extensionId,
                key,
                content,
                expectedRevision);

            Print(new
            {
                operation,
                result.Available,
                result.ObjectId,
                result.Revision,
                content
            });
            return;
        }

        if (operation == "read")
        {
            var result = await AccountExtensionDataStore.TryReadAsync(
                extensionId,
                key);

            Print(new
            {
                operation,
                result.Available,
                result.Exists,
                result.ObjectId,
                result.Revision,
                result.Content
            });
            return;
        }

        if (operation == "delete")
        {
            var expectedText = Optional(args, "--expected-revision");
            long? expectedRevision = string.IsNullOrWhiteSpace(expectedText)
                ? null
                : long.Parse(expectedText);

            var result = await AccountExtensionDataStore.DeleteAsync(
                extensionId,
                key,
                expectedRevision);

            Print(new
            {
                operation,
                result.Available,
                result.ObjectId,
                result.Revision
            });
            return;
        }

        throw new ArgumentException(
            $"Unsupported --operation '{operation}'. Expected write, read, delete, or list-test.");
    }

    private static void WriteSyncConfig(string baseUrl)
    {
        var json = JsonSerializer.Serialize(
            new SyncOptions { BaseUrl = baseUrl },
            JsonOptions);
        File.WriteAllText(
            SyncConfigLoader.ConfigPath,
            json,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static void WriteSession(string token, string userId)
    {
        SyncSessionStore.Save(new SyncSession
        {
            AccessToken = token,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(20).ToUnixTimeSeconds(),
            UserId = userId,
            Username = "cross-platform-extension-test"
        });
    }

    private static string Require(string[] args, string name)
    {
        var value = Optional(args, name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"Missing required option: {name}");
        }
        return value;
    }

    private static string? Optional(string[] args, string name)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }
        return null;
    }

    private static void Print(object value)
    {
        Console.WriteLine(
            "RESULT_JSON:" + JsonSerializer.Serialize(value, JsonOptions));
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
