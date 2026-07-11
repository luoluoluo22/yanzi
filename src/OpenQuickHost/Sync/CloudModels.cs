using System.Text.Json.Serialization;
using System.Text.Json;

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

    public bool IsAdmin { get; init; }
}

public sealed class AppUpdateInfoResponse
{
    [JsonPropertyName("channel")]
    public string Channel { get; init; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("notes")]
    public string Notes { get; init; } = string.Empty;

    [JsonPropertyName("download_url")]
    public string DownloadUrl { get; init; } = string.Empty;

    [JsonPropertyName("file_name")]
    public string FileName { get; init; } = string.Empty;

    [JsonPropertyName("download_code")]
    public string DownloadCode { get; init; } = string.Empty;

    [JsonPropertyName("provider")]
    public string Provider { get; init; } = string.Empty;

    [JsonPropertyName("sha256")]
    public string Sha256 { get; init; } = string.Empty;

    [JsonPropertyName("published_at")]
    public string PublishedAt { get; init; } = string.Empty;

    [JsonPropertyName("updated_at")]
    public string UpdatedAt { get; init; } = string.Empty;

}

public sealed class ExtensionListResponse
{
    public IReadOnlyList<CloudExtensionRecord> Items { get; init; } = [];

    [JsonPropertyName("page")]
    public int Page { get; init; } = 1;

    [JsonPropertyName("page_size")]
    public int PageSize { get; init; }

    [JsonPropertyName("total")]
    public int Total { get; init; }

    [JsonPropertyName("total_pages")]
    public int TotalPages { get; init; } = 1;

    [JsonPropertyName("has_more")]
    public bool HasMore { get; init; }
}

public sealed class UploadIconResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; init; }

    [JsonPropertyName("extensionId")]
    public string ExtensionId { get; init; } = string.Empty;

    [JsonPropertyName("icon_url")]
    public string IconUrl { get; init; } = string.Empty;
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

    [JsonPropertyName("publisher_user_id")]
    public string PublisherUserId { get; init; } = string.Empty;

    [JsonPropertyName("publisher_username")]
    public string PublisherUsername { get; init; } = string.Empty;

    [JsonPropertyName("published_at")]
    public string PublishedAt { get; init; } = string.Empty;

    [JsonPropertyName("is_published")]
    public int IsPublished { get; init; } = 1;

    [JsonPropertyName("updated_at")]
    public string UpdatedAt { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("category")]
    public string? Category { get; init; }

    [JsonPropertyName("icon")]
    public string? Icon { get; init; }

    [JsonPropertyName("accent_hex")]
    public string? AccentHex { get; init; }

    [JsonPropertyName("keywords")]
    public IReadOnlyList<string> Keywords { get; init; } = [];

    [JsonPropertyName("install_count")]
    public int InstallCount { get; init; }
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

    [JsonPropertyName("display_name")]
    public string? DisplayName { get; init; }

    [JsonPropertyName("latest_version")]
    public string? LatestVersion { get; init; }

    [JsonPropertyName("manifest_json")]
    public string? ManifestJson { get; init; }

    [JsonPropertyName("icon")]
    public string? Icon { get; init; }

    [JsonPropertyName("archive_sha256")]
    public string? ArchiveSha256 { get; init; }

    [JsonPropertyName("has_archive")]
    public bool HasArchive { get; init; }

    [JsonPropertyName("archive_download_url")]
    public string? ArchiveDownloadUrl { get; init; }

    [JsonPropertyName("is_private")]
    public bool IsPrivate { get; init; }

    [JsonPropertyName("is_published")]
    public int IsPublished { get; init; }
}

public sealed class WebDavConfigDto
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }
    
    [JsonPropertyName("serverUrl")]
    public string? ServerUrl { get; set; }
    
    [JsonPropertyName("rootPath")]
    public string? RootPath { get; set; }
    
    [JsonPropertyName("username")]
    public string? Username { get; set; }
    
    [JsonPropertyName("password")]
    public string? Password { get; set; }

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
                ServerUrl = value;
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
                RootPath = value;
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
                Username = value;
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
                Password = value;
            }
        }
    }
}

public sealed class YanmStateResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; init; }

    [JsonPropertyName("source")]
    public string Source { get; init; } = string.Empty;

    [JsonPropertyName("updatedAtUtc")]
    public string? UpdatedAtUtc { get; init; }

    [JsonPropertyName("yanm")]
    public YanmSettings? Yanm { get; init; }

    [JsonPropertyName("changed")]
    public bool? Changed { get; init; }

    [JsonPropertyName("bytes")]
    public int Bytes { get; init; }
}

public sealed class CloudSyncObjectListResponse
{
    public bool Ok { get; init; }

    public string UserId { get; init; } = string.Empty;

    public long SinceRevision { get; init; }

    public long CurrentRevision { get; init; }

    public long CursorRevision { get; init; }

    public bool HasMore { get; init; }

    public IReadOnlyList<CloudSyncObjectRecord> Objects { get; init; } = [];
}

public sealed class CloudSyncCapabilitiesResponse
{
    public bool Ok { get; init; }

    public int ProtocolVersion { get; init; }

    public bool ObjectSyncAvailable { get; init; }

    public bool ObjectHistoryAvailable { get; init; }

    public bool ObjectsAuthoritative { get; init; }

    public bool LegacySnapshotReadSupported { get; init; }

    public bool LegacySnapshotWriteRequired { get; init; } = true;

    public int MaxObjectPayloadBytes { get; init; }
}

public sealed class CloudSyncObjectHistoryResponse
{
    public bool Ok { get; init; }

    public string UserId { get; init; } = string.Empty;

    public string ObjectId { get; init; } = string.Empty;

    public long CurrentRevision { get; init; }

    public long NextBeforeRevision { get; init; }

    public bool HasMore { get; init; }

    public IReadOnlyList<CloudSyncObjectHistoryRecord> Versions { get; init; } = [];
}

public sealed class CloudSyncObjectHistoryRecord
{
    public string ObjectId { get; init; } = string.Empty;

    public int SchemaVersion { get; init; } = 1;

    public long Revision { get; init; }

    public string UpdatedAtUtc { get; init; } = string.Empty;

    public string? UpdatedByDeviceId { get; init; }

    public string? UpdatedByDeviceName { get; init; }

    public bool Deleted { get; init; }

    public JsonElement Payload { get; init; }

    public string Operation { get; init; } = "update";

    public long? RestoredFromRevision { get; init; }
}

public sealed class CloudSyncObjectWriteResponse
{
    public bool Ok { get; init; }

    public string UserId { get; init; } = string.Empty;

    public CloudSyncObjectRecord? Object { get; init; }

    public long? RestoredFromRevision { get; init; }
}

public sealed class CloudSyncObjectRecord
{
    public string ObjectId { get; init; } = string.Empty;

    public int SchemaVersion { get; init; } = 1;

    public long Revision { get; init; }

    public string UpdatedAtUtc { get; init; } = string.Empty;

    public string? UpdatedByDeviceId { get; init; }

    public string? UpdatedByDeviceName { get; init; }

    public bool Deleted { get; init; }

    public JsonElement Payload { get; init; }
}
