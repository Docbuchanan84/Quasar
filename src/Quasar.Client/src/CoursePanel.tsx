import {
  formatTime,
  type ArrivalKind, type AutomaticCoursePreview, type CoursePreview, type LabObject,
} from './lab'
import type { PlotInputs } from './useCoursePreview'
import type { useReachableDestinations } from './useReachableDestinations'
import CourseTuningControls from './CourseTuningControls'

function formatDuration(seconds: number) {
  seconds = Math.max(1, Math.round(seconds))
  const days = Math.floor(seconds / 86400)
  const hours = Math.floor(seconds / 3600) % 24
  const minutes = Math.floor(seconds / 60) % 60
  return [[days, 'd'], [hours, 'h'], [minutes, 'm'], [seconds % 60, 's']]
    .filter(([value]) => value !== 0)
    .slice(0, 2)
    .map(([value, unit]) => String(value) + unit)
    .join(' ')
}

export default function CoursePanel({
  inputs,
  objects,
  preview,
  routes,
  reachable,
  selectedLabel,
  onSelectRoute,
  error,
  loading,
  disabled,
  running,
  onChange,
  onRecalculate,
  onCancel,
  onIssue,
}: {
  inputs: PlotInputs
  objects: LabObject[]
  preview: CoursePreview | null
  routes: AutomaticCoursePreview | null
  reachable: ReturnType<typeof useReachableDestinations>
  selectedLabel?: string
  onSelectRoute: (previewId: string, label: string) => void
  error?: string
  loading: boolean
  disabled: boolean
  running: boolean
  onChange: (value: PlotInputs) => void
  onRecalculate: () => void
  onCancel: () => void
  onIssue: () => void
}) {
  const trajectory = preview?.trajectory
  const firstBurn = trajectory?.burnSchedule?.[0]
  const coastBeforeBurn = trajectory && firstBurn
    ? Number(BigInt(firstBurn.startTicks) - BigInt(trajectory.departureTicks)) / 10_000_000
    : 0
  const distinctTuned = routes?.tuned && routes.tuned.previewId !== routes.fastest?.previewId
    && routes.tuned.previewId !== routes.lowestManeuverCost?.previewId ? routes.tuned : null
  const origin = objects.find((object) => object.id === inputs.shipId)
  const destination = objects.find((object) => object.id === inputs.destinationId)
  const options = objects.filter((object) => reachable.ids.has(object.id) || routes?.fastest?.destinationId === object.id)
  const selectedDestination = options.some((object) => object.id === inputs.destinationId) ? inputs.destinationId : ''
  const navigation = origin?.navigation
  const arrivalKinds = navigation?.arrivalKinds ?? ['approach', 'orbit'] as const
  const sailTime = (route: CoursePreview) => route.trajectory.sailSchedule?.reduce((sum, trim) => sum + trim.durationSeconds, 0)
  const arrivalLabels: Record<ArrivalKind, string> = { approach: 'Match speed near destination', orbit: 'Enter orbit', flyby: 'Fly past' }
  const number = (value: number) =>
    value.toLocaleString(undefined, { maximumFractionDigits: 3 })
  return (
    <section className="course-panel" aria-label="Course plotting">
      <h2>Plot Course</h2>
      <p>
        Origin:{' '}
        {objects.find((o) => o.id === inputs.shipId)?.name ?? inputs.shipId} ·
        current departure state
      </p>
      <label>
        Arrival condition
        <select value={inputs.arrivalKind} disabled={disabled}
          onChange={(e) => onChange({ ...inputs, arrivalKind: e.target.value as ArrivalKind })}>
          {arrivalKinds.map((kind) => <option key={kind} value={kind}>{arrivalLabels[kind]}</option>)}
        </select>
      </label>
      <label>
        Destination
        <select
          aria-label="Course destination"
          value={selectedDestination}
          disabled={disabled || running || !options.length}
          onChange={(e) =>
            onChange({ ...inputs, destinationId: e.target.value })
          }
        >
          <option value="">Choose destination…</option>
          {options.map((o) => (
              <option key={o.id} value={o.id}>
                {o.name}
              </option>
            ))}
        </select>
      </label>
      {running && (
        <p role="status">
          Stop time processing to recalculate and review a course.
        </p>
      )}
      {reachable.loading && <p role="status">Checking reachable destinations…</p>}
      {reachable.incomplete && <p role="status">Some destinations could not be checked. Recalculate to try again.</p>}
      {reachable.active && !reachable.loading && !reachable.incomplete && !options.length && (
        <p role="status">No verified destinations for this arrival condition and these constraints.</p>
      )}
      {loading && <p role="status">Calculating course to {destination?.name ?? 'destination'}…</p>}
      {inputs.destinationId && navigation?.advancedConstraints && (
        <details>
        <summary>Advanced constraints</summary>
        <CourseTuningControls inputs={inputs} trajectory={trajectory} loading={loading}
          disabled={disabled || running} onChange={onChange} />
        </details>
      )}
      {error && <p role="alert">{error}</p>}
      {routes && !routes.fastest && (
        <div role="status">
          <p>No verified course to {destination?.name ?? 'the selected destination'} with these settings.</p>
          {!!routes.infeasibleReasons?.length && (
            <ul>{routes.infeasibleReasons.map((reason) => <li key={reason}>{reason}</li>)}</ul>
          )}
        </div>
      )}
      {routes?.fastest && (
        <>
          <p className="muted">Best routes found. {navigation?.efficiencyMetric === 'maneuver-time'
            ? 'Efficiency favors less time maneuvering under sail.' : 'Efficiency favors less propellant.'}
            {' '}Direct routes favor fewer assists and a shorter path.</p>
            <div className="course-options" role="group" aria-label="Route options">
              {([
                ...(distinctTuned ? [['Adjusted route', distinctTuned] as const] : []),
                ['Fastest', routes.fastest],
                ...(routes.lowestManeuverCost ? [['Most efficient', routes.lowestManeuverCost] as const] : []),
                ...(routes.mostDirect ? [['Most direct', routes.mostDirect] as const] : []),
              ] as const).map(([label, route]) => (
                <button key={label} aria-pressed={label === (selectedLabel ?? (preview === distinctTuned ? 'Adjusted route' : 'Fastest'))}
                  disabled={disabled} onClick={() => onSelectRoute(route.previewId, label)}>
                  <strong>{label}</strong>
                  <span>{formatDuration(route.trajectory.travelSeconds)} · {sailTime(route) !== undefined
                    ? (sailTime(route) ? formatDuration(sailTime(route)!) + ' under sail' : 'coast throughout')
                    : route.trajectory.fuel.propellantMassKg !== null ? number(route.trajectory.fuel.propellantMassKg) + ' kg propellant' : 'Propellant unavailable'}</span>
                  <span>ETA {formatTime(route.trajectory.arrivalTicks)}{route.trajectory.assistBodies?.length
                    ? ' · via ' + route.trajectory.assistBodies.join(', ') : ''}</span>
                  {route.trajectory.criticalBrakingReserve && <span className="course-reserve">Critically low propellant reserve</span>}
                </button>
              ))}
            </div>
        </>
      )}
      {trajectory && preview && (
        <>
          <p className="eyebrow">PREVIEW · NOT ORDERED</p>
          <dl>
            <div>
              <dt>Destination</dt>
              <dd>
                {objects.find((o) => o.id === preview.destinationId)?.name}
              </dd>
            </div>
            <div>
              <dt>{coastBeforeBurn > 0 ? 'Course starts' : 'Departure'}</dt>
              <dd>{formatTime(trajectory.departureTicks)}</dd>
            </div>
            {coastBeforeBurn > 0 && (
              <div>
                <dt>Coast before first burn</dt>
                <dd>{formatDuration(coastBeforeBurn)}</dd>
              </div>
            )}
            <div>
              <dt>Sampled path distance ≈</dt>
              <dd>{number(preview.sampledDistanceKm)} km</dd>
            </div>
            <div>
              <dt>Travel duration</dt>
              <dd>{formatDuration(trajectory.travelSeconds)}</dd>
            </div>
            <div>
              <dt>{trajectory.arrivalKind === 'flyby' ? 'Flyby ETA' : trajectory.arrival?.kind === 'orbit' ? 'Orbit insertion ETA' : 'Approach ETA'}</dt>
              <dd>{formatTime(trajectory.arrivalTicks)}</dd>
            </div>
            {trajectory.arrivalKind !== 'flyby' && <div>
              <dt>Required delta-v</dt>
              <dd>{number(trajectory.requiredDeltaVKmPerSecond)} km/s</dd>
            </div>}
            <div>
              <dt>Fuel estimate</dt>
              <dd>
                {trajectory.fuel.propellantMassKg === null
                  ? 'Unavailable'
                  : `${number(trajectory.fuel.propellantMassKg)} kg`}
              </dd>
            </div>
          </dl>
          {trajectory.arrivalKind === 'flyby' && trajectory.flyby && (
            <p>Pass {trajectory.flyby.bodyName} at {number(trajectory.flyby.closestApproachKm -
              (objects.find((object) => object.id === preview.destinationId)?.radiusKm ?? 0))} km altitude
                {' · '}{number(trajectory.flybyRelativeSpeedKmPerSecond ?? 0)} km/s relative speed.</p>
          )}
          <details>
          <summary>Maneuvers and navigation</summary>
          {trajectory.sailSchedule && (trajectory.sailSchedule.length > 0
            ? <ul>{trajectory.sailSchedule.map((trim) => <li key={trim.startTicks}>
              Sail trim {trim.angleDegrees}° · {formatTime(trim.startTicks)} · {formatDuration(trim.durationSeconds)}
            </li>)}</ul>
            : <p>Coast to the flyby with the sail feathered.</p>)}
          {trajectory.arrival && (
            <>
              <p>{trajectory.arrival.kind === 'orbit' ? 'Bound orbit' : 'Velocity-matched approach'}
                {' · '}{number(trajectory.arrival.targetRadiusKm)} km from target center
                {' · '}gravity primary {objects.find((o) => o.id === trajectory.arrival?.primaryBodyId)?.name}</p>
              <p>Arrival error {number(trajectory.arrival.positionErrorKm * 1000)} m · {number(trajectory.arrival.velocityErrorKmPerSecond * 1000)} m/s</p>
              <h3>Estimated burn schedule</h3>
              <ul>{trajectory.burnSchedule?.map((burn) => (
                <li key={burn.phase}>{burn.phase} · {formatTime(burn.startTicks)} · {formatDuration(burn.durationSeconds)} · {number(burn.propellantKg)} kg</li>
              ))}</ul>
              {trajectory.brakingMarginKg != null && (
                <p>Braking margin {number(trajectory.brakingMarginKg)} kg remaining after cutoff
                  {trajectory.brakingMarginPercent != null && (
                    <> ({trajectory.brakingMarginPercent < 0.01 ? '<0.01' : trajectory.brakingMarginPercent.toLocaleString(undefined, { maximumFractionDigits: 2 })}% of departure propellant)</>
                  )}
                </p>
              )}
              {trajectory.criticalBrakingReserve && (
                <p className="course-reserve course-reserve-alert" role="alert">
                  Critically low propellant reserve. This course leaves less than 1% of departure propellant for later maneuvers.
                </p>
              )}
            </>
          )}
          {trajectory.fuel.unavailableReason && (
            <p className="muted">{trajectory.fuel.unavailableReason}</p>
          )}
          {trajectory.flyby && trajectory.arrivalKind !== 'flyby' ? (
            <p>Gravity assist at {trajectory.flyby.bodyName} · {formatTime(trajectory.flyby.atTicks)}
              {' · '}closest approach {number(trajectory.flyby.closestApproachKm)} km from center</p>
          ) : trajectory.gravityTransitions?.length ? (
            <>
              <h3>Automatic gravity transitions</h3>
              <ul>{trajectory.gravityTransitions.map((transition) => (
                <li key={`${transition.atTicks}-${transition.bodyId}`}>
                  {transition.bodyName} · {formatTime(transition.atTicks)}
                </li>
              ))}</ul>
            </>
          ) : null}
          <ul className="course-warnings" aria-label="Feasibility warnings">
            {preview.warnings.map((w) => (
              <li key={w}>{w}</li>
            ))}
          </ul>
          </details>
        </>
      )}
      <div className="order-buttons">
        <button
          onClick={onRecalculate}
          disabled={
            disabled || running || loading || (!inputs.destinationId && reachable.loading)
          }
        >
          Recalculate
        </button>
        <button onClick={onCancel}>Cancel preview</button>
        <button
          className="primary"
          onClick={onIssue}
          disabled={disabled || running || !preview || loading}
        >
          Issue Order
        </button>
      </div>
    </section>
  )
}
