using Nop.Core.Domain.Cms;
using Nop.Plugin.Misc.NopwaraCheckout.Components;
using Nop.Services.Cms;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Plugins;
using Nop.Web.Framework.Infrastructure;
using Nop.Web.Framework.Mvc.Routing;

namespace Nop.Plugin.Misc.NopwaraCheckout;

/// <summary>
/// Represents the Nopwara single-page checkout plugin (also a widget: the "Buy now" button on product pages)
/// </summary>
public class NopwaraCheckoutPlugin : BasePlugin, IMiscPlugin, IWidgetPlugin
{
    #region Fields

    private readonly ILocalizationService _localizationService;
    private readonly INopUrlHelper _nopUrlHelper;
    private readonly ISettingService _settingService;
    private readonly WidgetSettings _widgetSettings;

    #endregion

    #region Ctor

    public NopwaraCheckoutPlugin(ILocalizationService localizationService,
        INopUrlHelper nopUrlHelper,
        ISettingService settingService,
        WidgetSettings widgetSettings)
    {
        _localizationService = localizationService;
        _nopUrlHelper = nopUrlHelper;
        _settingService = settingService;
        _widgetSettings = widgetSettings;
    }

    #endregion

    #region Utilities

    /// <summary>
    /// Adds (or updates) the plugin locale resources; used by install and by updates from earlier versions
    /// </summary>
    protected virtual async Task AddLocaleResourcesAsync()
    {
        await _localizationService.AddOrUpdateLocaleResourceAsync(new Dictionary<string, string>
        {
            //admin
            ["Plugins.Misc.NopwaraCheckout.Fields.Enabled"] = "Enabled",
            ["Plugins.Misc.NopwaraCheckout.Fields.Enabled.Hint"] = "Replace the core checkout with the single-page checkout.",
            ["Plugins.Misc.NopwaraCheckout.Fields.AllowedThemes"] = "Themes",
            ["Plugins.Misc.NopwaraCheckout.Fields.AllowedThemes.Hint"] = "Comma-separated theme system names the single-page checkout is used with. Leave empty to use it with every theme; themes not listed keep the core checkout.",
            ["Plugins.Misc.NopwaraCheckout.Fields.ThemesWithBuiltInStyles"] = "Themes with built-in styles",
            ["Plugins.Misc.NopwaraCheckout.Fields.ThemesWithBuiltInStyles.Hint"] = "Comma-separated themes that style the checkout views themselves (Nopwara does). Every other theme gets the plugin's own stylesheet, scoped to the checkout page.",
            ["Plugins.Misc.NopwaraCheckout.Fields.ThemesWithAlpine"] = "Themes that load Alpine.js",
            ["Plugins.Misc.NopwaraCheckout.Fields.ThemesWithAlpine.Hint"] = "Comma-separated themes that already load Alpine.js 3 (Nopwara does). Every other theme gets the plugin's copy on the checkout page.",
            ["Plugins.Misc.NopwaraCheckout.Fields.AllowCartEditing"] = "Allow cart editing",
            ["Plugins.Misc.NopwaraCheckout.Fields.AllowCartEditing.Hint"] = "Let customers change quantities and remove items on the checkout page.",
            ["Plugins.Misc.NopwaraCheckout.Fields.ShowDiscountBox"] = "Show discount box",
            ["Plugins.Misc.NopwaraCheckout.Fields.ShowDiscountBox.Hint"] = "Display the discount coupon box on the checkout page.",
            ["Plugins.Misc.NopwaraCheckout.Fields.ShowGiftCardBox"] = "Show gift card box",
            ["Plugins.Misc.NopwaraCheckout.Fields.ShowGiftCardBox.Hint"] = "Display the gift card box on the checkout page.",
            ["Plugins.Misc.NopwaraCheckout.Fields.ShowLoginOnCheckout"] = "Show login",
            ["Plugins.Misc.NopwaraCheckout.Fields.ShowLoginOnCheckout.Hint"] = "Let guests log in on the checkout page (\"Returning customer?\"). Their cart is kept and their saved addresses appear.",
            ["Plugins.Misc.NopwaraCheckout.Fields.ShowCheckoutAttributes"] = "Show checkout attributes",
            ["Plugins.Misc.NopwaraCheckout.Fields.ShowCheckoutAttributes.Hint"] = "Display checkout attributes (gift wrapping, delivery notes...) on the checkout page. File upload attributes stay on the cart page.",
            ["Plugins.Misc.NopwaraCheckout.Fields.SkipShoppingCart"] = "Skip shopping cart",
            ["Plugins.Misc.NopwaraCheckout.Fields.SkipShoppingCart.Hint"] = "Open the checkout page instead of the shopping cart page when the cart isn't empty (items can be edited on the checkout page). Carts with warnings still show the cart page.",
            ["Plugins.Misc.NopwaraCheckout.Fields.ShowBuyNowButton"] = "Show \"Buy now\" button",
            ["Plugins.Misc.NopwaraCheckout.Fields.ShowBuyNowButton.Hint"] = "Display a \"Buy now\" button next to \"Add to cart\" on product pages: it adds the product and opens the checkout. Rendered in the ProductDetailsAddInfo widget zone (the plugin must be active in Widgets).",

            //storefront
            ["Plugins.Misc.NopwaraCheckout.OrderSummary"] = "Order summary",
            ["Plugins.Misc.NopwaraCheckout.PlaceOrder"] = "Place order",
            ["Plugins.Misc.NopwaraCheckout.ShippingMethods.EnterAddress"] = "Enter your shipping address to see the available shipping methods.",
            ["Plugins.Misc.NopwaraCheckout.PaymentMethods.EnterAddress"] = "Enter your billing address to see the available payment methods.",
            ["Plugins.Misc.NopwaraCheckout.PaymentNotRequired"] = "No payment is required for this order.",
            ["Plugins.Misc.NopwaraCheckout.Items"] = "{0} item(s)",
            ["Plugins.Misc.NopwaraCheckout.EditCart"] = "Edit cart",
            ["Plugins.Misc.NopwaraCheckout.ContinueShopping"] = "Continue shopping",
            ["Plugins.Misc.NopwaraCheckout.ShowSummary"] = "Show order summary",
            ["Plugins.Misc.NopwaraCheckout.HideSummary"] = "Hide order summary",
            ["Plugins.Misc.NopwaraCheckout.FixErrors"] = "Please check the highlighted fields.",
            ["Plugins.Misc.NopwaraCheckout.ShippingNotAllowed"] = "We can't ship to the billing address country. Please enter a different shipping address.",
            ["Plugins.Misc.NopwaraCheckout.SecureCheckout"] = "Secure checkout",
            ["Plugins.Misc.NopwaraCheckout.Login.Title"] = "Returning customer?",
            ["Plugins.Misc.NopwaraCheckout.Login.Toggle"] = "Log in",
            ["Plugins.Misc.NopwaraCheckout.Login.Intro"] = "Log in to use your saved addresses. Your cart is kept.",
            ["Plugins.Misc.NopwaraCheckout.Login.Submit"] = "Log in and continue",
            ["Plugins.Misc.NopwaraCheckout.Login.Required"] = "Please enter your login details.",
            ["Plugins.Misc.NopwaraCheckout.Delivery"] = "Delivery",
            ["Plugins.Misc.NopwaraCheckout.Delivery.Ship"] = "Ship to an address",
            ["Plugins.Misc.NopwaraCheckout.Delivery.Pickup"] = "Pick up in store",
            ["Plugins.Misc.NopwaraCheckout.PickupPoints.Select"] = "Please select a pickup point.",
            ["Plugins.Misc.NopwaraCheckout.PickupPoints.OpeningHours"] = "Opening hours",
            ["Plugins.Misc.NopwaraCheckout.PickupPoints.Free"] = "Free",
            ["Plugins.Misc.NopwaraCheckout.CheckoutAttributes"] = "Order options",
            ["Plugins.Misc.NopwaraCheckout.CheckoutAttributes.FileUpload"] = "Files are uploaded on the shopping cart page.",
            ["Plugins.Misc.NopwaraCheckout.BuyNow"] = "Buy now",
            ["Plugins.Misc.NopwaraCheckout.BuyNow.Error"] = "The product could not be added. Please try again."
        });
    }

