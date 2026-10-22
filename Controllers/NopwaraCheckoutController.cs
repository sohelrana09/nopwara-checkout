using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Core.Domain.Security;
using Nop.Core.Http;
using Nop.Core.Http.Extensions;
using Nop.Plugin.Misc.NopwaraCheckout.Factories;
using Nop.Plugin.Misc.NopwaraCheckout.Models;
using Nop.Plugin.Misc.NopwaraCheckout.Services;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Customers;
using Nop.Services.Helpers;
using Nop.Services.Discounts;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Messages;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Web.Controllers;
using Nop.Web.Framework.Mvc.Filters;
using Nop.Web.Models.Checkout;
using Nop.Web.Models.ShoppingCart;
using ILogger = Nop.Services.Logging.ILogger;

namespace Nop.Plugin.Misc.NopwaraCheckout.Controllers;

/// <summary>
/// Single-page checkout. The page renders every section; each ajax endpoint saves one change and returns
/// the sections that depend on it (see NopwaraCheckoutResult). Order placement re-saves everything from the
/// posted form, so it doesn't depend on earlier ajax calls having completed.
/// </summary>
[AutoValidateAntiforgeryToken]
public class NopwaraCheckoutController : BasePublicController
{
    #region Constants

    private const string SECTION_CART = "cart";
    private const string SECTION_COUPONS = "coupons";
    private const string SECTION_TOTALS = "totals";
    private const string SECTION_SHIPPING_METHODS = "shipping-methods";
    private const string SECTION_PAYMENT = "payment";

    private const string AREA_ATTRIBUTES = "attributes";
    private const string AREA_LOGIN = "login";

    #endregion

    #region Fields

    private readonly CaptchaSettings _captchaSettings;
    private readonly CustomerSettings _customerSettings;
    private readonly ICustomerActivityService _customerActivityService;
    private readonly ICustomerRegistrationService _customerRegistrationService;
    private readonly ICustomerService _customerService;
    private readonly IDiscountService _discountService;
    private readonly IGenericAttributeService _genericAttributeService;
    private readonly IGiftCardService _giftCardService;
    private readonly ILocalizationService _localizationService;
    private readonly ILogger _logger;
    private readonly IOrderProcessingService _orderProcessingService;
    private readonly IOrderService _orderService;
    private readonly IPaymentPluginManager _paymentPluginManager;
    private readonly IPaymentService _paymentService;
    private readonly IProductService _productService;
    private readonly IShoppingCartService _shoppingCartService;
    private readonly IStoreContext _storeContext;
    private readonly IWebHelper _webHelper;
    private readonly IWorkContext _workContext;
    private readonly IWorkflowMessageService _workflowMessageService;
    private readonly NopwaraCheckoutModelFactory _modelFactory;
    private readonly NopwaraCheckoutService _checkoutService;
    private readonly NopwaraCheckoutSettings _settings;
    private readonly OrderSettings _orderSettings;

    #endregion

    #region Ctor

    public NopwaraCheckoutController(CaptchaSettings captchaSettings,
        CustomerSettings customerSettings,
        ICustomerActivityService customerActivityService,
        ICustomerRegistrationService customerRegistrationService,
        ICustomerService customerService,
        IDiscountService discountService,
        IGenericAttributeService genericAttributeService,
        IGiftCardService giftCardService,
        ILocalizationService localizationService,
        ILogger logger,
        IOrderProcessingService orderProcessingService,
        IOrderService orderService,
        IPaymentPluginManager paymentPluginManager,
        IPaymentService paymentService,
        IProductService productService,
        IShoppingCartService shoppingCartService,
        IStoreContext storeContext,
        IWebHelper webHelper,
        IWorkContext workContext,
        IWorkflowMessageService workflowMessageService,
        NopwaraCheckoutModelFactory modelFactory,
        NopwaraCheckoutService checkoutService,
        NopwaraCheckoutSettings settings,
        OrderSettings orderSettings)
    {
        _captchaSettings = captchaSettings;
        _customerSettings = customerSettings;
        _customerActivityService = customerActivityService;
        _customerRegistrationService = customerRegistrationService;
        _customerService = customerService;
        _discountService = discountService;
        _genericAttributeService = genericAttributeService;
        _giftCardService = giftCardService;
        _localizationService = localizationService;
        _logger = logger;
        _orderProcessingService = orderProcessingService;
        _orderService = orderService;
        _paymentPluginManager = paymentPluginManager;
        _paymentService = paymentService;
        _productService = productService;
        _shoppingCartService = shoppingCartService;
        _storeContext = storeContext;
        _webHelper = webHelper;
        _workContext = workContext;
        _workflowMessageService = workflowMessageService;
        _modelFactory = modelFactory;
        _checkoutService = checkoutService;
        _settings = settings;
        _orderSettings = orderSettings;
    }

