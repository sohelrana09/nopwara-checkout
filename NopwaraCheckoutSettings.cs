using Nop.Core.Configuration;

namespace Nop.Plugin.Misc.NopwaraCheckout;

/// <summary>
/// Represents plugin settings
/// </summary>
/// <remarks>
/// Properties added after 1.00 have initializers: settings missing from the database (an upgraded install)
/// keep these defaults when loaded.
/// </remarks>
public class NopwaraCheckoutSettings : ISettings
{
    /// <summary>
    /// Gets or sets a value indicating whether the single-page checkout replaces the core checkout
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets a comma-separated list of theme system names the single-page checkout is used with;
    /// empty to use it with every theme
    /// </summary>
    public string AllowedThemes { get; set; }

    /// <summary>
    /// Gets or sets a comma-separated list of themes that style the checkout views themselves (Nopwara builds them
    /// into its Tailwind CSS). Other themes get the plugin's own stylesheet.
    /// </summary>
    public string ThemesWithBuiltInStyles { get; set; } = NopwaraCheckoutDefaults.NOPWARA_THEME;

    /// <summary>
    /// Gets or sets a comma-separated list of themes that already load Alpine.js. Other themes get the plugin's copy.
    /// </summary>
    public string ThemesWithAlpine { get; set; } = NopwaraCheckoutDefaults.NOPWARA_THEME;

    /// <summary>
    /// Gets or sets a value indicating whether customers can change quantities and remove items on the checkout page
    /// </summary>
    public bool AllowCartEditing { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the discount coupon box is displayed
    /// </summary>
    public bool ShowDiscountBox { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the gift card box is displayed
    /// </summary>
    public bool ShowGiftCardBox { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether guests can log in on the checkout page
    /// </summary>
    public bool ShowLoginOnCheckout { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether checkout attributes (gift wrapping etc.) are displayed on the checkout page
    /// </summary>
    public bool ShowCheckoutAttributes { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the shopping cart page is skipped (a non-empty cart opens the checkout)
    /// </summary>
    public bool SkipShoppingCart { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a "Buy now" button is displayed next to "Add to cart" on product pages
    /// </summary>
    public bool ShowBuyNowButton { get; set; } = true;
}
