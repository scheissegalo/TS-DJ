using Microsoft.Extensions.DependencyInjection;
using TS_DJ.Core.Services;

namespace TS_DJ.Audio.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTsDjAudio(this IServiceCollection services)
    {
        services.AddSingleton<IAudioPlaybackService, AudioPlaybackService>();
        return services;
    }
}
