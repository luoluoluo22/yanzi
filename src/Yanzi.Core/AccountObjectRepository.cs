namespace Yanzi.Core;

/// <summary>Core synchronization depends on this contract, never on a window or UI framework.</summary>
public interface IAccountObjectRepository<TPage>
{
    Task<TPage> ReadSnapshotAsync(CancellationToken cancellationToken = default);
    Task<TPage> ReadChangesAsync(long cursor, CancellationToken cancellationToken = default);
}
