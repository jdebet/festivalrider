// Pure shims for CSV download. No logic.
window.festivalRiderCsv = (function () {
    function trigger(blob, filename) {
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = filename;
        document.body.appendChild(a);
        a.click();
        a.remove();
        setTimeout(function () { URL.revokeObjectURL(url); }, 0);
    }
    function downloadText(filename, mime, text) {
        trigger(new Blob([text], { type: mime || 'text/csv;charset=utf-8' }), filename);
    }
    function downloadBytes(filename, mime, bytes) {
        trigger(new Blob([bytes], { type: mime || 'application/octet-stream' }), filename);
    }
    return { downloadText, downloadBytes };
})();
