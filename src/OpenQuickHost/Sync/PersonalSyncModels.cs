using System.Text.Json.Serialization;

namespace OpenQuickHost.Sync;

public static class PersonalSyncProviders
{
    public const string None = "none";
    public const string WebDav = "webdav";
    public const string GitHub = "github";
    public const string Gitee = "gitee";
    public const string GitLab = "gitlab";
    public const string Gitea = "gitea";
    public const string S3 = "s3";

    public static readonly string[] All =
    [
        None,
        GitHub,
        Gitee,
        GitLab,
        Gitea,
        S3,
        WebDav
    ];

    public static string Normalize(string? value)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            GitHub => GitHub,
            Gitee => Gitee,
            GitLab => GitLab,
            Gitea => Gitea,
            S3 => S3,
            WebDav => WebDav,
            _ => None
        };
    }

    public static string GetDisplayName(string? value)
    {
        return Normalize(value) switch
        {
            GitHub => "GitHub",
            Gitee => "Gitee",
            GitLab => "GitLab",
            Gitea => "Gitea",
            S3 => "S3",
            WebDav => "WebDAV",
            _ => "未启用"
        };
    }
}

public sealed class PersonalSyncSettings
{
    public bool Enabled { get; set; }

    public string Provider { get; set; } = PersonalSyncProviders.WebDav;

    public PersonalSyncGitHubConfig GitHub { get; set; } = new();

    public PersonalSyncGiteeConfig Gitee { get; set; } = new();

    public PersonalSyncGitLabConfig GitLab { get; set; } = new();

    public PersonalSyncGiteaConfig Gitea { get; set; } = new();

    public PersonalSyncS3Config S3 { get; set; } = new();

    public PersonalSyncWebDavConfig WebDav { get; set; } = new();
}

public sealed class PersonalSyncGitHubConfig
{
    public string Username { get; set; } = string.Empty;

    public string Repo { get; set; } = "yanzi-sync";

    public string Branch { get; set; } = "main";

    public string PathPrefix { get; set; } = string.Empty;
}

public sealed class PersonalSyncGiteeConfig
{
    public string Username { get; set; } = string.Empty;

    public string Repo { get; set; } = "yanzi-sync";

    public string Branch { get; set; } = "master";

    public string PathPrefix { get; set; } = string.Empty;
}

public sealed class PersonalSyncGitLabConfig
{
    public string BaseUrl { get; set; } = "https://gitlab.com";

    public string ProjectPath { get; set; } = string.Empty;

    public string Branch { get; set; } = "main";

    public string PathPrefix { get; set; } = string.Empty;
}

public sealed class PersonalSyncGiteaConfig
{
    public string BaseUrl { get; set; } = "https://gitea.com";

    public string Username { get; set; } = string.Empty;

    public string Repo { get; set; } = "yanzi-sync";

    public string Branch { get; set; } = "main";

    public string PathPrefix { get; set; } = string.Empty;
}

public sealed class PersonalSyncS3Config
{
    public string AccessKeyId { get; set; } = string.Empty;

    public string Region { get; set; } = string.Empty;

    public string Bucket { get; set; } = string.Empty;

    public string Endpoint { get; set; } = string.Empty;

    public string PathPrefix { get; set; } = string.Empty;
}

public sealed class PersonalSyncWebDavConfig
{
    public string Url { get; set; } = "https://dav.jianguoyun.com/dav/";

    public string Username { get; set; } = string.Empty;

    public string PathPrefix { get; set; } = "/yanzi";
}

public sealed class PersonalSyncSecretBag
{
    public string GitHubToken { get; set; } = string.Empty;

    public string GiteeToken { get; set; } = string.Empty;

    public string GitLabToken { get; set; } = string.Empty;

    public string GiteaToken { get; set; } = string.Empty;

    public string WebDavPassword { get; set; } = string.Empty;

    public string S3SecretAccessKey { get; set; } = string.Empty;
}

public sealed class CloudPersonalSyncConfigSnapshot
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("provider")]
    public string Provider { get; set; } = PersonalSyncProviders.WebDav;

    [JsonPropertyName("settings")]
    public PersonalSyncSettings Settings { get; set; } = new();

    [JsonPropertyName("secrets")]
    public PersonalSyncSecretBag Secrets { get; set; } = new();

    [JsonPropertyName("autoSyncDelaySeconds")]
    public int AutoSyncDelaySeconds { get; set; } = 10;

    [JsonPropertyName("enableWebDavSync")]
    public bool? LegacyEnabled
    {
        get => null;
        set
        {
            if (value.HasValue)
            {
                Enabled = value.Value;
            }
        }
    }

    [JsonPropertyName("webDavServerUrl")]
    public string? LegacyServerUrl
    {
        get => null;
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                Settings.WebDav.Url = value;
            }
        }
    }

    [JsonPropertyName("serverUrl")]
    public string? LegacyShortServerUrl
    {
        get => null;
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                Settings.WebDav.Url = value;
            }
        }
    }

    [JsonPropertyName("webDavRootPath")]
    public string? LegacyRootPath
    {
        get => null;
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                Settings.WebDav.PathPrefix = value;
            }
        }
    }

    [JsonPropertyName("rootPath")]
    public string? LegacyShortRootPath
    {
        get => null;
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                Settings.WebDav.PathPrefix = value;
            }
        }
    }

    [JsonPropertyName("webDavUsername")]
    public string? LegacyUsername
    {
        get => null;
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                Settings.WebDav.Username = value;
            }
        }
    }

    [JsonPropertyName("username")]
    public string? LegacyShortUsername
    {
        get => null;
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                Settings.WebDav.Username = value;
            }
        }
    }

    [JsonPropertyName("webDavPassword")]
    public string? LegacyPassword
    {
        get => null;
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                Secrets.WebDavPassword = value;
            }
        }
    }

    [JsonPropertyName("password")]
    public string? LegacyShortPassword
    {
        get => null;
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                Secrets.WebDavPassword = value;
            }
        }
    }
}
