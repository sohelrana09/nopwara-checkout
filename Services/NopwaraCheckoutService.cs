using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Primitives;
using Nop.Core;
using Nop.Core.Caching;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Shipping;
using Nop.Core.Domain.Tax;
using Nop.Services.Attributes;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Customers;
using Nop.Services.Directory;
using Nop.Services.Localization;
using Nop.Services.Media;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Shipping;
using Nop.Services.Stores;
using Nop.Services.Tax;
using Nop.Services.Themes;
using Nop.Web.Models.Checkout;
using Nop.Web.Models.Common;

namespace Nop.Plugin.Misc.NopwaraCheckout.Services;

/// <summary>
/// Checkout state changes for the single-page checkout.
/// </summary>
/// <remarks>
/// Mirrors the logic of the core CheckoutController OPC actions (OpcSaveBilling, OpcSaveShipping,
/// OpcSaveShippingMethod, OpcSavePaymentMethod) but as independent operations, so every section can be
/// saved at any time instead of step by step. All state lives where core keeps it (customer billing/shipping
/// address ids and the NopCustomerDefaults generic attributes), so core order placement validates it as usual.
/// </remarks>
public class NopwaraCheckoutService
{
    #region Fields

    private static readonly string[] _separator = [NopwaraCheckoutDefaults.OPTION_SEPARATOR];
    private static readonly char[] _valueSeparator = [','];

    private readonly AddressSettings _addressSettings;
    private readonly CustomerSettings _customerSettings;
    private readonly IAddressService _addressService;
    private readonly IAttributeParser<AddressAttribute, AddressAttributeValue> _addressAttributeParser;
    private readonly IAttributeParser<CheckoutAttribute, CheckoutAttributeValue> _checkoutAttributeParser;
    private readonly IAttributeService<CheckoutAttribute, CheckoutAttributeValue> _checkoutAttributeService;
    private readonly ICountryService _countryService;
    private readonly ICustomerService _customerService;
    private readonly IDownloadService _downloadService;
    private readonly IGenericAttributeService _genericAttributeService;
    private readonly ILocalizationService _localizationService;
    private readonly IOrderService _orderService;
    private readonly IPaymentPluginManager _paymentPluginManager;
    private readonly IProductService _productService;
    private readonly IShippingService _shippingService;
    private readonly IShoppingCartService _shoppingCartService;
    private readonly IStaticCacheManager _staticCacheManager;
    private readonly IStoreContext _storeContext;
    private readonly IStoreMappingService _storeMappingService;
    private readonly ITaxService _taxService;
    private readonly IThemeContext _themeContext;
    private readonly NopwaraCheckoutSettings _settings;
    private readonly OrderSettings _orderSettings;
    private readonly RewardPointsSettings _rewardPointsSettings;
    private readonly ShippingSettings _shippingSettings;
    private readonly TaxSettings _taxSettings;

    #endregion

    #region Ctor

    public NopwaraCheckoutService(AddressSettings addressSettings,
        CustomerSettings customerSettings,
        IAddressService addressService,
        IAttributeParser<AddressAttribute, AddressAttributeValue> addressAttributeParser,
        IAttributeParser<CheckoutAttribute, CheckoutAttributeValue> checkoutAttributeParser,
        IAttributeService<CheckoutAttribute, CheckoutAttributeValue> checkoutAttributeService,
        ICountryService countryService,
        ICustomerService customerService,
        IDownloadService downloadService,
        IGenericAttributeService genericAttributeService,
        ILocalizationService localizationService,
        IOrderService orderService,
        IPaymentPluginManager paymentPluginManager,
        IProductService productService,
        IShippingService shippingService,
        IShoppingCartService shoppingCartService,
        IStaticCacheManager staticCacheManager,
        IStoreContext storeContext,
        IStoreMappingService storeMappingService,
        ITaxService taxService,
        IThemeContext themeContext,
        NopwaraCheckoutSettings settings,
        OrderSettings orderSettings,
        RewardPointsSettings rewardPointsSettings,
        ShippingSettings shippingSettings,
        TaxSettings taxSettings)
    {
        _addressSettings = addressSettings;
        _customerSettings = customerSettings;
        _addressService = addressService;
        _addressAttributeParser = addressAttributeParser;
        _checkoutAttributeParser = checkoutAttributeParser;
        _checkoutAttributeService = checkoutAttributeService;
        _countryService = countryService;
        _customerService = customerService;
        _downloadService = downloadService;
        _genericAttributeService = genericAttributeService;
        _localizationService = localizationService;
        _orderService = orderService;
        _paymentPluginManager = paymentPluginManager;
        _productService = productService;
        _shippingService = shippingService;
        _shoppingCartService = shoppingCartService;
        _staticCacheManager = staticCacheManager;
        _storeContext = storeContext;
        _storeMappingService = storeMappingService;
        _taxService = taxService;
        _themeContext = themeContext;
        _settings = settings;
        _orderSettings = orderSettings;
        _rewardPointsSettings = rewardPointsSettings;
        _shippingSettings = shippingSettings;
        _taxSettings = taxSettings;
    }

