using System.Text;

namespace OpenQuickHost.Sync;

internal static class CloudSyncDiagnostics
{
    public static void Log(string area, string message, params (string Key, object? Value)[] fields)
    {
        var builder = new StringBuilder();
        builder.Append('[').Append(area).Append("] ").Append(message);
        if (fields.Length > 0)
        {
            builder.Append(" | ");
            for (var index = 0; index < fields.Length; index++)
            {
                if (index > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(fields[index].Key)
                    .Append('=')
                    .Append(FormatValue(fields[index].Value));
            }
        }

        HostAssets.AppendCloudSyncDiagnosticLog(builder.ToString());
    }

    public static string DescribePersonalSync(PersonalSyncSettings? settings, PersonalSyncSecretBag? secrets)
    {
        settings ??= new PersonalSyncSettings();
        secrets ??= new PersonalSyncSecretBag();
        return string.Join(
            ", ",
            [
                $"provider={PersonalSyncProviders.Normalize(settings.Provider)}",
                $"enabled={settings.Enabled}",
                $"githubToken={!string.IsNullOrWhiteSpace(secrets.GitHubToken)}",
                $"giteeToken={!string.IsNullOrWhiteSpace(secrets.GiteeToken)}",
                $"gitlabToken={!string.IsNullOrWhiteSpace(secrets.GitLabToken)}",
                $"giteaToken={!string.IsNullOrWhiteSpace(secrets.GiteaToken)}",
                $"s3Secret={!string.IsNullOrWhiteSpace(secrets.S3SecretAccessKey)}",
                $"webdavPassword={!string.IsNullOrWhiteSpace(secrets.WebDavPassword)}",
                $"giteeRepo={Safe(settings.Gitee.Repo)}",
                $"giteeBranch={Safe(settings.Gitee.Branch)}",
                $"giteeUser={Safe(settings.Gitee.Username)}",
                $"giteePathPrefix={Safe(settings.Gitee.PathPrefix)}"
            ]);
    }

    public static string DescribeAuthState(CloudSyncClient? client)
    {
        return client == null
            ? "client=null"
            : $"hasCredential={client.HasCredential}, user={Safe(client.CurrentUserLabel)}";
    }

    private static string FormatValue(object? value)
    {
        return value switch
        {
            null => "null",
            bool boolValue => boolValue ? "true" : "false",
            string stringValue => Safe(stringValue),
            _ => Safe(value.ToString())
        };
    }

    private static string Safe(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "-";
        }

        return value
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }
}
