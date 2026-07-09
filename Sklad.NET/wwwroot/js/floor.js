(function () {
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
})();
