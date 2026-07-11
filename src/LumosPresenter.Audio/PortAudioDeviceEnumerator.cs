using LumosPresenter.Core.Abstractions;
using LumosPresenter.Core.Domain;
using PortAudioSharp;

namespace LumosPresenter.Audio;

/// <summary>Lists input devices via PortAudio. Pa_Initialize/Pa_Terminate are refcounted, so this is safe alongside an active capture.</summary>
public sealed class PortAudioDeviceEnumerator : IAudioDeviceEnumerator
{
    public IReadOnlyList<AudioDevice> ListInputDevices()
    {
        PortAudio.Initialize();
        try
        {
            var defaultInput = PortAudio.DefaultInputDevice;
            var devices = new List<AudioDevice>();
            for (var i = 0; i < PortAudio.DeviceCount; i++)
            {
                var info = PortAudio.GetDeviceInfo(i);
                if (info.maxInputChannels <= 0)
                {
                    continue; // output-only device
                }
                devices.Add(new AudioDevice(i, info.name, info.maxInputChannels, i == defaultInput));
            }
            return devices;
        }
        finally
        {
            PortAudio.Terminate();
        }
    }
}
