# JS interop — Agent rules

- Locked file surface:
  - `storage.js`: `getItem`, `setItem`, `removeItem`, `registerBeforeUnload(dotNetRef, methodName)`, `registerStorageEvent(dotNetRef, methodName)`.
  - `print.js`: `triggerPrint()` calls `window.print()`.
  - `csvio.js`: `downloadText(filename, mime, text)` triggers a blob download.
  - `sw-update.js`: listens for `controllerchange` and invokes a `[JSInvokable]` .NET callback.
- ALWAYS register new JS files in `wwwroot/index.html`. NEVER dynamic-import.
- Functions MUST be pure platform shims. NEVER place business logic in JS.
- Callbacks into .NET MUST use `dotNetRef.invokeMethodAsync('Name', args)` against a `[JSInvokable]` method.
- C# callers MUST dispose `DotNetObjectReference` instances.
- NEVER hand-edit `service-worker.js` or `service-worker.published.js`; the Blazor template owns them.
