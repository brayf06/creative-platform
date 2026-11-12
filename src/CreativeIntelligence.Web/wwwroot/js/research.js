// Investigación externa de un brief: lista sus corridas y sus referencias, y pide una corrida nueva mostrando
// antes la estimación de costo.
(function () {
    const box = $('#brief-research');
    if (!box.length) { return; }
    const briefId = box.data('brief-id');
    const runs = $('#brief-research-runs');
    const references = $('#brief-research-references');
    let timer = null;

    function load() {
        $.getJSON('/Research/ForBrief/' + briefId).done(function (data) {
            runs.empty();
            data.runs.forEach(function (run) {
                const row = $('<div class="list-group-item d-flex justify-content-between align-items-center px-0"></div>');
                row.append($('<span class="small"></span>').text('Corrida #' + run.id + ' · ' + run.createdAt.replace('T', ' ').slice(0, 16) + ' UTC'));
                if (run.status === 'Pending') {
                    row.append($('<span class="badge text-bg-secondary"></span>').text('Investigando…'));
                } else if (run.status === 'Failed') {
                    row.append($('<span class="badge text-bg-danger"></span>').attr('title', run.error || '').text('Falló'));
                } else {
                    row.append($('<span class="small text-secondary"></span>').text(
                        run.kept + ' guardadas de ' + run.found + ' propuestas · ' + run.searchQueries + ' búsquedas · USD ' + run.costUsd.toFixed(4)));
                }
                runs.append(row);
            });
            references.empty();
            data.references.forEach(function (reference) {
                references.append($('<li class="small"></li>')
                    .append($('<a target="_blank" rel="noopener noreferrer"></a>').attr('href', reference.url).text(reference.title))
                    .append(document.createTextNode(' · ' + reference.sourceName + ' · ' +
                        (reference.usage === 'Usable' ? 'utilizable' : 'sólo referencia') + (reference.summary ? ' — ' + reference.summary : ''))));
            });
            $('#brief-research-empty').toggleClass('d-none', data.references.length > 0);
            clearTimeout(timer);
            if (data.runs.some(function (r) { return r.status === 'Pending'; })) { timer = setTimeout(load, 3000); }
        });
    }

    $('#research-estimate').on('click', function () {
        $.getJSON('/Research/Estimate/' + briefId)
            .done(function (e) {
                $('#research-estimate-box').removeClass('d-none');
                let text = 'Busca en ' + e.sources + ' fuente(s) autorizada(s) · hasta ' + e.maxReferences + ' referencias · ' + e.model +
                    ' · costo estimado USD ' + e.costUsd.toFixed(4) + ', búsquedas incluidas (tope: USD ' + e.maxCostUsd.toFixed(4) + ').';
                if (e.sources === 0) { text += ' No hay fuentes autorizadas: primero autorizá alguna en Investigación.'; }
                if (!e.configured) { text += ' Falta configurar la clave del proveedor de IA (Ai:Gemini:ApiKey).'; }
                $('#research-estimate-text').text(text);
                $('#research-confirm').prop('disabled', !e.configured || e.sources === 0 || e.costUsd > e.maxCostUsd);
            })
            .fail(function (xhr) { window.ci.alert(window.ci.errorMessage(xhr)); });
    });
    $('#research-cancel').on('click', function () { $('#research-estimate-box').addClass('d-none'); });
    $('#research-confirm').on('click', function () {
        const button = $(this).prop('disabled', true);
        $.post('/Research/Start/' + briefId)
            .done(function () { $('#research-estimate-box').addClass('d-none'); })
            .fail(function (xhr) { window.ci.alert(window.ci.errorMessage(xhr)); })
            .always(function () { button.prop('disabled', false); load(); });
    });

    load();
})();
