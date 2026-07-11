using LumosPresenter.Core.Domain;

namespace LumosPresenter.Core.Abstractions;

/// <summary>Lists the microphone input devices the OS exposes.</summary>
public interface IAudioDeviceEnumerator
{
    IReadOnlyList<AudioDevice> ListInputDevices();
}
