/*
 * "Buy now" button (Views/BuyNow.cshtml): posts the product form to the core ajax add-to-cart endpoint, then opens
 * the checkout. Uses the theme's attribute check when there is one (Nopwara: window.nopwaraValidateProductAttributes),
 * otherwise the server's warnings are shown with displayBarNotification, like core "Add to cart".
 * Delegated, so buttons in ajax-loaded markup (quick view) work too.
 */
(function () {
    'use strict';

    function notify(message) {
        if (typeof window.displayBarNotification === 'function')
            window.displayBarNotification(message, 'error', 0);
        else
            window.alert(Array.isArray(message) ? message.join('\n') : message);
    }

    document.addEventListener('click', function (event) {
        var button = event.target.closest('[data-nopwara-buy-now]');
        if (!button || button.disabled)
            return;
        event.preventDefault();

        var form = button.closest('form') || document.getElementById('product-details-form');
        if (!form)
            return;
        if (typeof window.nopwaraValidateProductAttributes === 'function' && !window.nopwaraValidateProductAttributes(form))
            return;

        button.disabled = true;
        fetch(button.getAttribute('data-nopwara-buy-now'), {
            method: 'POST',
            body: new FormData(form),
            credentials: 'same-origin',
            headers: { 'X-Requested-With': 'XMLHttpRequest' }
        })
            .then(function (response) {
                if (!response.ok)
                    throw new Error(response.status);
                return response.json();
            })
            .then(function (result) {
                //"display cart after adding a product" answers with the cart URL: go to the checkout instead
                if (result.redirect && result.redirect !== button.getAttribute('data-cart-url')) {
                    window.location.href = result.redirect;
                    return;
                }
                if (result.success === false) {
                    button.disabled = false;
                    notify(result.message);
                    return;
                }
                window.location.href = button.getAttribute('data-checkout-url');
            })
            .catch(function () {
                button.disabled = false;
                notify(button.getAttribute('data-error'));
            });
    });
})();
