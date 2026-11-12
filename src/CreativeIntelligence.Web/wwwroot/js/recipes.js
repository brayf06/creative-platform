// Composición. En el editor arma el montaje (agregar, ordenar, quitar, función de cada fragmento) y lo guarda como
// receta. En el detalle pide renders y consulta el estado de los videos mientras haya alguno en curso.
(function () {
    const functionLabels = {
        Hook: 'Hook', Problem: 'Problema', Presentation: 'Presentación', Demonstration: 'Demostración',
        Benefit: 'Beneficio', SocialProof: 'Prueba social', Objection: 'Objeción', Comparison: 'Comparación',
        Offer: 'Oferta', Cta: 'CTA', Broll: 'B-roll', Closing: 'Cierre'
    };

    function clock(ms) {
        const totalTenths = Math.round(ms / 100);
        return Math.floor(totalTenths / 600) + ':' + String(Math.floor((totalTenths % 600) / 10)).padStart(2, '0') + ',' + (totalTenths % 10);
    }

    // ---------- Editor ----------
    const editor = $('#recipe-editor');
    if (editor.length) {
        const minTrimMs = 200;
        let items = [];

        function seconds(ms) { return (ms / 1000).toFixed(1); }

        // Campo de recorte: el valor siempre queda dentro del fragmento y deja al menos el mínimo de duración.
        function trimInput(label, value, min, max, apply) {
            return $('<input type="number" class="form-control form-control-sm recipe-trim" step="0.1" />')
                .attr({ 'aria-label': label, title: label, min: seconds(min), max: seconds(max) })
                .val(seconds(value))
                .on('change', function () {
                    const typed = Math.round(parseFloat(this.value) * 1000);
                    apply(Math.max(min, Math.min(max, isNaN(typed) ? value : typed)));
                    draw();
                });
        }

        function draw() {
            const list = $('#recipe-items').empty();
            let total = 0;
            items.forEach(function (item, index) {
                total += item.end - item.start;
                const head = $('<div class="d-flex align-items-center gap-2"></div>');
                head.append($('<span class="badge text-bg-light"></span>').text(index + 1));
                if (item.thumb) { head.append($('<img class="scene-thumb" alt="" />').attr('src', '/Scenes/Thumbnail/' + item.segmentId)); }
                head.append($('<span class="flex-grow-1 small" style="min-width: 0;"></span>')
                    .append($('<span class="d-block text-truncate"></span>').text(item.file))
                    .append($('<span class="text-secondary"></span>').text(
                        'fragmento ' + clock(item.segStart) + ' – ' + clock(item.segEnd) + ' · se usan ' + clock(item.end - item.start))));
                head.append($('<button type="button" class="btn btn-sm btn-outline-secondary" title="Subir">↑</button>')
                    .prop('disabled', index === 0)
                    .on('click', function () { items.splice(index - 1, 0, items.splice(index, 1)[0]); draw(); }));
                head.append($('<button type="button" class="btn btn-sm btn-outline-secondary" title="Bajar">↓</button>')
                    .prop('disabled', index === items.length - 1)
                    .on('click', function () { items.splice(index + 1, 0, items.splice(index, 1)[0]); draw(); }));
                head.append($('<button type="button" class="btn btn-sm btn-outline-danger" title="Quitar">×</button>')
                    .on('click', function () { items.splice(index, 1); draw(); }));

                const role = $('<select class="form-select form-select-sm w-auto" aria-label="Función"></select>')
                    .append($('<option value=""></option>').text('Sin función'));
                Object.keys(functionLabels).forEach(function (key) { role.append($('<option></option>').val(key).text(functionLabels[key])); });
                role.val(item.role).on('change', function () { item.role = this.value; });
                const detail = $('<div class="d-flex flex-wrap align-items-center gap-2 mt-2 small"></div>')
                    .append($('<span class="text-secondary"></span>').text('Usar desde'))
                    .append(trimInput('Desde (s)', item.start, item.segStart, item.end - minTrimMs, function (v) { item.start = v; }))
                    .append($('<span class="text-secondary"></span>').text('hasta'))
                    .append(trimInput('Hasta (s)', item.end, item.start + minTrimMs, item.segEnd, function (v) { item.end = v; }))
                    .append($('<span class="text-secondary me-2"></span>').text('s'))
                    .append(role);
                list.append($('<div class="list-group-item"></div>').append(head).append(detail));
            });
            $('#recipe-total').text(items.length ? '· ' + items.length + ' fragmento(s), ' + clock(total) : '');
            $('#recipe-empty').toggleClass('d-none', items.length > 0);
            $('#recipe-save, #preview-play').prop('disabled', items.length === 0);
        }

        // El mismo fragmento se puede usar más de una vez en un montaje.
        $('#available').on('click', '[data-action="add"]', function () {
            const source = $(this).closest('[data-segment-id]');
            items.push({
                segmentId: source.data('segment-id'), assetId: source.data('asset-id'), file: source.data('file'),
                start: source.data('start'), end: source.data('end'), segStart: source.data('start'), segEnd: source.data('end'),
                thumb: source.data('thumb') === true, role: source.data('role') || ''
            });
            draw();
        });

        // Al duplicar una receta, el editor arranca con sus datos.
        const draftElement = document.getElementById('recipe-draft');
        if (draftElement) {
            const draft = JSON.parse(draftElement.textContent);
            $('#recipe-name').val(draft.name);
            $('#recipe-concept').val(draft.concept || '');
            $('#recipe-hypothesis').val(draft.hypothesis || '');
            $('#recipe-format').val(draft.format);
            window.ciFinishing.write(draft);
            items = draft.items.map(function (item) {
                return {
                    segmentId: item.segmentId, assetId: item.assetId, file: item.fileName, start: item.startMs, end: item.endMs,
                    segStart: item.segmentStartMs, segEnd: item.segmentEndMs, thumb: item.hasThumbnail, role: item.role || ''
                };
            });
        }

        $('#recipe-save').on('click', function () {
            const button = $(this).prop('disabled', true);
            $.post('/Recipes/Create', $.extend(window.ciFinishing.read(), {
                Name: $('#recipe-name').val(),
                Concept: $('#recipe-concept').val(),
                Hypothesis: $('#recipe-hypothesis').val(),
                Format: $('#recipe-format').val(),
                Items: items.map(function (item) {
                    return { SegmentId: item.segmentId, Role: item.role || null, StartMs: item.start, EndMs: item.end };
                })
            }))
                .done(function (created) { location.href = '/Recipes/Detail/' + created.id; })
                .fail(function (xhr) { window.ci.alert(window.ci.errorMessage(xhr)); button.prop('disabled', false); });
        });

        // Vista previa: reproduce los tramos en orden directamente de los originales, con el recorte del formato
        // y los textos encima. No renderiza nada; lo que no puede mostrar (subtítulos, música, fundidos) se avisa.
        const video = document.getElementById('preview-video');
        const frame = $('#preview-frame');
        let playing = -1;
        let playedMs = 0;
        let totalMs = 0;
        let texts = [];

        function shapeFrame() {
            const parts = $('#recipe-format').val().split(':').map(Number);
            frame.css({ aspectRatio: parts[0] + ' / ' + parts[1], maxWidth: parts[0] > parts[1] ? '28rem' : '15rem' });
        }

        function showTexts(montageMs) {
            ['Top', 'Center', 'Bottom'].forEach(function (place) {
                const visible = texts.filter(function (t) {
                    if (t.Placement !== place) { return false; }
                    if (t.Moment === 'Opening') { return montageMs < 3000; }
                    if (t.Moment === 'Closing') { return montageMs >= totalMs - 3000; }
                    return true;
                }).map(function (t) { return t.Text; });
                frame.find('[data-place="' + place + '"]').text(visible.join(' · '));
            });
        }

        function stopPreview(message) {
            playing = -1;
            video.pause();
            frame.find('[data-place]').text('');
            $('#preview-status').text(message || '');
        }

        function playItem() {
            const item = items[playing];
            if (!item) { stopPreview('Fin de la vista previa.'); return; }
            $('#preview-status').text('Fragmento ' + (playing + 1) + ' de ' + items.length);
            const start = function () { video.currentTime = item.start / 1000; video.play(); };
            if (video.dataset.assetId === String(item.assetId)) {
                start();
            } else {
                video.dataset.assetId = String(item.assetId);
                video.src = '/Media/Original/' + item.assetId;
                video.addEventListener('loadedmetadata', start, { once: true });
            }
        }

        video.addEventListener('timeupdate', function () {
            const item = items[playing];
            if (!item) { return; }
            const positionMs = video.currentTime * 1000;
            showTexts(playedMs + Math.max(0, positionMs - item.start));
            if (positionMs >= item.end) {
                playedMs += item.end - item.start;
                playing++;
                playItem();
            }
        });

        $('#preview-play').on('click', function () {
            texts = window.ciFinishing.read().Overlays;
            totalMs = items.reduce(function (sum, item) { return sum + item.end - item.start; }, 0);
            playedMs = 0;
            playing = 0;
            playItem();
        });
        $('#recipe-format').on('change', shapeFrame);

        shapeFrame();
        draw();
    }
    // ---------- Detalle ----------
    const panel = $('#variants-panel');
    if (panel.length) {
        const recipeId = panel.data('recipe-id');
        const list = $('#variants-list');
        const ruleLabels = { duration: 'Duración', resolution: 'Resolución', audio: 'Audio', black: 'Cuadros negros', subtitles: 'Subtítulos' };
        let timer = null;
        let drawn = '';

        function render(data) {
            // Sólo se redibuja si algo cambió: redibujar reinicia un video que se está reproduciendo.
            const signature = JSON.stringify(data.variants);
            if (signature !== drawn) {
                drawn = signature;
                list.empty();
                data.variants.forEach(function (variant) {
                    const card = $('<div class="card h-100"></div>');
                    const body = $('<div class="card-body"></div>');
                    const head = $('<div class="d-flex flex-wrap align-items-center gap-1 mb-2"></div>')
                        .append($('<span class="fw-semibold me-2"></span>').text('Video #' + variant.id));
                    if (variant.status === 'Pending') {
                        head.append($('<span class="badge text-bg-secondary"></span>').text('Renderizando…'));
                    } else if (variant.status === 'Failed') {
                        head.append($('<span class="badge text-bg-danger"></span>').text('Falló'));
                    } else {
                        head.append($('<span class="badge"></span>')
                            .addClass(variant.passedQuality ? 'text-bg-success' : 'text-bg-warning')
                            .text(variant.passedQuality ? 'Pasó el control de calidad' : 'No pasó el control de calidad'));
                    }
                    body.append(head);
                    if (variant.status === 'Ready') {
                        body.append($('<video class="w-100 rounded bg-dark mb-2" style="max-height: 24rem;" controls preload="metadata"></video>')
                            .attr('src', '/Recipes/Video/' + variant.id));
                        body.append($('<div class="small text-secondary mb-1"></div>').text(
                            variant.width + '×' + variant.height + ' · ' + clock(variant.durationMs) + ' · ' +
                            (variant.sizeBytes / 1048576).toFixed(1) + ' MB'));
                        variant.checks.forEach(function (check) {
                            body.append($('<div class="small"></div>')
                                .append($('<span class="badge me-1"></span>').addClass(check.passed ? 'text-bg-success' : 'text-bg-danger')
                                    .text(ruleLabels[check.rule] || check.rule))
                                .append(document.createTextNode(check.detail)));
                        });
                        body.append($('<a class="btn btn-sm btn-outline-secondary mt-2">Descargar</a>')
                            .attr('href', '/Recipes/Video/' + variant.id + '?download=true'));
                    }
                    if (variant.error) { body.append($('<div class="small text-danger"></div>').text(variant.error)); }
                    list.append($('<div class="col-md-6"></div>').append(card.append(body)));
                });
                $('#variants-empty').toggleClass('d-none', data.variants.length > 0);
            }

            clearTimeout(timer);
            if (data.variants.some(function (v) { return v.status === 'Pending'; })) { timer = setTimeout(load, 2000); }
        }

        function load() {
            $.getJSON('/Recipes/Status/' + recipeId).done(render);
        }

        $('#variant-render').on('click', function () {
            const button = $(this).prop('disabled', true);
            $.post('/Recipes/Render/' + recipeId)
                .fail(function (xhr) { window.ci.alert(window.ci.errorMessage(xhr)); })
                .always(function () { button.prop('disabled', false); load(); });
        });

        load();
    }
})();
