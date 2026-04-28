// Pure shims for CSV download. No logic.
window.festivalRiderCsv = (function () {
    function downloadText(filename, mime, text) {
        const blob = new Blob([text], { type: mime || 'text/csv;charset=utf-8' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = filename;
        document.body.appendChild(a);
        a.click();
        a.remove();
        setTimeout(function () { URL.revokeObjectURL(url); }, 0);
    }
    return { downloadText };
})();
