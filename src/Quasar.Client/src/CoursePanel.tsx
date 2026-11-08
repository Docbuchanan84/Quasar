import { formatTime, type CoursePreview, type LabObject } from './lab'
import type { PlotInputs } from './useCoursePreview'

export default function CoursePanel({
  inputs,
  objects,
  preview,
  error,
  loading,
  valid,
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
  error?: string
  loading: boolean
  valid: boolean
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
        Select a planet on either map, in the object list, or below. Previewing
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
            .filter((o) => o.kind === 'planet')
            .map((o) => (
              <option key={o.id} value={o.id}>
                {o.name}
              </option>
            ))}
        </select>
      </label>
      <label>
        Flight duration (seconds)
        <input
          aria-label="Flight duration (seconds)"
          type="number"
          inputMode="numeric"
          min="1"
          max="315576000"
          step="1"
          value={inputs.flightSeconds}
          disabled={disabled}
          onChange={(e) =>
            onChange({ ...inputs, flightSeconds: e.target.value })
          }
        />
      </label>
      {!valid && (
        <p role="alert">Enter a whole number from 1 to 315,576,000 seconds.</p>
      )}
      {running && (
        <p role="status">
          Stop time processing to recalculate and review a course.
        </p>
      )}
      {loading && <p role="status">Calculating course…</p>}
      {error && <p role="alert">{error}</p>}
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
              <dd>{number(trajectory.travelSeconds)} s</dd>
            </div>
            <div>
              <dt>ETA</dt>
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
          {trajectory.fuel.unavailableReason && (
            <p className="muted">{trajectory.fuel.unavailableReason}</p>
          )}
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
            disabled || running || !valid || !inputs.destinationId || loading
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
