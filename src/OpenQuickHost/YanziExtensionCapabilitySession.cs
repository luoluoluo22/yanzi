using System.IO;
using System.Text.Json;

namespace OpenQuickHost;

/// <summary>A capability lease owned by one loaded C# runtime, using JSON across assembly boundaries.</summary>
internal sealed class YanziExtensionCapabilitySession : IDisposable
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, YanziExtensionCapabilitySession> Owners = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly CommandItem _command;
    private readonly Dictionary<string, YanziCapabilityDeclaration> _declarations;
    private bool _disposed;

    public YanziExtensionCapabilitySession(CommandItem command)
    {
        _command = command;
        var manifestPath = Path.Combine(command.ExtensionDirectoryPath!, "manifest.json");
        var manifest = File.Exists(manifestPath)
            ? JsonSerializer.Deserialize<YanziCapabilityManifest>(File.ReadAllText(manifestPath), JsonOptions) : null;
        _declarations = (manifest?.Provides ?? []).ToDictionary(x => x.Name.Trim(), StringComparer.OrdinalIgnoreCase);
    }

    public void Register(string name, Func<string, Task<string>> handler)
    {
        lock (Gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_declarations.TryGetValue(name.Trim(), out var declaration))
                throw new InvalidOperationException($"能力未在 provides 中声明：{name}");
            if (YanziCapabilityRegistry.TryGet(name, out var existing) && existing!.ProviderExtensionId != _command.ExtensionId)
                throw new InvalidOperationException($"能力已由其他提供者注册：{name}");
            YanziCapabilityProviderSdk.RegisterProvider(_command.ExtensionId, new[]
            {
                new YanziCapabilityProviderDefinition
                {
                    Name = declaration.Name, Description = declaration.Description, Version = declaration.Version,
                    InputSchema = declaration.InputSchema, OutputSchema = declaration.OutputSchema, Permissions = declaration.Permissions,
                    Handler = async payload =>
                    {
                        lock (Gate) ObjectDisposedException.ThrowIf(_disposed, this);
                        var json = await handler(JsonSerializer.Serialize(payload, JsonOptions)).ConfigureAwait(false);
                        using var document = JsonDocument.Parse(json);
                        var output = document.RootElement.Clone();
                        YanziCapabilitySchema.Validate(declaration.OutputSchema, output);
                        return output;
                    }
                }
            });
            Owners[_command.ExtensionId] = this;
        }
    }

    public Task<string> Invoke(string requestJson)
    {
        lock (Gate) ObjectDisposedException.ThrowIf(_disposed, this);
        return YanziCapabilityInvocationService.InvokeJsonAsync(requestJson,
            new YanziCapabilityCaller(_command.ExtensionId, _command.Permissions));
    }

    public void Dispose()
    {
        lock (Gate)
        {
            _disposed = true;
            if (Owners.TryGetValue(_command.ExtensionId, out var owner) && ReferenceEquals(owner, this))
            {
                Owners.Remove(_command.ExtensionId);
                YanziCapabilityRuntimeRegistry.RemoveByExtension(_command.ExtensionId);
            }
        }
    }
}