    #endregion

    #region Utilities

    protected virtual async Task<(Customer customer, IList<ShoppingCartItem> cart)> GetCartAsync()
    {
        var customer = await _workContext.GetCurrentCustomerAsync();
        var store = await _storeContext.GetCurrentStoreAsync();
        var cart = await _shoppingCartService.GetShoppingCartAsync(customer, ShoppingCartType.ShoppingCart, store.Id);

        return (customer, cart);
    }

    /// <summary>
    /// Renders the requested sections with the current checkout state
    /// </summary>
    protected virtual async Task RenderSectionsAsync(NopwaraCheckoutResult result, IList<ShoppingCartItem> cart,
        ShoppingCartModel cartModel, params string[] sections)
    {
        if (sections.Contains(SECTION_SHIPPING_METHODS))
            result.Sections[SECTION_SHIPPING_METHODS] = await RenderPartialViewToStringAsync("_ShippingMethods", await _modelFactory.PrepareShippingSectionAsync(cart));

        //payment depends on the shipping selection (fees, availability), so it is rendered after it
        if (sections.Contains(SECTION_PAYMENT))
            result.Sections[SECTION_PAYMENT] = await RenderPartialViewToStringAsync("_Payment", await _modelFactory.PreparePaymentSectionAsync(cart));

        if (sections.Contains(SECTION_CART) || sections.Contains(SECTION_COUPONS))
        {
            cartModel = await _modelFactory.PrepareCartAsync(cart, cartModel);
            if (sections.Contains(SECTION_CART))
                result.Sections[SECTION_CART] = await RenderPartialViewToStringAsync("_Cart", cartModel);
            if (sections.Contains(SECTION_COUPONS))
                result.Sections[SECTION_COUPONS] = await RenderPartialViewToStringAsync("_Coupons", cartModel);
        }

        if (sections.Contains(SECTION_TOTALS))
            result.Sections[SECTION_TOTALS] = await RenderPartialViewToStringAsync("_Totals", null);
    }

    /// <summary>
    /// Common validation for every ajax request
    /// </summary>
    protected virtual async Task<NopwaraCheckoutResult> ValidateRequestAsync(Customer customer, IList<ShoppingCartItem> cart)
    {
        var error = await _checkoutService.ValidateCanCheckoutAsync(customer, cart);
        if (string.IsNullOrEmpty(error))
            return null;

        var result = new NopwaraCheckoutResult();
        result.Errors["order"] = [error];
        if (!cart.Any() || _orderSettings.CheckoutDisabled)
            result.Redirect = Url.RouteUrl(NopRouteNames.General.CART);

        return result;
    }

    protected virtual async Task<JsonResult> HandleErrorAsync(Exception exception)
    {
        await _logger.WarningAsync(exception.Message, exception, await _workContext.GetCurrentCustomerAsync());

        var result = new NopwaraCheckoutResult();
        result.Errors["order"] = [exception.Message];
        return Json(result);
    }

    /// <summary>
    /// Saves billing and shipping addresses from the posted form
    /// </summary>
    protected virtual async Task SaveAddressesAsync(NopwaraCheckoutResult result, Customer customer, IList<ShoppingCartItem> cart,
        CheckoutBillingAddressModel billing, CheckoutShippingAddressModel shipping, IFormCollection form)
    {
        var billingErrors = await _checkoutService.SaveBillingAddressAsync(customer, billing, form, ModelState);
        if (billingErrors.Any())
            result.Errors["billing"] = billingErrors;

        if (!await _shoppingCartService.ShoppingCartRequiresShippingAsync(cart))
            return;

        //a pickup point replaces the shipping address (same as core)
        if (_checkoutService.IsPickupInStoreSelected(form))
            return;

        //"ship to the same address" needs a saved billing address
        if (billing.ShipToSameAddress && billingErrors.Any())
            return;

        var shippingErrors = await _checkoutService.SaveShippingAddressAsync(customer, billing.ShipToSameAddress, shipping, form, ModelState);
        if (shippingErrors.Any())
            result.Errors["shipping"] = shippingErrors;
    }

