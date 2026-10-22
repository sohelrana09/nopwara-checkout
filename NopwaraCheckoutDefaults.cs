namespace Nop.Plugin.Misc.NopwaraCheckout;

/// <summary>
/// Represents plugin constants
/// </summary>
public static class NopwaraCheckoutDefaults
{
    /// <summary>
    /// Gets the plugin system name
    /// </summary>
    public const string SYSTEM_NAME = "Misc.NopwaraCheckout";

    /// <summary>
    /// Gets the name of the storefront checkout route
    /// </summary>
    public const string CHECKOUT_ROUTE_NAME = "Plugin.Misc.NopwaraCheckout.Checkout";

    /// <summary>
    /// Gets the name of the route that completes redirection payments after the order is placed
    /// </summary>
    public const string COMPLETE_REDIRECTION_PAYMENT_ROUTE_NAME = "Plugin.Misc.NopwaraCheckout.CompleteRedirectionPayment";

    /// <summary>
    /// Gets the name of the admin configuration route
    /// </summary>
    public const string CONFIGURATION_ROUTE_NAME = "Plugin.Misc.NopwaraCheckout.Configure";

    /// <summary>
    /// Gets the storefront controller name (used to scope the plugin view location expander)
    /// </summary>
    public const string CONTROLLER_NAME = "NopwaraCheckout";

    /// <summary>
    /// Gets the folder (under Nop.Web/Plugins) the plugin is deployed to
    /// </summary>
    public const string PLUGIN_FOLDER = "Misc.NopwaraCheckout";

    /// <summary>
    /// Gets the separator used in shipping option / pickup point values ("{name}___{systemName}")
    /// </summary>
    public const string OPTION_SEPARATOR = "___";

    /// <summary>
    /// Gets the system name of the Nopwara theme (styles the checkout views and loads Alpine.js itself)
    /// </summary>
    public const string NOPWARA_THEME = "Nopwara";

    /// <summary>
    /// Gets the form key that switches the shipping card to pickup in store (same as core)
    /// </summary>
    public const string PICKUP_IN_STORE_FIELD = "PickupInStore";

    /// <summary>
    /// Gets the form key of the selected pickup point ("{id}___{providerSystemName}", same as core)
    /// </summary>
    public const string PICKUP_POINT_FIELD = "pickup-points-id";

    /// <summary>
    /// Gets the prefix of checkout attribute controls (same as core: checkout_attribute_{id})
    /// </summary>
    public const string CHECKOUT_ATTRIBUTE_PREFIX = "checkout_attribute_";
}
