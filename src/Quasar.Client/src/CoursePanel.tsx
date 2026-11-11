import {
  formatTime, isCourseDestination,
  type AutomaticCoursePreview, type CoursePreview, type LabObject,
} from './lab'
import type { PlotInputs } from './useCoursePreview'
import CourseTuningControls from './CourseTuningControls'

function formatDuration(seconds: number) {
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
  onSelectRoute: (previewId: string) => void
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
  const distinctTuned = routes?.tuned && routes.tuned.previewId !== routes.fastest?.previewId
    && routes.tuned.previewId !== routes.lowestManeuverCost?.previewId ? routes.tuned : null
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
      <p className="muted">
        Select any destination on either map, in the object list, or below. The computer plans gravity transitions. Previewing
        and cancelling a preview issue no orders.
      </p>
      <label>
        Destination
        <select
          aria-label="Course destination"
          value={inputs.destinationId}
          disabled={disabled}
          onChange={(e) =>
            onChange({ ...inputs, destinationId: e.target.value })
          }
        >
          <option value="">Choose destination…</option>
          {objects
            .filter(isCourseDestination)
            .map((o) => (
              <option key={o.id} value={o.id}>
                {o.name}
              </option>
            ))}
        </select>
      </label>
      <label>
        Arrival condition
        <select value={inputs.arrivalKind} disabled={disabled}
          onChange={(e) => onChange({ ...inputs, arrivalKind: e.target.value === 'orbit' ? 'orbit' : 'approach' })}>
          <option value="approach">Velocity-matched approach</option>
          <option value="orbit">Orbital insertion</option>
        </select>
      </label>
      {running && (
        <p role="status">
          Stop time processing to recalculate and review a course.
        </p>
      )}
      {loading && <p role="status">Calculating course…</p>}
      {inputs.destinationId && (
        <CourseTuningControls inputs={inputs} trajectory={trajectory} loading={loading}
          disabled={disabled || running} onChange={onChange} />
      )}
      {error && <p role="alert">{error}</p>}
      {routes && !routes.fastest && (
        <div role="status">
          <p>No executable course found in the bounded search.</p>
          {!!routes.infeasibleReasons?.length && (
            <ul>{routes.infeasibleReasons.map((reason) => <li key={reason}>{reason}</li>)}</ul>
          )}
        </div>
      )}
      {routes?.fastest && (
        <>
          <p className="muted">Best routes found for the flight times searched.</p>
          {routes.lowestManeuverCost || routes.tuned ? (
            <div className="course-options" role="group" aria-label="Route options">
              {([
                ...(distinctTuned ? [['Adjusted route', distinctTuned] as const] : []),
                ['Fastest', routes.fastest],
                ...(routes.lowestManeuverCost ? [['Lowest maneuver cost', routes.lowestManeuverCost] as const] : []),
              ] as const).map(([label, route]) => (
                <button key={route.previewId} aria-pressed={route === preview}
                  disabled={disabled} onClick={() => onSelectRoute(route.previewId)}>
                  <strong>{label}</strong>
                  <span>{formatDuration(route.trajectory.travelSeconds)} · {number(route.trajectory.requiredDeltaVKmPerSecond)} km/s</span>
                  <span>ETA {formatTime(route.trajectory.arrivalTicks)} · {route.trajectory.flyby
                    ? 'via ' + route.trajectory.flyby.bodyName : 'Direct'}</span>
                  {route.trajectory.criticalBrakingReserve && <span className="course-reserve">Critically low propellant reserve</span>}
                </button>
              ))}
            </div>
          ) : <h3>Fastest and lowest maneuver cost</h3>}
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
              <dt>Departure</dt>
              <dd>{formatTime(trajectory.departureTicks)}</dd>
            </div>
            <div>
              <dt>Sampled path distance ≈</dt>
              <dd>{number(preview.sampledDistanceKm)} km</dd>
            </div>
            <div>
              <dt>Travel duration</dt>
              <dd>{formatDuration(trajectory.travelSeconds)}</dd>
            </div>
            <div>
              <dt>{trajectory.arrival?.kind === 'orbit' ? 'Orbit insertion ETA' : 'Approach ETA'}</dt>
              <dd>{formatTime(trajectory.arrivalTicks)}</dd>
            </div>
            <div>
              <dt>Required delta-v</dt>
              <dd>{number(trajectory.requiredDeltaVKmPerSecond)} km/s</dd>
            </div>
            <div>
              <dt>Fuel estimate</dt>
              <dd>
                {trajectory.fuel.propellantMassKg === null
                  ? 'Unavailable'
                  : `${number(trajectory.fuel.propellantMassKg)} kg`}
              </dd>
            </div>
          </dl>
          {trajectory.arrival && (
            <>
              <p>{trajectory.arrival.kind === 'orbit' ? 'Bound orbit' : 'Velocity-matched approach'}
                {' · '}{number(trajectory.arrival.targetRadiusKm)} km from target center
                {' · '}gravity primary {objects.find((o) => o.id === trajectory.arrival?.primaryBodyId)?.name}</p>
              <p>Arrival error {number(trajectory.arrival.positionErrorKm * 1000)} m · {number(trajectory.arrival.velocityErrorKmPerSecond * 1000)} m/s</p>
              <h3>Estimated burn schedule</h3>
              <ul>{trajectory.burnSchedule?.map((burn) => (
                <li key={burn.phase}>{burn.phase} · {formatTime(burn.startTicks)} · {number(burn.durationSeconds)} s · {number(burn.propellantKg)} kg</li>
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
          {trajectory.flyby ? (
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
          ) : <p>Single gravity frame</p>}
          <ul className="course-warnings" aria-label="Feasibility warnings">
            {preview.warnings.map((w) => (
              <li key={w}>{w}</li>
            ))}
          </ul>
        </>
      )}
      <div className="order-buttons">
        <button
          onClick={onRecalculate}
          disabled={
            disabled || running || !inputs.destinationId || loading
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
