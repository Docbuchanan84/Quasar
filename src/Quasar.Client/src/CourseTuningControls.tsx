import type { Trajectory } from './lab'
import type { PlotInputs } from './useCoursePreview'

const maximumSeconds = 315576000
const timeScale = Math.log(maximumSeconds)

export default function CourseTuningControls({ inputs, trajectory, loading, disabled, onChange }: {
  inputs: PlotInputs
  trajectory?: Trajectory
  loading: boolean
  disabled: boolean
  onChange: (inputs: PlotInputs) => void
}) {
  const seconds = (!loading && trajectory?.travelSeconds) || inputs.preferredFlightSeconds || 86400
  const actualReserve = loading ? undefined : trajectory?.brakingMarginPercent
  const reserve = inputs.lockReserve ? inputs.reservePercent ?? 0
    : actualReserve ?? inputs.reservePercent ?? 0
  const timePosition = Math.log(Math.max(1, seconds)) / timeScale * 1000
  const days = seconds / 86400

  return (
    <section className="course-tuning" aria-label="Linked course controls">
      <h3>Tune course</h3>
      <p className="muted">Adjust travel time or fuel reserve. Lock either to hold it while Quasar adjusts the route.</p>
      <label>
        Travel time · {days >= 1 ? `${days.toFixed(2)} days` : `${(seconds / 3600).toFixed(2)} hours`}
        <input aria-label="Travel time" type="range" min="0" max="1000" step="1"
          value={timePosition} disabled={disabled}
          aria-valuetext={`${days.toFixed(2)} days`}
          onChange={(event) => onChange({ ...inputs,
            preferredFlightSeconds: Math.max(1, Math.round(Math.exp(Number(event.target.value) / 1000 * timeScale))),
            reservePercent: inputs.lockReserve ? inputs.reservePercent ?? Math.floor(reserve) : undefined,
          })} />
      </label>
      <button type="button" aria-pressed={!!inputs.lockFlightTime}
        disabled={disabled} aria-label={inputs.lockFlightTime ? 'Unlock travel time' : 'Lock travel time'}
        onClick={() => onChange({ ...inputs, lockFlightTime: !inputs.lockFlightTime, preferredFlightSeconds: seconds })}>
        {inputs.lockFlightTime ? '🔒 Travel time locked' : '🔓 Lock travel time'}
      </button>
      <label>
        {inputs.lockReserve ? 'Keep at least' : 'Propellant reserve'} · {reserve.toFixed(1)}%
        <input aria-label="Propellant reserve" type="range" min="0" max="99" step="1"
          value={Math.min(99, reserve)} disabled={disabled}
          onChange={(event) => onChange({ ...inputs,
            reservePercent: Number(event.target.value),
            preferredFlightSeconds: inputs.lockFlightTime ? seconds : undefined,
          })} />
      </label>
      <button type="button" aria-pressed={!!inputs.lockReserve}
        disabled={disabled} aria-label={inputs.lockReserve ? 'Unlock propellant reserve' : 'Lock propellant reserve'}
        onClick={() => onChange({ ...inputs, lockReserve: !inputs.lockReserve,
          reservePercent: inputs.lockReserve ? undefined : Math.min(99, Math.floor(reserve)),
        })}>
        {inputs.lockReserve ? '🔒 Reserve locked' : '🔓 Lock reserve'}
      </button>
      {!loading && trajectory && inputs.preferredFlightSeconds !== undefined && seconds !== inputs.preferredFlightSeconds && (
        <p role="status">Travel time adjusted to the nearest executable route found.</p>
      )}
    </section>
  )
}
