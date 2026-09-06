using LumosPresenter.Core.Abstractions;
using LumosPresenter.Core.Domain;

namespace LumosPresenter.Tests.Data;

/// <summary>
/// A remote source that is never configured, so tests exercise the local data layer
/// without touching the network. Mirrors how the app behaves with no API key set.
/// </summary>
internal sealed class OfflineScriptureSource : IRemoteScriptureSource
{
    public IReadOnlyList<Translation> Translations { get; init; } = [];

    public bool IsConfigured => false;

    public Task<RemoteChapter?> GetChapterAsync(
        string translationId, int bookNumber, int chapter, CancellationToken cancellationToken = default) =>
        Task.FromResult<RemoteChapter?>(null);
}
