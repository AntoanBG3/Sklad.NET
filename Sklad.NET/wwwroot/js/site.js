(function () {
    "use strict";

    document.addEventListener("DOMContentLoaded", function () {

        // Double-submit guard: a second click on a ledger form records a second
        // movement, so every POST form disables its submit buttons once a valid
        // submission is on its way. Forms whose response is a download never
        // navigate away, so they opt out with data-no-disable.
        document.querySelectorAll("form[method=post]:not([data-no-disable])").forEach(function (form) {
            form.addEventListener("submit", function () {
                if (window.jQuery && jQuery.fn.validate && !jQuery(form).valid()) {
                    form.querySelectorAll(".input-validation-error").forEach(function (el) {
                        el.classList.remove("shake");
                        void el.offsetWidth;
                        el.classList.add("shake");
                    });
                    return;
                }
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

        // Stat numbers count up briefly on load. The markup keeps the final
        // localized string, so anything this can't parse back exactly (a
        // decimal part, digits outside the run) is left untouched rather
        // than risking a wrong number.
        if (!window.matchMedia("(prefers-reduced-motion: reduce)").matches) {
            document.querySelectorAll(".stat-num").forEach(function (el) {
                var finalText = el.textContent;
                var match = finalText.match(/\d{1,3}(?:([.,\s])\d{3})*/);
                if (!match) return;
                if (/\d/.test(finalText.replace(match[0], ""))) return;
                var sep = match[1] || "";
                var target = parseInt(match[0].replace(/\D/g, ""), 10);
                if (!target) return;
                var start = null;
                var done = false;
                function finish() {
                    if (done) return;
                    done = true;
                    el.textContent = finalText;
                }
                function group(n) {
                    var s = String(n);
                    return sep ? s.replace(/\B(?=(\d{3})+(?!\d))/g, sep) : s;
                }
                function frame(ts) {
                    if (done) return;
                    if (start === null) start = ts;
                    var t = Math.min((ts - start) / 500, 1);
                    if (t < 1) {
                        var eased = 1 - Math.pow(1 - t, 3);
                        el.textContent = finalText.replace(match[0], group(Math.round(target * eased)));
                        requestAnimationFrame(frame);
                    } else {
                        finish();
                    }
                }
                el.textContent = finalText.replace(match[0], "0");
                requestAnimationFrame(frame);
                // rAF is throttled to nothing in background tabs; never leave a zero standing
                setTimeout(finish, 700);
            });
        }
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
