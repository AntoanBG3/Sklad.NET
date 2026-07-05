(function () {
    "use strict";

    document.addEventListener("DOMContentLoaded", function () {

        // Double-submit guard: a second click on a ledger form records a second
        // movement, so every POST form disables its submit buttons once a valid
        // submission is on its way. Forms whose response is a download never
        // navigate away, so they opt out with data-no-disable.
        document.querySelectorAll("form[method=post]:not([data-no-disable])").forEach(function (form) {
            form.addEventListener("submit", function () {
                if (window.jQuery && jQuery.fn.validate && !jQuery(form).valid()) return;
                setTimeout(function () {
                    form.querySelectorAll("button[type=submit], input[type=submit]").forEach(function (btn) {
                        btn.disabled = true;
                        btn.setAttribute("aria-busy", "true");
                    });
                }, 0);
            });
        });

        // Unsaved-changes guard on long forms (Create / Edit / RegisterMovement).
        var dirtyForms = [];
        document.querySelectorAll("form[data-guard]").forEach(function (form) {
            var state = { dirty: false };
            dirtyForms.push(state);
            form.addEventListener("input", function () { state.dirty = true; });
            form.addEventListener("change", function () { state.dirty = true; });
            form.addEventListener("submit", function () { state.dirty = false; });
        });
        if (dirtyForms.length) {
            window.addEventListener("beforeunload", function (e) {
                if (dirtyForms.some(function (s) { return s.dirty; })) {
                    e.preventDefault();
                    e.returnValue = "";
                }
            });
        }

        // Whole-row navigation: the hover tint already promises the row is
        // clickable, so honour it — but never steal clicks from real controls
        // or from a text selection.
        document.querySelectorAll("tr[data-href]").forEach(function (row) {
            row.addEventListener("click", function (e) {
                if (e.target.closest("a, button, input, select, textarea, label")) return;
                var selection = window.getSelection();
                if (selection && selection.toString().length > 0) return;
                window.location.href = row.dataset.href;
            });
        });

        document.querySelectorAll("[data-print]").forEach(function (btn) {
            btn.addEventListener("click", function () { window.print(); });
        });
    });

    // Back/forward cache restores the page with buttons still disabled.
    window.addEventListener("pageshow", function (e) {
        if (!e.persisted) return;
        document.querySelectorAll("button[disabled][aria-busy], input[disabled][aria-busy]").forEach(function (btn) {
            btn.disabled = false;
            btn.removeAttribute("aria-busy");
        });
    });
})();
