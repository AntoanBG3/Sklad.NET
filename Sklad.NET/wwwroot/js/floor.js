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

    // Progressive camera scanning. BarcodeDetector is deliberately optional:
    // unsupported browsers keep the existing keyboard/scanner input unchanged.
    document.querySelectorAll('[data-camera-scanner]').forEach(function (form) {
        var input = form.querySelector('input[name="code"]');
        var startButton = form.querySelector('[data-camera-start]');
        var stopButton = form.querySelector('[data-camera-stop]');
        var panel = form.querySelector('[data-camera-panel]');
        var video = form.querySelector('[data-camera-video]');
        var status = form.querySelector('[data-camera-status]');

        if (!input || !startButton || !stopButton || !panel || !video || !status) return;
        if (!('BarcodeDetector' in window) || !navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) return;

        var detector;
        var stream;
        var scanTimer;
        var scanning = false;

        function message(text, isError) {
            status.textContent = text || '';
            status.classList.toggle('is-error', Boolean(isError));
        }

        function stopCamera(finalMessage, restoreFocus) {
            scanning = false;
            window.clearTimeout(scanTimer);
            if (stream) {
                stream.getTracks().forEach(function (track) { track.stop(); });
                stream = null;
            }
            video.pause();
            video.srcObject = null;
            panel.hidden = true;
            startButton.hidden = false;
            startButton.disabled = false;
            startButton.setAttribute('aria-expanded', 'false');
            if (finalMessage) message(finalMessage, false);
            if (restoreFocus) startButton.focus();
        }

        async function detect() {
            if (!scanning) return;
            try {
                var barcodes = await detector.detect(video);
                var value = barcodes.length && barcodes[0].rawValue
                    ? barcodes[0].rawValue.trim()
                    : '';
                if (value) {
                    input.value = value;
                    message(form.dataset.cameraFound, false);
                    stopCamera();
                    form.requestSubmit();
                    return;
                }
            } catch (_) {
                // Some implementations reject frames while the video is warming
                // up. A later frame is usable, so keep scanning until stopped.
            }
            if (scanning) scanTimer = window.setTimeout(detect, 180);
        }

        startButton.hidden = false;
        startButton.addEventListener('click', async function () {
            startButton.disabled = true;
            message(form.dataset.cameraStarting, false);
            try {
                var preferred = ['code_128', 'code_39', 'ean_13', 'ean_8', 'upc_a', 'upc_e', 'itf', 'qr_code'];
                var formats = [];

                // Resolve supported formats before constructing the detector.
                // Older implementations expose BarcodeDetector but not the
                // static format query, where the default constructor is safest.
                if (typeof BarcodeDetector.getSupportedFormats === 'function') {
                    var supportedFormats = await BarcodeDetector.getSupportedFormats();
                    formats = preferred.filter(function (format) { return supportedFormats.includes(format); });
                }
                detector = formats.length ? new BarcodeDetector({ formats: formats }) : new BarcodeDetector();

                stream = await navigator.mediaDevices.getUserMedia({
                    audio: false,
                    video: { facingMode: { ideal: 'environment' } }
                });
                video.srcObject = stream;
                await video.play();
                panel.hidden = false;
                startButton.hidden = true;
                startButton.disabled = false;
                startButton.setAttribute('aria-expanded', 'true');
                scanning = true;
                message(form.dataset.cameraReady, false);
                stopButton.focus();
                detect();
            } catch (_) {
                stopCamera(null, true);
                message(form.dataset.cameraError, true);
            }
        });

        stopButton.addEventListener('click', function () {
            stopCamera(form.dataset.cameraStopped, true);
        });
        window.addEventListener('pagehide', function () {
            stopCamera();
            message('', false);
        });
        window.addEventListener('pageshow', function (event) {
            if (event.persisted) message('', false);
        });
    });

})();
