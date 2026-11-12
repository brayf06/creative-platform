// Planes creativos. En el brief: lista sus planes y pide uno nuevo mostrando antes la estimación de costo. En el
// plan: se actualiza mientras se genera y arma las recetas de los conceptos.
(function () {
    // ---------- En el detalle de un brief ----------
    const box = $('#brief-plans');
    if (box.length) {
        const briefId = box.data('brief-id');
        const list = $('#brief-plans-list');
        let timer = null;

        function load() {
            $.getJSON('/Plans/ForBrief/' + briefId).done(function (plans) {
                list.empty();
                plans.forEach(function (plan) {
                    const row = $('<a class="list-group-item list-group-item-action d-flex justify-content-between align-items-center"></a>')
                        .attr('href', '/Plans/Detail/' + plan.id);
                    const created = plan.createdAt.replace('T', ' ').slice(0, 16);
                    row.append($('<span></span>').text('Plan de conceptos #' + plan.id + ' · ' + created + ' UTC'));
                    if (plan.status === 'Pending') {
                        row.append($('<span class="badge text-bg-secondary"></span>').text('Generando…'));
                    } else if (plan.status === 'Failed') {
                        row.append($('<span class="badge text-bg-danger"></span>').attr('title', plan.error || '').text('Falló'));
                    } else {
                        row.append($('<span class="small text-secondary"></span>').text(
                            plan.concepts + ' conceptos · ' + plan.recipes + ' con receta · USD ' + plan.costUsd.toFixed(4)));
                    }
                    list.append(row);
                });
                $('#brief-plans-empty').toggleClass('d-none', plans.length > 0);
                clearTimeout(timer);
                if (plans.some(function (p) { return p.status === 'Pending'; })) { timer = setTimeout(load, 3000); }
            });
        }

        $('#plan-estimate').on('click', function () {
            $.getJSON('/Plans/Estimate/' + briefId)
                .done(function (e) {
                    $('#plan-estimate-box').removeClass('d-none');
                    let text = e.concepts + ' conceptos · ' + e.fragments + ' fragmentos disponibles para describirle · ' + e.model +
                        ' · unos ' + e.inputTokens + ' tokens de entrada y ' + e.outputTokens + ' de salida · costo estimado USD ' +
                        e.costUsd.toFixed(4) + ' (tope: USD ' + e.maxCostUsd.toFixed(4) + ').';
                    if (e.fragments === 0) { text += ' No hay fragmentos elegidos para producción: los conceptos saldrán sin material existente.'; }
                    if (!e.configured) { text += ' Falta configurar la clave del proveedor de IA (Ai:Gemini:ApiKey).'; }
                    $('#plan-estimate-text').text(text);
                    $('#plan-confirm').prop('disabled', !e.configured || e.costUsd > e.maxCostUsd);
                })
                .fail(function (xhr) { window.ci.alert(window.ci.errorMessage(xhr)); });
        });
        $('#plan-cancel').on('click', function () { $('#plan-estimate-box').addClass('d-none'); });
        $('#plan-confirm').on('click', function () {
            const button = $(this).prop('disabled', true);
            $.post('/Plans/Start/' + briefId)
                .done(function () { $('#plan-estimate-box').addClass('d-none'); })
                .fail(function (xhr) { window.ci.alert(window.ci.errorMessage(xhr)); })
                .always(function () { button.prop('disabled', false); load(); });
        });

        load();
    }

    // ---------- En el detalle de un plan ----------
    const panel = $('#plan-panel');
    if (panel.length) {
        if (panel.data('status') === 'Pending') { setTimeout(function () { location.reload(); }, 3000); }

        $('[data-action="recipe"]').on('click', function () {
            const button = $(this).prop('disabled', true);
            $.post('/Plans/Recipe/' + button.closest('[data-concept-id]').data('concept-id'))
                .done(function (created) { location.href = '/Recipes/Detail/' + created.id; })
                .fail(function (xhr) { window.ci.alert(window.ci.errorMessage(xhr)); button.prop('disabled', false); });
        });
        $('#plan-recipes').on('click', function () {
            const button = $(this).prop('disabled', true);
            $.post('/Plans/Recipes/' + panel.data('plan-id'))
                .done(function () { location.reload(); })
                .fail(function (xhr) { window.ci.alert(window.ci.errorMessage(xhr)); button.prop('disabled', false); });
        });
    }
})();
