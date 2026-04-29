// Pure shim for IPdfExportService. No logic.
window.festivalRiderPrint = (function () {
    function triggerPrint() {
        try { window.print(); } catch (e) { /* ignore */ }
    }
    return { triggerPrint };
})();
