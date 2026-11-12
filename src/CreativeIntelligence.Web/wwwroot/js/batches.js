// Lotes. En el formulario arma el pedido (fragmentos con su función y parámetros), muestra la vista previa de las
// variantes y crea el lote. En el detalle muestra el avance y los videos, consultando mientras haya renders en curso.
(function () {
    function clock(ms) {
        const totalTenths = Math.round(ms / 100);
        return Math.floor(totalTenths / 600) + ':' + String(Math.floor((totalTenths % 600) / 10)).padStart(2, '0') + ',' + (totalTenths % 10);
    }

    // ---------- Formulario ----------
    const editor = $('#batch-editor');
    if (editor.length) {
        let seed = 1;
        const names = {};
        $('#batch-pool [data-segment-id]').each(function () {
            const row = $(this);
            names[row.data('segment-id')] = row.data('file') + ' ' + clock(row.data('start')) + '–' + clock(row.data('end'));
        });

        function body() {
            return $.extend(window.ciFinishing.read(), {
                Name: $('#batch-name').val(),
                Brief: $('#batch-brief').val(),
                Format: $('#batch-format').val(),
                Count: $('#batch-count').val(),
                MaxDurationSeconds: $('#batch-duration').val(),
                SimilarityPercent: $('#batch-similarity').val(),
                Seed: seed,
                Items: $('#batch-pool [data-segment-id]').filter(function () { return $(this).find('input').prop('checked'); })
                    .map(function () { return { SegmentId: $(this).data('segment-id'), Role: $(this).find('select').val() || null }; }).get()
            });
        }

        function preview() {
            // La vista previa no exige nombre: se completa uno provisorio para pasar la validación.
            const data = body();
            if (!data.Name) { data.Name = 'Vista previa'; }
            $.post('/Batches/Preview', data)
                .done(function (result) {
                    $('#batch-preview-box, #batch-shuffle').removeClass('d-none');
                    let text = 'Saldrían ' + result.variants.length + ' variantes de ' + result.requested + ' pedidas';
                    if (result.discarded > 0) { text += ' (' + result.discarded + ' candidatos descartados por parecidos)'; }
                    text += '.';
                    if (result.variants.length < 5) { text += ' Un lote necesita al menos 5: agregá fragmentos, asignales funciones distintas o subí la similitud máxima.'; }
                    $('#batch-preview-text').text(text);
                    const list = $('#batch-preview-list').empty();
                    result.variants.forEach(function (variant) {
                        list.append($('<li></li>').text(variant.segmentIds.map(function (id) { return names[id]; }).join(' → ') + ' · ' + clock(variant.durationMs)));
                    });
                    $('#batch-create').prop('disabled', result.variants.length < 5);
                })
                .fail(function (xhr) { window.ci.alert(window.ci.errorMessage(xhr)); });
        }

        $('#batch-preview').on('click', preview);
        $('#batch-shuffle').on('click', function () { seed++; preview(); });
        // Si cambia el pedido, la vista previa ya no vale.
        editor.on('change', 'input, select, textarea', function () {
            if (this.id !== 'batch-name' && this.id !== 'batch-brief' && !$(this).closest('#finishing').length) { $('#batch-preview-box').addClass('d-none'); }
        });

        $('#batch-create').on('click', function () {
            const button = $(this).prop('disabled', true);
            $.post('/Batches/Create', body())
                .done(function (created) { location.href = '/Batches/Detail/' + created.id; })
                .fail(function (xhr) { window.ci.alert(window.ci.errorMessage(xhr)); button.prop('disabled', false); });
        });
    }

    // ---------- Detalle ----------
    const panel = $('#batch-panel');
    if (panel.length) {
        const batchId = panel.data('batch-id');
        const grid = $('#batch-variants');
        let drawn = '';
        let timer = null;

        function render(data) {
            const total = data.variants.length;
            const pending = total - data.ready - data.failed;
            $('#batch-progress').css('width', data.percent + '%').text(data.percent + '%')
                .toggleClass('bg-success', pending === 0 && data.failed === 0);
            let summary = data.ready + ' de ' + total + ' videos listos · ' + data.passedQuality + ' pasaron el control de calidad';
            if (data.failed > 0) { summary += ' · ' + data.failed + ' con error'; }
            if (pending > 0) { summary += ' · ' + pending + ' en cola o renderizando'; }
            summary += ' · tiempo de render ' + (data.renderMs / 1000).toFixed(1) + ' s (cómputo local, sin costo de IA)';
            $('#batch-summary').text(summary);
            $('#batch-cancel').toggleClass('d-none', pending === 0);
            $('#batch-retry').toggleClass('d-none', data.failed === 0 || pending > 0);

            // Sólo se redibuja si algo cambió: redibujar reinicia un video que se está reproduciendo.
            const signature = JSON.stringify(data.variants);
            if (signature !== drawn) {
                drawn = signature;
                grid.empty();
                data.variants.forEach(function (variant) {
                    const video = variant.video;
                    const body = $('<div class="card-body"></div>');
                    const head = $('<div class="d-flex flex-wrap align-items-center gap-1 mb-2"></div>')
                        .append($('<a class="fw-semibold me-2 text-truncate"></a>').attr('href', '/Recipes/Detail/' + variant.recipeId).text(variant.recipeName));
                    if (!video || video.status === 'Pending') {
                        head.append($('<span class="badge text-bg-secondary"></span>').text('En cola'));
                    } else if (video.status === 'Failed') {
                        head.append($('<span class="badge text-bg-danger"></span>').text('Falló'));
                    } else {
                        head.append($('<span class="badge"></span>').addClass(video.passedQuality ? 'text-bg-success' : 'text-bg-warning')
                            .text(video.passedQuality ? 'Pasó el control' : 'No pasó el control'));
                    }
                    body.append(head);
                    if (video && video.status === 'Ready') {
                        body.append($('<video class="w-100 rounded bg-dark mb-2" style="max-height: 20rem;" controls preload="metadata"></video>')
                            .attr('src', '/Recipes/Video/' + video.id));
                        body.append($('<div class="small text-secondary"></div>').text(
                            variant.items + ' fragmentos · ' + clock(video.durationMs) + ' · ' + (video.sizeBytes / 1048576).toFixed(1) + ' MB'));
                        video.checks.filter(function (c) { return !c.passed; }).forEach(function (check) {
                            body.append($('<div class="small text-danger"></div>').text(check.detail));
                        });
                        body.append($('<a class="btn btn-sm btn-outline-secondary mt-2">Descargar</a>').attr('href', '/Recipes/Video/' + video.id + '?download=true'));
                    }
                    if (video && video.error) { body.append($('<div class="small text-danger"></div>').text(video.error)); }
                    grid.append($('<div class="col-sm-6 col-lg-4 col-xl-3"></div>').append($('<div class="card h-100"></div>').append(body)));
                });
            }

            clearTimeout(timer);
            if (pending > 0) { timer = setTimeout(load, 2500); }
        }

        function load() {
            $.getJSON('/Batches/Status/' + batchId).done(render);
        }

        function act(button, action, message) {
            button.prop('disabled', true);
            $.post('/Batches/' + action + '/' + batchId)
                .done(function (result) { window.ci.alert(result.count + message, 'info'); })
                .fail(function (xhr) { window.ci.alert(window.ci.errorMessage(xhr)); })
                .always(function () { button.prop('disabled', false); load(); });
        }

        $('#batch-cancel').on('click', function () {
            if (window.confirm('Se cancelan los videos del lote que todavía no terminaron. ¿Continuar?')) {
                act($(this), 'Cancel', ' video(s) cancelados.');
            }
        });
        $('#batch-retry').on('click', function () { act($(this), 'Retry', ' video(s) vueltos a pedir.'); });

        load();
    }
})();
