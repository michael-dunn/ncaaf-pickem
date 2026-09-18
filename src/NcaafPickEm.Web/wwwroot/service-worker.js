// In development, always fetch from the network and do not enable offline support.
// This is because caching would make development more difficult (changes would not
// be reflected on the first load after each change).
self.addEventListener('fetch', () => { });

// Push stubs so the development worker has the same shape as the published one.
// P7-02 implements both; see wwwroot/service-worker.published.js.
self.addEventListener('push', () => { });
self.addEventListener('notificationclick', () => { });
