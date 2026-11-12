// Utilidades compartidas: antiforgery en toda llamada AJAX y avisos en pantalla.
(function () {
    const token = $('#af-token input[name="__RequestVerificationToken"]').val();
    $.ajaxSetup({ headers: { RequestVerificationToken: token } });

    window.ci = {
        // Muestra un aviso arriba de la página; se escribe como texto, nunca como HTML.
        alert: function (message, kind) {
            const box = $('<div class="alert alert-dismissible fade show" role="alert"></div>')
                .addClass('alert-' + (kind || 'danger'))
                .text(message);
            box.append('<button type="button" class="btn-close" data-bs-dismiss="alert" aria-label="Cerrar"></button>');
            $('#alerts').empty().append(box);
        },
        // Mensaje de un error AJAX: el del servidor si vino en JSON, si no uno genérico.
        errorMessage: function (xhr) {
            return (xhr.responseJSON && xhr.responseJSON.message) || 'No se pudo completar la operación.';
        }
    };
})();
