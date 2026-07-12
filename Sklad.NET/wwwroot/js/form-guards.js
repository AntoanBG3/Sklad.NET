(function () {
    "use strict";

    function installSubmitGuards() {
        document.querySelectorAll('form[method="post"]:not([data-no-disable])').forEach(function (form) {
            form.addEventListener("submit", function () {
                if (window.jQuery && jQuery.fn.validate && !jQuery(form).valid()) {
                    form.querySelectorAll(".input-validation-error").forEach(function (el) {
                        el.classList.remove("shake");
                        void el.offsetWidth;
                        el.classList.add("shake");
                    });
                    return;
                }

                // Other page behaviors (for example the unsaved-changes guard)
                // can react only after this single validation decision succeeds.
                form.dispatchEvent(new Event("sklad:valid-submit"));

                // Deferred: disabling synchronously drops the clicked submit
                // button's name/value from the request (the Floor form uses it
                // to distinguish In from Out).
                setTimeout(function () {
                    form.querySelectorAll('button[type="submit"], input[type="submit"]').forEach(function (button) {
                        button.disabled = true;
                        button.setAttribute("aria-busy", "true");
                    });
                }, 0);
            });
        });
    }

    document.addEventListener("DOMContentLoaded", installSubmitGuards);

    // Back/forward cache can restore a submitted form with its buttons disabled.
    window.addEventListener("pageshow", function (event) {
        if (!event.persisted) return;
        document.querySelectorAll('[disabled][aria-busy="true"]').forEach(function (button) {
            button.disabled = false;
            button.removeAttribute("aria-busy");
        });
    });
})();
