// Terminación de un video (subtítulos, transición, música y textos), compartida por los editores de receta y de
// lote. Expone window.ciFinishing.read(), que devuelve los campos con los nombres que espera el servidor.
(function () {
    const box = $('#finishing');
    if (!box.length) { return; }
    const overlays = $('#fin-overlays');
    const maxOverlays = 5;

    function addOverlay() {
        if (overlays.children().length >= maxOverlays) { return; }
        const row = $('<div class="row g-2 mb-2" data-overlay></div>');
        row.append($('<div class="col-md-6"></div>').append('<input type="text" class="form-control form-control-sm" maxlength="120" placeholder="Texto" />'));
        row.append($('<div class="col-5 col-md-2"></div>').append(
            '<select class="form-select form-select-sm" data-field="placement" aria-label="Posición">' +
            '<option value="Top">Arriba</option><option value="Center">Centro</option><option value="Bottom">Abajo</option></select>'));
        row.append($('<div class="col-5 col-md-3"></div>').append(
            '<select class="form-select form-select-sm" data-field="moment" aria-label="Momento">' +
            '<option value="Whole">Todo el video</option><option value="Opening">Primeros 3 s</option><option value="Closing">Últimos 3 s</option></select>'));
        row.append($('<div class="col-2 col-md-1"></div>').append(
            $('<button type="button" class="btn btn-sm btn-outline-danger" title="Quitar">×</button>').on('click', function () {
                row.remove();
                $('#fin-overlay-add').prop('disabled', false);
            })));
        overlays.append(row);
        $('#fin-overlay-add').prop('disabled', overlays.children().length >= maxOverlays);
    }

    $('#fin-overlay-add').on('click', addOverlay);

    window.ciFinishing = {
        // Carga una terminación ya definida (al duplicar una receta). Una música que ya no se ofrece queda sin elegir.
        write: function (finishing) {
            $('#fin-subtitles').prop('checked', finishing.subtitles);
            $('#fin-transition').val(finishing.transition);
            $('#fin-music').val(finishing.musicTrackId === null ? '' : String(finishing.musicTrackId));
            if ($('#fin-music').val() === null) { $('#fin-music').val(''); }
            $('#fin-volume').val(finishing.musicVolumePercent);
            overlays.empty();
            finishing.overlays.forEach(function (overlay) {
                addOverlay();
                const row = overlays.children().last();
                row.find('input').val(overlay.text);
                row.find('[data-field="placement"]').val(overlay.placement);
                row.find('[data-field="moment"]').val(overlay.moment);
            });
        },
        read: function () {
            return {
                Subtitles: $('#fin-subtitles').prop('checked'),
                Transition: $('#fin-transition').val(),
                MusicTrackId: $('#fin-music').val() || null,
                MusicVolumePercent: $('#fin-volume').val(),
                Overlays: overlays.find('[data-overlay]').map(function () {
                    const row = $(this);
                    return { Text: row.find('input').val(), Placement: row.find('[data-field="placement"]').val(), Moment: row.find('[data-field="moment"]').val() };
                }).get().filter(function (o) { return o.Text.trim().length > 0; })
            };
        }
    };
})();
