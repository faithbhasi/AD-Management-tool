using Microsoft.Extensions.DependencyInjection;

namespace Ilm.Modules.Leaver;

public static class LeaverModuleServiceCollectionExtensions
{
    public static IServiceCollection AddIlmLeaverModule(this IServiceCollection services)
    {
        services.AddScoped<ContainmentPlanner>();
        services.AddScoped<ContainmentExecutor>();
        services.AddScoped<LeaverTransitions>();
        services.AddScoped<NonUrgentStageService>();
        services.AddScoped<LeaverWorkflowService>();
        services.AddScoped<LeaverMaintenanceService>();
        return services;
    }
}
