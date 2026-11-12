// Análisis con IA del detalle de un video. Antes de gastar muestra la estimación (escenas, cuadros, modelo y
// costo) y pide confirmación. Mientras el análisis está en curso, consulta el estado cada 3 segundos.
(function () {
    const panel = $('#analysis-panel');
    if (!panel.length) { return; }
    const assetId = panel.data('asset-id');
    const canEdit = panel.data('can-edit') === true;
    const list = $('#analysis-list');
    const status = $('#analysis-status');
    const estimateBox = $('#analysis-estimate');
    const functionLabels = {
        Hook: 'Hook', Problem: 'Problema', Presentation: 'Presentación', Demonstration: 'Demostración',
        Benefit: 'Beneficio', SocialProof: 'Prueba social', Objection: 'Objeción', Comparison: 'Comparación',
        Offer: 'Oferta', Cta: 'CTA', Broll: 'B-roll', Closing: 'Cierre'
    };

    function clock(ms) {
        const totalTenths = Math.round(ms / 100);
        return Math.floor(totalTenths / 600) + ':' + String(Math.floor((totalTenths % 600) / 10)).padStart(2, '0') + ',' + (totalTenths % 10);
    }

    function field(label, value) {
        if (!value) { return null; }
        return $('<div class="small"></div>').append($('<span class="text-secondary"></span>').text(label + ': ')).append(document.createTextNode(value));
    }

    function render(data, scenes) {
        list.empty();
        const labels = { Pending: 'Analizando…', Failed: 'No se pudo analizar', Ready: 'Analizado' };
        status.attr('class', 'badge ms-2 ' + (data.status === 'Failed' ? 'text-bg-danger' : data.status === 'Ready' ? 'text-bg-success' : 'text-bg-secondary'))
            .text(labels[data.status] || 'Sin analizar');
        $('#analysis-error').toggleClass('d-none', !data.error).text(data.error || '');
        $('#analysis-estimate-button').toggleClass('d-none', !canEdit || data.status === 'Pending');

        const narratives = $('#analysis-narratives-list').empty();
        $('#analysis-narratives').toggleClass('d-none', !data.narratives.length);
        data.narratives.forEach(function (n) {
            narratives.append($('<button type="button" class="list-group-item list-group-item-action"></button>')
                .append($('<span class="badge text-bg-dark me-2"></span>').text(functionLabels[n.function] || 'Tramo'))
                .append($('<span class="small text-secondary me-2"></span>').text(clock(n.startMs) + ' – ' + clock(n.endMs)))
                .append(document.createTextNode(n.summary))
                .on('click', function () { $(document).trigger('scenes:play-range', [n.startMs, n.endMs]); }));
        });

        data.items.forEach(function (item) {
            const index = scenes.segments.findIndex(function (s) { return s.id === item.segmentId; });
            const segment = scenes.segments[index];
            if (!segment) { return; }
            const card = $('<div class="list-group-item"></div>');
            const head = $('<div class="d-flex flex-wrap align-items-center gap-1 mb-1"></div>')
                .append($('<span class="fw-semibold me-2"></span>').text('Escena ' + (index + 1) + ' · ' + clock(segment.startMs) + ' – ' + clock(segment.endMs)));
            item.functions.forEach(function (f) { head.append($('<span class="badge text-bg-primary"></span>').text(functionLabels[f] || f)); });
            if (item.isStale) { head.append($('<span class="badge text-bg-warning"></span>').text('Desactualizado: la escena cambió')); }
            if (item.inherited) { head.append($('<span class="badge text-bg-secondary"></span>').text('Heredado del original')); }
            card.append(head);
            [field('Se ve', item.visualDescription), field('Mensaje', item.mainMessage), field('Texto en pantalla', item.onScreenText),
                field('Producto', item.product), field('Acciones', item.actions), field('Enfoque', item.commercialAngle),
                field('Público', item.audience)].forEach(function (row) { if (row) { card.append(row); } });
            card.append($('<div class="small text-secondary mt-1"></div>').text(
                'Calidad visual ' + item.visualQuality + '/5 · confianza del modelo ' + Math.round(item.confidence * 100) + '% · ' +
                item.model + ' · ' + item.promptVersion));
            list.append(card);
        });

        if (data.status === 'Pending') { setTimeout(load, 3000); }
        // Las etiquetas automáticas se ven en la lista de escenas: se refresca al terminar.
        if (data.status === 'Ready' && panel.data('was-pending')) { panel.data('was-pending', false); $(document).trigger('transcript:ready'); }
        if (data.status === 'Pending') { panel.data('was-pending', true); }
    }

    function load() {
        $.when($.getJSON('/Analysis/Get/' + assetId), $.getJSON('/Scenes/List/' + assetId)).done(function (analysis, scenes) {
            render(analysis[0], scenes[0]);
        });
    }

    $('#analysis-estimate-button').on('click', function () {
        $.getJSON('/Analysis/Estimate/' + assetId)
            .done(function (e) {
                estimateBox.removeClass('d-none');
                const inherits = e.inheritsFromAssetId !== null;
                $('#analysis-confirm').prop('disabled', !inherits && (!e.configured || e.costUsd > e.maxCostUsd));
                let text = e.scenes + ' escenas, ' + e.frames + ' cuadros · ' + e.model + ' · unos ' + e.inputTokens + ' tokens de entrada y ' +
                    e.outputTokens + ' de salida · costo estimado USD ' + e.costUsd.toFixed(4) + ' (tope por video: USD ' + e.maxCostUsd.toFixed(4) + ').';
                if (e.cached) { text = 'Este análisis ya se hizo con el mismo modelo y las mismas escenas: se reutiliza sin costo.'; }
                if (inherits) {
                    text = 'Este video es un duplicado visual del archivo #' + e.inheritsFromAssetId +
                        ', que ya está analizado: se copia su análisis, sin costo y sin llamar al proveedor de IA.';
                } else if (!e.configured) {
                    text += ' Falta configurar la clave del proveedor de IA (Ai:Gemini:ApiKey).';
                }
                $('#analysis-estimate-text').text(text);
            })
            .fail(function (xhr) { window.ci.alert(window.ci.errorMessage(xhr)); });
    });

    $('#analysis-confirm').on('click', function () {
        const button = $(this).prop('disabled', true);
        $.post('/Analysis/Start/' + assetId)
            .done(function () { estimateBox.addClass('d-none'); })
            .fail(function (xhr) { window.ci.alert(window.ci.errorMessage(xhr)); })
            .always(function () { button.prop('disabled', false); load(); });
    });

    $('#analysis-cancel').on('click', function () { estimateBox.addClass('d-none'); });

    load();
})();
