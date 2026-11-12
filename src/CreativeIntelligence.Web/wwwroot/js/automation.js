// Automatización: recuerda la pestaña abierta. Cada formulario de la pantalla vuelve a cargarla, y sin esto se
// volvería siempre a "Acciones".
(function () {
    const key = 'ci.automation.tab';
    const tabs = document.querySelectorAll('#automation-tabs [data-bs-toggle="tab"]');
    tabs.forEach(function (tab) {
        tab.addEventListener('shown.bs.tab', function () { sessionStorage.setItem(key, tab.id); });
    });
    const saved = sessionStorage.getItem(key);
    const target = saved ? document.getElementById(saved) : null;
    if (target) {
        bootstrap.Tab.getOrCreateInstance(target).show();
    }
})();