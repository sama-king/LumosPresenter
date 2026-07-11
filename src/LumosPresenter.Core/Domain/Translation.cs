namespace LumosPresenter.Core.Domain;

/// <summary>A Bible translation available in the local store (bundled or imported).</summary>
public sealed record Translation(string Id, string Name, string Language);