    #endregion

    #region Utilities

    /// <summary>
    /// Gets model errors only for fields under the passed prefix (billing and shipping models are bound from the same form)
    /// </summary>
    protected virtual IList<string> GetModelErrors(ModelStateDictionary modelState, params string[] prefixes)
    {
        return modelState
            .Where(entry => prefixes.Any(prefix => entry.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .SelectMany(entry => entry.Value.Errors)
            .Select(error => error.ErrorMessage)
            .Where(message => !string.IsNullOrEmpty(message))
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// Finds an existing customer address with the same values or creates a new one
    /// </summary>
    protected virtual async Task<Address> FindOrCreateAddressAsync(Customer customer, AddressModel model, string customAttributes)
    {
        //try to find an address with the same values (don't duplicate records)
        var address = _addressService.FindAddress((await _customerService.GetAddressesByCustomerIdAsync(customer.Id)).ToList(),
            model.FirstName, model.LastName, model.PhoneNumber,
            model.Email, model.FaxNumber, model.Company,
            model.Address1, model.Address2, model.City,
            model.County, model.StateProvinceId, model.ZipPostalCode,
            model.CountryId, customAttributes);

        if (address != null)
            return address;

        address = model.ToEntity();
        address.CustomAttributes = customAttributes;
        address.CreatedOnUtc = DateTime.UtcNow;

        if (address.CountryId == 0)
            address.CountryId = null;
        if (address.StateProvinceId == 0)
            address.StateProvinceId = null;

        await _addressService.InsertAddressAsync(address);
        await _customerService.InsertCustomerAddressAsync(customer, address);

        return address;
    }

    /// <summary>
    /// Save customer VAT number (same as core CheckoutController.SaveCustomerVatNumberAsync)
    /// </summary>
    protected virtual async Task<string> SaveCustomerVatNumberAsync(string fullVatNumber, Customer customer)
    {
        var (vatNumberStatus, _, _) = await _taxService.GetVatNumberStatusAsync(fullVatNumber);
        customer.VatNumberStatus = vatNumberStatus;
        customer.VatNumber = fullVatNumber;
        await _customerService.UpdateCustomerAsync(customer);

        if (vatNumberStatus != VatNumberStatus.Valid && !string.IsNullOrEmpty(fullVatNumber))
        {
            var warning = await _localizationService.GetResourceAsync("Checkout.VatNumber.Warning");
            return string.Format(warning, await _localizationService.GetLocalizedEnumAsync(vatNumberStatus));
        }

        return string.Empty;
    }

    /// <summary>
    /// Gets a value indicating whether the working theme is in a comma-separated list of theme system names
    /// </summary>
    protected virtual async Task<bool> IsWorkingThemeListedAsync(string themes)
    {
        if (string.IsNullOrWhiteSpace(themes))
            return false;

        var theme = await _themeContext.GetWorkingThemeNameAsync();
        return themes
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(theme, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets the checkout attributes of the current store that apply to the cart (same filter as core)
    /// </summary>
    protected virtual async Task<IList<CheckoutAttribute>> GetCheckoutAttributesAsync(IList<ShoppingCartItem> cart)
    {
        var store = await _storeContext.GetCurrentStoreAsync();
        var excludeShippableAttributes = !await _shoppingCartService.ShoppingCartRequiresShippingAsync(cart);
        return await _checkoutAttributeService.GetAllAttributesAsync(_staticCacheManager, _storeMappingService, store.Id, excludeShippableAttributes);
    }

    #endregion

    #region Methods

    /// <summary>
    /// Gets a value indicating whether the single-page checkout should be used for the current request
    /// </summary>
    public virtual async Task<bool> IsActiveAsync()
    {
        if (!_settings.Enabled)
            return false;

        //no list: every theme
        return string.IsNullOrWhiteSpace(_settings.AllowedThemes) || await IsWorkingThemeListedAsync(_settings.AllowedThemes);
    }

    /// <summary>
    /// Gets a value indicating whether the working theme styles the checkout views itself (otherwise the plugin stylesheet is loaded)
    /// </summary>
    public virtual Task<bool> ThemeHasBuiltInStylesAsync()
    {
        return IsWorkingThemeListedAsync(_settings.ThemesWithBuiltInStyles);
    }

    /// <summary>
    /// Gets a value indicating whether the working theme loads Alpine.js itself (otherwise the plugin's copy is loaded)
    /// </summary>
    public virtual Task<bool> ThemeLoadsAlpineAsync()
    {
        return IsWorkingThemeListedAsync(_settings.ThemesWithAlpine);
    }

    /// <summary>
    /// Gets a value indicating whether the customer chose pickup in store (core form key "PickupInStore")
    /// </summary>
    public virtual bool IsPickupInStoreSelected(IFormCollection form)
    {
        return _shippingSettings.AllowPickupInStore
            && bool.TryParse(form[NopwaraCheckoutDefaults.PICKUP_IN_STORE_FIELD].FirstOrDefault(), out var pickupInStore)
            && pickupInStore;
    }

    /// <summary>
    /// Saves the selected pickup point as the shipping option (same as core ParsePickupOptionAsync + SavePickupOptionAsync)
    /// </summary>
    /// <returns>Error message or null</returns>
    public virtual async Task<string> SavePickupPointAsync(Customer customer, IList<ShoppingCartItem> cart, IFormCollection form)
    {
        var store = await _storeContext.GetCurrentStoreAsync();

        //"{id}___{providerSystemName}"
        var parts = form[NopwaraCheckoutDefaults.PICKUP_POINT_FIELD].ToString().Split(_separator, StringSplitOptions.None);
        if (parts.Length != 2 || string.IsNullOrEmpty(parts[0]))
            return await _localizationService.GetResourceAsync("Plugins.Misc.NopwaraCheckout.PickupPoints.Select");

        //pickup points are looked up by the billing address (same as core)
        var address = await _customerService.GetCustomerBillingAddressAsync(customer);
        var response = await _shippingService.GetPickupPointsAsync(cart, address, customer, parts[1], store.Id);
        var pickupPoint = response.PickupPoints.FirstOrDefault(point => point.Id.Equals(parts[0]));
        if (pickupPoint == null)
            return await _localizationService.GetResourceAsync("Plugins.Misc.NopwaraCheckout.PickupPoints.Select");

        var name = !string.IsNullOrEmpty(pickupPoint.Name)
            ? string.Format(await _localizationService.GetResourceAsync("Checkout.PickupPoints.Name"), pickupPoint.Name)
            : await _localizationService.GetResourceAsync("Checkout.PickupPoints.NullName");
        var option = new ShippingOption
        {
            Name = name,
            Rate = pickupPoint.PickupFee,
            Description = pickupPoint.Description,
            ShippingRateComputationMethodSystemName = pickupPoint.ProviderSystemName,
            IsPickupInStore = true
        };

        await _genericAttributeService.SaveAttributeAsync(customer, NopCustomerDefaults.SelectedShippingOptionAttribute, option, store.Id);
        await _genericAttributeService.SaveAttributeAsync(customer, NopCustomerDefaults.SelectedPickupPointAttribute, pickupPoint, store.Id);

        return null;
    }

    /// <summary>
    /// Clears a pickup selection after the customer switched back to delivery
    /// </summary>
    public virtual async Task ClearPickupPointAsync(Customer customer)
    {
        var store = await _storeContext.GetCurrentStoreAsync();
        var option = await _genericAttributeService.GetAttributeAsync<ShippingOption>(customer, NopCustomerDefaults.SelectedShippingOptionAttribute, store.Id);
        if (option?.IsPickupInStore == true)
            await _genericAttributeService.SaveAttributeAsync<ShippingOption>(customer, NopCustomerDefaults.SelectedShippingOptionAttribute, null, store.Id);
        await _genericAttributeService.SaveAttributeAsync<PickupPoint>(customer, NopCustomerDefaults.SelectedPickupPointAttribute, null, store.Id);
    }

    /// <summary>
    /// Parses checkout attributes from the posted form and saves them (same as core ShoppingCartController.ParseAndSaveCheckoutAttributesAsync)
    /// </summary>
    /// <returns>Ids of conditional attributes whose condition is met / not met (to show or hide them)</returns>
    public virtual async Task<(IList<int> enabledIds, IList<int> disabledIds)> SaveCheckoutAttributesAsync(Customer customer,
        IList<ShoppingCartItem> cart, IFormCollection form)
    {
        var store = await _storeContext.GetCurrentStoreAsync();
        var attributes = await GetCheckoutAttributesAsync(cart);
        var attributesXml = string.Empty;

        foreach (var attribute in attributes)
        {
            var controlId = $"{NopwaraCheckoutDefaults.CHECKOUT_ATTRIBUTE_PREFIX}{attribute.Id}";
            switch (attribute.AttributeControlType)
            {
                case AttributeControlType.DropdownList:
                case AttributeControlType.RadioList:
                case AttributeControlType.ColorSquares:
                case AttributeControlType.ImageSquares:
                    if (int.TryParse(form[controlId], out var selectedId) && selectedId > 0)
                        attributesXml = _checkoutAttributeParser.AddAttribute(attributesXml, attribute, selectedId.ToString());
                    break;

                case AttributeControlType.Checkboxes:
                    foreach (var item in form[controlId].ToString().Split(_valueSeparator, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (int.TryParse(item, out var valueId) && valueId > 0)
                            attributesXml = _checkoutAttributeParser.AddAttribute(attributesXml, attribute, valueId.ToString());
                    }
                    break;

                case AttributeControlType.ReadonlyCheckboxes:
                    //read-only values are always the preselected ones
                    foreach (var value in (await _checkoutAttributeService.GetAttributeValuesAsync(attribute.Id)).Where(value => value.IsPreSelected))
                        attributesXml = _checkoutAttributeParser.AddAttribute(attributesXml, attribute, value.Id.ToString());
                    break;

                case AttributeControlType.TextBox:
                case AttributeControlType.MultilineTextbox:
                    if (!StringValues.IsNullOrEmpty(form[controlId]))
                        attributesXml = _checkoutAttributeParser.AddAttribute(attributesXml, attribute, form[controlId].ToString().Trim());
                    break;

                case AttributeControlType.Datepicker:
                    if (int.TryParse(form[controlId + "_year"], out var year)
                        && int.TryParse(form[controlId + "_month"], out var month)
                        && int.TryParse(form[controlId + "_day"], out var day)
                        && year > 0 && month is >= 1 and <= 12 && day >= 1 && day <= DateTime.DaysInMonth(year, month))
                    {
                        attributesXml = _checkoutAttributeParser.AddAttribute(attributesXml, attribute, new DateTime(year, month, day).ToString("D"));
                    }
                    break;

                case AttributeControlType.FileUpload:
                    //uploaded on the cart page; the page posts the current download guid back
                    if (Guid.TryParse(form[controlId], out var downloadGuid) && await _downloadService.GetDownloadByGuidAsync(downloadGuid) is { } download)
                        attributesXml = _checkoutAttributeParser.AddAttribute(attributesXml, attribute, download.DownloadGuid.ToString());
                    break;
            }
        }

        //conditional attributes: drop the values of the ones whose condition isn't met
        var enabledIds = new List<int>();
        var disabledIds = new List<int>();
        foreach (var attribute in attributes)
        {
            var conditionMet = await _checkoutAttributeParser.IsConditionMetAsync(attribute.ConditionAttributeXml, attributesXml);
            if (!conditionMet.HasValue)
                continue;

            if (conditionMet.Value)
                enabledIds.Add(attribute.Id);
            else
            {
                disabledIds.Add(attribute.Id);
                attributesXml = _checkoutAttributeParser.RemoveAttribute(attributesXml, attribute.Id);
            }
        }

        await _genericAttributeService.SaveAttributeAsync(customer, NopCustomerDefaults.CheckoutAttributes, attributesXml, store.Id);

        return (enabledIds, disabledIds);
    }

    /// <summary>
    /// Validates that the current customer can check out (mirrors the checks of the core OPC actions)
    /// </summary>
    /// <returns>Error message or null</returns>
    public virtual async Task<string> ValidateCanCheckoutAsync(Customer customer, IList<ShoppingCartItem> cart)
    {
        if (_orderSettings.CheckoutDisabled)
            return await _localizationService.GetResourceAsync("Checkout.Disabled");

        if (!cart.Any())
            return await _localizationService.GetResourceAsync("ShoppingCart.CartIsEmpty");

        if (await _customerService.IsGuestAsync(customer))
        {
            if (!_orderSettings.AnonymousCheckoutAllowed)
                return "Anonymous checkout is not allowed";

            if (_customerSettings.RequireRegistrationForDownloadableProducts
                && await _productService.HasAnyDownloadableProductAsync(cart.Select(item => item.ProductId).ToArray()))
                return "Registration is required for downloadable products";
        }

        return null;
    }

    /// <summary>
    /// Gets cart validation warnings (same checks as core Checkout.Index, except checkout attributes when they are on the page)
    /// </summary>
    public virtual async Task<IList<string>> GetCartWarningsAsync(Customer customer, IList<ShoppingCartItem> cart)
    {
        var store = await _storeContext.GetCurrentStoreAsync();
        var checkoutAttributesXml = await _genericAttributeService.GetAttributeAsync<string>(customer, NopCustomerDefaults.CheckoutAttributes, store.Id);
        //checkout attributes shown on the checkout page are chosen there (and validated when the order is placed)
        var warnings = (await _shoppingCartService.GetShoppingCartWarningsAsync(cart, checkoutAttributesXml, !_settings.ShowCheckoutAttributes)).ToList();

        foreach (var item in cart)
        {
            var product = await _productService.GetProductByIdAsync(item.ProductId);
            warnings.AddRange(await _shoppingCartService.GetShoppingCartItemWarningsAsync(customer, item.ShoppingCartType, product, item.StoreId,
                item.AttributesXml, item.CustomerEnteredPrice, item.RentalStartDateUtc, item.RentalEndDateUtc, item.Quantity, false, item.Id));
        }

        return warnings;
    }

    /// <summary>
    /// Saves the billing address (existing one selected by "billing_address_id" or a new one)
    /// </summary>
    /// <returns>Errors; empty when the billing address is saved</returns>
    public virtual async Task<IList<string>> SaveBillingAddressAsync(Customer customer, CheckoutBillingAddressModel model,
        IFormCollection form, ModelStateDictionary modelState)
    {
        _ = int.TryParse(form["billing_address_id"], out var billingAddressId);
        if (billingAddressId > 0)
        {
            var existing = await _customerService.GetCustomerAddressAsync(customer.Id, billingAddressId);
            if (existing == null)
                return [await _localizationService.GetResourceAsync("Checkout.Address.NotFound")];

            customer.BillingAddressId = existing.Id;
            await _customerService.UpdateCustomerAsync(customer);
            return [];
        }

        var errors = GetModelErrors(modelState, nameof(CheckoutBillingAddressModel.BillingNewAddress), nameof(CheckoutBillingAddressModel.VatNumber));

        if (await _customerService.IsGuestAsync(customer) && _taxSettings.EuVatEnabled && _taxSettings.EuVatEnabledForGuests)
        {
            var warning = await SaveCustomerVatNumberAsync(model.VatNumber, customer);
            if (!string.IsNullOrEmpty(warning))
                errors.Add(warning);
        }

        //custom address attributes are rendered with the billing address only
        var customAttributes = await _addressAttributeParser.ParseCustomAttributesAsync(form, NopCommonDefaults.AddressAttributeControlName);
        foreach (var warning in await _addressAttributeParser.GetAttributeWarningsAsync(customAttributes))
            errors.Add(warning);

        if (errors.Any() || model.BillingNewAddress == null)
            return errors;

        var address = await FindOrCreateAddressAsync(customer, model.BillingNewAddress, customAttributes);
        customer.BillingAddressId = address.Id;
        await _customerService.UpdateCustomerAsync(customer);

        return [];
    }

    /// <summary>
    /// Saves the shipping address: the billing one ("ship to the same address") or an existing/new address
    /// </summary>
    /// <returns>Errors; empty when the shipping address is saved</returns>
    public virtual async Task<IList<string>> SaveShippingAddressAsync(Customer customer, bool shipToSameAddress,
        CheckoutShippingAddressModel model, IFormCollection form, ModelStateDictionary modelState)
    {
        var store = await _storeContext.GetCurrentStoreAsync();
        var previousShippingAddressId = customer.ShippingAddressId;

        if (shipToSameAddress && _shippingSettings.ShipToSameAddress)
        {
            var billingAddress = await _customerService.GetCustomerBillingAddressAsync(customer);
            if (billingAddress == null)
                return [];

            //by default shipping is available if the country is not specified
            var shippingAllowed = !_addressSettings.CountryEnabled
                || ((await _countryService.GetCountryByAddressAsync(billingAddress))?.AllowsShipping ?? false);
            if (!shippingAllowed)
                return [await _localizationService.GetResourceAsync("Plugins.Misc.NopwaraCheckout.ShippingNotAllowed")];

            customer.ShippingAddressId = billingAddress.Id;
        }
        else
        {
            _ = int.TryParse(form["shipping_address_id"], out var shippingAddressId);
            if (shippingAddressId > 0)
            {
                var existing = await _customerService.GetCustomerAddressAsync(customer.Id, shippingAddressId);
                if (existing == null)
                    return [await _localizationService.GetResourceAsync("Checkout.Address.NotFound")];

                customer.ShippingAddressId = existing.Id;
            }
            else
            {
                var errors = GetModelErrors(modelState, nameof(CheckoutShippingAddressModel.ShippingNewAddress));
                if (errors.Any() || model.ShippingNewAddress == null)
                    return errors;

                var address = await FindOrCreateAddressAsync(customer, model.ShippingNewAddress, string.Empty);
                customer.ShippingAddressId = address.Id;
            }
        }

        await _customerService.UpdateCustomerAsync(customer);

        //rates depend on the address: drop the selection so the first available option is preselected again
        if (previousShippingAddressId != customer.ShippingAddressId)
            await _genericAttributeService.SaveAttributeAsync<ShippingOption>(customer, NopCustomerDefaults.SelectedShippingOptionAttribute, null, store.Id);
        await _genericAttributeService.SaveAttributeAsync<PickupPoint>(customer, NopCustomerDefaults.SelectedPickupPointAttribute, null, store.Id);

        return [];
    }

    /// <summary>
    /// Saves the selected shipping option ("{name}___{shippingRateComputationMethodSystemName}")
    /// </summary>
    /// <returns>Error message or null</returns>
    public virtual async Task<string> SaveShippingOptionAsync(Customer customer, IList<ShoppingCartItem> cart, string shippingOption, IFormCollection form)
    {
        var store = await _storeContext.GetCurrentStoreAsync();

        var parts = (shippingOption ?? string.Empty).Split(_separator, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
            return await _localizationService.GetResourceAsync("ShippingMethod.SpecifyMethodError");

        var (selectedName, systemName) = (parts[0], parts[1]);

        //performance optimization: options offered on the page are cached by the checkout model factory
        var options = await _genericAttributeService.GetAttributeAsync<List<ShippingOption>>(customer, NopCustomerDefaults.OfferedShippingOptionsAttribute, store.Id);
        options = options == null || !options.Any()
            ? (await _shippingService.GetShippingOptionsAsync(cart, await _customerService.GetCustomerShippingAddressAsync(customer), customer, systemName, store.Id)).ShippingOptions.ToList()
            : options.Where(option => option.ShippingRateComputationMethodSystemName.Equals(systemName, StringComparison.InvariantCultureIgnoreCase)).ToList();

        var option = options.Find(o => !string.IsNullOrEmpty(o.Name) && o.Name.Equals(selectedName, StringComparison.InvariantCultureIgnoreCase));
        if (option == null)
            return await _localizationService.GetResourceAsync("ShippingMethod.SpecifyMethodError");

        //desired delivery date (same form key as core)
        if (_shippingSettings.AllowCustomerToChooseDeliveryDate)
        {
            var index = options.IndexOf(option);
            var value = form[$"ShippingMethods[{index}].DesiredDeliveryDate.SelectedDate"].FirstOrDefault();
            if (!string.IsNullOrEmpty(value) && DateTime.TryParse(value, out var desiredDeliveryDate))
                await _genericAttributeService.SaveAttributeAsync<DateTime?>(customer, NopCustomerDefaults.DesiredDeliveryDate, desiredDeliveryDate, store.Id);
        }

        await _genericAttributeService.SaveAttributeAsync(customer, NopCustomerDefaults.SelectedShippingOptionAttribute, option, store.Id);
        return null;
    }

    /// <summary>
    /// Saves the selected payment method and the "use reward points" choice
    /// </summary>
    /// <returns>Error message or null</returns>
    public virtual async Task<string> SavePaymentMethodAsync(Customer customer, string paymentMethodSystemName, bool useRewardPoints)
    {
        var store = await _storeContext.GetCurrentStoreAsync();

        if (_rewardPointsSettings.Enabled)
            await _genericAttributeService.SaveAttributeAsync(customer, NopCustomerDefaults.UseRewardPointsDuringCheckoutAttribute, useRewardPoints, store.Id);

        if (string.IsNullOrEmpty(paymentMethodSystemName))
            return await _localizationService.GetResourceAsync("PaymentMethod.SpecifyMethodError");

        var paymentMethod = await _paymentPluginManager.LoadPluginBySystemNameAsync(paymentMethodSystemName, customer, store.Id);
        if (!_paymentPluginManager.IsPluginActive(paymentMethod))
            return await _localizationService.GetResourceAsync("PaymentMethod.SpecifyMethodError");

        await _genericAttributeService.SaveAttributeAsync(customer, NopCustomerDefaults.SelectedPaymentMethodAttribute, paymentMethodSystemName, store.Id);
        return null;
    }

    /// <summary>
    /// Prevents two orders being placed within the configured interval (same as core CheckoutController)
    /// </summary>
    public virtual async Task<bool> IsMinimumOrderPlacementIntervalValidAsync(Customer customer)
    {
        if (_orderSettings.MinimumOrderPlacementInterval == 0)
            return true;

        var store = await _storeContext.GetCurrentStoreAsync();
        var lastOrder = (await _orderService.SearchOrdersAsync(storeId: store.Id, customerId: customer.Id, pageSize: 1)).FirstOrDefault();
        if (lastOrder == null)
            return true;

        return (DateTime.UtcNow - lastOrder.CreatedOnUtc).TotalMinutes > _orderSettings.MinimumOrderPlacementInterval;
    }

    #endregion
}
