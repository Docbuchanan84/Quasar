import { useEffect, useRef, useState } from 'react'
import { formatTime, sailcraftId, type SailFlyby, type SailPreview, type SailStatus } from './lab'

function Encounters({ label, flybys }: { label: string; flybys: SailFlyby[] }) {
  return (
    <div>
      <strong>{label}</strong>
      {flybys.length === 0 && <p>No flyby in this preview.</p>}
      {flybys.map((flyby, index) => (
        <p key={`${flyby.bodyId}-${index}`}>
          {flyby.bodyName} · {flyby.closestAltitudeKm.toLocaleString(undefined, { maximumFractionDigits: 0 })} km closest altitude
          {flyby.solarSpeedChangeKmPerSecond !== null && (
            <> · {flyby.solarSpeedChangeKmPerSecond.toLocaleString(undefined, {
              maximumFractionDigits: 2, minimumFractionDigits: 2, signDisplay: 'always',
            })} km/s solar-relative speed change</>
          )}
        </p>
      ))}
    </div>
  )
}

export default function SailPanel({ ship, primary, disabled, preview, onPreview, onCommand }: {
  ship: SailStatus
  primary?: string
  disabled: boolean
  preview: SailPreview | null
  onPreview: (preview: SailPreview | null) => void
  onCommand: (operation: string, fields: Record<string, unknown>) => Promise<boolean>
}) {
  const [angle, setAngle] = useState('35')
  const [duration, setDuration] = useState('86400')
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)
  const request = useRef<AbortController | null>(null)
  useEffect(() => () => request.current?.abort(), [])

  function clearPreview() {
    onPreview(null)
    setError('')
  }

  async function calculate(withTrim: boolean) {
    clearPreview()
    const controller = new AbortController()
    request.current?.abort()
    request.current = controller
    setLoading(true)
    const params = withTrim ? new URLSearchParams({ angleDegrees: angle, durationSeconds: duration }) : ''
    try {
      const response = await fetch(`/api/lab/sail-preview?${params}`, {
        signal: AbortSignal.any([controller.signal, AbortSignal.timeout(5000)]),
        cache: 'no-store',
      })
      const result = await response.json()
      if (controller.signal.aborted) return
      if (response.ok) onPreview(result)
      else setError(result.error ?? 'Could not preview sail flight.')
    } catch {
      if (!controller.signal.aborted) setError('Could not preview sail flight.')
    } finally {
      if (!controller.signal.aborted) setLoading(false)
    }
  }

  return (
    <>
      <div className="ship-state">
        <strong>{ship.status === 'Sailing' ? 'Sailing' : 'Coasting · sail feathered'}</strong>
        <p>{ship.speedKmPerSecond.toFixed(3)} km/s · Gravity primary {primary ?? ship.primaryBodyId}</p>
        <p>{ship.solarDistanceAu.toFixed(3)} AU from the Sun</p>
      </div>
      <h3>Solar sail</h3>
      <dl>
        <div><dt>Ship mass</dt><dd>{ship.massKg} kg</dd></div>
        <div><dt>Sail area</dt><dd>{ship.areaSquareMeters.toLocaleString()} m²</dd></div>
        <div><dt>Sunlight acceleration, face-on</dt><dd>{(ship.faceOnAccelerationMPerSecond2 * 1000).toFixed(3)} mm/s²</dd></div>
      </dl>
      <p className="muted">Sunlight supplies thrust without propellant. Positive tilt adds orbital speed. Negative tilt sheds it. Thrust falls with distance from the Sun.</p>
      {ship.trimEndTicks ? (
        <>
          <p>Sail at {ship.angleDegrees}°. Automatic feather at {formatTime(ship.trimEndTicks)}.</p>
          <button disabled={disabled} onClick={() => void onCommand('feather-sail', { shipId: sailcraftId })}>
            Feather sail now
          </button>
        </>
      ) : (
        <form className="burn-form" onSubmit={(event) => { event.preventDefault(); void calculate(true) }}>
          <label>
            Sail angle (°)
            <input type="number" min="-80" max="80" step="any" required value={angle}
              disabled={disabled || loading} onChange={(event) => { setAngle(event.target.value); clearPreview() }} />
          </label>
          <label>
            Sail trim duration
            <select value={duration} disabled={disabled || loading}
              onChange={(event) => { setDuration(event.target.value); clearPreview() }}>
              <option value="3600">1 hour</option>
              <option value="21600">6 hours</option>
              <option value="86400">1 day</option>
              <option value="172800">2 days</option>
              <option value="432000">5 days</option>
              <option value="1296000">15 days</option>
              <option value="2592000">30 days</option>
            </select>
          </label>
          <button type="submit" disabled={disabled || loading}>Compare trim with coast</button>
          <button type="button" disabled={disabled || loading} onClick={() => void calculate(false)}>Preview coast</button>
          <p className="muted">Feather before a gravity handoff. Previews follow the coast for seven days after the trim.</p>
        </form>
      )}
      {loading && <p role="status">Predicting sail flight…</p>}
      {error && <p role="alert">{error}</p>}
      {preview && (
        <div className="ship-state">
          {preview.coastWarning
            ? <p role="status">{preview.coastWarning}</p>
            : <Encounters label="Coast (blue)" flybys={preview.coastFlybys} />}
          {preview.trim && <Encounters label="Trim then coast (pink)" flybys={preview.trimFlybys} />}
          {preview.previewId && (
            <button className="primary" disabled={disabled} onClick={() => {
              void onCommand('start-sail-trim', { shipId: sailcraftId, previewId: preview.previewId })
              onPreview(null)
            }}>Confirm sail trim</button>
          )}
        </div>
      )}
    </>
  )
}