    #endregion

    #region Page

    public virtual async Task<IActionResult> Index()
    {
        if (!await _checkoutService.IsActiveAsync())
            return RedirectToRoute(NopRouteNames.Standard.CHECKOUT);

        var (customer, cart) = await GetCartAsync();

        if (_orderSettings.CheckoutDisabled || !cart.Any())
            return RedirectToRoute(NopRouteNames.General.CART);

        if (!string.IsNullOrEmpty(await _checkoutService.ValidateCanCheckoutAsync(customer, cart)))
            return Challenge();

        //same cart validation as core Checkout.Index
        if ((await _checkoutService.GetCartWarningsAsync(customer, cart)).Any())
            return RedirectToRoute(NopRouteNames.General.CART);

        var model = await _modelFactory.PrepareCheckoutModelAsync(cart);
        return View("Checkout", model);
    }

    #endregion

    #region Ajax sections

    [HttpPost]
    public virtual async Task<IActionResult> SaveAddresses(CheckoutBillingAddressModel billing, CheckoutShippingAddressModel shipping, IFormCollection form)
    {
        try
        {
            var (customer, cart) = await GetCartAsync();
            var result = await ValidateRequestAsync(customer, cart) ?? new NopwaraCheckoutResult();
            if (!result.Success)
                return Json(result);

            await SaveAddressesAsync(result, customer, cart, billing, shipping, form);
            await RenderSectionsAsync(result, cart, null, SECTION_SHIPPING_METHODS, SECTION_PAYMENT, SECTION_TOTALS);

            return Json(result);
        }
        catch (Exception exception)
        {
            return await HandleErrorAsync(exception);
        }
    }

    [HttpPost]
    public virtual async Task<IActionResult> SelectShipping(string shippingoption, IFormCollection form)
    {
        try
        {
            var (customer, cart) = await GetCartAsync();
            var result = await ValidateRequestAsync(customer, cart) ?? new NopwaraCheckoutResult();
            if (!result.Success)
                return Json(result);

            string error;
            if (_checkoutService.IsPickupInStoreSelected(form))
                error = await _checkoutService.SavePickupPointAsync(customer, cart, form);
            else
            {
                //back to delivery: drop the pickup point; the shipping methods section preselects a delivery option again
                await _checkoutService.ClearPickupPointAsync(customer);
                error = string.IsNullOrEmpty(shippingoption) ? null : await _checkoutService.SaveShippingOptionAsync(customer, cart, shippingoption, form);
            }
            if (!string.IsNullOrEmpty(error))
                result.Errors["shipping-methods"] = [error];

            await RenderSectionsAsync(result, cart, null, SECTION_SHIPPING_METHODS, SECTION_PAYMENT, SECTION_TOTALS);

            return Json(result);
        }
        catch (Exception exception)
        {
            return await HandleErrorAsync(exception);
        }
    }

    [HttpPost]
    public virtual async Task<IActionResult> CheckoutAttributes(IFormCollection form)
    {
        try
        {
            var (customer, cart) = await GetCartAsync();
            var result = await ValidateRequestAsync(customer, cart) ?? new NopwaraCheckoutResult();
            if (!result.Success)
                return Json(result);

            if (!_settings.ShowCheckoutAttributes)
                throw new Exception("Checkout attributes are not displayed on the checkout page");

            (result.EnabledAttributeIds, result.DisabledAttributeIds) = await _checkoutService.SaveCheckoutAttributesAsync(customer, cart, form);

            //attribute prices change the totals; weight/shippable attributes can change shipping rates
            await RenderSectionsAsync(result, cart, null, SECTION_SHIPPING_METHODS, SECTION_PAYMENT, SECTION_TOTALS);

            return Json(result);
        }
        catch (Exception exception)
        {
            return await HandleErrorAsync(exception);
        }
    }

