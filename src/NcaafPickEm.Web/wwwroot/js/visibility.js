// JS module for the Page Visibility API (P6-03). Loaded via
// IJSRuntime.InvokeAsync<IJSObjectReference>("import", "./js/visibility.js") - no inline
// <script> per 05-Conventions.md. Used by the dashboard's auto-refresh timer to pause polling
// while the tab/app is not visible, and resume immediately on return.

/**
 * Subscribes a Blazor component to document.visibilityState changes.
 * @param {import('@microsoft/dotnet-js-interop').DotNet.DotNetObject} dotNetRef - a
 *   DotNetObjectReference to the calling component.
 * @param {string} callbackMethodName - the [JSInvokable] instance method to call with the
 *   current visibility as a bool (true = visible).
 * @returns {() => void} an unsubscribe function; also exposed as `dispose()` on the returned
 *   handle from `subscribe()` below.
 */
function addVisibilityListener(dotNetRef, callbackMethodName) {
    const handler = () => {
        dotNetRef.invokeMethodAsync(callbackMethodName, document.visibilityState === 'visible');
    };
    document.addEventListener('visibilitychange', handler);
    return () => document.removeEventListener('visibilitychange', handler);
}

/**
 * @param {import('@microsoft/dotnet-js-interop').DotNet.DotNetObject} dotNetRef
 * @param {string} callbackMethodName
 * @returns {{ isVisible: boolean }} the current visibility, so the caller does not need a
 *   second round trip just to read the initial state.
 */
export function subscribe(dotNetRef, callbackMethodName) {
    const unsubscribe = addVisibilityListener(dotNetRef, callbackMethodName);
    // Stash the unsubscribe function on the dotnet ref's module-side handle via a WeakMap keyed
    // by the ref itself would be over-engineering here - the caller holds the IJSObjectReference
    // for this whole module and calls `unsubscribe()` (below) with the same dotNetRef, which is
    // enough for one subscriber per component instance.
    subscriptions.set(dotNetRef, unsubscribe);
    return { isVisible: document.visibilityState === 'visible' };
}

/** @type {Map<object, () => void>} */
const subscriptions = new Map();

/**
 * @param {import('@microsoft/dotnet-js-interop').DotNet.DotNetObject} dotNetRef
 */
export function unsubscribe(dotNetRef) {
    const fn = subscriptions.get(dotNetRef);
    if (fn) {
        fn();
        subscriptions.delete(dotNetRef);
    }
}
