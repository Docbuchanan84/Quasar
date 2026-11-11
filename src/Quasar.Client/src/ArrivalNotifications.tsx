import { useEffect, useState } from 'react'

type Device = {
  worker: ServiceWorkerRegistration
  publicKey: Uint8Array<ArrayBuffer>
  subscription: PushSubscription | null
  enabled: boolean
}

async function notificationRequest(
  route: string,
  body?: object,
  signal?: AbortSignal,
) {
  const timeout = AbortSignal.timeout(8000)
  const response = await fetch(`/api/notifications/${route}`, {
    method: body ? 'POST' : 'GET',
    headers: body ? { 'Content-Type': 'application/json' } : undefined,
    body: body ? JSON.stringify(body) : undefined,
    signal: signal ? AbortSignal.any([signal, timeout]) : timeout,
  })
  if (!response.ok) {
    const result = await response.json().catch(() => null)
    throw new Error(result?.error ?? 'Arrival notifications are unavailable.')
  }
  return response
}

function applicationKey(encoded: string) {
  const base64 = encoded.replaceAll('-', '+').replaceAll('_', '/')
  return Uint8Array.from(atob(base64), (character) => character.charCodeAt(0))
}

async function prepareDevice(signal: AbortSignal): Promise<Device> {
  const [, keyResponse] = await Promise.all([
    navigator.serviceWorker.register('/arrival-worker.js'),
    notificationRequest('key', undefined, signal),
  ])
  const worker = await navigator.serviceWorker.ready
  const { publicKey: encoded } = (await keyResponse.json()) as { publicKey: string }
  const publicKey = applicationKey(encoded)
  const subscription = await worker.pushManager.getSubscription()
  if (!subscription) return { worker, publicKey, subscription, enabled: false }

  const endpoint = subscription.endpoint
  const status = await notificationRequest('status', { endpoint }, signal)
  const { enabled } = (await status.json()) as { enabled: boolean }
  const subscribedKey = new Uint8Array(subscription.options.applicationServerKey ?? new ArrayBuffer(0))
  if (enabled && subscribedKey.length === publicKey.length && subscribedKey.every((byte, i) => byte === publicKey[i]))
    return { worker, publicKey, subscription, enabled: true }

  signal.throwIfAborted()
  if (enabled) await notificationRequest('unsubscribe', { endpoint }, signal)
  await subscription.unsubscribe()
  return { worker, publicKey, subscription: null, enabled: false }
}

export default function ArrivalNotifications() {
  const standalone = (navigator as Navigator & { standalone?: boolean }).standalone === true
  const supported = standalone && 'serviceWorker' in navigator && 'PushManager' in window && 'Notification' in window
  const [device, setDevice] = useState<Device | null>(null)
  const [pending, setPending] = useState(false)
  const [message, setMessage] = useState('')
  const [attempt, setAttempt] = useState(0)

  useEffect(() => {
    if (!supported) return
    const controller = new AbortController()
    void prepareDevice(controller.signal)
      .then((ready) => {
        if (!controller.signal.aborted) setDevice(ready)
      })
      .catch(() => {
        if (!controller.signal.aborted) setMessage('Could not check arrival notifications.')
      })
    return () => controller.abort()
  }, [supported, attempt])

  if (!supported) return null
  const denied = Notification.permission === 'denied'

  async function changePreference(current: Device) {
    setPending(true)
    setMessage('')
    try {
      if (current.enabled && current.subscription) {
        const subscription = current.subscription
        await notificationRequest('unsubscribe', { endpoint: subscription.endpoint })
        setDevice({ ...current, enabled: false })
        await subscription.unsubscribe()
        setDevice({ ...current, enabled: false, subscription: null })
      } else {
        // Subscribe immediately from the tap so iOS can show its permission prompt.
        const subscription = current.subscription ?? await current.worker.pushManager.subscribe({
          userVisibleOnly: true,
          applicationServerKey: current.publicKey,
        })
        setDevice({ ...current, subscription })
        const { keys } = subscription.toJSON()
        await notificationRequest('subscribe', {
          endpoint: subscription.endpoint,
          p256dh: keys?.p256dh,
          auth: keys?.auth,
        })
        setDevice({ ...current, subscription, enabled: true })
      }
    } catch (error) {
      setMessage(Notification.permission === 'denied'
        ? 'Allow Quasar notifications in iPhone Settings, then reopen the app.'
        : error instanceof Error ? error.message : 'Could not change arrival notifications. Try again.')
    } finally {
      setPending(false)
    }
  }

  return (
    <section className="arrival-notifications" aria-label="Ship arrival notifications">
      <button
        type="button"
        disabled={pending || (!device && !message) || (denied && !device?.enabled && !!device)}
        onClick={() => {
          if (device) void changePreference(device)
          else {
            setMessage('')
            setAttempt((value) => value + 1)
          }
        }}
      >
        {pending ? 'Saving…'
          : device?.enabled ? 'Disable arrival notifications'
          : !device && message ? 'Retry arrival notifications'
          : 'Enable arrival notifications'}
      </button>
      <span role="status">
        {message || (denied
          ? 'Allow Quasar notifications in iPhone Settings, then reopen the app.'
          : device?.enabled ? 'This iPhone gets an alert when an arrival pauses the game.' : '')}
      </span>
    </section>
  )
}
