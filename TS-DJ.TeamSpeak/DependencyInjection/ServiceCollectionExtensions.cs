using Microsoft.Extensions.DependencyInjection;
using TS_DJ.Core.Services;

namespace TS_DJ.TeamSpeak.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTsDjTeamSpeak(this IServiceCollection services)
    {
        services.AddSingleton<TeamSpeakOptions>();
        services.AddSingleton<IdentityStore>();
        services.AddSingleton<ITeamSpeakService, TeamSpeakService>();
        services.AddSingleton<TeamSpeakService>(sp => (TeamSpeakService)sp.GetRequiredService<ITeamSpeakService>());
        services.AddSingleton<TeamSpeakNicknameService>();

        return services;
    }
}
