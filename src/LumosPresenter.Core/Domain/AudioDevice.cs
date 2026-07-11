namespace LumosPresenter.Core.Domain;

/// <summary>An available microphone input device.</summary>
public sealed record AudioDevice(int Id, string Name, int MaxInputChannels, bool IsDefault);
