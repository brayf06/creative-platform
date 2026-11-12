// Carga masiva de la biblioteca: sube los archivos elegidos de a uno, con progreso y resultado por archivo.
(function () {
    const form = $('#upload-form');
    const results = $('#upload-results');

    // Mientras haya archivos en análisis la página se vuelve a pedir sola, salvo que haya una carga a la vista:
    // recargar borraría sus resultados.
    if ($('[data-status="Pending"]').length) {
        setTimeout(function () {
            if (!results.children().length) { location.reload(); }
        }, 3000);
    }

    // Análisis en lote: muestra cuántos videos pendientes entran en una tanda y cuánto costaría; nada se gasta
    // sin confirmar. El modo automático lo cambia sólo quien administra la organización.
    const batch = $('#batch-panel');
    function loadBatch() {
        $.getJSON('/Analysis/BatchEstimate').done(function (e) {
            $('#batch-auto').prop('checked', e.autoEnabled).prop('disabled', batch.data('is-admin') !== true);
            let text;
            if (e.pending === 0) {
                text = 'No hay videos pendientes de análisis.';
            } else {
                text = e.pending + ' video(s) con escenas detectadas todavía sin analizar. ';
                if (e.videos > 0) {
                    text += 'En esta tanda entran ' + e.videos + ' (' + e.scenes + ' escenas) · ' + e.model + ' · costo estimado USD ' +
                        e.costUsd.toFixed(4) + ' (tope de la tanda: USD ' + e.limitUsd.toFixed(2) + '). ';
                    if (e.inheriting > 0) { text += e.inheriting + ' copian el análisis de su original, sin costo. '; }
                }
                if (e.overBudget > 0) { text += e.overBudget + ' quedan para otra tanda por el tope. '; }
                if (e.overVideoCap > 0) { text += e.overVideoCap + ' superan el tope por video y hay que pedirlos uno por uno. '; }
                if (e.notConfigured > 0) { text += e.notConfigured + ' no se pueden analizar: falta la clave del proveedor de IA (Ai:Gemini:ApiKey). '; }
            }
            $('#batch-text').text(text);
            $('#batch-start').toggleClass('d-none', e.videos === 0);
        }).fail(function (xhr) { $('#batch-text').text(window.ci.errorMessage(xhr)); });
    }
    if (batch.length) {
        loadBatch();
        $('#batch-start').on('click', function () {
            const button = $(this).prop('disabled', true);
            $.post('/Analysis/BatchStart')
                .done(function (r) {
                    $('#batch-done').removeClass('d-none').text(r.enqueued + ' análisis en cola (USD ' + r.estimatedCostUsd.toFixed(4) +
                        ' estimados). El avance se ve en Trabajos.');
                })
                .fail(function (xhr) { window.ci.alert(window.ci.errorMessage(xhr)); })
                .always(function () { button.prop('disabled', false); loadBatch(); });
        });
        $('#batch-auto').on('change', function () {
            $.post('/Analysis/Auto', { Enabled: this.checked })
                .fail(function (xhr) { window.ci.alert(window.ci.errorMessage(xhr)); })
                .always(loadBatch);
        });
    }

    if (!form.length) { return; }

    // Sube un archivo con los datos de derechos del formulario. Expuesto para poder cargar archivos generados.
    function uploadOne(file, onProgress) {
        const data = new FormData();
        data.append('File', file, file.name);
        ['RightsHolder', 'LicenseNotes', 'RightsExpireOn'].forEach(function (name) {
            data.append(name, form.find('[name="' + name + '"]').val());
        });
        if (form.find('[name="ReuseAuthorized"]').prop('checked')) { data.append('ReuseAuthorized', 'true'); }

        return $.ajax({
            url: '/Media/Upload',
            method: 'POST',
            data: data,
            processData: false,
            contentType: false,
            xhr: function () {
                const xhr = $.ajaxSettings.xhr();
                xhr.upload.addEventListener('progress', function (event) {
                    if (event.lengthComputable && onProgress) { onProgress(Math.round(event.loaded * 100 / event.total)); }
                });
                return xhr;
            }
        });
    }

    function resultRow(file) {
        const row = $('<li class="list-group-item d-flex justify-content-between align-items-center gap-3"></li>');
        row.append($('<span class="text-truncate"></span>').text(file.name));
        row.append($('<span class="small text-secondary text-nowrap" data-role="status"></span>').text('En espera'));
        results.append(row);
        return row.find('[data-role="status"]');
    }

    async function uploadAll(files) {
        const button = $('#upload-button').prop('disabled', true);
        results.empty();
        const pending = files.map(function (file) { return { file: file, status: resultRow(file) }; });
        let created = 0;
        for (const item of pending) {
            try {
                const result = await uploadOne(item.file, function (percent) { item.status.text('Subiendo ' + percent + '%'); });
                const link = $('<a></a>').attr('href', '/Media/Detail/' + result.assetId).text('#' + result.assetId);
                if (result.outcome === 'Created') {
                    created++;
                    item.status.attr('class', 'small text-success text-nowrap').text('Cargado ').append(link);
                } else {
                    item.status.attr('class', 'small text-warning-emphasis text-nowrap').text('Ya estaba en la biblioteca: ').append(link);
                }
            } catch (xhr) {
                item.status.attr('class', 'small text-danger').text(window.ci.errorMessage(xhr));
            }
        }
        button.prop('disabled', false);
        $('#upload-refresh').toggleClass('d-none', created === 0);
        return created;
    }

    form.on('submit', function (event) {
        event.preventDefault();
        uploadAll(Array.from(document.getElementById('Files').files));
    });

    window.mediaUpload = { uploadAll: uploadAll };
})();
