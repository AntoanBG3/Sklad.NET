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

    // A canvas ignores the page's prefers-reduced-motion CSS, so the check
    // happens here; bars grow from the axis with a slight left-to-right stagger.
    if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
        Chart.defaults.animation = false;
    } else {
        Chart.defaults.animation = {
            duration: 500,
            easing: 'easeOutQuart',
            delay: function (ctx) {
                return ctx.type === 'data' && ctx.mode === 'default' ? ctx.dataIndex * 18 : 0;
            }
        };
    }
    // window.print() fires immediately from the Print button and would capture
    // a canvas mid-tween; beforeprint runs synchronously first, so jumping every
    // chart to its final frame here keeps the printout complete.
    var charts = [];
    window.addEventListener('beforeprint', function () {
        charts.forEach(function (c) {
            c.stop();
            c.update('none');
        });
    });
    // Chart.js formats axis ticks with its own locale, which defaults to en-US and
    // would print "25,000" beside the page's own "140 750".
    Chart.defaults.locale = document.documentElement.lang || undefined;
    Chart.defaults.color = ink50;
    Chart.defaults.borderColor = ruleSoft;
    Chart.defaults.font.family = getComputedStyle(document.body).fontFamily;

    function horizontalBar(id, labels, values) {
        var el = document.getElementById(id);
        if (!el || !labels.length) return;
        charts.push(new Chart(el, {
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
        }));
    }

    horizontalBar('brand-chart', data.brandLabels, data.brandValues);
    horizontalBar('season-chart', data.seasonLabels, data.seasonValues);

    var trendEl = document.getElementById('trend-chart');
    if (trendEl) {
        charts.push(new Chart(trendEl, {
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
        }));
    }
})();
