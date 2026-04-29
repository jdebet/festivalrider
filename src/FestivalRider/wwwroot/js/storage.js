// Pure shims for IStorageService. No logic.
window.festivalRiderStorage = (function () {
    function getItem(key) {
        try { return window.localStorage.getItem(key); } catch (e) { return null; }
    }
    function setItem(key, value) {
        try { window.localStorage.setItem(key, value); return true; } catch (e) { return false; }
    }
    function removeItem(key) {
        try { window.localStorage.removeItem(key); } catch (e) { /* ignore */ }
    }
    function registerBeforeUnload(dotNetRef, methodName) {
        const handler = function () {
            try { dotNetRef.invokeMethodAsync(methodName); } catch (e) { /* ignore */ }
        };
        window.addEventListener('beforeunload', handler);
        return true;
    }
    function registerStorageEvent(dotNetRef, methodName) {
        const handler = function (e) {
            if (!e || !e.key) return;
            try { dotNetRef.invokeMethodAsync(methodName, e.key, e.newValue); } catch (err) { /* ignore */ }
        };
        window.addEventListener('storage', handler);
        return true;
    }
    return { getItem, setItem, removeItem, registerBeforeUnload, registerStorageEvent };
})();