    [HttpPost]
    public virtual async Task<IActionResult> SelectPayment(string paymentmethod, bool useRewardPoints)
    {
        try
        {
            var (customer, cart) = await GetCartAsync();
            var result = await ValidateRequestAsync(customer, cart) ?? new NopwaraCheckoutResult();
            if (!result.Success)
                return Json(result);

            var error = await _checkoutService.SavePaymentMethodAsync(customer, paymentmethod, useRewardPoints);
            if (!string.IsNullOrEmpty(error))
                result.Errors["payment"] = [error];

            await RenderSectionsAsync(result, cart, null, SECTION_PAYMENT, SECTION_TOTALS);

            return Json(result);
        }
        catch (Exception exception)
        {
            return await HandleErrorAsync(exception);
        }
    }

    [HttpPost]
    public virtual async Task<IActionResult> UpdateCart(int itemId, int quantity)
    {
        try
        {
            var (customer, cart) = await GetCartAsync();
            var result = await ValidateRequestAsync(customer, cart) ?? new NopwaraCheckoutResult();
            if (!result.Success)
                return Json(result);

            if (!_settings.AllowCartEditing)
                throw new Exception("Cart editing is disabled");

            var item = cart.FirstOrDefault(sci => sci.Id == itemId)
                ?? throw new Exception("Shopping cart item can't be loaded");

            //quantity 0 removes the item; keep the shipping/payment selections (they're re-validated when sections render)
            var warnings = await _shoppingCartService.UpdateShoppingCartItemAsync(customer, item.Id, item.AttributesXml,
                item.CustomerEnteredPrice, item.RentalStartDateUtc, item.RentalEndDateUtc, Math.Max(quantity, 0), false);
            if (warnings.Any())
                result.Errors["cart"] = warnings;

            (_, cart) = await GetCartAsync();
            if (!cart.Any())
            {
                result.Redirect = Url.RouteUrl(NopRouteNames.General.CART);
                return Json(result);
            }

            await RenderSectionsAsync(result, cart, null, SECTION_CART, SECTION_COUPONS, SECTION_SHIPPING_METHODS, SECTION_PAYMENT, SECTION_TOTALS);

            return Json(result);
        }
        catch (Exception exception)
        {
            return await HandleErrorAsync(exception);
        }
    }

    [HttpPost]
    public virtual async Task<IActionResult> ApplyDiscountCoupon(string discountcouponcode)
    {
        try
        {
            var (customer, cart) = await GetCartAsync();
            var result = await ValidateRequestAsync(customer, cart) ?? new NopwaraCheckoutResult();
            if (!result.Success)
                return Json(result);

            //same flow as core ShoppingCartController.ApplyDiscountCoupon
            var model = new ShoppingCartModel();
            discountcouponcode = discountcouponcode?.Trim();
            if (string.IsNullOrWhiteSpace(discountcouponcode))
                model.DiscountBox.Messages.Add(await _localizationService.GetResourceAsync("ShoppingCart.DiscountCouponCode.Empty"));
            else
            {
                //we find even hidden records here. this way we can display a user-friendly message if it's expired
                var discounts = (await _discountService.GetAllDiscountsAsync(couponCode: discountcouponcode, showHidden: true))
                    .Where(discount => discount.RequiresCouponCode)
                    .ToList();
                if (!discounts.Any())
                    model.DiscountBox.Messages.Add(await _localizationService.GetResourceAsync("ShoppingCart.DiscountCouponCode.CannotBeFound"));
                else
                {
                    var userErrors = new List<string>();
                    var anyValidDiscount = await discounts.AnyAwaitAsync(async discount =>
                    {
                        var validationResult = await _discountService.ValidateDiscountAsync(discount, customer, [discountcouponcode]);
                        userErrors.AddRange(validationResult.Errors);
                        return validationResult.IsValid;
                    });

                    if (anyValidDiscount)
                    {
                        await _customerService.ApplyDiscountCouponCodeAsync(customer, discountcouponcode);
                        model.DiscountBox.Messages.Add(await _localizationService.GetResourceAsync("ShoppingCart.DiscountCouponCode.Applied"));
                        model.DiscountBox.IsApplied = true;
                    }
                    else if (userErrors.Any())
                        model.DiscountBox.Messages = userErrors;
                    else
                        model.DiscountBox.Messages.Add(await _localizationService.GetResourceAsync("ShoppingCart.DiscountCouponCode.WrongDiscount"));
                }
            }

            await RenderSectionsAsync(result, cart, model, SECTION_CART, SECTION_COUPONS, SECTION_SHIPPING_METHODS, SECTION_PAYMENT, SECTION_TOTALS);

            return Json(result);
        }
        catch (Exception exception)
        {
            return await HandleErrorAsync(exception);
        }
    }

