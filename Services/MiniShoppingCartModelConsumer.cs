using Nop.Core;
using Nop.Core.Domain.Orders;
using Nop.Services.Events;
using Nop.Services.Orders;
using Nop.Web.Framework.Events;
using Nop.Web.Framework.Models;
using Nop.Web.Models.ShoppingCart;

namespace Nop.Plugin.Misc.NopwaraCheckout.Services;

/// <summary>
/// Shows the mini shopping cart's "Checkout" button when the single-page checkout is used.
/// </summary>
/// <remarks>
/// Core hides it when the store has checkout attributes or terms of service on the cart page, because those are
/// filled in on the cart page; the single-page checkout shows checkout attributes itself. The other core conditions
/// (minimum order subtotal, customers who can't check out) still hide it.
/// </remarks>
public class MiniShoppingCartModelConsumer : IConsumer<ModelPreparedEvent<BaseNopModel>>
{
    #region Fields

    private readonly IOrderProcessingService _orderProcessingService;
    private readonly IShoppingCartService _shoppingCartService;
    private readonly IStoreContext _storeContext;
    private readonly IWorkContext _workContext;
    private readonly NopwaraCheckoutService _checkoutService;
    private readonly NopwaraCheckoutSettings _settings;

    #endregion

    #region Ctor

    public MiniShoppingCartModelConsumer(IOrderProcessingService orderProcessingService,
        IShoppingCartService shoppingCartService,
        IStoreContext storeContext,
        IWorkContext workContext,
        NopwaraCheckoutService checkoutService,
        NopwaraCheckoutSettings settings)
    {
        _orderProcessingService = orderProcessingService;
        _shoppingCartService = shoppingCartService;
        _storeContext = storeContext;
        _workContext = workContext;
        _checkoutService = checkoutService;
        _settings = settings;
    }

    #endregion

    #region Methods

    public async Task HandleEventAsync(ModelPreparedEvent<BaseNopModel> eventMessage)
    {
        if (eventMessage.Model is not MiniShoppingCartModel model || model.DisplayCheckoutButton || model.TotalProducts == 0)
            return;

        //checkout attributes on the cart page only: keep core's rule
        if (!_settings.ShowCheckoutAttributes || !await _checkoutService.IsActiveAsync())
            return;

        var customer = await _workContext.GetCurrentCustomerAsync();
        var store = await _storeContext.GetCurrentStoreAsync();
        var cart = await _shoppingCartService.GetShoppingCartAsync(customer, ShoppingCartType.ShoppingCart, store.Id);

        model.DisplayCheckoutButton = await _orderProcessingService.ValidateMinOrderSubtotalAmountAsync(cart)
            && string.IsNullOrEmpty(await _checkoutService.ValidateCanCheckoutAsync(customer, cart));
    }

    #endregion
}
