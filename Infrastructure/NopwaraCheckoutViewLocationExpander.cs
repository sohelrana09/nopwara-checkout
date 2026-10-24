using Microsoft.AspNetCore.Mvc.Razor;

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
    //the keys core 4.90 uses to cache the theme name (protected constants of ThemeableViewLocationExpander)
    private const string THEME_KEY = "nop.themename";
    private const string HTTP_CONTEXT_THEME_CACHE_KEY = "http-context-theme-cache-key";

    public void PopulateValues(ViewLocationExpanderContext context)
    {
        if (!IsPluginController(context))
            return;

        //the theme name is cached per request by the core theme context (see ThemeableViewLocationExpander)
        if (context.ActionContext.HttpContext.Items.TryGetValue(HTTP_CONTEXT_THEME_CACHE_KEY, out var theme))
            context.Values[THEME_KEY] = (string)theme;
    }

    public IEnumerable<string> ExpandViewLocations(ViewLocationExpanderContext context, IEnumerable<string> viewLocations)
    {
        if (!IsPluginController(context))
            return viewLocations;

        var locations = new List<string>();
        if (context.Values.TryGetValue(THEME_KEY, out var theme))
            locations.Add($"/Themes/{theme}/Views/Plugins/{NopwaraCheckoutDefaults.CONTROLLER_NAME}/{{0}}.cshtml");
        locations.Add($"/Plugins/{NopwaraCheckoutDefaults.PLUGIN_FOLDER}/Views/{{0}}.cshtml");

        return locations.Concat(viewLocations);
    }

    private static bool IsPluginController(ViewLocationExpanderContext context)
    {
        return string.Equals(context.ControllerName, NopwaraCheckoutDefaults.CONTROLLER_NAME, StringComparison.OrdinalIgnoreCase);
    }
}
