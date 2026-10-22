using Microsoft.AspNetCore.Mvc;
using Nop.Core.Domain.Orders;
using Nop.Core.Http;
using Nop.Plugin.Misc.NopwaraCheckout.Models;
using Nop.Plugin.Misc.NopwaraCheckout.Services;
using Nop.Web.Framework.Components;
using Nop.Web.Models.Catalog;

namespace Nop.Plugin.Misc.NopwaraCheckout.Components;

/// <summary>
/// Renders the "Buy now" button after "Add to cart" (widget zone ProductDetailsAddInfo)
/// </summary>
public class BuyNowViewComponent : NopViewComponent
{
    #region Fields

    private readonly NopwaraCheckoutService _checkoutService;
    private readonly NopwaraCheckoutSettings _settings;
    private readonly OrderSettings _orderSettings;

    #endregion

    #region Ctor

    public BuyNowViewComponent(NopwaraCheckoutService checkoutService,
        NopwaraCheckoutSettings settings,
        OrderSettings orderSettings)
    {
        _checkoutService = checkoutService;
        _settings = settings;
        _orderSettings = orderSettings;
    }

    #endregion

    #region Methods

    public async Task<IViewComponentResult> InvokeAsync(string widgetZone, object additionalData)
    {
        //no button: buying disabled, editing an existing cart item, rentals (dates are picked on the product page
        //but checked by the cart), or no single-page checkout
        if (!_settings.ShowBuyNowButton
            || additionalData is not ProductDetailsModel.AddToCartModel addToCart
            || addToCart.DisableBuyButton
            || addToCart.UpdatedShoppingCartItemId > 0
            || addToCart.IsRental
            || _orderSettings.CheckoutDisabled
            || !await _checkoutService.IsActiveAsync())
        {
            return Content(string.Empty);
        }

        var model = new BuyNowModel
        {
            ProductId = addToCart.ProductId,
            AddToCartUrl = Url.RouteUrl(NopRouteNames.Ajax.ADD_PRODUCT_TO_CART_DETAILS,
                new { productId = addToCart.ProductId, shoppingCartTypeId = (int)ShoppingCartType.ShoppingCart }),
            CheckoutUrl = Url.RouteUrl(NopwaraCheckoutDefaults.CHECKOUT_ROUTE_NAME),
            ThemeStyled = await _checkoutService.ThemeHasBuiltInStylesAsync()
        };

        return View($"~/Plugins/{NopwaraCheckoutDefaults.PLUGIN_FOLDER}/Views/BuyNow.cshtml", model);
    }

    #endregion
}
