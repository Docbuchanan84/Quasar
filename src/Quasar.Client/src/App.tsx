import { useCallback, useEffect, useRef, useState } from 'react'

type Connection = {
  status: 'checking' | 'connected' | 'offline'
  checkedAt?: string
}

function App() {
  const [connection, setConnection] = useState<Connection>({ status: 'checking' })
  const pendingRequest = useRef<AbortController | null>(null)

  const checkConnection = useCallback(async () => {
    pendingRequest.current?.abort()
    const controller = new AbortController()
    pendingRequest.current = controller
    const timeout = window.setTimeout(() => controller.abort(), 8000)

    try {
      const response = await fetch('/api/health', {
        cache: 'no-store',
        signal: controller.signal,
      })
      if (!response.ok) throw new Error('Host unavailable')
      const health = await response.json()
      if (health.application !== 'Quasar' || health.status !== 'ready'
        || typeof health.serverTimeUtc !== 'string'
        || Number.isNaN(Date.parse(health.serverTimeUtc))) {
        throw new Error('Unexpected host response')
      }
      if (pendingRequest.current === controller) {
        setConnection({ status: 'connected', checkedAt: health.serverTimeUtc })
      }
    } catch {
      if (pendingRequest.current === controller) setConnection({ status: 'offline' })
    } finally {
      window.clearTimeout(timeout)
    }
  }, [])

  useEffect(() => {
    // Let mount cleanup cancel the initial request before it starts.
    const initialCheck = window.setTimeout(() => void checkConnection(), 0)
    const resume = () => {
      if (document.visibilityState === 'visible') {
        setConnection({ status: 'checking' })
        void checkConnection()
      }
    }
    const disconnect = () => {
      pendingRequest.current?.abort()
      pendingRequest.current = null
      setConnection({ status: 'offline' })
    }
    document.addEventListener('visibilitychange', resume)
    window.addEventListener('online', resume)
    window.addEventListener('offline', disconnect)
    return () => {
      window.clearTimeout(initialCheck)
      document.removeEventListener('visibilitychange', resume)
      window.removeEventListener('online', resume)
      window.removeEventListener('offline', disconnect)
      pendingRequest.current?.abort()
      pendingRequest.current = null
    }
  }, [checkConnection])

  const statusLabel = {
    checking: 'Checking connection',
    connected: 'Connected to your host',
    offline: 'Host not reachable',
  }[connection.status]

  return (
    <main>
      <header className="masthead">
        <a className="brand" href="/" aria-label="Quasar home">
          <span className="brand-mark" aria-hidden="true">Q</span>
          QUASAR
        </a>
        <span className="preview-label">Development preview</span>
      </header>

      <section className="intro" aria-labelledby="page-title">
        <p className="eyebrow">A civilisation in the making</p>
        <h1 id="page-title">A universe.<br />Within reach.</h1>
        <p className="intro-copy">Your private Quasar workspace, ready for phone and desktop.</p>
      </section>

      <section className="connection-card" aria-labelledby="connection-title">
        <div className="card-heading">
          <span className={`status-dot ${connection.status}`} aria-hidden="true" />
          <h2 id="connection-title">Host connection</h2>
        </div>
        <p className="connection-status" role="status" aria-live="polite">{statusLabel}</p>
        <p className="connection-detail">
          {connection.status === 'connected'
            ? `Host responded at ${new Date(connection.checkedAt!).toLocaleTimeString()}.`
            : connection.status === 'checking'
              ? 'Contacting the Quasar backend…'
              : 'Check Tailscale and that your PC is awake, then try again.'}
        </p>
        <button
          type="button"
          onClick={() => {
            setConnection({ status: 'checking' })
            void checkConnection()
          }}
          disabled={connection.status === 'checking'}
        >
          {connection.status === 'checking' ? 'Checking…' : 'Check connection'}
          <span aria-hidden="true">↗</span>
        </button>
      </section>

      <section className="foundation" aria-labelledby="foundation-title">
        <p className="eyebrow" id="foundation-title">The foundation</p>
        <div className="foundation-grid">
          <article><span className="feature-number">01</span><h2>Inspect</h2><p>Clear controls and detailed information.</p></article>
          <article><span className="feature-number">02</span><h2>Explore</h2><p>2D maps and 3D worlds are planned.</p></article>
          <article><span className="feature-number">03</span><h2>Simulate</h2><p>A separate C# simulation will power the world.</p></article>
        </div>
      </section>

      <footer><span className="footer-dot" aria-hidden="true" />Development skeleton. Game systems are still to come.</footer>
    </main>
  )
}

export default App
