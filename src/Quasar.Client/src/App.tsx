import { lazy, Suspense, useCallback, useMemo, useState } from 'react'
import { formatTime, isCourseDestination, type Snapshot } from './lab'
import CampaignPanel from './CampaignPanel'
import SiteNav from './SiteNav'
import { useLabSession } from './useLabSession'
import MapBoundary from './MapBoundary'
import CoursePanel from './CoursePanel'
import SystemObjectTree from './SystemObjectTree'
import { useCoursePreview, type PlotInputs } from './useCoursePreview'
import type { CourseLayer } from './lab'

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
  const [selectedId, setSelectedId] = useState('ship-pathfinder')
  const [view, setView] = useState<'2d' | '3d'>('2d')
  const [references, setReferences] = useState(true)
  const [burnDirection, setBurnDirection] = useState('prograde')
  const [burnSeconds, setBurnSeconds] = useState('30')
  const [plotting, setPlotting] = useState<PlotInputs | null>(null)
  const onInstanceChange = useCallback((next: Snapshot | null) => {
    setPlotting(null)
    if (next) {
      setSelectedId((id) =>
        next.world.objects.some((object) => object.id === id)
          ? id
          : (next.world.objects.find((object) => object.kind === 'spacecraft')?.id ??
            next.world.objects[0]?.id ??
            ''),
      )
    }
  }, [])
  const {
    snapshot, status, online, pending, notice, campaignMessage, refresh,
    command, campaignAction,
  } = useLabSession(onInstanceChange)

  const world = snapshot?.world,
    selected = world?.objects.find((o) => o.id === selectedId)
  const disabled = !online || pending || !snapshot
  const course = useCoursePreview(snapshot, plotting, online)
  const committedTrajectory = world?.trajectory
  const [selectedRouteId, setSelectedRouteId] = useState<string | null>(null)
  const fastest = course.routes?.fastest ?? null
  const lowestCost = course.routes?.lowestManeuverCost ?? null
  const tuned = course.routes?.tuned ?? null
  const preview = [fastest, lowestCost, tuned].find((route) => route?.previewId === selectedRouteId)
    ?? tuned ?? fastest
  const courses = useMemo<CourseLayer[]>(
    () => [
      ...(committedTrajectory
        ? [{ kind: 'committed' as const, trajectory: committedTrajectory }]
        : []),
      ...(preview
        ? [{ kind: 'preview' as const, trajectory: preview.trajectory }]
        : []),
    ],
    [committedTrajectory, preview],
  )
  const selectObject = (id: string) => {
    setSelectedId(id)
    if (
      plotting &&
      world?.objects.some((o) => o.id === id && isCourseDestination(o))
    )
      setPlotting({ ...plotting, destinationId: id })
  }
  const MapView = view === '2d' ? SystemMap : SpaceMap
  const campaignControls = (
    <>
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
      <SiteNav current="/" />
      <CampaignPanel
        status={status}
        online={online}
        pending={pending}
        message={campaignMessage}
        onAction={campaignAction}
      />
      {(notice || !online) && (
        <div className="notice" role="alert">
          {notice || 'Waiting for the authoritative host. Orders are disabled.'}
          <button onClick={() => void refresh()}>Refresh state</button>
        </div>
      )}
    </>
  )

  if (!world || !snapshot) return <main>{campaignControls}</main>
  return (
    <main>
      {campaignControls}
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
      <div role="status" aria-atomic="true">
        {snapshot.interrupt && (
          <div className="notice">
            <span>
              <strong>Paused at {formatTime(snapshot.interrupt.atTicks)}</strong>
              {' · '}{snapshot.interrupt.message}
            </span>
          </div>
        )}
      </div>
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
            <MapBoundary key={`${view}:${snapshot.instanceId}`}>
              <Suspense
                fallback={
                  <p className="loading">
                    Loading {view.toUpperCase()} display…
                  </p>
                }
              >
                <MapView
                  objects={world.objects}
                  courses={courses}
                  selectedId={selectedId}
                  references={references}
                  onSelect={selectObject}
                />
              </Suspense>
            </MapBoundary>
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
          <p className="course-legend">
            <span>━━ Committed</span> · <span>┄┄ Preview</span> · □ Departure ·
            ◇ Course endpoint / future destination
          </p>
          <p className="map-note">
            Symbols enlarged for readability · {world.referenceEpoch ? `Orbital epoch ${world.referenceEpoch}` : 'Legacy reference guides'}
          </p>
          <SystemObjectTree
            key={snapshot.instanceId}
            objects={world.objects}
            selectedId={selectedId}
            onSelect={selectObject}
          />
        </section>
        <aside className="inspector">
          {plotting && world && (
            <CoursePanel
              inputs={plotting}
              objects={world.objects}
              preview={preview}
              routes={course.routes}
              onSelectRoute={setSelectedRouteId}
              error={course.error}
              loading={course.loading}
              disabled={disabled}
              running={snapshot?.running ?? false}
              onChange={setPlotting}
              onRecalculate={course.recalculate}
              onCancel={() => setPlotting(null)}
              onIssue={() => {
                if (!preview) return
                void command('commit-course', {
                  previewId: preview.previewId,
                }).then(() => setPlotting(null))
              }}
            />
          )}

          <details open>
            <summary>Object inspection & orders</summary>
            {selected && world && (
              <div className="details-body">
                <p className="eyebrow">{selected.kind.toUpperCase()}</p>
                <h2>{selected.name}</h2>
                <p className="object-id">{selected.id}</p>
                {selected.parentId && <p className="muted">Orbits {world.objects.find((o) => o.id === selected.parentId)?.name}</p>}
                {!!selected.radiusKm && <p className="muted">Reference radius {selected.radiusKm.toLocaleString()} km</p>}
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
                      <strong>{world.ship.status === 'Arrived' ? 'Legacy body-center arrival' : world.ship.status}</strong>
                      <p>
                        Speed: {world.ship.speedKmPerSecond.toFixed(3)} km/s
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
                          <p>{world.trajectory?.arrivalKind === 'body-center' ? 'Legacy body-center arrival'
                            : world.trajectory?.arrivalKind === 'orbit' ? 'Planned orbital insertion' : 'Planned approach'}{' '}
                            {formatTime(world.ship.arrivalTicks!)}</p>
                        </>
                      )}
                    </div>
                    <h3>Course orders</h3>
                    <button
                      disabled={disabled || snapshot?.running || !!plotting || !!world.ship.burnEndTicks}
                      onClick={() =>
                        setPlotting({
                          shipId: selected.id,
                          destinationId: '',
                          arrivalKind: 'approach',
                        })
                      }
                    >
                      Plot Course
                    </button>
                    {world.ship.destinationId && (
                      <button
                        disabled={disabled || snapshot?.running}
                        onClick={() => void command('cancel-travel')}
                      >
                        Cancel committed course
                      </button>
                    )}
                    <p className="muted">
                      Stop time processing to plot, replace or cancel an order.
                      Cancelling a committed course retains momentum.
                    </p>
                    <h3>Propulsion</h3>
                    <a className="torch-designer-link" href="/torch-designer/">Open Torch Designer</a>
                    {world.ship.propulsion ? (
                      <dl>
                        {[
                          ['Dry mass', world.ship.propulsion.dryMassKg, 'kg'],
                          ['Wet mass', world.ship.propulsion.wetMassKg, 'kg'],
                          ['Available propellant', world.ship.propulsion.availablePropellantKg, 'kg'],
                          ['Rated mass flow', world.ship.propulsion.ratedMassFlowKgPerSecond, 'kg/s'],
                          ['Initial thrust acceleration', world.ship.propulsion.initialAccelerationMPerSecond2, 'm/s²'],
                          ['Theoretical impulsive delta-v', world.ship.propulsion.theoreticalDeltaVMPerSecond / 1000, 'km/s'],
                        ].map(([label, value, unit]) => (
                          <div key={label}>
                            <dt>{label}</dt>
                            <dd>{value.toLocaleString()} <small>{unit}</small></dd>
                          </div>
                        ))}
                      </dl>
                    ) : <p className="muted">Propulsion not modeled</p>}
                    <details><summary>Lab refit and refuel</summary>
                    <button disabled={disabled || snapshot.running || world.ship.status !== 'Coasting'}
                      onClick={() => void command('fit-exploration-torch')}>
                      Fit exploration torch and refuel
                    </button>
                    <p className="muted">Test refit. 300 kN thrust, 1,000 km/s exhaust velocity, and 20,000 kg of reaction mass.</p>
                    </details>
                    {world.ship.propulsion && (
                      <>
                        <h3>Manual engine burn</h3>
                        {world.ship.burnEndTicks ? (
                          <div className="order-buttons">
                            <p>Scheduled cutoff at {formatTime(world.ship.burnEndTicks)}.</p>
                            <button disabled={disabled || snapshot?.running} onClick={() => void command('cut-thrust')}>
                              Cut thrust
                            </button>
                            <p className="muted">Stop pauses time. Cut thrust ends the burn while paused.</p>
                          </div>
                        ) : (
                          <form className="order-buttons" onSubmit={(event) => {
                            event.preventDefault()
                            void command('start-burn', { burnDirection, burnSeconds: Number(burnSeconds) })
                          }}>
                            <label>
                              Burn direction{' '}
                              <select value={burnDirection} onChange={(event) => setBurnDirection(event.target.value)}>
                                <option value="prograde">Prograde</option>
                                <option value="retrograde">Retrograde</option>
                              </select>
                            </label>
                            <label>
                              Burn duration (seconds){' '}
                              <input type="number" min="1" max="3600" step="1" required
                                value={burnSeconds} onChange={(event) => setBurnSeconds(event.target.value)} />
                            </label>
                            <button disabled={disabled || snapshot?.running || !!plotting
                              || world.ship.status !== 'Coasting' || world.ship.propulsion.availablePropellantKg <= 0}>
                              Ignite engine
                            </button>
                            <p className="muted">Direction is fixed at ignition. Thrust stops when time or propellant runs out.</p>
                          </form>
                        )}
                      </>
                    )}
                  </>
                ) : selected.kind === 'planet' ? (
                  <>
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
                    {selected.kind === 'star' ? 'System reference origin and gravitational primary.' : selected.kind === 'station' ? 'Fictional orbital installation.' : 'Natural orbital body.'}
                  </p>
                )}
              </div>
            )}
          </details>
        </aside>
      </div>
      <details className="history" open>
        <summary>
          Event history <span>{world.events.length} records</span>
        </summary>
        <ol>
          {world.events
            .slice()
            .reverse()
            .map((event) => (
              <li key={event.sequence}>
                <time>{formatTime(event.atTicks)}</time>
                <span className={`event-kind ${event.kind}`}>{event.arrivalLabel ?? (event.kind === 'arrival' ? 'Legacy body-center arrival' : event.kind)}</span>
                <span>{event.message}</span>
              </li>
            ))}
        </ol>
      </details>
      <footer>
        Continuous play runs on the host, even with the browser closed.
        A game interrupt or Stop pauses the clock.
      </footer>
    </main>
  )
}
export default App
