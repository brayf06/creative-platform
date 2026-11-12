// Transcripción del detalle de un video: muestra las frases con su tiempo (clic para saltar a ese momento) y
// permite volver a pedirla. Mientras está en curso, consulta el estado cada 3 segundos.
(function () {
    const panel = $('#transcript-panel');
    if (!panel.length) { return; }
    const assetId = panel.data('asset-id');
    const canEdit = panel.data('can-edit') === true;
    const video = document.querySelector('video');
    const list = $('#transcript-lines');
    const status = $('#transcript-status');

    function clock(ms) {
        const totalTenths = Math.round(ms / 100);
        return Math.floor(totalTenths / 600) + ':' + String(Math.floor((totalTenths % 600) / 10)).padStart(2, '0') + ',' + (totalTenths % 10);
    }

    function render(data) {
        list.empty();
        let label = 'Sin transcribir';
        let css = 'text-bg-secondary';
        if (!data.hasAudio) { label = 'Sin audio'; }
        else if (data.status === 'Pending') { label = 'Transcribiendo…'; }
        else if (data.status === 'Failed') { label = 'No se pudo transcribir'; css = 'text-bg-danger'; }
        else if (data.status === 'Ready') { label = data.lines.length ? data.lines.length + (data.lines.length === 1 ? ' frase' : ' frases') : 'Sin voz'; css = 'text-bg-success'; }
        status.attr('class', 'badge ms-2 ' + css).text(label);
        $('#transcript-error').toggleClass('d-none', !data.error).text(data.error || '');
        $('#transcript-meta').text(data.model ? 'Idioma: ' + (data.language || 'no detectado') + ' · Modelo: ' + data.model : '');
        $('#transcript-start').toggleClass('d-none', !canEdit || !data.hasAudio || data.status === 'Pending');

        data.lines.forEach(function (line) {
            list.append($('<button type="button" class="list-group-item list-group-item-action d-flex gap-3"></button>')
                .append($('<span class="small text-secondary text-nowrap"></span>').text(clock(line.startMs)))
                .append($('<span></span>').text(line.text))
                .on('click', function () {
                    if (video) { video.currentTime = line.startMs / 1000; video.play(); }
                }));
        });

        if (data.status === 'Pending') { setTimeout(load, 3000); }
    }

    function load() {
        $.getJSON('/Transcript/Get/' + assetId).done(function (data) {
            render(data);
            // El texto por escena sale de la transcripción: al terminar, se refresca también la lista de escenas.
            if (data.status === 'Ready') { $(document).trigger('transcript:ready'); }
        });
    }

    $('#transcript-start').on('click', function () {
        const button = $(this).prop('disabled', true);
        $.post('/Transcript/Start/' + assetId)
            .fail(function (xhr) { window.ci.alert(window.ci.errorMessage(xhr)); })
            .always(function () { button.prop('disabled', false); load(); });
    });

    load();
})();
