(function () {
    var island = document.getElementById('report-data');
    if (!island || typeof Chart === 'undefined') return;

    var data = JSON.parse(island.textContent);
    var css = getComputedStyle(document.documentElement);
    var read = function (name, fallback) {
        return css.getPropertyValue(name).trim() || fallback;
    };

    var black = read('--black', '#000000');
    var ruleSoft = read('--rule-soft', 'rgba(0,0,0,.14)');
    var ink70 = read('--ink-70', 'rgba(0,0,0,.70)');
    var ink50 = read('--ink-50', 'rgba(0,0,0,.58)');

    // site.css keeps a four-colour discipline (black, white, gray, red), and red
    // is reserved for danger. Charts therefore separate series by tint of black,
    // which also survives a grayscale print rather than collapsing to one shade.
    var inFill = black;
    var outFill = 'rgba(0, 0, 0, .32)';

    // window.print() fires immediately from the Print button and would otherwise
    // capture a canvas mid-tween; this also honours prefers-reduced-motion,
    // which CSS cannot enforce on a canvas.
    Chart.defaults.animation = false;
    Chart.defaults.color = ink50;
    Chart.defaults.borderColor = ruleSoft;
    Chart.defaults.font.family = getComputedStyle(document.body).fontFamily;

    function horizontalBar(id, labels, values) {
        var el = document.getElementById(id);
        if (!el || !labels.length) return;
        new Chart(el, {
            type: 'bar',
            data: {
                labels: labels,
                datasets: [{ data: values, backgroundColor: ink70, hoverBackgroundColor: black, borderWidth: 0 }]
            },
            options: {
                indexAxis: 'y',
                responsive: true,
                maintainAspectRatio: false,
                plugins: { legend: { display: false } },
                scales: { x: { beginAtZero: true, grid: { color: ruleSoft } }, y: { grid: { display: false } } }
            }
        });
    }

    horizontalBar('brand-chart', data.brandLabels, data.brandValues);
    horizontalBar('season-chart', data.seasonLabels, data.seasonValues);

    var trendEl = document.getElementById('trend-chart');
    if (trendEl) {
        new Chart(trendEl, {
            type: 'bar',
            data: {
                labels: data.trendLabels,
                datasets: [
                    { label: data.inLabel, data: data.trendIn, backgroundColor: inFill, borderWidth: 0 },
                    { label: data.outLabel, data: data.trendOut, backgroundColor: outFill, borderWidth: 0 }
                ]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: { legend: { position: 'top', align: 'end' } },
                scales: {
                    x: { grid: { display: false } },
                    y: { beginAtZero: true, ticks: { precision: 0 }, grid: { color: ruleSoft } }
                }
            }
        });
    }
})();
