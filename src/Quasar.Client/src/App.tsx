import { lazy, Suspense, useCallback, useEffect, useRef, useState } from 'react'
import { formatTime, objectColor, type Snapshot } from './lab'
import MapBoundary from './MapBoundary'

const SystemMap = lazy(() => import('./SystemMap'))
const SpaceMap = lazy(() => import('./SpaceMap'))
const durations = [
  [1, '1 second'],
  [5, '5 seconds'],
  [30, '30 seconds'],
  [60, '1 minute'],
  [3600, '1 hour'],
  [86400, '1 day'],
  [432000, '5 days'],
  [1296000, '15 days'],
  [2592000, '30 days'],
] as const

function App() {
  const [snapshot, setSnapshot] = useState<Snapshot | null>(null)
  const latest = useRef<Snapshot | null>(null)
  const [selectedId, setSelectedId] = useState('ship-pathfinder')
  const [view, setView] = useState<'2d' | '3d'>('2d')
  const [references, setReferences] = useState(true)
  const [online, setOnline] = useState(false)
  const [pending, setPending] = useState(false)
  const [notice, setNotice] = useState('')
  const [clientId] = useState(() => crypto.randomUUID())
  const mounted = useRef(true),
    fetching = useRef(false)
  const accept = useCallback((next: Snapshot) => {
    if (!mounted.current) return
    const previous = latest.current
    if (
      previous?.instanceId === next.instanceId &&
      next.revision < previous.revision
    )
      return
    if (previous && previous.instanceId !== next.instanceId)
      setNotice('Host restarted. The in-memory lab has reset.')
    latest.current = next
    setSnapshot(next)
    setOnline(true)
  }, [])
  const refresh = useCallback(async () => {
    if (fetching.current) return
    fetching.current = true
    try {
      const response = await fetch('/api/lab', {
        cache: 'no-store',
        signal: AbortSignal.timeout(5000),
      })
      if (!response.ok) throw new Error('Host unavailable')
      accept(await response.json())
    } catch {
      if (mounted.current) setOnline(false)
    } finally {
      fetching.current = false
    }
  }, [accept])
  useEffect(() => {
    mounted.current = true
    const resume = () => {
      if (!document.hidden) void refresh()
    }
    const offline = () => setOnline(false)
    const timer = window.setInterval(() => {
      if (!document.hidden) void refresh()
    }, 500)
    const initial = window.setTimeout(() => void refresh(), 0)
    // A reload gets a new client ID and cannot silently renew the previous browser's run.
    const heartbeat = window.setInterval(() => {
      const state = latest.current
      if (!document.hidden && state?.running)
        void fetch('/api/lab/lease', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ instanceId: state.instanceId, clientId }),
          signal: AbortSignal.timeout(3000),
        }).catch(() => {})
    }, 1000)
    document.addEventListener('visibilitychange', resume)
    window.addEventListener('online', resume)
    window.addEventListener('offline', offline)
    return () => {
      mounted.current = false
      clearTimeout(initial)
      clearInterval(timer)
      clearInterval(heartbeat)
      document.removeEventListener('visibilitychange', resume)
      window.removeEventListener('online', resume)
      window.removeEventListener('offline', offline)
    }
  }, [clientId, refresh])

  async function command(
    operation: string,
    extra: Record<string, unknown> = {},
  ) {
    const state = latest.current
    if (!state || pending) return
    setPending(true)
    setNotice('')
    try {
      const response = await fetch('/api/lab/commands', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          instanceId: state.instanceId,
          revision: state.revision,
          clientId,
          operation,
          ...extra,
        }),
        signal: AbortSignal.timeout(5000),
      })
      const result = await response.json()
      if (!response.ok) {
        setNotice(result.error ?? 'Command rejected.')
        await refresh()
      } else accept(result)
    } catch {
      setNotice(
        'Command outcome uncertain. Refreshing state; the command will not be resent.',
      )
      setOnline(false)
      await refresh()
    } finally {
      setPending(false)
    }
  }

  const world = snapshot?.world,
    selected = world?.objects.find((o) => o.id === selectedId)
  const disabled = !online || pending || !snapshot
  const MapView = view === '2d' ? SystemMap : SpaceMap
  return (
    <main>
      <header className="masthead">
        <div>
          <span className="brand">QUASAR</span>
          <span className="lab-tag"> / SYSTEM LAB 002</span>
        </div>
        <span
          className={`connection ${online ? 'connected' : ''}`}
          role="status"
        >
          {online ? 'HOST CONNECTED' : 'CONNECTING / OFFLINE'}
        </span>
      </header>
      <section className="time-console" aria-label="Simulation time controls">
        <div className="clock">
          <span className="eyebrow">SIMULATION ELAPSED</span>
          <strong>{formatTime(world?.elapsedTicks ?? 0)}</strong>
          <span className="run-status">
            {snapshot?.stopReason ?? 'Contacting host…'}
          </span>
        </div>
        <div className="time-inputs">
          <label>
            Advance interval
            <select
              aria-label="Advance interval"
              value={snapshot?.durationSeconds ?? 3600}
              disabled={disabled || snapshot?.running}
              onChange={(e) =>
                void command('configure', {
                  durationSeconds: Number(e.target.value),
                  mode: snapshot?.mode,
                })
              }
            >
              {durations.map(([value, label]) => (
                <option key={value} value={value}>
                  {label}
                </option>
              ))}
            </select>
          </label>
          <label>
            Processing mode
            <select
              aria-label="Processing mode"
              value={snapshot?.mode ?? 'once'}
              disabled={disabled || snapshot?.running}
              onChange={(e) =>
                void command('configure', {
                  mode: e.target.value,
                  durationSeconds: snapshot?.durationSeconds,
                })
              }
            >
              <option value="once">Once</option>
              <option value="continuous">Continuous</option>
            </select>
          </label>
          <button
            className={snapshot?.running ? 'stop-button' : 'primary'}
            disabled={disabled}
            onClick={() => void command(snapshot?.running ? 'stop' : 'play')}
          >
            {pending ? 'Sending…' : snapshot?.running ? '■ Stop' : '▶ Play'}
          </button>
        </div>
      </section>
      {(notice || !online) && (
        <div className="notice" role="alert">
          {notice || 'Waiting for the authoritative host. Orders are disabled.'}
          <button onClick={() => void refresh()}>Refresh state</button>
        </div>
      )}
      <div className="workspace">
        <section className="map-panel" aria-label="System display">
          <div className="panel-heading">
            <h1>
              HELIOS <span>TEST SYSTEM</span>
            </h1>
            <div className="view-switch" aria-label="Map view">
              <button
                aria-pressed={view === '2d'}
                onClick={() => setView('2d')}
              >
                2D
              </button>
              <button
                aria-pressed={view === '3d'}
                onClick={() => setView('3d')}
              >
                3D
              </button>
            </div>
          </div>
          <div className="map-viewport">
            {world ? (
              <MapBoundary key={view}>
                <Suspense
                  fallback={
                    <p className="loading">
                      Loading {view.toUpperCase()} display…
                    </p>
                  }
                >
                  <MapView
                    objects={world.objects}
                    selectedId={selectedId}
                    destinationId={world.ship.destinationId}
                    references={references}
                    onSelect={setSelectedId}
                  />
                </Suspense>
              </MapBoundary>
            ) : (
              <p className="loading">Acquiring system state…</p>
            )}
          </div>
          <div className="map-footer">
            <label>
              <input
                type="checkbox"
                checked={references}
                onChange={(e) => setReferences(e.target.checked)}
              />{' '}
              Reference guides
            </label>
            <span>
              {view === '2d'
                ? 'XY projection · labels show Z'
                : 'XYZ space · drag to rotate'}
            </span>
          </div>
          <p className="map-note">
            Symbols enlarged for readability · planets stationary · orbit guides
            illustrative
          </p>
          <nav className="object-list" aria-label="System objects">
            {world?.objects.map((object) => (
              <button
                key={object.id}
                aria-pressed={selectedId === object.id}
                onClick={() => setSelectedId(object.id)}
              >
                <span
                  style={{ color: objectColor(object.kind) }}
                  aria-hidden="true"
                >
                  {object.kind === 'star'
                    ? '☉'
                    : object.kind === 'planet'
                      ? '●'
                      : '▲'}
                </span>{' '}
                {object.name}
              </button>
            ))}
          </nav>
        </section>
        <aside className="inspector">
          <details open>
            <summary>Object inspection & orders</summary>
            {selected && world && (
              <div className="details-body">
                <p className="eyebrow">{selected.kind.toUpperCase()}</p>
                <h2>{selected.name}</h2>
                <p className="object-id">{selected.id}</p>
                <dl>
                  {(['x', 'y', 'z'] as const).map((axis) => (
                    <div key={axis}>
                      <dt>{axis.toUpperCase()}</dt>
                      <dd data-testid={`position-${axis}`}>
                        {selected.position[axis].toLocaleString(undefined, {
                          maximumFractionDigits: 3,
                        })}{' '}
                        <small>km</small>
                      </dd>
                    </div>
                  ))}
                </dl>
                {selected.kind === 'spacecraft' ? (
                  <>
                    <div className="ship-state">
                      <strong>{world.ship.status}</strong>
                      <p>
                        Speed: {world.ship.speedKmPerSecond} km/s · provisional
                      </p>
                      {world.ship.destinationId && (
                        <>
                          <p>
                            Destination:{' '}
                            {
                              world.objects.find(
                                (o) => o.id === world.ship.destinationId,
                              )?.name
                            }
                          </p>
                          <p>
                            Remaining:{' '}
                            {world.ship.remainingDistanceKm.toLocaleString(
                              undefined,
                              { maximumFractionDigits: 0 },
                            )}{' '}
                            km
                          </p>
                          <p>Arrival: {formatTime(world.ship.arrivalTicks!)}</p>
                        </>
                      )}
                    </div>
                    <h3>Travel orders</h3>
                    <div className="order-buttons">
                      {world.objects
                        .filter((o) => o.kind === 'planet')
                        .map((planet) => (
                          <button
                            key={planet.id}
                            disabled={
                              disabled ||
                              snapshot?.running ||
                              world.ship.destinationId === planet.id ||
                              (selected.position.x === planet.position.x &&
                                selected.position.y === planet.position.y &&
                                selected.position.z === planet.position.z)
                            }
                            onClick={() =>
                              void command('travel', {
                                destinationId: planet.id,
                              })
                            }
                          >
                            Travel to {planet.name}
                          </button>
                        ))}
                    </div>
                    <p className="muted">
                      Stop time processing to issue or replace an order.
                    </p>
                  </>
                ) : selected.kind === 'planet' ? (
                  <>
                    <p className="muted">
                      Stationary lab body · parent: Helios
                    </p>
                    <div
                      className="surface-inset"
                      role="img"
                      aria-label="Illustrative planetary terrain with a coordinate grid and sample tactical overlays"
                    >
                      <span className="surface-marker">◇</span>
                      <span className="surface-caption">
                        SURFACE STUDY / {selected.name.toUpperCase()}
                      </span>
                    </div>
                    <p className="muted">
                      Representative terrain and overlays only. No simulated
                      geography or infrastructure.
                    </p>
                  </>
                ) : (
                  <p className="muted">
                    System reference origin. Stellar physics deferred.
                  </p>
                )}
              </div>
            )}
          </details>
        </aside>
      </div>
      <details className="history" open>
        <summary>
          Event history <span>{world?.events.length ?? 0} records</span>
        </summary>
        <ol>
          {world?.events
            .slice()
            .reverse()
            .map((event) => (
              <li key={event.sequence}>
                <time>{formatTime(event.atTicks)}</time>
                <span className={`event-kind ${event.kind}`}>{event.kind}</span>
                <span>{event.message}</span>
              </li>
            ))}
        </ol>
      </details>
      <footer>
        IN-MEMORY TEST ENVIRONMENT · Refresh retains state. Host restart resets
        the lab. Continuous processing stops on arrival or controller lease
        expiry.
      </footer>
    </main>
  )
}
export default App