    [HttpPost]
    public virtual async Task<IActionResult> RemoveDiscountCoupon(int discountId)
    {
        try
        {
            var (customer, cart) = await GetCartAsync();
            var result = await ValidateRequestAsync(customer, cart) ?? new NopwaraCheckoutResult();
            if (!result.Success)
                return Json(result);

            var discount = await _discountService.GetDiscountByIdAsync(discountId);
            if (discount != null)
                await _customerService.RemoveDiscountCouponCodeAsync(customer, discount.CouponCode);

            await RenderSectionsAsync(result, cart, null, SECTION_CART, SECTION_COUPONS, SECTION_SHIPPING_METHODS, SECTION_PAYMENT, SECTION_TOTALS);

            return Json(result);
        }
        catch (Exception exception)
        {
            return await HandleErrorAsync(exception);
        }
    }

    [HttpPost]
    public virtual async Task<IActionResult> ApplyGiftCard(string giftcardcouponcode)
    {
        try
        {
            var (customer, cart) = await GetCartAsync();
            var result = await ValidateRequestAsync(customer, cart) ?? new NopwaraCheckoutResult();
            if (!result.Success)
                return Json(result);

            //same checks as core ShoppingCartController.GetGiftCardValidationErrorAsync
            var model = new ShoppingCartModel();
            giftcardcouponcode = giftcardcouponcode?.Trim();
            string error = null;
            if (string.IsNullOrWhiteSpace(giftcardcouponcode))
                error = await _localizationService.GetResourceAsync("ShoppingCart.GiftCardCouponCode.WrongGiftCard");
            else if (await _shoppingCartService.ShoppingCartIsRecurringAsync(cart))
                error = await _localizationService.GetResourceAsync("ShoppingCart.GiftCardCouponCode.DontWorkWithAutoshipProducts");
            else
            {
                var giftCard = (await _giftCardService.GetAllGiftCardsAsync(giftCardCouponCode: giftcardcouponcode)).FirstOrDefault();
                if (giftCard == null || !await _giftCardService.IsGiftCardValidAsync(giftCard))
                    error = await _localizationService.GetResourceAsync("ShoppingCart.GiftCardCouponCode.WrongGiftCard");
                else if (await _productService.HasAnyGiftCardProductAsync(cart.Select(item => item.ProductId).ToArray()))
                    error = await _localizationService.GetResourceAsync("ShoppingCart.GiftCardCouponCode.DontWorkWithGiftCards");
            }

            if (string.IsNullOrEmpty(error))
            {
                await _customerService.ApplyGiftCardCouponCodeAsync(customer, giftcardcouponcode);
                model.GiftCardBox.Message = await _localizationService.GetResourceAsync("ShoppingCart.GiftCardCouponCode.Applied");
                model.GiftCardBox.IsApplied = true;
            }
            else
            {
                model.GiftCardBox.Message = error;
                model.GiftCardBox.IsApplied = false;
            }

            await RenderSectionsAsync(result, cart, model, SECTION_COUPONS, SECTION_PAYMENT, SECTION_TOTALS);

            return Json(result);
        }
        catch (Exception exception)
        {
            return await HandleErrorAsync(exception);
        }
    }

    [HttpPost]
    public virtual async Task<IActionResult> RemoveGiftCard(int giftCardId)
    {
        try
        {
            var (customer, cart) = await GetCartAsync();
            var result = await ValidateRequestAsync(customer, cart) ?? new NopwaraCheckoutResult();
            if (!result.Success)
                return Json(result);

            var giftCard = await _giftCardService.GetGiftCardByIdAsync(giftCardId);
            if (giftCard != null)
                await _customerService.RemoveGiftCardCouponCodeAsync(customer, giftCard.GiftCardCouponCode);

            await RenderSectionsAsync(result, cart, null, SECTION_COUPONS, SECTION_PAYMENT, SECTION_TOTALS);

            return Json(result);
        }
        catch (Exception exception)
        {
            return await HandleErrorAsync(exception);
        }
    }

    #endregion

    #region Login

