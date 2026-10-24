using Nop.Web.Framework.Models;
using Nop.Web.Models.Checkout;
using Nop.Web.Models.ShoppingCart;

namespace Nop.Plugin.Misc.NopwaraCheckout.Models;

/// <summary>
/// Represents the single-page checkout model (all sections on one page)
/// </summary>
public record NopwaraCheckoutModel : BaseNopModel
{
    public bool ShippingRequired { get; set; }

    public CheckoutBillingAddressModel Billing { get; set; } = new();

    public int SelectedBillingAddressId { get; set; }

    public CheckoutShippingAddressModel Shipping { get; set; } = new();

    public int SelectedShippingAddressId { get; set; }

    public bool ShipToSameAddress { get; set; }

    public NopwaraShippingSectionModel ShippingSection { get; set; } = new();

    public NopwaraPaymentSectionModel PaymentSection { get; set; } = new();

    public ShoppingCartModel Cart { get; set; } = new();

    public CheckoutConfirmModel Confirm { get; set; } = new();

    public bool AllowCartEditing { get; set; }

    public bool ShowDiscountBox { get; set; }

    public bool ShowGiftCardBox { get; set; }

    public bool IsReCaptchaV3 { get; set; }

    public string ReCaptchaPublicKey { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the plugin stylesheet is loaded (the theme doesn't style the views itself)
    /// </summary>
    public bool IncludeStyles { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the plugin's Alpine.js is loaded (the theme doesn't load it)
    /// </summary>
    public bool IncludeAlpine { get; set; }

    /// <summary>
    /// Gets or sets the "returning customer" login card; null when it isn't displayed
    /// </summary>
    public NopwaraLoginModel Login { get; set; }

    /// <summary>
    /// Gets or sets pickup points; null when pickup in store isn't offered
    /// </summary>
    public CheckoutPickupPointsModel PickupPoints { get; set; }

    public bool PickupInStore { get; set; }

    public bool ShowCheckoutAttributes { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the cart page is skipped (no "Edit cart" link then)
    /// </summary>
    public bool SkipShoppingCart { get; set; }
}

/// <summary>
/// Represents the login card shown to guests
/// </summary>
public record NopwaraLoginModel : BaseNopModel
{
    public bool UsernamesEnabled { get; set; }

    public bool DisplayCaptcha { get; set; }
}

/// <summary>
/// Represents the shipping methods section
/// </summary>
public record NopwaraShippingSectionModel : BaseNopModel
{
    /// <summary>
    /// Gets or sets the shipping methods; null until a shipping address is known
    /// </summary>
    public CheckoutShippingMethodModel ShippingMethods { get; set; }
}

/// <summary>
/// Represents the payment methods + payment info section
/// </summary>
public record NopwaraPaymentSectionModel : BaseNopModel
{
    /// <summary>
    /// Gets or sets a value indicating whether payment is required (false for zero-total orders)
    /// </summary>
    public bool PaymentRequired { get; set; } = true;

    /// <summary>
    /// Gets or sets the payment methods; null until the billing address is known (methods can be filtered by country)
    /// </summary>
    public CheckoutPaymentMethodModel PaymentMethods { get; set; }

    /// <summary>
    /// Gets or sets the payment info of the selected method; null when the method has no payment form
    /// </summary>
    public CheckoutPaymentInfoModel PaymentInfo { get; set; }
}

/// <summary>
/// Represents the JSON result of an ajax checkout request
/// </summary>
public record NopwaraCheckoutResult
{
    /// <summary>
    /// Gets or sets re-rendered sections by name
    /// </summary>
    public Dictionary<string, string> Sections { get; set; } = new();

    /// <summary>
    /// Gets or sets errors by area ("billing", "shipping", "shipping-methods", "payment", "order", ...)
    /// </summary>
    public Dictionary<string, IList<string>> Errors { get; set; } = new();

    /// <summary>
    /// Gets or sets a URL to navigate to
    /// </summary>
    public string Redirect { get; set; }

    /// <summary>
    /// Gets or sets ids of conditional checkout attributes to show
    /// </summary>
    public IList<int> EnabledAttributeIds { get; set; }

    /// <summary>
    /// Gets or sets ids of conditional checkout attributes to hide
    /// </summary>
    public IList<int> DisabledAttributeIds { get; set; }

    public bool Success => !Errors.Any();
}

/// <summary>
/// Represents the "Buy now" button on product pages
/// </summary>
public record BuyNowModel : BaseNopModel
{
    public int ProductId { get; set; }

    public string AddToCartUrl { get; set; }

    public string CheckoutUrl { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the theme styles the button (Tailwind classes); otherwise core button classes are used
    /// </summary>
    public bool ThemeStyled { get; set; }
}
