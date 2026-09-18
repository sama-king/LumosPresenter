using System.Runtime.InteropServices;
using LumosPresenter.Core.Abstractions;
using LumosPresenter.Core.Domain;
using PortAudioSharp;

namespace LumosPresenter.Audio;

/// <summary>
/// Lists input devices via PortAudio. Pa_Initialize/Pa_Terminate are refcounted, so this is safe alongside an active capture.
///
/// On Windows PortAudio exposes every microphone once per host API (MME, DirectSound, WASAPI,
/// WDM-KS), so one mic array shows up half a dozen times — and the WASAPI and WDM-KS copies
/// cannot open at our 16 kHz mono format. Only DirectSound devices are listed there: one entry
/// per mic, full names, and Windows resamples to whatever rate we ask for. Platforms without
/// DirectSound (macOS, Linux) list everything, as they only have one host API in practice.
/// </summary>
public sealed class PortAudioDeviceEnumerator : IAudioDeviceEnumerator
{
    private const int DirectSoundTypeId = 1; // paDirectSound in PaHostApiTypeId

    public IReadOnlyList<AudioDevice> ListInputDevices()
    {
        lock (PortAudioLock.Gate)
        {
            return ListInputDevicesLocked();
        }
    }

    private static List<AudioDevice> ListInputDevicesLocked()
    {
        PortAudio.Initialize();
        try
        {
            var defaultInput = PortAudio.DefaultInputDevice;
            // Negative (paHostApiNotFound) where DirectSound isn't compiled in, i.e. off Windows.
            var directSound = DirectSoundIndex();
            if (directSound >= 0)
            {
                var hostInfo = Marshal.PtrToStructure<HostApiInfo>(Pa_GetHostApiInfo(directSound));
                defaultInput = hostInfo.defaultInputDevice;
            }

            var devices = new List<AudioDevice>();
            for (var i = 0; i < PortAudio.DeviceCount; i++)
            {
                var info = PortAudio.GetDeviceInfo(i);
                if (info.maxInputChannels <= 0)
                {
                    continue; // output-only device
                }
                if (directSound >= 0 && info.hostApi != directSound)
                {
                    continue; // the same mic again through another host API
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

    [StructLayout(LayoutKind.Sequential)]
    private struct HostApiInfo
    {
        public int structVersion;
        public int type;
        public IntPtr name;
        public int deviceCount;
        public int defaultInputDevice;
        public int defaultOutputDevice;
    }

    /// <summary>DirectSound's host API index, or -1 where it isn't available.</summary>
    private static int DirectSoundIndex()
    {
        try
        {
            return Pa_HostApiTypeIdToHostApiIndex(DirectSoundTypeId);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return -1; // the wrapper resolved the native library some other way; list everything
        }
    }

    [DllImport("portaudio")]
    private static extern int Pa_HostApiTypeIdToHostApiIndex(int type);

    [DllImport("portaudio")]
    private static extern IntPtr Pa_GetHostApiInfo(int hostApi);
}
