/*
 * Nopwara single-page checkout (Alpine component "nopwaraCheckout").
 *
 * - One form holds every input. Changes are handled by delegated listeners on the form, so markup re-rendered
 *   by the server (shipping/payment/cart/totals sections) needs no Alpine directives of its own.
 * - Requests are queued (one at a time) so section updates never race each other.
 * - Sections are swapped with nw.html (Nopwara theme) or jQuery's .html() so inline <script> of payment plugins and core
 *   partials run
 *   (the server renders them inline because requests send X-Requested-With: XMLHttpRequest).
 * - The root element is x-ignore'd and initialized on DOMContentLoaded, when Alpine is guaranteed to be
 *   running, whether scripts are bundled or not.
 */
(function () {
    'use strict';

    var ADDRESS_DEBOUNCE_MS = 600;

    function nopwaraCheckout(config) {
        return {
            busy: {},
            errors: {},
            placing: false,
            summaryOpen: false,
            shipToSame: !!config.shipToSame,
            billingAddressId: config.billingAddressId || 0,
            shippingAddressId: config.shippingAddressId || 0,
            pickup: !!config.pickupInStore,
            texts: config.texts || {},
            _queue: Promise.resolve(),
            _addressTimer: null,

            init: function () {
                //by id: for guests the login form (_Login.cshtml) comes first
                this.form = this.$root.querySelector('#nopwara-checkout-form');

                //existing address / "ship to same" switches are saved right away - after Alpine has shown/hidden
                //the related address form, so the completeness check sees the right fields
                var saveAfterRender = () => this.$nextTick(() => this.saveAddresses());
                this.$watch('billingAddressId', saveAfterRender);
                this.$watch('shippingAddressId', saveAfterRender);
                this.$watch('shipToSame', saveAfterRender);

                //conditional checkout attributes are shown/hidden by the server's answer (same as core on the cart page)
                if (this.form.querySelector('[data-checkout-attributes]'))
                    this.saveCheckoutAttributes();
            },

            // ---------- requests ----------

            url: function (action) {
                return config.baseUrl.replace(/\/?$/, '/') + action + '/';
            },

            token: function () {
                var input = this.form.querySelector('input[name="__RequestVerificationToken"]')
                    || document.querySelector('input[name="__RequestVerificationToken"]');
                return input ? input.value : '';
            },

            /**
             * Queue a POST. options: { data: {}, includeForm: bool, sections: [names shown as busy], areas: [error areas owned] }
             */
            post: function (action, options) {
                options = options || {};
                var self = this;

                var run = function () {
                    var body = options.form ? new FormData(options.form) : options.includeForm ? new FormData(self.form) : new FormData();
                    Object.keys(options.data || {}).forEach(function (key) { body.set(key, options.data[key]); });
                    if (!body.has('__RequestVerificationToken'))
                        body.set('__RequestVerificationToken', self.token());

                    (options.sections || []).forEach(function (name) { self.busy[name] = true; });

                    return fetch(self.url(action), {
                        method: 'POST',
                        body: body,
                        credentials: 'same-origin',
                        headers: { 'X-Requested-With': 'XMLHttpRequest' }
                    })
                        .then(function (response) {
                            if (!response.ok)
                                throw new Error(response.status + ' ' + response.statusText);
                            return response.json();
                        })
                        .then(function (result) { return self.apply(result, options.areas || []); })
                        .catch(function (error) {
                            self.errors = Object.assign({}, self.errors, { order: [String(error.message || error)] });
                            return { success: false };
                        })
                        .finally(function () {
                            (options.sections || []).forEach(function (name) { self.busy[name] = false; });
                        });
                };

                this._queue = this._queue.then(run, run);
                return this._queue;
            },

            /** nopCommerce serializes JSON results in PascalCase; accept either casing */
            normalize: function (raw) {
                raw = raw || {};
                return {
                    sections: raw.sections || raw.Sections || {},
                    errors: raw.errors || raw.Errors || {},
                    redirect: raw.redirect || raw.Redirect || null,
                    enabledAttributeIds: raw.enabledAttributeIds || raw.EnabledAttributeIds || [],
                    disabledAttributeIds: raw.disabledAttributeIds || raw.DisabledAttributeIds || [],
                    success: raw.success !== undefined ? raw.success : raw.Success
                };
            },

            apply: function (raw, areas) {
                var result = this.normalize(raw);
                if (result.redirect) {
                    window.location.href = result.redirect;
                    return result;
                }

                var self = this;
                Object.keys(result.sections || {}).forEach(function (name) {
                    var target = self.form.querySelector('[data-section="' + name + '"]');
                    if (!target)
                        return;
                    //inline scripts of payment plugins and core partials must run: nw.html (Nopwara theme), jQuery elsewhere
                    if (window.nw)
                        window.nw.html(target, result.sections[name]);
                    else if (window.jQuery)
                        window.jQuery(target).html(result.sections[name]);
                    else
                        target.innerHTML = result.sections[name];
                });

                result.enabledAttributeIds.forEach(function (id) { self.toggleAttribute(id, true); });
                result.disabledAttributeIds.forEach(function (id) { self.toggleAttribute(id, false); });

                //replace errors of the areas this request owns, keep the others
                var errors = Object.assign({}, this.errors);
                areas.concat(['order']).forEach(function (area) { delete errors[area]; });
                this.errors = Object.assign(errors, result.errors || {});

                return result;
            },

            // ---------- addresses ----------

            /**
             * true when the required fields of a new-address form pass unobtrusive validation (without showing errors).
             * Based on state, not visibility: Alpine shows x-show elements on the next animation frame, so a form that
             * was just revealed can still be display:none here.
             */
            isAddressComplete: function (kind) {
                var container = this.form.querySelector('[data-new-address="' + kind + '"]');
                if (!container)
                    return true;
                //light check only (the server runs the full core address validation): required fields filled,
                //emails look like emails. jQuery Validate's check() can't be used: it throws for ignored (hidden) fields.
                var emailPattern = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
                return Array.prototype.slice.call(container.querySelectorAll('input, select, textarea'))
                    .filter(function (el) { return !el.disabled && el.type !== 'hidden'; })
                    .every(function (el) {
                        var value = (el.value || '').trim();
                        if (el.hasAttribute('data-val-required') && (value === '' || (el.tagName === 'SELECT' && value === '0')))
                            return false;
                        if (el.hasAttribute('data-val-email') && value !== '' && !emailPattern.test(value))
                            return false;
                        return true;
                    });
            },

            canSaveAddresses: function () {
                if (!this.billingAddressId && !this.isAddressComplete('billing'))
                    return false;

                //a pickup point replaces the shipping address
                if (!config.shippingRequired || this.shipToSame || this.pickup)
                    return true;

                return !!this.shippingAddressId || this.isAddressComplete('shipping');
            },

            scheduleAddressSave: function () {
                clearTimeout(this._addressTimer);
                this._addressTimer = setTimeout(() => this.saveAddresses(), ADDRESS_DEBOUNCE_MS);
            },

            saveAddresses: function () {
                clearTimeout(this._addressTimer);
                if (!this.canSaveAddresses())
                    return Promise.resolve();

                return this.post('saveaddresses', {
                    includeForm: true,
                    sections: ['shipping-methods', 'payment', 'totals'],
                    areas: ['billing', 'shipping', 'shipping-methods', 'payment']
                });
            },

            // ---------- pickup in store ----------

            setPickup: function (pickup) {
                if (this.pickup === pickup)
                    return;
                this.pickup = pickup;

                //preselect the first pickup point, so the choice can be saved right away
                if (pickup && !this.form.querySelector('input[name="pickup-points-id"]:checked')) {
                    var first = this.form.querySelector('input[name="pickup-points-id"]');
                    if (first)
                        first.checked = true;
                }

                //after Alpine updated the hidden PickupInStore input
                this.$nextTick(() => {
                    //back to delivery with a complete address: save it, which also offers its shipping methods again
                    if (!pickup && this.canSaveAddresses())
                        this.post('selectshipping', {
                            includeForm: true,
                            data: { shippingoption: '' },
                            sections: ['shipping-methods', 'payment', 'totals'],
                            areas: ['shipping-methods', 'payment']
                        }).then(() => this.saveAddresses());
                    else
                        this.selectShipping();
                });
            },

            selectShipping: function () {
                return this.post('selectshipping', {
                    includeForm: true,
                    sections: ['shipping-methods', 'payment', 'totals'],
                    areas: ['shipping-methods', 'payment']
                });
            },

            // ---------- checkout attributes ----------

            saveCheckoutAttributes: function () {
                return this.post('checkoutattributes', {
                    includeForm: true,
                    sections: ['attributes', 'shipping-methods', 'payment', 'totals'],
                    areas: ['attributes']
                });
            },

            toggleAttribute: function (id, visible) {
                var block = this.form.querySelector('#checkout_attribute_block_' + id);
                if (block)
                    block.hidden = !visible;
            },

            // ---------- login (guests) ----------

            login: function (form) {
                var self = this;
                //not queued behind checkout saves: it's a separate form
                this.busy.login = true;
                var body = new FormData(form);
                if (!body.has('__RequestVerificationToken'))
                    body.set('__RequestVerificationToken', this.token());

                return fetch(this.url('login'), {
                    method: 'POST',
                    body: body,
                    credentials: 'same-origin',
                    headers: { 'X-Requested-With': 'XMLHttpRequest' }
                })
                    .then(function (response) {
                        if (!response.ok)
                            throw new Error(response.status + ' ' + response.statusText);
                        return response.json();
                    })
                    .then(function (raw) {
                        var result = self.normalize(raw);
                        if (result.redirect) {
                            window.location.href = result.redirect;
                            return;
                        }
                        self.errors = Object.assign({}, self.errors, { login: result.errors.login || null });
                        self.busy.login = false;
                        //reCAPTCHA tokens are single use
                        if (window.grecaptcha && form.querySelector('.g-recaptcha'))
                            try { window.grecaptcha.reset(); } catch (e) { }
                    })
                    .catch(function (error) {
                        self.errors = Object.assign({}, self.errors, { login: [String(error.message || error)] });
                        self.busy.login = false;
                    });
            },

            // ---------- delegated events ----------

            onInput: function (event) {
                var type = event.target.type;
                if (event.target.closest('[data-address]') && type !== 'checkbox' && type !== 'radio' && event.target.tagName !== 'SELECT')
                    this.scheduleAddressSave();
                //typing in a checkout attribute text box: saved on change (blur), like core
            },

            onChange: function (event) {
                var target = event.target;
                var name = target.name || '';

                if (name === 'shippingoption' || name === 'pickup-points-id') {
                    this.selectShipping();
                    return;
                }

                if (name.indexOf('checkout_attribute_') === 0) {
                    this.saveCheckoutAttributes();
                    return;
                }

                if (name === 'paymentmethod' || name === 'UseRewardPoints') {
                    var selected = this.form.querySelector('input[name="paymentmethod"]:checked');
                    var rewardPoints = this.form.querySelector('input[name="UseRewardPoints"]');
                    this.post('selectpayment', {
                        data: {
                            paymentmethod: selected ? selected.value : '',
                            useRewardPoints: rewardPoints && rewardPoints.checked ? 'true' : 'false'
                        },
                        sections: ['payment', 'totals'],
                        areas: ['payment']
                    });
                    return;
                }

                if (target.hasAttribute('data-cart-select')) {
                    this.updateCart(target.getAttribute('data-cart-select'), target.value);
                    return;
                }

                //country/state selects, checkboxes etc. inside an address card ("ship to same"/address selects use $watch)
                if (target.closest('[data-address]') && name !== 'ShipToSameAddress' && name !== 'billing_address_id' && name !== 'shipping_address_id')
                    this.scheduleAddressSave();
            },

            onClick: function (event) {
                var button = event.target.closest('[data-cart-qty]');
                if (button) {
                    event.preventDefault();
                    this.updateCart(button.getAttribute('data-cart-qty'), button.getAttribute('data-quantity'));
                }
            },

            onSubmit: function (event) {
                var submitter = event.submitter || {};
                var name = submitter.name || '';

                if (name === 'applydiscountcouponcode')
                    return this.couponAction('applydiscountcoupon', { discountcouponcode: this.fieldValue('discountcouponcode') });
                if (name === 'applygiftcardcouponcode')
                    return this.couponAction('applygiftcard', { giftcardcouponcode: this.fieldValue('giftcardcouponcode') });
                if (name.indexOf('removediscount-') === 0)
                    return this.couponAction('removediscountcoupon', { discountId: name.substring('removediscount-'.length) });
                if (name.indexOf('removegiftcard-') === 0)
                    return this.couponAction('removegiftcard', { giftCardId: name.substring('removegiftcard-'.length) });
                if (name === 'placeorder')
                    return this.placeOrder();

                //implicit submission (Enter in a text field): save addresses instead of submitting
                if (event.target === this.form && document.activeElement && document.activeElement.closest('[data-address]'))
                    this.saveAddresses();
            },

            fieldValue: function (name) {
                var field = this.form.querySelector('[name="' + name + '"]');
                return field ? field.value : '';
            },

            // ---------- cart / coupons ----------

            updateCart: function (itemId, quantity) {
                return this.post('updatecart', {
                    data: { itemId: itemId, quantity: quantity },
                    sections: ['cart', 'coupons', 'shipping-methods', 'payment', 'totals'],
                    areas: ['cart']
                });
            },

            couponAction: function (action, data) {
                return this.post(action, {
                    data: data,
                    sections: ['cart', 'coupons', 'totals'],
                    areas: ['cart']
                });
            },

            // ---------- place order ----------

            placeOrder: function () {
                if (this.placing)
                    return;

                //client-side validation of the visible fields (hidden address forms are skipped): nw.validate with the
                //Nopwara theme, jQuery Validate where a theme loads it
                var jq = window.jQuery;
                var invalid = window.nw ? !window.nw.validate(this.form) : !!(jq && jq(this.form).data('validator') && !jq(this.form).valid());
                if (invalid) {
                    this.errors = Object.assign({}, this.errors, { order: [this.texts.fixErrors] });
                    this.scrollToFirstError();
                    return;
                }

                this.placing = true;
                clearTimeout(this._addressTimer);

                var self = this;
                return this.post('placeorder', {
                    includeForm: true,
                    sections: ['shipping-methods', 'totals'],
                    areas: ['billing', 'shipping', 'shipping-methods', 'payment', 'cart', 'attributes']
                }).then(function (result) {
                    //on success the page navigates away (redirect); keep the button disabled meanwhile
                    if (!result || !result.redirect) {
                        self.placing = false;
                        self.$nextTick(function () { self.scrollToFirstError(); });
                    }
                });
            },

            scrollToFirstError: function () {
                var first = this.form.querySelector('.input-validation-error, .message-error:not(:empty)');
                if (first)
                    first.scrollIntoView({ behavior: 'smooth', block: 'center' });
            }
        };
    }

    window.nopwaraCheckout = nopwaraCheckout;

    document.addEventListener('DOMContentLoaded', function () {
        var root = document.getElementById('nopwara-checkout');
        if (!root || !window.Alpine)
            return;

        window.Alpine.data('nopwaraCheckout', nopwaraCheckout);

        //Alpine.start() already walked the root and flagged it ignored; the flag is only cleared asynchronously
        //by Alpine's mutation observer when the attribute goes away, so clear it before initializing
        root.removeAttribute('x-ignore');
        delete root._x_ignore;
        delete root._x_ignoreSelf;
        window.Alpine.initTree(root);
    });
})();
