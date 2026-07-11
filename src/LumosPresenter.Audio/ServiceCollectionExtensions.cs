using LumosPresenter.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LumosPresenter.Audio;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers PortAudio microphone capture and device enumeration.</summary>
    public static IServiceCollection AddLumosAudio(this IServiceCollection services)
    {
        services.AddSingleton<IAudioCapture, PortAudioCapture>();
        services.AddSingleton<IAudioDeviceEnumerator, PortAudioDeviceEnumerator>();
        return services;
    }
}
