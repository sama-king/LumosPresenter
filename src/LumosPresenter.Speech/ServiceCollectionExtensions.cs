using LumosPresenter.Core.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LumosPresenter.Speech;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers both speech engines behind <see cref="ISpeechEngineProvider"/>. The active
    /// engine starts from "Speech:Engine" and is switchable at runtime from the operator
    /// console. Engines load their models lazily on first use.
    /// </summary>
    public static IServiceCollection AddLumosSpeech(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SpeechOptions>(configuration.GetSection(SpeechOptions.SectionName));
        services.AddSingleton<WhisperSpeechEngine>();
        services.AddSingleton<SherpaOnnxSpeechEngine>();
        services.AddSingleton<ISpeechEngineProvider, SpeechEngineProvider>();
        return services;
    }
}
