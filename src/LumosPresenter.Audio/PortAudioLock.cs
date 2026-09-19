namespace LumosPresenter.Audio;

/// <summary>
/// Pa_Initialize/Pa_Terminate are refcounted but not thread-safe: two overlapping calls
/// (two console tabs polling the device list, or a poll racing capture start/stop) crash
/// the process with an access violation inside PortAudio. Every lifecycle and device-query
/// call goes through this one lock.
/// </summary>
internal static class PortAudioLock
{
    public static readonly Lock Gate = new();
}
