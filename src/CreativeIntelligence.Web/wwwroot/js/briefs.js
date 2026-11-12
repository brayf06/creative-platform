// Formulario de un brief: completa campos desde el texto libre, ofrece sólo los productos de la marca elegida y
// guarda por AJAX para no perder lo escrito si el servidor rechaza algo (por ejemplo, un mensaje prohibido).
(function () {
    const form = $('#brief-form');
    if (!form.length) { return; }
    const briefId = form.data('brief-id');
    const product = $('#ProductId');
    const platformLabels = { MetaReels: 'Reels', MetaStories: 'Historias', MetaFeed: 'Feed', Other: 'otra plataforma' };

    function showProducts() {
        const brand = $('#BrandId').val();
        product.find('option[data-brand]').each(function () {
            const visible = String($(this).data('brand')) === brand;
            $(this).prop('hidden', !visible).prop('disabled', !visible);
        });
        if (product.find('option:selected').prop('disabled')) { product.val(''); }
    }

    $('#BrandId').on('change', showProducts);
    product.val(String(product.data('selected') || ''));
    showProducts();

    // Lo deducido sólo completa campos vacíos: no pisa lo que la persona ya escribió.
    $('#brief-interpret').on('click', function () {
        $.post('/Briefs/Interpret', { Text: $('#OriginalText').val() })
            .done(function (result) {
                if (result.videoCount !== null && !$('#VideoCount').val()) { $('#VideoCount').val(result.videoCount); }
                if (result.durationSeconds !== null && !$('#DurationSeconds').val()) { $('#DurationSeconds').val(result.durationSeconds); }
                if (result.occasion !== null && !$('#Occasion').val()) { $('#Occasion').val(result.occasion); }
                if (result.platform !== null) { $('#Platform').val(result.platform); }
                if (result.useExternalResearch === true) { $('#UseExternalResearch').prop('checked', true); }
                const understood = result.understood.map(function (item) {
                    return item.replace(/plataforma: (\w+)/, function (match, key) { return 'plataforma: ' + (platformLabels[key] || key); });
                });
                $('#brief-understood').text(understood.length
                    ? 'Entendí: ' + understood.join(' · ') + '.'
                    : 'No encontré datos explícitos en el texto. Completá los campos a mano.');
            })
            .fail(function (xhr) { window.ci.alert(window.ci.errorMessage(xhr)); });
    });

    form.on('submit', function (event) {
        event.preventDefault();
        const button = form.find('[type="submit"]').prop('disabled', true);
        $.post(briefId ? '/Briefs/Update/' + briefId : '/Briefs/Create', form.serialize())
            .done(function (saved) { location.href = '/Briefs/Detail/' + saved.id; })
            .fail(function (xhr) { window.ci.alert(window.ci.errorMessage(xhr)); button.prop('disabled', false); window.scrollTo(0, 0); });
    });
})();