    /// <summary>
    /// Logs a guest in from the checkout page (same flow as core CustomerController.Login, answered with JSON)
    /// </summary>
    [HttpPost]
    [ValidateCaptcha]
    public virtual async Task<IActionResult> Login(string username, string email, string password, bool rememberMe, bool captchaValid)
    {
        try
        {
            var result = new NopwaraCheckoutResult();
            var checkoutUrl = Url.RouteUrl(NopwaraCheckoutDefaults.CHECKOUT_ROUTE_NAME);

            var customer = await _workContext.GetCurrentCustomerAsync();
            if (!_settings.ShowLoginOnCheckout || !await _customerService.IsGuestAsync(customer))
            {
                result.Redirect = checkoutUrl;
                return Json(result);
            }

            if (_captchaSettings.Enabled && _captchaSettings.ShowOnLoginPage && !captchaValid)
            {
                result.Errors[AREA_LOGIN] = [await _localizationService.GetResourceAsync("Common.WrongCaptchaMessage")];
                return Json(result);
            }

            var usernameOrEmail = (_customerSettings.UsernamesEnabled ? username : email)?.Trim();
            if (string.IsNullOrEmpty(usernameOrEmail) || string.IsNullOrEmpty(password))
            {
                result.Errors[AREA_LOGIN] = [await _localizationService.GetResourceAsync("Plugins.Misc.NopwaraCheckout.Login.Required")];
                return Json(result);
            }

            var loginResult = await _customerRegistrationService.ValidateCustomerAsync(usernameOrEmail, password);
            switch (loginResult)
            {
                case CustomerLoginResults.Successful:
                {
                    var registered = _customerSettings.UsernamesEnabled
                        ? await _customerService.GetCustomerByUsernameAsync(usernameOrEmail)
                        : await _customerService.GetCustomerByEmailAsync(usernameOrEmail);

                    //signs in, migrates the guest cart and raises the login events; its redirect result isn't used (ajax)
                    await _customerRegistrationService.SignInCustomerAsync(registered, checkoutUrl, rememberMe);
                    result.Redirect = checkoutUrl;
                    return Json(result);
                }
                case CustomerLoginResults.MultiFactorAuthenticationRequired:
                {
                    await HttpContext.Session.SetAsync(NopCustomerDefaults.CustomerMultiFactorAuthenticationInfo, new CustomerMultiFactorAuthenticationInfo
                    {
                        UserName = usernameOrEmail,
                        RememberMe = rememberMe,
                        ReturnUrl = checkoutUrl
                    });
                    result.Redirect = Url.RouteUrl(NopRouteNames.Standard.MULTIFACTOR_VERIFICATION);
                    return Json(result);
                }
                case CustomerLoginResults.Deleted:
                    result.Errors[AREA_LOGIN] = [await _localizationService.GetResourceAsync("Account.Login.WrongCredentials.Deleted")];
                    break;
                case CustomerLoginResults.NotActive:
                    result.Errors[AREA_LOGIN] = [await _localizationService.GetResourceAsync("Account.Login.WrongCredentials.NotActive")];
                    break;
                case CustomerLoginResults.LockedOut:
                    result.Errors[AREA_LOGIN] = [await _localizationService.GetResourceAsync("Account.Login.WrongCredentials.LockedOut")];
                    break;
                default:
                    result.Errors[AREA_LOGIN] = [await _localizationService.GetResourceAsync("Account.Login.WrongCredentials")];
                    break;
            }

            if (loginResult == CustomerLoginResults.WrongPassword && _customerSettings.NotifyFailedLoginAttempt)
            {
                var registered = _customerSettings.UsernamesEnabled
                    ? await _customerService.GetCustomerByUsernameAsync(usernameOrEmail)
                    : await _customerService.GetCustomerByEmailAsync(usernameOrEmail);
                if (registered != null)
                    await _workflowMessageService.SendCustomerFailedLoginAttemptNotificationAsync(registered, registered.LanguageId ?? 0);
            }

            await _customerActivityService.InsertActivityAsync("PublicStore.FailedLogin",
                string.Format(await _localizationService.GetResourceAsync("ActivityLog.PublicStore.Login.Fail"), usernameOrEmail));

            return Json(result);
        }
        catch (Exception exception)
        {
            await _logger.WarningAsync(exception.Message, exception, await _workContext.GetCurrentCustomerAsync());

            var result = new NopwaraCheckoutResult();
            result.Errors[AREA_LOGIN] = [exception.Message];
            return Json(result);
        }
    }

    #endregion

    #region Place order

