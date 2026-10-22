using Nop.Core;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Core.Domain.Security;
using Nop.Core.Domain.Shipping;
using Nop.Core.Domain.Tax;
using Nop.Core.Http;
using Nop.Plugin.Misc.NopwaraCheckout.Models;
using Nop.Plugin.Misc.NopwaraCheckout.Services;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Customers;
using Nop.Services.Directory;
using Nop.Services.Localization;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Shipping;
using Nop.Services.Shipping.Pickup;
using Nop.Services.Tax;
using Nop.Web.Factories;
using Nop.Web.Framework.Mvc.Routing;
using Nop.Web.Models.Checkout;
using Nop.Web.Models.ShoppingCart;

namespace Nop.Plugin.Misc.NopwaraCheckout.Factories;

/// <summary>
/// Prepares the single-page checkout models using the core checkout and shopping cart model factories
/// </summary>
public class NopwaraCheckoutModelFactory
{
    #region Fields

    private readonly AddressSettings _addressSettings;
    private readonly CaptchaSettings _captchaSettings;
    private readonly CustomerSettings _customerSettings;
    private readonly ICheckoutModelFactory _checkoutModelFactory;
    private readonly ICountryService _countryService;
    private readonly ICurrencyService _currencyService;
    private readonly ICustomerService _customerService;
    private readonly IGenericAttributeService _genericAttributeService;
    private readonly ILocalizationService _localizationService;
    private readonly INopUrlHelper _nopUrlHelper;
    private readonly IOrderProcessingService _orderProcessingService;
    private readonly IOrderTotalCalculationService _orderTotalCalculationService;
    private readonly IPaymentPluginManager _paymentPluginManager;
    private readonly IPickupPluginManager _pickupPluginManager;
    private readonly IPriceFormatter _priceFormatter;
    private readonly IShippingPluginManager _shippingPluginManager;
    private readonly IShippingService _shippingService;
    private readonly IShoppingCartModelFactory _shoppingCartModelFactory;
    private readonly IShoppingCartService _shoppingCartService;
    private readonly IStateProvinceService _stateProvinceService;
    private readonly IStoreContext _storeContext;
    private readonly ITaxService _taxService;
    private readonly IWorkContext _workContext;
    private readonly NopwaraCheckoutService _checkoutService;
    private readonly NopwaraCheckoutSettings _settings;
    private readonly OtpSettings _otpSettings;
    private readonly PaymentSettings _paymentSettings;
    private readonly ShippingSettings _shippingSettings;

    #endregion

    #region Ctor

