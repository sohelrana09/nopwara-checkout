# Nopwara single-page checkout for nopCommerce

A true single-page checkout for nopCommerce: addresses, delivery, shipping, order options, payment, cart and totals on one page. Every change is saved over ajax and refreshes only the sections that depend on it. Built with **Tailwind CSS** and **Alpine.js**. It works with the Nopwara theme and with any other theme (it brings its own stylesheet and Alpine.js).

## Features

- **One page, no steps.** The page saves each change on its own: billing and shipping address, shipping method, payment method, reward points, coupons and gift cards, and cart quantities. "Place order" posts the whole form again, so it never depends on an earlier ajax call having finished.
- **Login on the checkout page.** Guests get a "Returning customer? Log in" card:
  - it uses email or username (`CustomerSettings.UsernamesEnabled`), plus remember me and forgot password;
  - captcha appears when `CaptchaSettings.ShowOnLoginPage` is on;
  - it has the same results and messages as the core login, including multi-factor authentication, the failed-login email and the activity log;
  - the guest cart is kept, and the page reloads with the customer's saved addresses;
  - phone (OTP) login is offered as a link to the core login page.
- **Pickup in store.** When `ShippingSettings.AllowPickupInStore` is on and a pickup point provider returns points (for example the bundled *Pickup in store* plugin):
  - the delivery card offers "Ship to an address" or "Pick up in store";
  - each pickup point shows its address, opening hours and fee;
  - choosing pickup hides the shipping address and the shipping methods;
  - orders are placed with `PickupInStore` and the pickup point address, as in core;
  - when no shipping rate provider is active, the card offers pickup only.
- **Checkout attributes on the page.** An "Order options" card shows checkout attributes such as gift wrapping:
  - supported types: dropdown, radio, checkboxes, read-only checkboxes, text, multiline text, color squares and date picker;
  - totals, shipping and payment refresh when an option changes;
  - conditional attributes show and hide as in core;
  - file upload attributes stay on the cart page, and their current file is kept.
- **Skip the shopping cart** (optional). A non-empty cart opens the checkout, where items can be edited. These cases still show the cart page: an empty cart, a cart with warnings, and customers who can't check out (checkout disabled, or anonymous checkout off).
- **"Buy now" button** under "Add to cart" on product pages and in quick view:
  - it adds the product with the product form, then opens the checkout;
  - with Nopwara, required attributes are checked in the browser first (`window.nopwaraValidateProductAttributes`); otherwise the server's warnings are shown;
  - it is hidden for rentals, for editing an existing cart item, and when the buy button is disabled.
- **Checkout button in the mini cart.** Core hides it when the store has checkout attributes or terms of service on the cart page. With the single-page checkout (and "Show checkout attributes" on) it is shown, except below the minimum order subtotal or for customers who can't check out. Nopwara links it straight to `/checkout/onepage/`, guests included.
- **Any theme:**
  - *Nopwara* styles the views in its own Tailwind build and loads Alpine.js.
  - *Other themes* get `Content/css/nopwara-checkout.min.css` and `Content/js/alpine.min.js` on the checkout page only. The stylesheet has no global reset: its reset and component rules are scoped to `#nopwara-checkout` and its utilities are `!important`, so it wins over the host theme on the checkout page and changes nothing elsewhere.
  - Themes can override any view at `Themes/{theme}/Views/Plugins/NopwaraCheckout/{view}.cshtml`.
- **Core compatible:**
  - all state lives where core keeps it (customer addresses and `NopCustomerDefaults` generic attributes), so core order placement validates it as usual;
  - payment plugins render their own payment forms inline, and redirection payments complete through a regular GET after the order is placed;
  - the core one-page checkout widget zones are rendered;
  - the core checkout URLs redirect to `/checkout/onepage/`.

## Requirements

- nopCommerce **5.00**

## Install

1. Copy this folder to `src/Plugins/`, add the project to `NopCommerce.sln` and build.
2. Install **Nopwara single-page checkout** (Configuration → Local plugins) and restart. This also activates the "Buy now" widget (Configuration → Widgets).
3. Open the plugin's configuration page and tick **Enabled**.
4. Nopwara theme only: its Tailwind entry CSS already scans this plugin's views (`@source "../../../../../Plugins/Nop.Plugin.Misc.NopwaraCheckout/Views";`). Rebuild the theme CSS after changing the views.

Upgrading from 1.00: the plugin's update step adds the new text strings and activates the widget. New settings start with their defaults.

## Settings

| Setting | Default | Notes |
|---|---|---|
| Enabled | off | Replaces the core checkout |
| Themes | *(empty)* | Comma-separated themes the checkout is used with; empty means every theme |
| Themes with built-in styles | `Nopwara` | Other themes get the plugin stylesheet |
| Themes that load Alpine.js | `Nopwara` | Other themes get the plugin's Alpine.js 3 |
| Allow cart editing | on | Quantities and remove on the checkout page |
| Show discount box / gift card box | on | |
| Show login | on | "Returning customer?" card for guests |
| Show checkout attributes | on | "Order options" card |
| Skip shopping cart | off | A non-empty cart opens the checkout |
| Show "Buy now" button | on | Widget zone `ProductDetailsAddInfo` |

## Plugin stylesheet

`Content/css/nopwara-checkout.min.css` is prebuilt. To rebuild it after changing the views:

```bash
cd tailwind
npm install
npm run build:css
```

## License

Copyright (C) 2026 Sohel Rana

Licensed under the [GNU Affero General Public License v3.0](LICENSE), the license nopCommerce itself is built on (the [nopCommerce Public License](https://www.nopcommerce.com/license) is AGPL v3 plus its additional terms). Parts of this project are adapted from nopCommerce source code. A store that uses it must keep nopCommerce's "Powered by nopCommerce" link, as the nopCommerce Public License requires.
