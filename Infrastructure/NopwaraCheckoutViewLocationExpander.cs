using Microsoft.AspNetCore.Mvc.Razor;
using Nop.Services.Themes;

namespace Nop.Plugin.Misc.NopwaraCheckout.Infrastructure;

/// <summary>
/// Lets the plugin controller return short view names and lets themes override them.
/// </summary>
/// <remarks>
/// Search order for views of the NopwaraCheckout controller:
/// 1. /Themes/{theme}/Views/Plugins/NopwaraCheckout/{view}.cshtml (theme override)
/// 2. /Plugins/Misc.NopwaraCheckout/Views/{view}.cshtml (plugin default)
/// 3. the usual locations (theme Shared, Views/Shared) - so core partials like _CreateOrUpdateAddress still resolve
/// </remarks>
public class NopwaraCheckoutViewLocationExpander : IViewLocationExpander
{
    public void PopulateValues(ViewLocationExpanderContext context)
    {
        if (!IsPluginController(context))
            return;

        //the theme name is cached per request by the core theme context (see ThemeableViewLocationExpander)
        if (context.ActionContext.HttpContext.Items.TryGetValue(NopThemeDefaults.HttpContextThemeCacheKey, out var theme))
            context.Values[NopThemeDefaults.ThemeKey] = (string)theme;
    }

    public IEnumerable<string> ExpandViewLocations(ViewLocationExpanderContext context, IEnumerable<string> viewLocations)
    {
        if (!IsPluginController(context))
            return viewLocations;

        var locations = new List<string>();
        if (context.Values.TryGetValue(NopThemeDefaults.ThemeKey, out var theme))
            locations.Add($"/Themes/{theme}/Views/Plugins/{NopwaraCheckoutDefaults.CONTROLLER_NAME}/{{0}}.cshtml");
        locations.Add($"/Plugins/{NopwaraCheckoutDefaults.PLUGIN_FOLDER}/Views/{{0}}.cshtml");

        return locations.Concat(viewLocations);
    }

    private static bool IsPluginController(ViewLocationExpanderContext context)
    {
        return string.Equals(context.ControllerName, NopwaraCheckoutDefaults.CONTROLLER_NAME, StringComparison.OrdinalIgnoreCase);
    }
}
