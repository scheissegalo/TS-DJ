using Microsoft.Extensions.DependencyInjection;
using TS_DJ.Audio.Media;
using TS_DJ.Core.Services;
using TS_DJ.Audio.Mixing;
using TS_DJ.Audio.Playback;
using TSLib.Helper;

namespace TS_DJ.Audio.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTsDjAudio(this IServiceCollection services)
    {
        services.AddSingleton<MediaLoadTimingTracker>();
        services.AddSingleton<IMediaPlaybackLoader, MediaPlaybackLoader>();
        services.AddSingleton<MediaLoadPrefetchCache>(sp =>
            new MediaLoadPrefetchCache(
                sp.GetRequiredService<IMediaPlaybackLoader>(),
                sp.GetService<Microsoft.Extensions.Logging.ILogger<MediaLoadPrefetchCache>>()));
        services.AddSingleton<AudioMixerService>(sp =>
        {
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AudioMixerService>>();
            var musicLogger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Mixing.Sources.MusicTrackSource>>();
            var soundboardLogger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Mixing.Sources.SoundEffectSource>>();
            var outputLogger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<MixerOutputProducer>>();
            var teamSpeak = sp.GetRequiredService<TeamSpeak.TeamSpeakService>();
            return new AudioMixerService(
                logger,
                musicLogger,
                soundboardLogger,
                outputLogger,
                teamSpeak,
                new Id(2),
                sp.GetRequiredService<IMediaPlaybackLoader>(),
                sp.GetService<IMediaSourceRegistry>(),
                sp.GetService<MediaLoadPrefetchCache>(),
                sp.GetService<MediaLoadTimingTracker>());
        });
        services.AddSingleton<IAudioMixerService>(sp => sp.GetRequiredService<AudioMixerService>());
        services.AddSingleton<IAudioPlaybackService, AudioPlaybackService>();
        services.AddSingleton<SoundboardService>();
        services.AddSingleton<ISoundboardService>(sp => sp.GetRequiredService<SoundboardService>());
        return services;
    }
}
