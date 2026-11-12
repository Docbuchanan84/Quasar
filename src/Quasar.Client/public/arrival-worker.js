self.addEventListener('install', (event) => {
  event.waitUntil(self.skipWaiting())
})

self.addEventListener('push', (event) => {
  let message
  try {
    message = event.data?.json()
  } catch {
    // Safari requires a visible notification for each push.
  }
  event.waitUntil(
    self.registration.showNotification(message?.title ?? 'Quasar', {
      body: message?.body ?? 'Ship arrived. Game paused.',
    }),
  )
})

self.addEventListener('notificationclick', (event) => {
  event.notification.close()
  event.waitUntil(
    (async () => {
      const windows = await self.clients.matchAll({
        type: 'window',
        includeUncontrolled: true,
      })
      const game = windows.find((client) => {
        const path = new URL(client.url).pathname
        return path === '/' || path === '/index.html'
      })
      return game ? game.focus() : self.clients.openWindow('/')
    })(),
  )
})