    [HttpPost]
    [ValidateCaptcha]
    public virtual async Task<IActionResult> PlaceOrder(CheckoutBillingAddressModel billing, CheckoutShippingAddressModel shipping,
        IFormCollection form, bool captchaValid)
    {
        try
        {
            var (customer, cart) = await GetCartAsync();
            var result = await ValidateRequestAsync(customer, cart) ?? new NopwaraCheckoutResult();
            if (!result.Success)
                return Json(result);

            var store = await _storeContext.GetCurrentStoreAsync();

            //captcha validation for guest customers (same rule as core OpcConfirmOrder)
            if (await _customerService.IsGuestAsync(customer) && _captchaSettings.Enabled && _captchaSettings.ShowOnCheckoutPageForGuests && !captchaValid)
            {
                result.Errors["order"] = [await _localizationService.GetResourceAsync("Common.WrongCaptchaMessage")];
                return Json(result);
            }

            //0. checkout attributes (only when they are on the page; otherwise the ones chosen on the cart page stay)
            if (_settings.ShowCheckoutAttributes)
            {
                await _checkoutService.SaveCheckoutAttributesAsync(customer, cart, form);
                var attributesXml = await _genericAttributeService.GetAttributeAsync<string>(customer, NopCustomerDefaults.CheckoutAttributes, store.Id);
                var warnings = await _shoppingCartService.GetShoppingCartWarningsAsync(cart, attributesXml, true);
                if (warnings.Any())
                    result.Errors[AREA_ATTRIBUTES] = warnings;
            }

            //1. addresses
            await SaveAddressesAsync(result, customer, cart, billing, shipping, form);

            //2. shipping method or pickup point
            if (!result.Errors.Any() && await _shoppingCartService.ShoppingCartRequiresShippingAsync(cart))
            {
                var error = _checkoutService.IsPickupInStoreSelected(form)
                    ? await _checkoutService.SavePickupPointAsync(customer, cart, form)
                    : await _checkoutService.SaveShippingOptionAsync(customer, cart, form["shippingoption"], form);
                if (!string.IsNullOrEmpty(error))
                    result.Errors["shipping-methods"] = [error];
            }

            //3. payment method + payment info
            if (!result.Errors.Any())
            {
                if (await _orderProcessingService.IsPaymentWorkflowRequiredAsync(cart))
                {
                    _ = bool.TryParse(form["UseRewardPoints"].FirstOrDefault(), out var useRewardPoints);
                    string paymentMethodSystemName = form["paymentmethod"];
                    var error = await _checkoutService.SavePaymentMethodAsync(customer, paymentMethodSystemName, useRewardPoints);
                    if (!string.IsNullOrEmpty(error))
                        result.Errors["payment"] = [error];
                    else
                    {
                        var paymentMethod = await _paymentPluginManager.LoadPluginBySystemNameAsync(paymentMethodSystemName, customer, store.Id);
                        if (_modelFactory.IsPaymentInfoRequired(paymentMethod))
                        {
                            var warnings = await paymentMethod.ValidatePaymentFormAsync(form);
                            if (warnings.Any())
                                result.Errors["payment"] = warnings;
                            else
                                await _orderProcessingService.SetProcessPaymentRequestAsync(await paymentMethod.GetPaymentInfoAsync(form));
                        }
                        else
                            await _orderProcessingService.SetProcessPaymentRequestAsync(new ProcessPaymentRequest());
                    }
                }
                else
                {
                    await _genericAttributeService.SaveAttributeAsync<string>(customer, NopCustomerDefaults.SelectedPaymentMethodAttribute, null, store.Id);
                    await _orderProcessingService.SetProcessPaymentRequestAsync(new ProcessPaymentRequest());
                }
            }

            //4. terms of service
            if (!result.Errors.Any() && _orderSettings.TermsOfServiceOnOrderConfirmPage && !form["termsofservice"].Any(value => value is "on" or "true"))
                result.Errors["order"] = [await _localizationService.GetResourceAsync("Checkout.TermsOfService.PleaseAccept")];

            //5. prevent 2 orders being placed within an X seconds time frame
            if (!result.Errors.Any() && !await _checkoutService.IsMinimumOrderPlacementIntervalValidAsync(customer))
                result.Errors["order"] = [await _localizationService.GetResourceAsync("Checkout.MinOrderPlacementInterval")];

            if (result.Errors.Any())
            {
                if (!result.Errors.ContainsKey("order"))
                    result.Errors["order"] = [await _localizationService.GetResourceAsync("Plugins.Misc.NopwaraCheckout.FixErrors")];

                await RenderSectionsAsync(result, cart, null, SECTION_SHIPPING_METHODS, SECTION_TOTALS);
                return Json(result);
            }

            //6. place the order (same as core OpcConfirmOrder)
            var processPaymentRequest = await _orderProcessingService.GetProcessPaymentRequestAsync() ?? new ProcessPaymentRequest();
            processPaymentRequest.StoreId = store.Id;
            processPaymentRequest.CustomerId = customer.Id;
            processPaymentRequest.PaymentMethodSystemName = await _genericAttributeService.GetAttributeAsync<string>(customer,
                NopCustomerDefaults.SelectedPaymentMethodAttribute, store.Id);
            await _orderProcessingService.SetProcessPaymentRequestAsync(processPaymentRequest);

            var placeOrderResult = await _orderProcessingService.PlaceOrderAsync(processPaymentRequest);
            if (!placeOrderResult.Success)
            {
                result.Errors["order"] = placeOrderResult.Errors;
                await RenderSectionsAsync(result, cart, null, SECTION_TOTALS);
                return Json(result);
            }

            await _orderProcessingService.SetProcessPaymentRequestAsync(null);
            var order = placeOrderResult.PlacedOrder;

            //payment method could be null if order total is 0
            var placedPaymentMethod = await _paymentPluginManager.LoadPluginBySystemNameAsync(order.PaymentMethodSystemName, customer, store.Id);
            if (placedPaymentMethod?.PaymentMethodType == PaymentMethodType.Redirection)
            {
                //an ajax request can't redirect to the payment provider: do it from a regular GET
                result.Redirect = Url.RouteUrl(NopwaraCheckoutDefaults.COMPLETE_REDIRECTION_PAYMENT_ROUTE_NAME);
                return Json(result);
            }

            if (placedPaymentMethod != null)
                await _paymentService.PostProcessPaymentAsync(new PostProcessPaymentRequest { Order = order });

            result.Redirect = Url.RouteUrl(NopRouteNames.Standard.CHECKOUT_COMPLETED, new { orderId = order.Id });
            return Json(result);
        }
        catch (Exception exception)
        {
            return await HandleErrorAsync(exception);
        }
    }

