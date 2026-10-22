using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nop.Core.Infrastructure;
using Nop.Plugin.Misc.NopwaraCheckout.Factories;
using Nop.Plugin.Misc.NopwaraCheckout.Services;

namespace Nop.Plugin.Misc.NopwaraCheckout.Infrastructure;

/// <summary>
/// Represents object for the configuring services on application startup
/// </summary>
public class PluginNopStartup : INopStartup
{
    /// <summary>
    /// Add and configure any of the middleware
    /// </summary>
    /// <param name="services">Collection of service descriptors</param>
    /// <param name="configuration">Configuration of the application</param>
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<NopwaraCheckoutService>();
        services.AddScoped<NopwaraCheckoutModelFactory>();
        services.AddScoped<NopwaraCheckoutRedirectFilter>();

        //send customers from the core checkout pages to the single-page checkout (when active)
        services.Configure<MvcOptions>(options => options.Filters.AddService<NopwaraCheckoutRedirectFilter>());

        //short view names for the storefront controller + theme overrides
        services.Configure<RazorViewEngineOptions>(options => options.ViewLocationExpanders.Add(new NopwaraCheckoutViewLocationExpander()));
    }

    /// <summary>
    /// Configure the using of added middleware
    /// </summary>
    /// <param name="application">Builder for configuring an application's request pipeline</param>
    public void Configure(IApplicationBuilder application)
    {
    }

    /// <summary>
    /// Gets order of this startup configuration implementation
    /// </summary>
    public int Order => 3000;
}
