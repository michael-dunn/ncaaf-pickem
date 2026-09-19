// JS module for web push (Feature 11, P7-02). Loaded via
// IJSRuntime.InvokeAsync<IJSObjectReference>("import", "./js/push.js") - no inline <script>
// per 05-Conventions.md.
//
// Object shapes returned to Blazor use explicit camelCase keys throughout, matched on the C#
// side by [JsonPropertyName] rather than relying on any interop naming convention.

/**
 * iOS/iPadOS detection. iPadOS 13+ reports as "Macintosh" with touch support, so a plain
 * userAgent check for "iPad" is not enough.
 * @returns {boolean}
 */
function detectIos() {
    const ua = navigator.userAgent || '';
    if (/iPhone|iPad|iPod/i.test(ua)) {
        return true;
    }
    return ua.includes('Macintosh') && navigator.maxTouchPoints > 1;
}

/**
 * @returns {boolean} true once the app is running from its installed Home Screen icon.
 */
function detectStandalone() {
    const mediaStandalone = typeof window.matchMedia === 'function'
        && window.matchMedia('(display-mode: standalone)').matches;
    return mediaStandalone || navigator.standalone === true;
}

/**
 * @returns {{ supported: boolean, isIos: boolean, isStandalone: boolean, permission: string }}
 * `supported` is false on iOS until the app is installed to the Home Screen (Safari tabs cannot
 * receive push at all - WorkItems/11-Notifications.txt), and false when the browser lacks the
 * Push API entirely.
 */
export function getSupport() {
    const isIos = detectIos();
    const isStandalone = detectStandalone();
    const hasPushApi = 'serviceWorker' in navigator && 'PushManager' in window && 'Notification' in window;
    const supported = hasPushApi && (!isIos || isStandalone);

    return {
        supported,
        isIos,
        isStandalone,
        permission: 'Notification' in window ? Notification.permission : 'denied',
    };
}

/** @param {string} base64String @returns {Uint8Array} */
function urlBase64ToUint8Array(base64String) {
    const padding = '='.repeat((4 - (base64String.length % 4)) % 4);
    const base64 = (base64String + padding).replace(/-/g, '+').replace(/_/g, '/');
    const rawData = atob(base64);
    const outputArray = new Uint8Array(rawData.length);
    for (let i = 0; i < rawData.length; i++) {
        outputArray[i] = rawData.charCodeAt(i);
    }
    return outputArray;
}

/** @param {ArrayBuffer} buffer @returns {string} base64url, no padding. */
function arrayBufferToBase64Url(buffer) {
    const bytes = new Uint8Array(buffer);
    let binary = '';
    for (let i = 0; i < bytes.length; i++) {
        binary += String.fromCharCode(bytes[i]);
    }
    return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

/** @param {PushSubscription} subscription */
function toSubscriptionInfo(subscription) {
    return {
        endpoint: subscription.endpoint,
        p256dh: arrayBufferToBase64Url(subscription.getKey('p256dh')),
        auth: arrayBufferToBase64Url(subscription.getKey('auth')),
        userAgent: navigator.userAgent,
    };
}

/**
 * Requests notification permission (must be invoked from a user-gesture tap handler - the
 * browser refuses a background call) and subscribes the service worker to push. Reuses an
 * existing browser subscription rather than creating a second one.
 * @param {string} vapidPublicKey base64url-encoded VAPID application public key.
 * @returns {Promise<{ granted: boolean, endpoint?: string, p256dh?: string, auth?: string, userAgent?: string }>}
 */
export async function subscribe(vapidPublicKey) {
    const permission = await Notification.requestPermission();
    if (permission !== 'granted') {
        return { granted: false };
    }

    const registration = await navigator.serviceWorker.ready;
    let subscription = await registration.pushManager.getSubscription();
    if (!subscription) {
        subscription = await registration.pushManager.subscribe({
            userVisibleOnly: true,
            applicationServerKey: urlBase64ToUint8Array(vapidPublicKey),
        });
    }

    return { granted: true, ...toSubscriptionInfo(subscription) };
}

/**
 * @returns {Promise<{ endpoint: string, p256dh: string, auth: string, userAgent: string }|null>}
 * the browser's current subscription for this device, or null when there is none.
 */
export async function getExistingSubscription() {
    if (!('serviceWorker' in navigator)) {
        return null;
    }

    const registration = await navigator.serviceWorker.getRegistration();
    if (!registration) {
        return null;
    }

    const subscription = await registration.pushManager.getSubscription();
    return subscription ? toSubscriptionInfo(subscription) : null;
}

/** Unsubscribes the browser's current push subscription for this device, if any. */
export async function unsubscribe() {
    if (!('serviceWorker' in navigator)) {
        return;
    }

    const registration = await navigator.serviceWorker.getRegistration();
    if (!registration) {
        return;
    }

    const subscription = await registration.pushManager.getSubscription();
    if (subscription) {
        await subscription.unsubscribe();
    }
}