    /// <summary>
    /// Completes a redirection payment right after the order is placed (copy of core OpcCompleteRedirectionPayment,
    /// which requires the core one-page checkout to be enabled)
    /// </summary>
    public virtual async Task<IActionResult> CompleteRedirectionPayment()
    {
        try
        {
            var customer = await _workContext.GetCurrentCustomerAsync();
            if (await _customerService.IsGuestAsync(customer) && !_orderSettings.AnonymousCheckoutAllowed)
                return Challenge();

            var store = await _storeContext.GetCurrentStoreAsync();
            var order = (await _orderService.SearchOrdersAsync(storeId: store.Id, customerId: customer.Id, pageSize: 1)).FirstOrDefault();
            if (order == null)
                return RedirectToRoute(NopRouteNames.General.HOMEPAGE);

            var paymentMethod = await _paymentPluginManager.LoadPluginBySystemNameAsync(order.PaymentMethodSystemName, customer, store.Id);
            if (paymentMethod is not { PaymentMethodType: PaymentMethodType.Redirection })
                return RedirectToRoute(NopRouteNames.General.HOMEPAGE);

            //ensure that order has been just placed
            if ((DateTime.UtcNow - order.CreatedOnUtc).TotalMinutes > 3)
                return RedirectToRoute(NopRouteNames.General.HOMEPAGE);

            await _paymentService.PostProcessPaymentAsync(new PostProcessPaymentRequest { Order = order });

            //redirection or POST has been done in PostProcessPayment
            if (_webHelper.IsRequestBeingRedirected || _webHelper.IsPostBeingDone)
                return new EmptyResult();

            return RedirectToRoute(NopRouteNames.Standard.CHECKOUT_COMPLETED, new { orderId = order.Id });
        }
        catch (Exception exception)
        {
            await _logger.WarningAsync(exception.Message, exception, await _workContext.GetCurrentCustomerAsync());
            return Content(exception.Message);
        }
    }

    #endregion
}
