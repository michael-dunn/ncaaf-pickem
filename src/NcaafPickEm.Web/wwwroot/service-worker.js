// In development, always fetch from the network and do not enable offline support.
// This is because caching would make development more difficult (changes would not
// be reflected on the first load after each change).
self.addEventListener('fetch', () => { });

// Web push (Feature 11, P7-02). Same handlers as service-worker.published.js - kept in sync by
// hand since the dev worker otherwise has no caching logic to share a module with.
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
