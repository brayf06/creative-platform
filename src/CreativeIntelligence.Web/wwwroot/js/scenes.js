// Línea de escenas del detalle de un video: dibuja los cortes, reproduce cada fragmento y, para quien puede
// editar, permite mover un corte, dividir, fusionar, etiquetar y elegir para producción. Cada cambio se guarda
// en el servidor y se vuelve a pedir la lista: la pantalla nunca calcula por su cuenta cómo quedaron los tramos.
(function () {
    const panel = $('#scenes-panel');
    if (!panel.length) { return; }
    const assetId = panel.data('asset-id');
    const canEdit = panel.data('can-edit') === true;
    const video = document.querySelector('video');
    const strip = $('#scenes-strip');
    const list = $('#scenes-list');
    const status = $('#scenes-status');
    const editor = $('#scene-editor');
    let current = { segments: [], canSelect: false };
    let selectedId = null;
    let stopAtMs = null;
    let refreshTimer = null;

    function clock(ms) {
        const totalTenths = Math.round(ms / 100);
        const minutes = Math.floor(totalTenths / 600);
        const seconds = Math.floor((totalTenths % 600) / 10);
        return minutes + ':' + String(seconds).padStart(2, '0') + ',' + (totalTenths % 10);
    }

    function positionMs() { return video ? Math.round(video.currentTime * 1000) : 0; }
    function selected() { return current.segments.find(function (s) { return s.id === selectedId; }); }

    // Reproduce sólo el fragmento: salta al inicio y pausa al llegar al fin.
    function playSegment(segment) {
        selectedId = segment.id;
        showEditor();
        if (!video) { return; }
        stopAtMs = segment.endMs;
        video.currentTime = segment.startMs / 1000;
        video.play();
    }

    if (video) {
        video.addEventListener('timeupdate', function () {
            $('#scene-editor-position').text(clock(positionMs()));
            if (stopAtMs !== null && positionMs() >= stopAtMs) {
                video.pause();
                stopAtMs = null;
            }
        });
    }

    // Reproduce un tramo cualquiera (por ejemplo, uno narrativo, que abarca varias escenas).
    $(document).on('scenes:play-range', function (event, startMs, endMs) {
        if (!video) { return; }
        stopAtMs = endMs;
        video.currentTime = startMs / 1000;
        video.play();
        video.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    });

    // Arrastrar un corte: mientras se arrastra el video muestra el cuadro de esa posición; al soltar se guarda.
    // El servidor valida el rango de todos modos: acá sólo se evita ofrecer una posición imposible.
    function addHandle(segment, next, total) {
        const minimum = 100;
        const handle = $('<span class="scene-handle" title="Arrastrá para mover el corte"></span>')
            .css('left', (segment.endMs * 100 / total) + '%');
        let draggedMs = null;
        function msAt(event) {
            const box = strip[0].getBoundingClientRect();
            const ms = Math.round((event.clientX - box.left) * total / box.width);
            return Math.max(segment.startMs + minimum, Math.min(next.endMs - minimum, ms));
        }
        handle.on('pointerdown', function (event) {
            event.preventDefault();
            handle[0].setPointerCapture(event.pointerId);
            handle.addClass('dragging');
            draggedMs = segment.endMs;
            stopAtMs = null;
            if (video) { video.pause(); }
        });
        handle.on('pointermove', function (event) {
            if (draggedMs === null) { return; }
            draggedMs = msAt(event);
            handle.css('left', (draggedMs * 100 / total) + '%').attr('data-time', clock(draggedMs));
            if (video) { video.currentTime = draggedMs / 1000; }
        });
        handle.on('pointerup pointercancel', function (event) {
            if (draggedMs === null) { return; }
            const target = draggedMs;
            draggedMs = null;
            handle.removeClass('dragging');
            if (event.type === 'pointerup' && target !== segment.endMs) {
                send('MoveBoundary', segment.id, { EndMs: target });
            } else {
                handle.css('left', (segment.endMs * 100 / total) + '%');
            }
        });
        strip.append(handle);
    }

    function showEditor() {
        $('[data-segment-id]').removeClass('active');
        const segment = selected();
        editor.toggleClass('d-none', !canEdit || !segment);
        if (!segment) { return; }
        $('[data-segment-id="' + segment.id + '"]').addClass('active');
        const index = current.segments.indexOf(segment);
        const isLast = index === current.segments.length - 1;
        $('#scene-editor-title').text('Escena ' + (index + 1) + ' · ' + clock(segment.startMs) + ' – ' + clock(segment.endMs));
        $('#scene-end').val((segment.endMs / 1000).toFixed(1)).prop('disabled', isLast);
        $('#scene-end-current, #scene-move, #scene-merge').prop('disabled', isLast);
        $('#scene-tags').val(segment.tags.join(', '));
        $('#scene-selected').prop('checked', segment.selectedForProduction)
            .prop('disabled', !current.canSelect && !segment.selectedForProduction);
        $('#scene-selected-note').toggleClass('d-none', current.canSelect);
    }

    // Desde el buscador se llega con ?segment=ID: se abre esa escena y se posiciona el video en su inicio.
    // Con ?at=MS (un tramo narrativo) sólo se posiciona el video.
    let pendingSegment = parseInt(new URLSearchParams(location.search).get('segment'), 10);
    let pendingAt = parseInt(new URLSearchParams(location.search).get('at'), 10);

    function render(data) {
        current = data;
        const linked = data.segments.find(function (s) { return s.id === pendingSegment; });
        if (linked) {
            pendingSegment = NaN;
            selectedId = linked.id;
            if (video) { video.currentTime = linked.startMs / 1000; }
        }
        if (!isNaN(pendingAt) && video && data.segments.length) {
            video.currentTime = pendingAt / 1000;
            pendingAt = NaN;
        }
        strip.empty();
        list.empty();
        const labels = { Pending: 'Detectando escenas…', Failed: 'No se pudieron detectar', Ready: data.segments.length + (data.segments.length === 1 ? ' escena' : ' escenas') };
        status.attr('class', 'badge ms-2 ' + (data.status === 'Failed' ? 'text-bg-danger' : data.status === 'Ready' ? 'text-bg-success' : 'text-bg-secondary'))
            .text(labels[data.status] || 'Sin detectar');
        $('#scenes-error').toggleClass('d-none', !data.error).text(data.error || '');
        $('#scenes-detect').toggleClass('d-none', !canEdit || data.status === 'Pending');

        const total = data.durationMs || 1;
        data.segments.forEach(function (segment, index) {
            const length = segment.endMs - segment.startMs;
            const title = 'Escena ' + (index + 1) + ' · ' + clock(segment.startMs) + ' – ' + clock(segment.endMs);
            strip.append($('<button type="button" class="scene-block"></button>')
                .attr('data-segment-id', segment.id)
                .attr('title', title)
                .toggleClass('scene-selected', segment.selectedForProduction)
                .css('width', (length * 100 / total) + '%')
                .text(index + 1)
                .on('click', function () { playSegment(segment); }));

            const item = $('<button type="button" class="list-group-item list-group-item-action d-flex align-items-center gap-3"></button>')
                .attr('data-segment-id', segment.id)
                .on('click', function () { playSegment(segment); });
            if (segment.hasThumbnail) {
                item.append($('<img class="scene-thumb" alt="" loading="lazy" />').attr('src', '/Scenes/Thumbnail/' + segment.id));
            } else {
                item.append($('<span class="scene-thumb d-inline-block"></span>'));
            }
            const text = $('<span class="flex-grow-1"></span>')
                .append($('<span class="fw-semibold d-block"></span>').text('Escena ' + (index + 1)))
                .append($('<span class="small text-secondary d-block"></span>').text(clock(segment.startMs) + ' – ' + clock(segment.endMs)));
            if (segment.transcript) { text.append($('<span class="small fst-italic d-block"></span>').text('“' + segment.transcript + '”')); }
            const badges = $('<span class="d-block"></span>');
            segment.tags.forEach(function (tag) { badges.append($('<span class="badge text-bg-info me-1"></span>').text(tag)); });
            // Calidad técnica del audio según su volumen; sin dato si el video no tiene audio o se está midiendo.
            const audio = {
                Silent: ['Sin sonido', 'text-bg-secondary'], Low: ['Audio bajo', 'text-bg-warning'],
                Good: ['Audio correcto', 'text-bg-light'], Clipped: ['Audio saturado', 'text-bg-danger']
            }[segment.audioQuality];
            if (audio) {
                badges.append($('<span class="badge me-1"></span>').addClass(audio[1]).text(audio[0])
                    .attr('title', 'Volumen medio ' + segment.audioMeanDb + ' dB, pico ' + segment.audioPeakDb + ' dB'));
            }
            if (segment.origin === 'Manual') { badges.append($('<span class="badge text-bg-warning me-1"></span>').text('Corregida a mano')); }
            if (segment.selectedForProduction) { badges.append($('<span class="badge text-bg-success me-1"></span>').text('Para producción')); }
            item.append(text.append(badges));
            item.append($('<span class="badge text-bg-light"></span>').text(clock(length)));
            list.append(item);
        });
        if (canEdit) {
            data.segments.slice(0, -1).forEach(function (segment, index) { addHandle(segment, data.segments[index + 1], total); });
        }
        showEditor();

        // Se vuelve a consultar mientras haya detección en curso o miniaturas regenerándose.
        clearTimeout(refreshTimer);
        const waiting = data.status === 'Pending' || data.segments.some(function (s) { return !s.hasThumbnail; });
        if (waiting) { refreshTimer = setTimeout(load, 3000); }
    }

    function load() {
        return $.getJSON('/Scenes/List/' + assetId).done(render);
    }

    // Envía una edición y recarga la lista; si falla muestra el mensaje del servidor y recarga igual, porque un
    // 409 significa que lo que se ve está desactualizado.
    function send(action, id, data) {
        return $.post('/Scenes/' + action + '/' + id, data || {})
            .fail(function (xhr) { window.ci.alert(window.ci.errorMessage(xhr)); })
            .always(load);
    }

    $('#scene-end-current').on('click', function () { $('#scene-end').val((positionMs() / 1000).toFixed(1)); });
    $('#scene-move').on('click', function () {
        send('MoveBoundary', selectedId, { EndMs: Math.round(parseFloat($('#scene-end').val()) * 1000) });
    });
    $('#scene-split').on('click', function () { send('Split', selectedId, { AtMs: positionMs() }); });
    $('#scene-merge').on('click', function () { send('Merge', selectedId); });
    $('#scene-tags-save').on('click', function () { send('Tags', selectedId, { Tags: $('#scene-tags').val() }); });
    $('#scene-selected').on('change', function () { send('Select', selectedId, { Selected: this.checked }); });

    $('#scenes-detect').on('click', function () {
        const button = $(this).prop('disabled', true);
        $.post('/Scenes/Detect/' + assetId)
            .fail(function (xhr) {
                const response = xhr.responseJSON || {};
                // Hay correcciones manuales: se pide confirmación explícita antes de descartarlas.
                if (response.code === 'HAS_MANUAL_EDITS' && window.confirm(response.message + ' ¿Continuar?')) {
                    $.post('/Scenes/Detect/' + assetId, { DiscardManualEdits: true })
                        .fail(function (retry) { window.ci.alert(window.ci.errorMessage(retry)); })
                        .always(load);
                } else if (response.code !== 'HAS_MANUAL_EDITS') {
                    window.ci.alert(window.ci.errorMessage(xhr));
                }
            })
            .always(function () { button.prop('disabled', false); load(); });
    });

    // Cuando termina una transcripción, el texto por escena cambia.
    let transcriptSeen = false;
    $(document).on('transcript:ready', function () {
        if (!transcriptSeen) { transcriptSeen = true; load(); }
    });

    load();
})();
