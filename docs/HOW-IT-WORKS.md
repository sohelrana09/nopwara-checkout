# How the Nopwara single-page checkout works

A guided tour of the code, for developers who want to read, change or extend it. If you know Magento, the comparisons along the way map nopCommerce ideas to Magento ones.

## Contents

1. [The idea in one picture](#1-the-idea-in-one-picture)
2. [nopCommerce in Magento terms](#2-nopcommerce-in-magento-terms)
3. [File map](#3-file-map)
4. [How the plugin plugs in](#4-how-the-plugin-plugs-in)
5. [Where checkout state lives](#5-where-checkout-state-lives)
6. [Opening the checkout page](#6-opening-the-checkout-page)
7. [The ajax loop: change → save → re-render](#7-the-ajax-loop-change--save--re-render)
8. [Each section in detail](#8-each-section-in-detail)
9. [Placing the order](#9-placing-the-order)
10. [Features outside the checkout page](#10-features-outside-the-checkout-page)
11. [Working with any theme](#11-working-with-any-theme)
12. [Install, update, settings](#12-install-update-settings)
13. [Extending it](#13-extending-it)
14. [Gotchas](#14-gotchas)

---

## 1. The idea in one picture

The core nopCommerce checkout is a sequence of steps: billing → shipping address → shipping method → payment method → payment info → confirm. Its "one-page checkout" is still those same steps, shown as an accordion.

This plugin puts **every section on one page at once** and saves each change on its own:

```
 Browser (Alpine.js component "nopwaraCheckout")             Server (NopwaraCheckoutController)
 ────────────────────────────────────────────────            ─────────────────────────────────────
 customer changes something (address, method, qty…)
   │
   ├─ POST /checkout/onepage/{action}/  (whole form) ──────▶ save that one change (service)
   │                                                         re-render only the sections that depend on it
   │                                                         ◀── JSON { Sections, Errors, Redirect }
   └─ swap those sections' HTML, show errors

 "Place order" ── POST /checkout/onepage/placeorder/ ──────▶ save EVERYTHING again from the form,
                                                             validate, place the order (core service)
                                                             ◀── { Redirect: "/checkout/completed/123" }
```

Two rules hold everything together:

- **The server is the source of truth.** The browser never computes totals or decides which methods are available. It sends the form and shows the HTML the server returns.
- **State lives where core keeps it.** Addresses and selections are saved exactly where the core checkout saves them (§5), so core's order placement validates and uses them unchanged.

## 2. nopCommerce in Magento terms

| nopCommerce / ASP.NET Core | Closest Magento idea | In this plugin |
|---|---|---|
| Plugin (`plugin.json` + a class deriving from `BasePlugin`) | Module (`module.xml`, `registration.php`) plus `Setup/` install scripts | [`plugin.json`](../plugin.json), [`NopwaraCheckoutPlugin.cs`](../NopwaraCheckoutPlugin.cs) |
| `INopStartup.ConfigureServices` | `di.xml` | [`Infrastructure/PluginNopStartup.cs`](../Infrastructure/PluginNopStartup.cs) |
| `IRouteProvider` | `routes.xml` | [`Infrastructure/RouteProvider.cs`](../Infrastructure/RouteProvider.cs) |
| Controller action | Controller action class (`execute()`) | [`Controllers/NopwaraCheckoutController.cs`](../Controllers/NopwaraCheckoutController.cs) |
| Service class | Service / model / repository | [`Services/NopwaraCheckoutService.cs`](../Services/NopwaraCheckoutService.cs) |
| Model factory + view model | Block / ViewModel that prepares template data | [`Factories/NopwaraCheckoutModelFactory.cs`](../Factories/NopwaraCheckoutModelFactory.cs), [`Models/`](../Models) |
| Razor view (`.cshtml`) | `.phtml` template | [`Views/`](../Views) |
| Partial view (`Html.PartialAsync`) | Child block / `getChildHtml()` | `_Cart`, `_Payment`, `_ShippingMethods`… |
| View component (`InvokeAsync` + its own view) | A block with its own class and template | [`Components/BuyNowViewComponent.cs`](../Components/BuyNowViewComponent.cs) |
| Widget zone | Layout container that other modules add blocks to | `ProductDetailsAddInfo` (Buy now) |
| Action filter (`IAsyncActionFilter`) | Plugin (interceptor) `around` a controller, or a `controller_action_predispatch` observer | [`Infrastructure/NopwaraCheckoutRedirectFilter.cs`](../Infrastructure/NopwaraCheckoutRedirectFilter.cs) |
| Event consumer (`IConsumer<T>`) | Observer (`events.xml`) | [`Services/MiniShoppingCartModelConsumer.cs`](../Services/MiniShoppingCartModelConsumer.cs) |
| Settings class (`ISettings`) | `system.xml` config values (`core_config_data`) | [`NopwaraCheckoutSettings.cs`](../NopwaraCheckoutSettings.cs) |
| Locale resources (`T("key")`) | `i18n/*.csv` translations | added in `AddLocaleResourcesAsync` |
| `IViewLocationExpander` | Theme fallback for templates | [`Infrastructure/NopwaraCheckoutViewLocationExpander.cs`](../Infrastructure/NopwaraCheckoutViewLocationExpander.cs) |
| Generic attributes (key/value per customer) | Fields on the quote | §5 |

C# notes for PHP developers:
- `async Task<T>` / `await` is like a promise you wait for. Every database call in nopCommerce is async, which is why every method is.
- Constructor parameters are dependency injection, as in Magento: the container builds the class and passes the services in.
- `protected virtual` means a subclass can override the method. That's how you customise without editing this code (§13).

## 3. File map

```
Nop.Plugin.Misc.NopwaraCheckout/
├── plugin.json                         plugin descriptor: system name, version, supported nopCommerce versions
├── NopwaraCheckoutPlugin.cs            install / update / uninstall, widget zones, text strings
├── NopwaraCheckoutSettings.cs          settings stored in the Setting table
├── NopwaraCheckoutDefaults.cs          constants: route names, form field names
├── Controllers/
│   ├── NopwaraCheckoutController.cs        the checkout page + all ajax endpoints + place order
│   └── NopwaraCheckoutAdminController.cs   admin configuration page
├── Services/
│   ├── NopwaraCheckoutService.cs           saves each piece of checkout state (addresses, methods, pickup, attributes)
│   └── MiniShoppingCartModelConsumer.cs    shows the mini cart's Checkout button
├── Factories/
│   └── NopwaraCheckoutModelFactory.cs      builds the view models (mostly by calling core factories)
├── Models/                                 view models + the JSON result of ajax calls
├── Components/
│   └── BuyNowViewComponent.cs              "Buy now" button on product pages (widget)
├── Infrastructure/
│   ├── PluginNopStartup.cs                 DI registrations, global filter, view expander
│   ├── RouteProvider.cs                    URLs
│   ├── NopwaraCheckoutRedirectFilter.cs    sends core checkout pages (and optionally the cart) here
│   └── NopwaraCheckoutViewLocationExpander.cs   where views are looked up (theme overrides first)
├── Views/
│   ├── Checkout.cshtml                     the page: every card + the Alpine root
│   ├── _ShippingMethods / _Payment / _Cart / _Coupons / _Totals.cshtml   sections re-rendered by ajax
│   ├── _Login / _PickupPoints / _CheckoutAttributes.cshtml              feature cards
│   ├── BuyNow.cshtml                       the Buy now button
│   └── Admin/Configure.cshtml              admin settings form
├── Content/
│   ├── js/nopwara-checkout.js              the Alpine component (all browser logic for the page)
│   ├── js/nopwara-buynow.js                Buy now click handler
│   ├── js/alpine.min.js                    Alpine.js, loaded only for themes that don't load it
│   └── css/nopwara-checkout.min.css            built stylesheet, loaded only for themes that don't style the views
└── tailwind/                               source of nopwara-checkout.min.css (npm run build:css)
```

## 4. How the plugin plugs in

nopCommerce has no central config file. At startup it scans every loaded assembly, plugins included, and wires up what it finds.

**Discovered automatically**, with no registration needed:
- controllers, view components and event consumers (`IConsumer<T>`);
- `IRouteProvider` and `INopStartup` implementations.

**Registered by hand** in [`PluginNopStartup.ConfigureServices`](../Infrastructure/PluginNopStartup.cs#L22):
- the service, the factory and the redirect filter (`AddScoped`: one instance per HTTP request, like a Magento shared instance per request);
- the redirect filter as a **global MVC filter** ([line 29](../Infrastructure/PluginNopStartup.cs#L29)), so it runs before every controller action in the store;
- the view location expander ([line 32](../Infrastructure/PluginNopStartup.cs#L32)).

**URLs** ([`RouteProvider.cs`](../Infrastructure/RouteProvider.cs)):

| URL | Action |
|---|---|
| `/checkout/onepage/` | `Index`: the page |
| `/checkout/onepage/{action}/` | the ajax endpoints: `saveaddresses`, `selectshipping`, `selectpayment`, `checkoutattributes`, `updatecart`, `applydiscountcoupon`, `removediscountcoupon`, `applygiftcard`, `removegiftcard`, `login`, `placeorder` |
| `/checkout/onepage/complete-payment/` | `CompleteRedirectionPayment`: hands off to PayPal-style payment providers after the order is placed |
| `/Admin/NopwaraCheckout/Configure` | admin settings |

Core's own one-page checkout lives at `/onepagecheckout/`, so the two never clash. Customers are sent to the plugin's URL by the redirect filter below.

**Taking over the core checkout** ([`NopwaraCheckoutRedirectFilter`](../Infrastructure/NopwaraCheckoutRedirectFilter.cs#L88)). When the customer clicks Checkout on the cart page:
1. Core `CheckoutController.Index` still runs first. It validates the cart, resets old checkout data, and redirects to core's one-page or multi-step checkout.
2. The filter catches those GET requests (`OnePageCheckout`, `BillingAddress`, `ShippingAddress`, `ShippingMethod`, `PaymentMethod`, `PaymentInfo`, `Confirm`) and redirects them to this plugin's route.
3. If the plugin is disabled, or the theme isn't allowed (`IsActiveAsync`), the filter does nothing and core's checkout runs as usual. Turning the plugin off is always safe.

## 5. Where checkout state lives

nopCommerce has no "quote" object like Magento. The cart is a list of `ShoppingCartItem` rows, and checkout choices are stored **on the customer**, per store:

| What | Where | Core constant |
|---|---|---|
| Billing / shipping address | `Customer.BillingAddressId` / `ShippingAddressId` (the addresses live in the customer's address book) | n/a |
| Chosen shipping method | generic attribute, a serialized `ShippingOption` | `NopCustomerDefaults.SelectedShippingOptionAttribute` |
| Shipping methods last offered (a cache) | generic attribute | `OfferedShippingOptionsAttribute` |
| Chosen pickup point | generic attribute, a serialized `PickupPoint` | `SelectedPickupPointAttribute` |
| Chosen payment method | generic attribute (system name) | `SelectedPaymentMethodAttribute` |
| Use reward points | generic attribute | `UseRewardPointsDuringCheckoutAttribute` |
| Checkout attributes (gift wrapping…) | generic attribute (XML) | `CheckoutAttributes` |
| Payment form data (card fields…) | session, through `IOrderProcessingService.SetProcessPaymentRequestAsync` | n/a |

"Generic attributes" are rows in the `GenericAttribute` table (entity, key, value, store id), nopCommerce's key/value store for anything that isn't a column. The service reads and writes them through `IGenericAttributeService`.

Because the plugin uses exactly these places, core's `IOrderProcessingService.PlaceOrderAsync` finds everything it expects, and nothing about order placement is reimplemented.

## 6. Opening the checkout page

[`NopwaraCheckoutController.Index`](../Controllers/NopwaraCheckoutController.cs#L229):

1. Not active (plugin disabled or theme not allowed) → redirect to the core checkout.
2. Checkout disabled, or empty cart → redirect to the cart.
3. The customer can't check out → `Challenge()`, which sends guests to the login page. That happens when anonymous checkout is off, or when downloadable products require registration (`ValidateCanCheckoutAsync`, [service line 405](../Services/NopwaraCheckoutService.cs#L405)).
4. Cart warnings → back to the cart page, which shows them. Same checks as core, except checkout attributes when they're shown on this page ([`GetCartWarningsAsync`](../Services/NopwaraCheckoutService.cs#L429)).
5. [`PrepareCheckoutModelAsync`](../Factories/NopwaraCheckoutModelFactory.cs#L135) builds one big `NopwaraCheckoutModel`, mostly by calling core factories:
   - billing and shipping address models: core `ICheckoutModelFactory`;
   - shipping section, payment section and cart: §8;
   - the confirm model (terms of service, minimum order total warning, captcha): core;
   - plugin-only pieces: the login card model (guests only), pickup points, "include styles/Alpine" flags, skip-cart flag.
6. The view `Checkout` is rendered. The short name works because of the view expander (§11).

**The view** ([`Views/Checkout.cshtml`](../Views/Checkout.cshtml)):
- The root `<div id="nopwara-checkout" x-ignore x-data="nopwaraCheckout(@config)">` ([line 56](../Views/Checkout.cshtml#L56)) carries a JSON config: URLs, initial selections and translated texts.
- **One `<form id="nopwara-checkout-form">`** ([line 80](../Views/Checkout.cshtml#L80)) holds every input on the page. Any request can post the whole form, and "Place order" always does.
- The login card is a separate form **outside** it ([line 77](../Views/Checkout.cshtml#L77)), so checkout fields are never posted to the login endpoint.
- Re-renderable parts are wrapped in `<div data-section="…">`: `shipping-methods`, `payment`, `cart`, `coupons`, `totals`.
- Address cards carry `data-address="billing|shipping"` so the script knows typing there means "save addresses".
- Step numbers come from a CSS counter ([line 89](../Views/Checkout.cshtml#L89)). When pickup hides the shipping-method card, the remaining steps renumber themselves.

## 7. The ajax loop: change → save → re-render

### Browser side ([`Content/js/nopwara-checkout.js`](../Content/js/nopwara-checkout.js))

It's one Alpine component. Alpine is a small library (like a lightweight Vue) that binds a JavaScript object to the HTML through `x-data`, `x-show`, `x-text` and so on.

**Starting up** ([line 425](../Content/js/nopwara-checkout.js#L425)). The root is rendered with `x-ignore`, so Alpine skips it while it boots. On `DOMContentLoaded` the script registers the component, removes `x-ignore`, and calls `Alpine.initTree(root)`. This works whatever order the theme loads its scripts in, bundled or not.

**Delegated events.** The form has four listeners: `onChange`, `onInput`, `onClick` and `onSubmit` ([lines 288–360](../Content/js/nopwara-checkout.js#L288)). Server-rendered HTML that gets swapped in needs no Alpine attributes of its own, because events bubble up to the form:

| Event | What it does |
|---|---|
| typing in an address field | `scheduleAddressSave()`: waits 600 ms, then `saveaddresses` |
| changing country/state, or picking an existing address | saves the addresses |
| `shippingoption` or `pickup-points-id` radio | `selectshipping` |
| `paymentmethod` radio or reward points | `selectpayment` |
| `checkout_attribute_*` field | `checkoutattributes` |
| quantity buttons (`data-cart-qty`) or the select (`data-cart-select`) | `updatecart` |
| coupon and gift card submit buttons | the matching `apply…` / `remove…` |
| "Place order" | `placeorder` |

**One request at a time** ([`post`, line 61](../Content/js/nopwara-checkout.js#L61)). Each call is chained onto a promise (`this._queue = this._queue.then(run, run)`, [line 94](../Content/js/nopwara-checkout.js#L94)). If the customer changes the shipping method while an address save is still running, the second request waits, so responses can't arrive out of order and overwrite each other.

`post(action, options)` takes:
- `includeForm`: send the whole form (most actions) or only `data`;
- `sections`: names to mark busy (greyed out) while the request runs;
- `areas`: the error areas this request "owns". Their old errors are cleared when the answer arrives, and other areas keep theirs.

**Applying the answer** ([`apply`, line 111](../Content/js/nopwara-checkout.js#L111)):
1. If `Redirect` is set, go there. That covers order placed, cart emptied and login done.
2. Each returned section is swapped with jQuery's `.html()`, not `innerHTML`. jQuery runs the `<script>` tags inside the HTML, which payment plugins and core partials rely on.
3. Errors are merged into `this.errors`. The page shows them with `x-text`, which treats them as text, so they're safe from HTML injection.
4. Conditional checkout attributes are shown or hidden (§8.5).

`normalize` ([line 99](../Content/js/nopwara-checkout.js#L99)) accepts both `Sections` and `sections`, because nopCommerce's `Json()` keeps C# PascalCase names.

### Server side

Every ajax action follows the same shape. Here is `SelectPayment` ([line 334](../Controllers/NopwaraCheckoutController.cs#L334)):

```csharp
var (customer, cart) = await GetCartAsync();
var result = await ValidateRequestAsync(customer, cart) ?? new NopwaraCheckoutResult();   // checkout disabled? empty cart? guest not allowed?
if (!result.Success)
    return Json(result);

var error = await _checkoutService.SavePaymentMethodAsync(customer, paymentmethod, useRewardPoints);   // 1. save the change
if (!string.IsNullOrEmpty(error))
    result.Errors["payment"] = [error];

await RenderSectionsAsync(result, cart, null, SECTION_PAYMENT, SECTION_TOTALS);   // 2. re-render what depends on it
return Json(result);
```

The response ([`NopwaraCheckoutResult`](../Models/NopwaraCheckoutModel.cs#L129)) looks like:

```json
{
  "Sections": { "payment": "<div class=\"section payment-method\">…</div>", "totals": "<div class=\"total-info\">…</div>" },
  "Errors":   { "payment": ["Please select a payment method"] },
  "Redirect": null,
  "EnabledAttributeIds": null,
  "DisabledAttributeIds": null,
  "Success": false
}
```

[`RenderSectionsAsync`](../Controllers/NopwaraCheckoutController.cs#L150) renders partial views to strings. It always renders shipping before payment, because payment fees and availability can depend on the shipping choice.

Which action re-renders what:

| Action | Saves | Re-renders |
|---|---|---|
| `saveaddresses` | billing (+ shipping) address | shipping methods, payment, totals |
| `selectshipping` | shipping method **or** pickup point | shipping methods, payment, totals |
| `selectpayment` | payment method, reward points | payment, totals |
| `checkoutattributes` | checkout attributes | shipping methods, payment, totals |
| `updatecart` | a cart item's quantity (0 = remove) | cart, coupons, shipping methods, payment, totals |
| `applydiscountcoupon` / `removediscountcoupon` | the coupon | cart, coupons, shipping methods, payment, totals |
| `applygiftcard` / `removegiftcard` | the gift card | coupons, payment, totals |

Errors from any action are caught by [`HandleErrorAsync`](../Controllers/NopwaraCheckoutController.cs#L190), which logs a warning and returns the message in the `order` error area.

## 8. Each section in detail

### 8.1 Addresses

The billing and shipping cards use core's `_CreateOrUpdateAddress` partial, so they include every field and address setting core supports: required fields, country/state, custom address attributes. The partial is rendered with an HTML field prefix (`BillingNewAddress.` / `ShippingNewAddress.`), so ASP.NET binds the posted fields straight into `CheckoutBillingAddressModel` and `CheckoutShippingAddressModel`. Model binding works like this: form field names map onto C# object properties.

The browser only saves when the visible address looks complete (`canSaveAddresses`, [line 166](../Content/js/nopwara-checkout.js#L166)): required fields are filled and emails look like emails. The real validation is on the server:

- [`SaveBillingAddressAsync`](../Services/NopwaraCheckoutService.cs#L450): `billing_address_id > 0` uses an address from the address book. Otherwise it collects the binding errors for `BillingNewAddress.*`, checks custom address attributes and the EU VAT number, then finds an identical existing address or creates one (`FindOrCreateAddressAsync`, so no duplicates).
- [`SaveShippingAddressAsync`](../Services/NopwaraCheckoutService.cs#L493): "ship to the same address" copies the billing address id, but only if that country allows shipping. Otherwise it works like billing. If the address changed, it clears the chosen shipping method, so the first method for the new address is preselected.
- The controller's [`SaveAddressesAsync`](../Controllers/NopwaraCheckoutController.cs#L202) skips the shipping address when the cart needs no shipping, or when pickup in store is chosen.

### 8.2 Shipping methods

[`PrepareShippingSectionAsync`](../Factories/NopwaraCheckoutModelFactory.cs#L204):
1. No shipping address yet → the section says "Enter your shipping address…".
2. It calls core `PrepareShippingMethodModelAsync`, which asks every active shipping provider for rates and caches them in `OfferedShippingOptionsAttribute`.
3. **Pickup guard:** if the saved option is a pickup point, it leaves it alone.
4. Core *shows* a preselected method but doesn't *save* it. The factory saves it whenever the shown one differs from the saved one, including the same method at a new price after a quantity change. That keeps the totals in step with what the customer sees.

Choosing a method posts `shippingoption = "{name}___{providerSystemName}"`. [`SaveShippingOptionAsync`](../Services/NopwaraCheckoutService.cs#L549) finds that option in the cached offer (or asks the provider again) and saves it, along with the desired delivery date when that feature is on.

### 8.3 Payment

[`PreparePaymentSectionAsync`](../Factories/NopwaraCheckoutModelFactory.cs#L325):
- Order total is 0 → no payment needed; it clears the saved method.
- Waits for a billing address when countries are enabled, since payment methods can be restricted by country.
- Core `PreparePaymentMethodModelAsync` builds the list. As with shipping, the preselected method is saved.
- The **payment form** of the selected method (a card form, for example) is rendered inline under that method. It's the payment plugin's own view component. Its fields belong to the main form, so "Place order" posts them.

`IsPaymentInfoRequired` ([line 381](../Factories/NopwaraCheckoutModelFactory.cs#L381)) mirrors core: no form when the method says `SkipPaymentInfo`, or when it's a redirection method and the store skips that step.

### 8.4 Cart, coupons, gift cards, totals

- `_Cart` is a compact summary of core's `ShoppingCartModel`. Quantity changes call `updatecart`, which uses core `UpdateShoppingCartItemAsync`, so stock and quantity rules still apply. If the last item is removed, the response redirects to the (now empty) cart page.
- `_Coupons` renders core's own discount and gift card box partials. Their submit buttons (`applydiscountcouponcode`, `removediscount-{id}`, …) are turned into ajax calls in `onSubmit`. The controller actions copy core's `ShoppingCartController` logic, messages included.
- `_Totals` renders core's `OrderTotals` view component, so taxes, discounts, reward points and fees are computed by core.

### 8.5 Checkout attributes ("Order options")

Store-defined questions such as gift wrapping, shown in [`_CheckoutAttributes.cshtml`](../Views/_CheckoutAttributes.cshtml).

The controls use **core's field names**: `checkout_attribute_{id}`, plus `_day`, `_month` and `_year` for dates. The parser [`SaveCheckoutAttributesAsync`](../Services/NopwaraCheckoutService.cs#L320) is a port of core's `ParseAndSaveCheckoutAttributesAsync`. It builds the attribute XML, removes the values of conditional attributes whose condition isn't met, saves the XML to the `CheckoutAttributes` generic attribute, and returns which conditional attributes to show and hide.

File-upload attributes stay on the cart page. The checkout posts the current file id back unchanged.

On page load the script posts once, to apply the conditions. "Place order" parses and validates the attributes again (§9, step 0).

### 8.6 Pickup in store

Offered when `ShippingSettings.AllowPickupInStore` is on and a pickup provider returns points, such as the core "Pickup in store" plugin.

- **Points list:** [`PreparePickupPointsModelAsync`](../Factories/NopwaraCheckoutModelFactory.cs#L250) is a copy of a *protected* core factory method, which core only calls from one of its checkout steps. When there are no shipping rate providers at all, the card becomes pickup only.
- **The toggle:** a hidden `PickupInStore` input (core's field name, [view line 139](../Views/Checkout.cshtml#L139)) follows the Alpine `pickup` flag. `setPickup` ([JS line 196](../Content/js/nopwara-checkout.js#L196)) preselects the first point and saves.
- **Saving:** `SelectShipping` branches on [`IsPickupInStoreSelected`](../Services/NopwaraCheckoutService.cs#L259):
  - **pickup** → [`SavePickupPointAsync`](../Services/NopwaraCheckoutService.cs#L270) validates the point against the provider and saves a `ShippingOption` with `IsPickupInStore = true`, plus the `PickupPoint`. That's exactly what core saves.
  - **delivery** → [`ClearPickupPointAsync`](../Services/NopwaraCheckoutService.cs#L307) removes the pickup choice, so the shipping section preselects a delivery method again.
- **At order time:** core sees `IsPickupInStore`, uses the pickup point's address and sets `Order.PickupInStore`.

### 8.7 Login for guests

[`_Login.cshtml`](../Views/_Login.cshtml) and [`Login`](../Controllers/NopwaraCheckoutController.cs#L558) mirror core `CustomerController.Login`, answering with JSON instead of a page:

- `ICustomerRegistrationService.ValidateCustomerAsync` checks the credentials, by email or username depending on `CustomerSettings.UsernamesEnabled`.
- Success → `SignInCustomerAsync`. That signs the customer in, **merges the guest cart into the customer's cart** and raises the login events. The response then redirects to the checkout, which reloads with the customer's saved addresses.
- Multi-factor authentication → the same session data core stores, then a redirect to core's verification page, which returns to the checkout afterwards.
- Failures → core's messages. Wrong passwords also send core's failed-login email when that setting is on, and write the activity log.
- Captcha appears when `CaptchaSettings.ShowOnLoginPage` is on. `[ValidateCaptcha]` sets the `captchaValid` parameter.

## 9. Placing the order

[`PlaceOrder`](../Controllers/NopwaraCheckoutController.cs#L654) **re-saves everything from the posted form**, so it never depends on an earlier ajax call having finished or succeeded:

| Step | Line | What |
|---|---|---|
| captcha | | for guests, when `ShowOnCheckoutPageForGuests` is on (same rule as core) |
| 0 | [673](../Controllers/NopwaraCheckoutController.cs#L673) | checkout attributes: parse, save, and get core's warnings for them |
| 1 | [683](../Controllers/NopwaraCheckoutController.cs#L683) | billing and shipping addresses |
| 2 | [686](../Controllers/NopwaraCheckoutController.cs#L686) | shipping method, or pickup point |
| 3 | [696](../Controllers/NopwaraCheckoutController.cs#L696) | payment method. If it has a payment form: `ValidatePaymentFormAsync`, then `GetPaymentInfoAsync` stored in the session |
| 4 | [728](../Controllers/NopwaraCheckoutController.cs#L728) | terms of service accepted (when required) |
| 5 | [732](../Controllers/NopwaraCheckoutController.cs#L732) | not two orders within the minimum interval |
| 6 | [745](../Controllers/NopwaraCheckoutController.cs#L745) | core `PlaceOrderAsync`: the same call core's one-page checkout makes |

If any step fails, the errors go back to their areas, plus "Please check the highlighted fields". The browser scrolls to the first error.

After a successful order:
- **Redirection payment** (PayPal-style, where the customer pays on the provider's site): an ajax response can't send the browser to the provider, so it redirects to `/checkout/onepage/complete-payment/`. [`CompleteRedirectionPayment`](../Controllers/NopwaraCheckoutController.cs#L789) is a normal GET that calls `PostProcessPaymentAsync`, and the provider takes over.
- **Any other method:** `PostProcessPaymentAsync`, then a redirect to the order-completed page.

## 10. Features outside the checkout page

### Skip shopping cart (optional)

[`ShouldSkipCartAsync`](../Infrastructure/NopwaraCheckoutRedirectFilter.cs#L67): when the setting is on, the same global filter also catches GET `ShoppingCart.Cart` and redirects to the checkout. It skips the redirect for:
- **an empty cart**, so the empty-cart message still shows;
- **customers who can't check out**;
- **carts with warnings.** Without this, the checkout would send them back to the cart, and the cart would send them to the checkout, forever.

### Buy now

The plugin is also an `IWidgetPlugin` ([`NopwaraCheckoutPlugin`](../NopwaraCheckoutPlugin.cs#L135)). nopCommerce calls `GetWidgetZonesAsync`, sees `ProductDetailsAddInfo` (a zone at the end of the product page's add-to-cart area), and renders [`BuyNowViewComponent`](../Components/BuyNowViewComponent.cs#L39) there. Think of a Magento block added to a layout container.

- The component skips the button for disabled buy buttons, rentals, cart-item edits, or when the checkout isn't active.
- [`nopwara-buynow.js`](../Content/js/nopwara-buynow.js) uses one listener on the whole document, so buttons inside the quick-view modal work too. On click it:
  1. runs the theme's attribute check if there is one (`window.nopwaraValidateProductAttributes`, provided by the Nopwara theme);
  2. posts the product form to **core's** add-to-cart endpoint, so all core rules apply;
  3. then opens the checkout.
- When the store is set to "display cart after adding a product", core answers with the cart URL. The script goes to the checkout instead.

### Mini cart Checkout button

Core hides the mini cart's Checkout button whenever the store has checkout attributes, because core expects them to be filled in on the cart page. [`MiniShoppingCartModelConsumer`](../Services/MiniShoppingCartModelConsumer.cs#L53) listens for `ModelPreparedEvent`. nopCommerce raises it for every view model a view component renders, much like a Magento observer on block HTML.

When the model is the mini cart's and this checkout handles attributes, it turns the button back on. The minimum order subtotal and "can this customer check out" rules still apply.

## 11. Working with any theme

**Finding views** ([`NopwaraCheckoutViewLocationExpander`](../Infrastructure/NopwaraCheckoutViewLocationExpander.cs)). For this plugin's controller, views are looked up in this order:
1. `Themes/{theme}/Views/Plugins/NopwaraCheckout/{view}.cshtml`: a theme override, so copy a view there to restyle it;
2. `Plugins/Misc.NopwaraCheckout/Views/{view}.cshtml`: the plugin's default;
3. the normal locations, so core partials such as `_CreateOrUpdateAddress` still resolve, including the theme's own version of them.

**Styles and Alpine.** The views use Tailwind classes and Alpine.
- *Nopwara* builds these classes into its own CSS and loads Alpine.js itself.
- *Every other theme* gets two extra files on the checkout page only ([`Checkout.cshtml` lines 11–18](../Views/Checkout.cshtml#L11)):
  - `nopwara-checkout.min.css`, built from [`tailwind/app.css`](../tailwind/app.css);
  - `alpine.min.js`.

The settings "Themes with built-in styles" and "Themes that load Alpine.js" decide which themes count as Nopwara-like.

The stylesheet is built so it can't affect anything outside the checkout page, yet still beats the host theme on it:
- **No preflight.** Tailwind's global reset is left out. A small reset scoped to `#nopwara-checkout` replaces it.
- **No cascade layers.** A theme's unlayered CSS always beats layered CSS, whatever the specificity.
- **`!important` utilities** (`@import "tailwindcss/utilities.css" important`), and component rules prefixed with the `#nopwara-checkout` id.
- **No `checkout-page` class.** The root drops it on other themes, because those themes style that class for their own checkout.

## 12. Install, update, settings

- **Install** ([`InstallAsync`](../NopwaraCheckoutPlugin.cs#L152)): saves default settings, activates the widget and adds the text strings. **Enabled starts off**, so installing doesn't change a live store's checkout.
- **Update** ([`UpdateAsync`](../NopwaraCheckoutPlugin.cs#L178)): nopCommerce calls it at startup when the `Version` in `plugin.json` is higher than the installed one. It adds new text strings and activates the widget.
- **Settings** ([`NopwaraCheckoutSettings`](../NopwaraCheckoutSettings.cs)):
  - stored as `nopwaracheckoutsettings.*` rows in the `Setting` table, like `core_config_data`;
  - settings added after 1.00 have property initializers (`= true`), so a store upgrading from 1.00 gets sensible defaults without a migration;
  - changed on the admin page ([`NopwaraCheckoutAdminController`](../Controllers/NopwaraCheckoutAdminController.cs)), which saves them and clears the settings cache.
- **Uninstall:** removes the settings, the text strings and the widget activation. The plugin has no tables of its own.

## 13. Extending it

- **Change markup:** override a view in your theme (§11). The views keep core class names (`method-list`, `total-info`, …) where core scripts or styles expect them.
- **Change behaviour:** the service, factory and controller methods are `protected virtual` or `public virtual`. To customise:
  1. Subclass the class and override a method.
  2. Register the subclass in your own plugin's `INopStartup`, with a higher `Order`, so it replaces the original: `services.AddScoped<NopwaraCheckoutService, MyCheckoutService>()`.

  This is like a Magento DI preference.
- **Add content:** the page renders core's one-page checkout widget zones (`op_checkout_billing_address_top`, `op_checkout_payment_method_top`, …), so widgets written for core's checkout appear here too.
- **Payment plugins:** anything that works with core's one-page checkout should work here: the same payment form view component, `ValidatePaymentFormAsync`, `GetPaymentInfoAsync` and redirection flow. Test new payment providers before going live.

## 14. Gotchas

These were learned the hard way while building it.

- **Inline scripts in ajax HTML.** nopCommerce's `<script asp-location="Footer">` tag helper only renders inline when the request has `X-Requested-With: XMLHttpRequest`. That's why every request sends it, and why sections are swapped with jQuery (which runs the scripts).
- **A `@attribute` variable in Razor.** `@attribute` is a Razor directive, so a C# variable named `attribute` must be written `@(attribute.Name)`.
- **Alpine attributes on tag helpers.** Use `x-on:click` rather than `@click` on elements Razor treats as tag helpers (`<form>`, `<input asp-for>`). Elsewhere, write `@@click`.
- **Alpine `x-show` timing.** Alpine shows `x-show` elements on the next animation frame, so code that runs right after a state change mustn't check visibility. The address completeness check reads state, not `offsetParent`.
- **jQuery Validate.** `check()` throws on hidden fields, which is why the script uses its own light completeness check before saving.
- **Re-initializing an `x-ignore` root.** Alpine marks it ignored internally (`_x_ignore`). The init code deletes those flags before `initTree`.
- **`@apply` and `!important`.** In the standalone stylesheet, `@apply`'d utilities are `!important` too. `@apply m-0` on a list item beats `space-y-*` on its parent, so set margins explicitly.
- **Testing with curl.** nopCommerce treats curl's default user agent as a search engine ("can't add to cart"). Send a browser user agent.