    /// <summary>
    /// Activates the widget ("Buy now" button)
    /// </summary>
    protected virtual async Task ActivateWidgetAsync()
    {
        if (_widgetSettings.ActiveWidgetSystemNames.Contains(NopwaraCheckoutDefaults.SYSTEM_NAME))
            return;

        _widgetSettings.ActiveWidgetSystemNames.Add(NopwaraCheckoutDefaults.SYSTEM_NAME);
        await _settingService.SaveSettingAsync(_widgetSettings);
    }

    #endregion

    #region Methods

    /// <summary>
    /// Gets a configuration page URL
    /// </summary>
    public override string GetConfigurationPageUrl()
    {
        return _nopUrlHelper.RouteUrl(NopwaraCheckoutDefaults.CONFIGURATION_ROUTE_NAME);
    }

    /// <summary>
    /// Gets widget zones where this widget should be rendered
    /// </summary>
    public Task<IList<string>> GetWidgetZonesAsync()
    {
        //inside _AddToCart, right after the "Add to cart" button
        return Task.FromResult<IList<string>>(new List<string> { PublicWidgetZones.ProductDetailsAddInfo });
    }

    /// <summary>
    /// Gets a type of a view component for displaying widget
    /// </summary>
    public Type GetWidgetViewComponent(string widgetZone)
    {
        return typeof(BuyNowViewComponent);
    }

