import {
  formatTime, isCourseDestination,
  type AutomaticCoursePreview, type CoursePreview, type LabObject,
} from './lab'
import type { PlotInputs } from './useCoursePreview'

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
        Select a planet or moon on either map, in the object list, or below. Previewing
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
          <option value="orbit">Orbit preset (Earth or Moon)</option>
        </select>
      </label>
      {inputs.maneuverBudget !== undefined && (
        <details>
          <summary>Advanced</summary>
          <label>
            Additional delta-v limit · {inputs.maneuverBudget} km/s
            <input aria-label="Additional delta-v limit" type="range" min="1" max="100"
              value={inputs.maneuverBudget} disabled={disabled}
              onChange={(e) => onChange({ ...inputs, maneuverBudget: Number(e.target.value) })}
            />
          </label>
        </details>
      )}
      {running && (
        <p role="status">
          Stop time processing to recalculate and review a course.
        </p>
      )}
      {loading && <p role="status">Calculating course…</p>}
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
          {routes.lowestManeuverCost ? (
            <div className="course-options" role="group" aria-label="Route options">
              {([
                ['Fastest', routes.fastest],
                ['Lowest maneuver cost', routes.lowestManeuverCost],
              ] as const).map(([label, route]) => (
                <button key={route.previewId} aria-pressed={route === preview}
                  disabled={disabled} onClick={() => onSelectRoute(route.previewId)}>
                  <strong>{label}</strong>
                  <span>{formatDuration(route.trajectory.travelSeconds)} · {number(route.trajectory.requiredDeltaVKmPerSecond)} km/s</span>
                  <span>ETA {formatTime(route.trajectory.arrivalTicks)} · {route.trajectory.flyby
                    ? 'via ' + route.trajectory.flyby.bodyName : 'Direct'}</span>
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
              <dt>Arrival ETA</dt>
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
              <p>Braking margin {number(trajectory.brakingMarginKg ?? 0)} kg remaining after cutoff</p>
            </>
          )}
          {trajectory.fuel.unavailableReason && (
            <p className="muted">{trajectory.fuel.unavailableReason}</p>
          )}
          {trajectory.flyby ? (
            <p>Gravity assist at {trajectory.flyby.bodyName} · {formatTime(trajectory.flyby.atTicks)}
              {' · '}closest approach {number(trajectory.flyby.closestApproachKm)} km from center</p>
          ) : <p>Direct transfer</p>}
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
