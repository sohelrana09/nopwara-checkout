using Nop.Web.Framework.Models;
using Nop.Web.Framework.Mvc.ModelBinding;

namespace Nop.Plugin.Misc.NopwaraCheckout.Models;

/// <summary>
/// Represents the plugin configuration model
/// </summary>
public record ConfigurationModel : BaseNopModel
{
    [NopResourceDisplayName("Plugins.Misc.NopwaraCheckout.Fields.Enabled")]
    public bool Enabled { get; set; }

    [NopResourceDisplayName("Plugins.Misc.NopwaraCheckout.Fields.AllowedThemes")]
    public string AllowedThemes { get; set; }

    [NopResourceDisplayName("Plugins.Misc.NopwaraCheckout.Fields.AllowCartEditing")]
    public bool AllowCartEditing { get; set; }

    [NopResourceDisplayName("Plugins.Misc.NopwaraCheckout.Fields.ShowDiscountBox")]
    public bool ShowDiscountBox { get; set; }

    [NopResourceDisplayName("Plugins.Misc.NopwaraCheckout.Fields.ShowGiftCardBox")]
    public bool ShowGiftCardBox { get; set; }

    [NopResourceDisplayName("Plugins.Misc.NopwaraCheckout.Fields.ThemesWithBuiltInStyles")]
    public string ThemesWithBuiltInStyles { get; set; }

    [NopResourceDisplayName("Plugins.Misc.NopwaraCheckout.Fields.ThemesWithAlpine")]
    public string ThemesWithAlpine { get; set; }

    [NopResourceDisplayName("Plugins.Misc.NopwaraCheckout.Fields.ShowLoginOnCheckout")]
    public bool ShowLoginOnCheckout { get; set; }

    [NopResourceDisplayName("Plugins.Misc.NopwaraCheckout.Fields.ShowCheckoutAttributes")]
    public bool ShowCheckoutAttributes { get; set; }

    [NopResourceDisplayName("Plugins.Misc.NopwaraCheckout.Fields.SkipShoppingCart")]
    public bool SkipShoppingCart { get; set; }

    [NopResourceDisplayName("Plugins.Misc.NopwaraCheckout.Fields.ShowBuyNowButton")]
    public bool ShowBuyNowButton { get; set; }
}
