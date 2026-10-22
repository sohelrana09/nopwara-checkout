using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Nop.Web.Framework;
using Nop.Web.Framework.Mvc.Routing;
using Nop.Web.Infrastructure;

namespace Nop.Plugin.Misc.NopwaraCheckout.Infrastructure;

/// <summary>
/// Represents plugin route provider
/// </summary>
public class RouteProvider : BaseRouteProvider, IRouteProvider
{
    /// <summary>
    /// Register routes
    /// </summary>
    /// <param name="endpointRouteBuilder">Route builder</param>
    public void RegisterRoutes(IEndpointRouteBuilder endpointRouteBuilder)
    {
        endpointRouteBuilder.MapControllerRoute(name: NopwaraCheckoutDefaults.CONFIGURATION_ROUTE_NAME,
            pattern: "Admin/NopwaraCheckout/Configure",
            defaults: new { controller = "NopwaraCheckoutAdmin", action = "Configure", area = AreaNames.ADMIN });

        var lang = GetLanguageRoutePattern();

        //the checkout page
        endpointRouteBuilder.MapControllerRoute(name: NopwaraCheckoutDefaults.CHECKOUT_ROUTE_NAME,
            pattern: $"{lang}/checkout/onepage/",
            defaults: new { controller = NopwaraCheckoutDefaults.CONTROLLER_NAME, action = "Index" });

        //redirection payments are completed by a regular GET after the order is placed (an ajax call can't redirect)
        endpointRouteBuilder.MapControllerRoute(name: NopwaraCheckoutDefaults.COMPLETE_REDIRECTION_PAYMENT_ROUTE_NAME,
            pattern: $"{lang}/checkout/onepage/complete-payment/",
            defaults: new { controller = NopwaraCheckoutDefaults.CONTROLLER_NAME, action = "CompleteRedirectionPayment" });

        //ajax endpoints: /checkout/onepage/{action}
        endpointRouteBuilder.MapControllerRoute(name: "Plugin.Misc.NopwaraCheckout.Ajax",
            pattern: $"{lang}/checkout/onepage/{{action}}/",
            defaults: new { controller = NopwaraCheckoutDefaults.CONTROLLER_NAME });
    }

    /// <summary>
    /// Gets a priority of route provider
    /// </summary>
    public int Priority => 0;
}
