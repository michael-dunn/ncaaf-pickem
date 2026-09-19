// Caution! Be sure you understand the caveats before publishing an application with
// offline support. See https://aka.ms/blazor-offline-considerations

self.importScripts('./service-worker-assets.js');
self.addEventListener('install', event => event.waitUntil(onInstall(event)));
self.addEventListener('activate', event => event.waitUntil(onActivate(event)));
// Server-owned paths must never be answered from the offline cache: the Google OAuth callback
// (/auth/callback/google) is a top-level navigation, and serving index.html for it means the
// server never receives the code, no session cookie is issued, and sign-in silently loops.
// /api is fetched by the app itself and /health by the container healthcheck.
const serverOwnedPathPrefixes = ['/auth/', '/api/', '/health'];
function isServerOwned(request) {
    const path = new URL(request.url).pathname;
    return serverOwnedPathPrefixes.some(prefix => path.startsWith(prefix));
}
self.addEventListener('fetch', event => {
    if (isServerOwned(event.request)) {
        return; // let the browser talk to the server directly
    }
    event.respondWith(onFetch(event));
});

const cacheNamePrefix = 'offline-cache-';
const cacheName = `${cacheNamePrefix}${self.assetsManifest.version}`;
const offlineAssetsInclude = [ /\.dll$/, /\.pdb$/, /\.wasm/, /\.html/, /\.js$/, /\.json$/, /\.css$/, /\.woff$/, /\.png$/, /\.jpe?g$/, /\.gif$/, /\.ico$/, /\.blat$/, /\.dat$/, /\.webmanifest$/ ];
const offlineAssetsExclude = [ /^service-worker\.js$/ ];

// Replace with your base path if you are hosting on a subfolder. Ensure there is a trailing '/'.
const base = "/";
const baseUrl = new URL(base, self.origin);
const manifestUrlList = self.assetsManifest.assets.map(asset => new URL(asset.url, baseUrl).href);

async function onInstall(event) {
    // Activate a new version as soon as it is installed so a fix like the one above reaches an
    // already-installed phone on the next reload instead of after every tab is closed.
    self.skipWaiting();
    console.info('Service worker: Install');

    // Fetch and cache all matching items from the assets manifest
    const assetsRequests = self.assetsManifest.assets
        .filter(asset => offlineAssetsInclude.some(pattern => pattern.test(asset.url)))
        .filter(asset => !offlineAssetsExclude.some(pattern => pattern.test(asset.url)))
        .map(asset => new Request(asset.url, { integrity: asset.hash, cache: 'no-cache' }));
    await caches.open(cacheName).then(cache => cache.addAll(assetsRequests));
}

async function onActivate(event) {
    await self.clients.claim();
    console.info('Service worker: Activate');

    // Delete unused caches
    const cacheKeys = await caches.keys();
    await Promise.all(cacheKeys
        .filter(key => key.startsWith(cacheNamePrefix) && key !== cacheName)
        .map(key => caches.delete(key)));
}

async function onFetch(event) {
    let cachedResponse = null;
    if (event.request.method === 'GET') {
        // For all navigation requests, try to serve index.html from cache,
        // unless that request is for an offline resource.
        // If you need some URLs to be server-rendered, edit the following check to exclude those URLs
        const shouldServeIndexHtml = event.request.mode === 'navigate'
            && !manifestUrlList.some(url => url === event.request.url);

        const request = shouldServeIndexHtml ? 'index.html' : event.request;
        const cache = await caches.open(cacheName);
        cachedResponse = await cache.match(request);
    }

    return cachedResponse || fetch(event.request);
}

// ---------------------------------------------------------------- web push
// Feature 11, P7-02. Kept in sync by hand with the identical handlers in service-worker.js
// (the dev worker has no caching logic to share a module with).
self.addEventListener('push', event => {
    event.waitUntil(showPushNotification(event));
});

self.addEventListener('notificationclick', event => {
    event.waitUntil(handleNotificationClick(event));
});

async function showPushNotification(event) {
    let payload = {};
    try {
        payload = event.data ? event.data.json() : {};
    } catch {
        // Not JSON (or no payload at all) - fall back to a generic notification rather than
        // throwing and losing the push entirely.
    }

    const title = payload.title || 'NCAAF Pick Em';
    const url = payload.url || './';

    await self.registration.showNotification(title, {
        body: payload.body || '',
        tag: payload.tag,
        icon: 'icon-192.png',
        badge: 'badge-96.png',
        renotify: true,
        data: { url },
    });
}

async function handleNotificationClick(event) {
    event.notification.close();

    const targetUrl = new URL(event.notification.data?.url || './', self.registration.scope).href;

    const clientList = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });

    for (const client of clientList) {
        if (client.url === targetUrl) {
            await client.focus();
            return;
        }
    }

    // Fall back to any open window in scope: focus it and navigate, so an installed iOS app
    // reuses its one standalone window instead of opening a second.
    for (const client of clientList) {
        await client.focus();
        if ('navigate' in client) {
            await client.navigate(targetUrl);
        }
        return;
    }

    await self.clients.openWindow(targetUrl);
}
