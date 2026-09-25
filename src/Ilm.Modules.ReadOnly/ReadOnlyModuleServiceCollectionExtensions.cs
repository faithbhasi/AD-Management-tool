using Microsoft.Extensions.DependencyInjection;

namespace Ilm.Modules.ReadOnly;

public static class ReadOnlyModuleServiceCollectionExtensions
{
    public static IServiceCollection AddIlmReadOnlyModule(this IServiceCollection services)
    {
        services.AddScoped<DirectoryViewService>();
        services.AddScoped<PersonViewService>();
        return services;
    }
}
