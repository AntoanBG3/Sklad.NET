(function () {
    "use strict";

    document.querySelectorAll('[data-stepper]').forEach(function (stepper) {
        var input = stepper.querySelector('input[type="number"]');
        if (!input) return;

        function button(label, delta, aria) {
            var b = document.createElement('button');
            b.type = 'button';
            b.className = 'btn btn-secondary floor-step';
            b.textContent = label;
            b.setAttribute('aria-label', aria);
            b.addEventListener('click', function () {
                var next = (parseInt(input.value, 10) || 1) + delta;
                input.value = next < 1 ? 1 : next;
            });
            return b;
        }

        stepper.insertBefore(button('−', -1, stepper.dataset.less || 'Less'), input);
        stepper.appendChild(button('+', 1, stepper.dataset.more || 'More'));
    });

    // Double-submit guard: booking is one tap, and a double-tap (or tapping In
    // then Out) would record two ledger movements. Disable both submit buttons
    // once a valid submission is on its way. Native constraint validation blocks
    // the submit event for an empty or below-min quantity, so this only fires on
    // a genuine post.
    document.querySelectorAll('form.floor-book').forEach(function (form) {
        form.addEventListener('submit', function () {
            // Deferred: disabling synchronously drops the clicked submit button's
            // name/value, which is how In vs Out reaches the server.
            setTimeout(function () {
                form.querySelectorAll('button[type="submit"]').forEach(function (btn) {
                    btn.disabled = true;
                    btn.setAttribute('aria-busy', 'true');
                });
            }, 0);
        });
    });

    // Back/forward cache can restore the booking screen with its buttons still
    // disabled from the submission that navigated away.
    window.addEventListener('pageshow', function (e) {
        if (!e.persisted) return;
        document.querySelectorAll('button[disabled][aria-busy]').forEach(function (btn) {
            btn.disabled = false;
            btn.removeAttribute('aria-busy');
        });
    });
})();