    /// <summary>
    /// Install the plugin
    /// </summary>
    public override async Task InstallAsync()
    {
        await _settingService.SaveSettingAsync(new NopwaraCheckoutSettings
        {
            Enabled = false,
            AllowedThemes = string.Empty,
            ThemesWithBuiltInStyles = NopwaraCheckoutDefaults.NOPWARA_THEME,
            ThemesWithAlpine = NopwaraCheckoutDefaults.NOPWARA_THEME,
            AllowCartEditing = true,
            ShowDiscountBox = true,
            ShowGiftCardBox = true,
            ShowLoginOnCheckout = true,
            ShowCheckoutAttributes = true,
            SkipShoppingCart = false,
            ShowBuyNowButton = true
        });

        await ActivateWidgetAsync();
        await AddLocaleResourcesAsync();

        await base.InstallAsync();
    }

    /// <summary>
    /// Update the plugin (new locale resources; new settings load with their defaults)
    /// </summary>
    public override async Task UpdateAsync(string currentVersion, string targetVersion)
    {
        await ActivateWidgetAsync();
        await AddLocaleResourcesAsync();

        await base.UpdateAsync(currentVersion, targetVersion);
    }

    /// <summary>
    /// Uninstall the plugin
    /// </summary>
    public override async Task UninstallAsync()
    {
        if (_widgetSettings.ActiveWidgetSystemNames.Contains(NopwaraCheckoutDefaults.SYSTEM_NAME))
        {
            _widgetSettings.ActiveWidgetSystemNames.Remove(NopwaraCheckoutDefaults.SYSTEM_NAME);
            await _settingService.SaveSettingAsync(_widgetSettings);
        }

        await _settingService.DeleteSettingAsync<NopwaraCheckoutSettings>();
        await _localizationService.DeleteLocaleResourcesAsync("Plugins.Misc.NopwaraCheckout");

        await base.UninstallAsync();
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets a value indicating whether to hide this plugin on the widget list page in the admin area
    /// </summary>
    public bool HideInWidgetList => false;

    #endregion
}
