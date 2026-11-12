// Listado de trabajos: dibuja las filas, las actualiza en vivo por SignalR y maneja lanzar, cancelar y reintentar.
(function () {
    const page = window.jobsPage;
    const body = $('#jobs-body');
    const statusLabels = {
        Pending: ['En cola', 'text-bg-secondary'],
        Running: ['En curso', 'text-bg-primary'],
        Succeeded: ['Terminado', 'text-bg-success'],
        Failed: ['Fallido', 'text-bg-danger'],
        Cancelled: ['Cancelado', 'text-bg-warning']
    };

    // Nombre legible de cada tipo de trabajo; el identificador interno queda como ayuda al pasar el mouse.
    const typeNames = {
        'diagnostic.progress': 'Prueba de diagnóstico',
        'media.probe': 'Leer las propiedades de un archivo',
        'media.scenes': 'Detectar las escenas de un video',
        'media.segment-thumbnails': 'Regenerar miniaturas y audio de escenas',
        'media.transcribe': 'Transcribir un video',
        'media.analyze': 'Analizar un video con IA',
        'media.embed': 'Indexar fragmentos para el buscador',
        'media.folder-scan': 'Revisar una carpeta vigilada',
        'creative.render': 'Generar un video',
        'music.probe': 'Leer una pista de música',
        'strategy.plan': 'Armar un plan de conceptos',
        'strategy.research': 'Investigar referencias externas',
        'meta.import-csv': 'Importar un informe de métricas',
        'automation.execute': 'Ejecutar una acción de automatización'
    };

    // La base guarda UTC sin zona; se la agrega para que el navegador lo convierta a hora local.
    function localTime(value) {
        if (!value) { return ''; }
        return new Date(/Z|[+-]\d\d:\d\d$/.test(value) ? value : value + 'Z').toLocaleString();
    }

    function buildRow(job) {
        const label = statusLabels[job.status] || [job.status, 'text-bg-secondary'];
        const row = $('<tr></tr>').attr('data-job-id', job.id);
        row.append($('<td></td>').text(job.id));
        row.append($('<td></td>').attr('title', job.type).text(typeNames[job.type] || job.type));

        const status = $('<td></td>').append($('<span class="badge"></span>').addClass(label[1]).text(label[0]));
        if (job.cancelRequested && job.status === 'Running') {
            status.append(' ').append($('<span class="badge text-bg-warning"></span>').text('Cancelando…'));
        }
        row.append(status);

        const bar = $('<div class="progress-bar"></div>').css('width', job.progressPercent + '%').text(job.progressPercent + '%');
        if (job.status === 'Running') { bar.addClass('progress-bar-striped progress-bar-animated'); }
        if (job.status === 'Failed') { bar.addClass('bg-danger'); }
        if (job.status === 'Succeeded') { bar.addClass('bg-success'); }
        row.append($('<td></td>')
            .append($('<div class="progress" role="progressbar"></div>').attr('aria-valuenow', job.progressPercent).append(bar))
            .append($('<div class="job-step text-secondary"></div>').text(job.progressStep || '')));

        row.append($('<td></td>').text(job.attempts + ' / ' + job.maxAttempts));
        row.append($('<td class="text-nowrap"></td>').text(localTime(job.createdAt)));
        row.append($('<td class="job-error small text-danger text-truncate"></td>').attr('title', job.error || '').text(job.error || ''));

        const actions = $('<td class="text-end text-nowrap"></td>');
        if (page.canEdit) {
            if ((job.status === 'Pending' || job.status === 'Running') && !job.cancelRequested) {
                actions.append($('<button type="button" class="btn btn-sm btn-outline-danger"></button>')
                    .attr('data-action', 'Cancel').text('Cancelar'));
            }
            if (job.status === 'Failed' || job.status === 'Cancelled') {
                actions.append($('<button type="button" class="btn btn-sm btn-outline-primary"></button>')
                    .attr('data-action', 'Retry').text('Reintentar'));
            }
        }
        row.append(actions);
        return row;
    }

    function upsert(job) {
        const existing = body.find('tr[data-job-id="' + job.id + '"]');
        if (existing.length) {
            existing.replaceWith(buildRow(job));
        } else if (page.isFirstPage) {
            // Un trabajo nuevo sólo aparece solo en la primera página, que es donde corresponde por orden.
            body.prepend(buildRow(job));
        }
        $('#jobs-empty').toggleClass('d-none', body.children().length > 0);
    }

    page.jobs.forEach(function (job) { body.append(buildRow(job)); });
    $('#jobs-empty').toggleClass('d-none', body.children().length > 0);

    body.on('click', 'button[data-action]', function () {
        const button = $(this).prop('disabled', true);
        const id = button.closest('tr').data('job-id');
        $.post('/Jobs/' + button.data('action') + '/' + id)
            .fail(function (xhr) {
                button.prop('disabled', false);
                window.ci.alert(window.ci.errorMessage(xhr));
            });
    });

    $('#diagnostic-form').on('submit', function (event) {
        event.preventDefault();
        $.post('/Jobs/Diagnostic', $(this).serialize())
            .fail(function (xhr) { window.ci.alert(window.ci.errorMessage(xhr)); });
    });

    const live = $('#live-status');
    function setLive(text, css) { live.attr('class', 'badge ' + css).text(text); }

    const connection = new signalR.HubConnectionBuilder().withUrl('/hubs/jobs').withAutomaticReconnect().build();
    connection.on('jobUpdated', upsert);
    connection.onreconnecting(function () { setLive('Reconectando…', 'text-bg-warning'); });
    connection.onreconnected(function () { setLive('En vivo', 'text-bg-success'); });
    connection.onclose(function () { setLive('Desconectado', 'text-bg-danger'); });
    connection.start()
        .then(function () { setLive('En vivo', 'text-bg-success'); })
        .catch(function () { setLive('Sin conexión en vivo', 'text-bg-danger'); });
})();
