// Service worker update detection.
// Registers /service-worker.js (replacing the inline registration that previously
// lived in index.html) and notifies a Blazor component when a new worker takes
// control via `controllerchange`. The component shows a toast prompting reload.
(function () {
    const api = {
        _ref: null,
        register: function (dotnetRef) {
            api._ref = dotnetRef;
        },
        reload: function () {
            window.location.reload();
        }
    };
    window.swUpdate = api;

    if (!('serviceWorker' in navigator)) {
        return;
    }

    // Track whether the page already had a controller at load time. If it did,
    // any subsequent `controllerchange` indicates an updated worker has taken
    // over. If it did not, the first `controllerchange` is the initial install
    // and must not surface a "reload available" prompt.
    let hadInitialController = !!navigator.serviceWorker.controller;

    navigator.serviceWorker.addEventListener('controllerchange', function () {
        if (!hadInitialController) {
            hadInitialController = true;
            return;
        }
        if (api._ref) {
            api._ref.invokeMethodAsync('OnUpdateAvailable').catch(function (e) {
                console.warn('swUpdate: failed to notify .NET', e);
            });
        }
    });

    navigator.serviceWorker.register('service-worker.js').catch(function (e) {
        console.warn('swUpdate: registration failed', e);
    });
})();
