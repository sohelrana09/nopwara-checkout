using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Nop.Core;
using Nop.Core.Domain.Orders;
using Nop.Plugin.Misc.NopwaraCheckout.Services;
using Nop.Services.Orders;

namespace Nop.Plugin.Misc.NopwaraCheckout.Infrastructure;

/// <summary>
/// Sends customers from the core checkout pages to the single-page checkout.
/// </summary>
/// <remarks>
/// Checkout.Index still runs first (core validation, ResetCheckoutDataAsync) and then redirects to
/// one-page or multistep checkout; those GET actions are intercepted here. When the plugin is disabled
/// or the current theme isn't allowed, nothing happens and the core checkout is used.
/// With "Skip shopping cart" on, GET ShoppingCart.Cart opens the checkout too, unless the cart is empty or
/// the customer can't check out (then the cart page explains why / core sends guests to the login page).
/// </remarks>
public class NopwaraCheckoutRedirectFilter : IAsyncActionFilter
{
    #region Fields

    private static readonly HashSet<string> _checkoutActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "OnePageCheckout",
        "BillingAddress",
        "ShippingAddress",
        "ShippingMethod",
        "PaymentMethod",
        "PaymentInfo",
        "Confirm"
    };

    private readonly IShoppingCartService _shoppingCartService;
    private readonly IStoreContext _storeContext;
    private readonly IWorkContext _workContext;
    private readonly NopwaraCheckoutService _checkoutService;
    private readonly NopwaraCheckoutSettings _settings;

    #endregion

    #region Ctor

    public NopwaraCheckoutRedirectFilter(IShoppingCartService shoppingCartService,
        IStoreContext storeContext,
        IWorkContext workContext,
        NopwaraCheckoutService checkoutService,
        NopwaraCheckoutSettings settings)
    {
        _shoppingCartService = shoppingCartService;
        _storeContext = storeContext;
        _workContext = workContext;
        _checkoutService = checkoutService;
        _settings = settings;
    }

    #endregion

    #region Utilities

    /// <summary>
    /// Gets a value indicating whether the cart page should be skipped for the current customer
    /// </summary>
    protected virtual async Task<bool> ShouldSkipCartAsync()
    {
        if (!_settings.SkipShoppingCart || !await _checkoutService.IsActiveAsync())
            return false;

        var customer = await _workContext.GetCurrentCustomerAsync();
        var store = await _storeContext.GetCurrentStoreAsync();
        var cart = await _shoppingCartService.GetShoppingCartAsync(customer, ShoppingCartType.ShoppingCart, store.Id);

        //checkout disabled, anonymous checkout not allowed, ...: keep the cart page (and core's own handling of it)
        if (!cart.Any() || !string.IsNullOrEmpty(await _checkoutService.ValidateCanCheckoutAsync(customer, cart)))
            return false;

        //the checkout page sends carts with warnings back to the cart page, where they are shown
        return !(await _checkoutService.GetCartWarningsAsync(customer, cart)).Any();
    }

    #endregion

    #region Methods

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (HttpMethods.IsGet(context.HttpContext.Request.Method) && context.ActionDescriptor is ControllerActionDescriptor descriptor)
        {
            var redirect = descriptor.ControllerName.Equals("Checkout", StringComparison.OrdinalIgnoreCase)
                ? _checkoutActions.Contains(descriptor.ActionName) && await _checkoutService.IsActiveAsync()
                : descriptor.ControllerName.Equals("ShoppingCart", StringComparison.OrdinalIgnoreCase)
                    && descriptor.ActionName.Equals("Cart", StringComparison.OrdinalIgnoreCase)
                    && await ShouldSkipCartAsync();

            if (redirect)
            {
                context.Result = new RedirectToRouteResult(NopwaraCheckoutDefaults.CHECKOUT_ROUTE_NAME, null);
                return;
            }
        }

        await next();
    }

    #endregion
}
