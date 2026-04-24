using System.Text.Json.Serialization;

namespace OpenQuickHost.Sync;

public sealed class HealthResponse
{
    public bool Ok { get; init; }

    public string? Now { get; init; }
}

public sealed class AuthResponse
{
    public string AccessToken { get; init; } = string.Empty;

    public long ExpiresAt { get; init; }

    public string UserId { get; init; } = string.Empty;

    public string Username { get; init; } = string.Empty;

    public string? Email { get; init; }
}

public sealed class SendCodeResponse
{
    public bool Ok { get; init; }

    public string Email { get; init; } = string.Empty;

    public int ExpiresInSeconds { get; init; }

    public string? PreviewCode { get; init; }
}

public sealed class AuthMeResponse
{
    public string UserId { get; init; } = string.Empty;

    public string Username { get; init; } = string.Empty;

    public string? Email { get; init; }
}

public sealed class ExtensionListResponse
{
    public IReadOnlyList<CloudExtensionRecord> Items { get; init; } = [];
}

public sealed class CloudExtensionRecord
{
    [JsonPropertyName("extension_id")]
    public string ExtensionId { get; init; } = string.Empty;

    [JsonPropertyName("display_name")]
    public string DisplayName { get; init; } = string.Empty;

    [JsonPropertyName("latest_version")]
    public string? LatestVersion { get; init; }

    [JsonPropertyName("manifest_json")]
    public string? ManifestJson { get; init; }

    [JsonPropertyName("archive_key")]
    public string? ArchiveKey { get; init; }

    [JsonPropertyName("archive_sha256")]
    public string? ArchiveSha256 { get; init; }

    [JsonPropertyName("updated_at")]
    public string UpdatedAt { get; init; } = string.Empty;
}

public sealed class UserExtensionListResponse
{
    public string UserId { get; init; } = string.Empty;

    public IReadOnlyList<UserExtensionRecord> Items { get; init; } = [];
}

public sealed class UserExtensionRecord
{
    [JsonPropertyName("user_id")]
    public string UserId { get; init; } = string.Empty;

    [JsonPropertyName("extension_id")]
    public string ExtensionId { get; init; } = string.Empty;

    [JsonPropertyName("installed_version")]
    public string InstalledVersion { get; init; } = string.Empty;

    [JsonPropertyName("enabled")]
    public int Enabled { get; init; }

    [JsonPropertyName("settings_json")]
    public string SettingsJson { get; init; } = string.Empty;

    [JsonPropertyName("updated_at")]
    public string UpdatedAt { get; init; } = string.Empty;
}
