(function () {
    "use strict";

    document.addEventListener("DOMContentLoaded", function () {

        // Unsaved-changes guard on long forms (Create / Edit / RegisterMovement).
        var dirtyForms = [];
        document.querySelectorAll("form[data-guard]").forEach(function (form) {
            var state = { dirty: false };
            dirtyForms.push(state);
            form.addEventListener("input", function () { state.dirty = true; });
            form.addEventListener("change", function () { state.dirty = true; });
            form.addEventListener("sklad:valid-submit", function () { state.dirty = false; });
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

        // Collapsed nav drawer. Escape and an outside click both close it,
        // so the open panel can never trap the page. The scrim is the
        // topbar's ::before, so a tap on it targets the topbar element
        // itself — that counts as outside too.
        var navToggle = document.querySelector("[data-nav-toggle]");
        if (navToggle) {
            var topbar = navToggle.closest(".topbar");
            var setNavOpen = function (open) {
                topbar.classList.toggle("nav-open", open);
                document.documentElement.classList.toggle("nav-locked", open);
                navToggle.setAttribute("aria-expanded", open ? "true" : "false");
            };
            navToggle.addEventListener("click", function () {
                setNavOpen(!topbar.classList.contains("nav-open"));
            });
            document.addEventListener("keydown", function (e) {
                if (e.key === "Escape") setNavOpen(false);
            });
            document.addEventListener("click", function (e) {
                if (!topbar.contains(e.target) || e.target === topbar) setNavOpen(false);
            });
        }

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

})();