    public NopwaraCheckoutModelFactory(AddressSettings addressSettings,
        CaptchaSettings captchaSettings,
        CustomerSettings customerSettings,
        ICheckoutModelFactory checkoutModelFactory,
        ICountryService countryService,
        ICurrencyService currencyService,
        ICustomerService customerService,
        IGenericAttributeService genericAttributeService,
        ILocalizationService localizationService,
        INopUrlHelper nopUrlHelper,
        IOrderProcessingService orderProcessingService,
        IOrderTotalCalculationService orderTotalCalculationService,
        IPaymentPluginManager paymentPluginManager,
        IPickupPluginManager pickupPluginManager,
        IPriceFormatter priceFormatter,
        IShippingPluginManager shippingPluginManager,
        IShippingService shippingService,
        IShoppingCartModelFactory shoppingCartModelFactory,
        IShoppingCartService shoppingCartService,
        IStateProvinceService stateProvinceService,
        IStoreContext storeContext,
        ITaxService taxService,
        IWorkContext workContext,
        NopwaraCheckoutService checkoutService,
        NopwaraCheckoutSettings settings,
        OtpSettings otpSettings,
        PaymentSettings paymentSettings,
        ShippingSettings shippingSettings)
    {
        _addressSettings = addressSettings;
        _captchaSettings = captchaSettings;
        _customerSettings = customerSettings;
        _checkoutModelFactory = checkoutModelFactory;
        _countryService = countryService;
        _currencyService = currencyService;
        _customerService = customerService;
        _genericAttributeService = genericAttributeService;
        _localizationService = localizationService;
        _nopUrlHelper = nopUrlHelper;
        _orderProcessingService = orderProcessingService;
        _orderTotalCalculationService = orderTotalCalculationService;
        _paymentPluginManager = paymentPluginManager;
        _pickupPluginManager = pickupPluginManager;
        _priceFormatter = priceFormatter;
        _shippingPluginManager = shippingPluginManager;
        _shippingService = shippingService;
        _shoppingCartModelFactory = shoppingCartModelFactory;
        _shoppingCartService = shoppingCartService;
        _stateProvinceService = stateProvinceService;
        _storeContext = storeContext;
        _taxService = taxService;
        _workContext = workContext;
        _checkoutService = checkoutService;
        _settings = settings;
        _otpSettings = otpSettings;
        _paymentSettings = paymentSettings;
        _shippingSettings = shippingSettings;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Prepares the whole checkout page
    /// </summary>
    public virtual async Task<NopwaraCheckoutModel> PrepareCheckoutModelAsync(IList<ShoppingCartItem> cart)
    {
        var customer = await _workContext.GetCurrentCustomerAsync();

        var model = new NopwaraCheckoutModel
        {
            ShippingRequired = await _shoppingCartService.ShoppingCartRequiresShippingAsync(cart),
            AllowCartEditing = _settings.AllowCartEditing,
            ShowDiscountBox = _settings.ShowDiscountBox,
            ShowGiftCardBox = _settings.ShowGiftCardBox,
            IsReCaptchaV3 = _captchaSettings.CaptchaType == CaptchaType.ReCaptchaV3,
            ReCaptchaPublicKey = _captchaSettings.ReCaptchaPublicKey,
            IncludeStyles = !await _checkoutService.ThemeHasBuiltInStylesAsync(),
            IncludeAlpine = !await _checkoutService.ThemeLoadsAlpineAsync(),
            SkipShoppingCart = _settings.SkipShoppingCart
        };

        if (_settings.ShowLoginOnCheckout && await _customerService.IsGuestAsync(customer))
        {
            model.Login = new NopwaraLoginModel
            {
                UsernamesEnabled = _customerSettings.UsernamesEnabled,
                DisplayCaptcha = _captchaSettings.Enabled && _captchaSettings.ShowOnLoginPage,
                LoginByPhoneEnabled = _otpSettings.LoginByPhoneEnabled,
                LoginPageUrl = _nopUrlHelper.RouteUrl(NopRouteNames.General.LOGIN,
                    new { returnUrl = _nopUrlHelper.RouteUrl(NopwaraCheckoutDefaults.CHECKOUT_ROUTE_NAME) })
            };
        }

        //billing address
        await _checkoutModelFactory.PrepareBillingAddressModelAsync(model.Billing, cart, prePopulateNewAddressWithCustomerFields: true);
        model.SelectedBillingAddressId = model.Billing.ExistingAddresses.Any(address => address.Id == customer.BillingAddressId)
            ? customer.BillingAddressId ?? 0
            : 0;

        //shipping address
        if (model.ShippingRequired)
        {
            await _checkoutModelFactory.PrepareShippingAddressModelAsync(model.Shipping, cart);

            //custom address attributes share control names, so they are rendered (and saved) with the billing address only
            model.Shipping.ShippingNewAddress.CustomAddressAttributes.Clear();

            model.SelectedShippingAddressId = model.Shipping.ExistingAddresses.Any(address => address.Id == customer.ShippingAddressId)
                ? customer.ShippingAddressId ?? 0
                : 0;
            model.ShipToSameAddress = model.Billing.ShipToSameAddressAllowed
                && (!customer.ShippingAddressId.HasValue || customer.ShippingAddressId == customer.BillingAddressId);

            var pickupPoints = await PreparePickupPointsModelAsync(cart);
            if (pickupPoints.AllowPickupInStore && (pickupPoints.PickupPoints.Any() || pickupPoints.PickupInStoreOnly || pickupPoints.Warnings.Any()))
            {
                model.PickupPoints = pickupPoints;
                model.PickupInStore = pickupPoints.PickupInStore;
            }
        }

        model.ShippingSection = await PrepareShippingSectionAsync(cart);
        model.PaymentSection = await PreparePaymentSectionAsync(cart);
        model.Cart = await PrepareCartAsync(cart);
        model.ShowCheckoutAttributes = _settings.ShowCheckoutAttributes && model.Cart.CheckoutAttributes.Any();
        model.Confirm = await _checkoutModelFactory.PrepareConfirmOrderModelAsync(cart);

        return model;
    }

    /// <summary>
    /// Prepares the shipping methods section and makes sure the displayed selection is the saved one
    /// </summary>
    public virtual async Task<NopwaraShippingSectionModel> PrepareShippingSectionAsync(IList<ShoppingCartItem> cart)
    {
        var section = new NopwaraShippingSectionModel();
        if (!await _shoppingCartService.ShoppingCartRequiresShippingAsync(cart))
            return section;

        var customer = await _workContext.GetCurrentCustomerAsync();
        var shippingAddress = await _customerService.GetCustomerShippingAddressAsync(customer);
        if (shippingAddress == null)
            return section;

        //also caches the offered options in OfferedShippingOptionsAttribute
        var methods = await _checkoutModelFactory.PrepareShippingMethodModelAsync(cart, shippingAddress);
        section.ShippingMethods = methods;

        //a pickup point is the shipping option while pickup in store is chosen: keep it
        var store = await _storeContext.GetCurrentStoreAsync();
        var saved = await _genericAttributeService.GetAttributeAsync<ShippingOption>(customer, NopCustomerDefaults.SelectedShippingOptionAttribute, store.Id);
        if (saved?.IsPickupInStore == true && _shippingSettings.AllowPickupInStore)
            return section;

        //the core factory preselects the saved option (or the first one) without saving it;
        //save it so totals always match what the customer sees
        var selected = methods.ShippingMethods.FirstOrDefault(method => method.Selected) ?? methods.ShippingMethods.FirstOrDefault();
        if (selected == null)
            return section;

        foreach (var method in methods.ShippingMethods)
            method.Selected = method == selected;

        if (saved == null
            || !string.Equals(saved.Name, selected.ShippingOption.Name, StringComparison.InvariantCultureIgnoreCase)
            || !string.Equals(saved.ShippingRateComputationMethodSystemName, selected.ShippingRateComputationMethodSystemName, StringComparison.InvariantCultureIgnoreCase)
            //the same method can cost more after a quantity change (weight / total based rates): totals use the saved rate
            || saved.Rate != selected.ShippingOption.Rate)
        {
            await _genericAttributeService.SaveAttributeAsync(customer, NopCustomerDefaults.SelectedShippingOptionAttribute, selected.ShippingOption, store.Id);
        }

        return section;
    }

    /// <summary>
    /// Prepares pickup points (port of the protected core CheckoutModelFactory.PrepareCheckoutPickupPointsModelAsync,
    /// which core only runs for one of its checkout steps)
    /// </summary>
    public virtual async Task<CheckoutPickupPointsModel> PreparePickupPointsModelAsync(IList<ShoppingCartItem> cart)
    {
        var model = new CheckoutPickupPointsModel { AllowPickupInStore = _shippingSettings.AllowPickupInStore };
        if (!model.AllowPickupInStore)
            return model;

        var customer = await _workContext.GetCurrentCustomerAsync();
        var store = await _storeContext.GetCurrentStoreAsync();
        var pickupPointProviders = await _pickupPluginManager.LoadActivePluginsAsync(customer, store.Id);
        if (pickupPointProviders.Any())
        {
            var languageId = (await _workContext.GetWorkingLanguageAsync()).Id;
            var currency = await _workContext.GetWorkingCurrencyAsync();
            var address = await _customerService.GetCustomerBillingAddressAsync(customer);
            var response = await _shippingService.GetPickupPointsAsync(cart, address, customer, storeId: store.Id);
            if (response.Success)
            {
                var selectedPoint = await _genericAttributeService.GetAttributeAsync<PickupPoint>(customer, NopCustomerDefaults.SelectedPickupPointAttribute, store.Id);
                var selectedOption = await _genericAttributeService.GetAttributeAsync<ShippingOption>(customer, NopCustomerDefaults.SelectedShippingOptionAttribute, store.Id);
                model.PickupInStore = selectedOption?.IsPickupInStore == true;

                foreach (var point in response.PickupPoints)
                {
                    var country = await _countryService.GetCountryByTwoLetterIsoCodeAsync(point.CountryCode);
                    var state = await _stateProvinceService.GetStateProvinceByAbbreviationAsync(point.StateAbbreviation, country?.Id);

                    var (shippingTotal, _) = await _orderTotalCalculationService.AdjustShippingRateAsync(point.PickupFee, cart, true);
                    var (rateBase, _) = await _taxService.GetShippingPriceAsync(shippingTotal, customer);
                    var rate = await _currencyService.ConvertFromPrimaryStoreCurrencyAsync(rateBase, currency);

                    model.PickupPoints.Add(new CheckoutPickupPointModel
                    {
                        Id = point.Id,
                        Name = point.Name,
                        Description = point.Description,
                        ProviderSystemName = point.ProviderSystemName,
                        Address = point.Address,
                        City = point.City,
                        County = point.County,
                        StateName = state != null ? await _localizationService.GetLocalizedAsync(state, x => x.Name, languageId) : string.Empty,
                        CountryName = country != null ? await _localizationService.GetLocalizedAsync(country, x => x.Name, languageId) : string.Empty,
                        ZipPostalCode = point.ZipPostalCode,
                        Latitude = point.Latitude,
                        Longitude = point.Longitude,
                        OpeningHours = point.OpeningHours,
                        IsPreSelected = selectedPoint?.Id == point.Id,
                        PickupFee = await _priceFormatter.FormatShippingPriceAsync(rate, true)
                    });
                }
            }
            else
            {
                foreach (var error in response.Errors)
                    model.Warnings.Add(error);
            }
        }

        //no shipping rate providers: pickup only
        if (!(await _shippingPluginManager.LoadActivePluginsAsync(customer, store.Id)).Any())
        {
            if (!pickupPointProviders.Any())
            {
                model.Warnings.Add(await _localizationService.GetResourceAsync("Checkout.ShippingIsNotAllowed"));
                model.Warnings.Add(await _localizationService.GetResourceAsync("Checkout.PickupPoints.NotAvailable"));
            }
            model.PickupInStoreOnly = true;
            model.PickupInStore = true;
        }

        return model;
    }

    /// <summary>
    /// Prepares the payment methods + payment info section and makes sure the displayed selection is the saved one
    /// </summary>
    public virtual async Task<NopwaraPaymentSectionModel> PreparePaymentSectionAsync(IList<ShoppingCartItem> cart)
    {
        var section = new NopwaraPaymentSectionModel();
        var customer = await _workContext.GetCurrentCustomerAsync();
        var store = await _storeContext.GetCurrentStoreAsync();

        //we ignore reward points during the cart total calculation (same as core)
        section.PaymentRequired = await _orderProcessingService.IsPaymentWorkflowRequiredAsync(cart, false);
        if (!section.PaymentRequired)
        {
            await _genericAttributeService.SaveAttributeAsync<string>(customer, NopCustomerDefaults.SelectedPaymentMethodAttribute, null, store.Id);
            return section;
        }

        //payment methods can be filtered by the billing country, so wait for the billing address
        var billingAddress = await _customerService.GetCustomerBillingAddressAsync(customer);
        if (_addressSettings.CountryEnabled && billingAddress == null)
            return section;

        var methods = await _checkoutModelFactory.PreparePaymentMethodModelAsync(cart, _addressSettings.CountryEnabled ? billingAddress?.CountryId ?? 0 : 0);
        section.PaymentMethods = methods;

        var selected = methods.PaymentMethods.FirstOrDefault(method => method.Selected) ?? methods.PaymentMethods.FirstOrDefault();
        if (selected == null)
            return section;

        foreach (var method in methods.PaymentMethods)
            method.Selected = method == selected;

        var saved = await _genericAttributeService.GetAttributeAsync<string>(customer, NopCustomerDefaults.SelectedPaymentMethodAttribute, store.Id);
        if (!string.Equals(saved, selected.PaymentMethodSystemName, StringComparison.InvariantCultureIgnoreCase))
            await _genericAttributeService.SaveAttributeAsync(customer, NopCustomerDefaults.SelectedPaymentMethodAttribute, selected.PaymentMethodSystemName, store.Id);

        section.PaymentInfo = await PreparePaymentInfoAsync(selected.PaymentMethodSystemName);

        return section;
    }

    /// <summary>
    /// Prepares the payment form of a payment method; null when the method has no payment form
    /// </summary>
    public virtual async Task<CheckoutPaymentInfoModel> PreparePaymentInfoAsync(string paymentMethodSystemName)
    {
        var customer = await _workContext.GetCurrentCustomerAsync();
        var store = await _storeContext.GetCurrentStoreAsync();

        var paymentMethod = await _paymentPluginManager.LoadPluginBySystemNameAsync(paymentMethodSystemName, customer, store.Id);
        if (paymentMethod == null || !IsPaymentInfoRequired(paymentMethod))
            return null;

        return await _checkoutModelFactory.PreparePaymentInfoModelAsync(paymentMethod);
    }

    /// <summary>
    /// Gets a value indicating whether a payment method needs its payment form (same rule as core OPC)
    /// </summary>
    public virtual bool IsPaymentInfoRequired(IPaymentMethod paymentMethod)
    {
        return !(paymentMethod.SkipPaymentInfo
            || (paymentMethod.PaymentMethodType == PaymentMethodType.Redirection && _paymentSettings.SkipPaymentInfoStepForRedirectionPaymentMethods));
    }

    /// <summary>
    /// Prepares the cart (items, discount box, gift card box)
    /// </summary>
    /// <param name="cart">Cart</param>
    /// <param name="model">Pre-filled model (e.g. with discount/gift card messages)</param>
    public virtual async Task<ShoppingCartModel> PrepareCartAsync(IList<ShoppingCartItem> cart, ShoppingCartModel model = null)
    {
        return await _shoppingCartModelFactory.PrepareShoppingCartModelAsync(model ?? new ShoppingCartModel(), cart, isEditable: _settings.AllowCartEditing);
    }

    #endregion
}
