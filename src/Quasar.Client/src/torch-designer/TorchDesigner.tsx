import { useEffect, useId, useRef, useState } from 'react'

const stock = { jetPowerGw: 150, exhaustSpeedKmPerSecond: 1000, nozzleDiameterM: 1.5 }
type Design = typeof stock
type Draft = Record<keyof Design, string>
const stockDraft: Draft = { jetPowerGw: '150', exhaustSpeedKmPerSecond: '1000', nozzleDiameterM: '1.5' }
const storageKey = 'quasar.torch-designer.draft'
const controls = [
  { key: 'jetPowerGw', label: 'Jet power', unit: 'GW', min: 50, max: 300, step: 1,
    help: 'Power delivered to the exhaust. At the same exhaust speed, more power means more thrust and faster reaction-mass use.' },
  { key: 'exhaustSpeedKmPerSecond', label: 'Exhaust speed', unit: 'km/s', min: 600, max: 1600, step: 10,
    help: 'How fast the drive throws reaction mass backward. At the same power, faster exhaust saves reaction mass and gives a full tank more delta-v, with less thrust.' },
  { key: 'nozzleDiameterM', label: 'Magnetic nozzle diameter', unit: 'm', min: 1, max: 2.5, step: 0.01,
    help: 'The width of the coil assembly that guides the exhaust. A wider assembly can handle more power and adds mass to the ship.' },
] as const

type Preview = {
  maximumJetPowerGw: number
  driveDryMassKg: number
  driveLengthM: number
  thrustN: number
  performance: {
    ratedMassFlowKgPerSecond: number
    initialAccelerationMPerSecond2: number
    theoreticalDeltaVMPerSecond: number
  }
}
type Burn = { reactionMassUsedKg: number; velocityChangeKmPerSecond: number }
type PreviewComparison = { stock: Preview; draft: Preview }
type BurnComparison = { durationSeconds: number; stock: Burn; draft: Burn }
type FitStatus = { instanceId: string; revision: number; reason: string | null }
type Readback<T> = { key: string; data?: T; error?: string }
const number = (value: number, digits = 2) => value.toLocaleString(undefined, { maximumFractionDigits: digits })
const errorText = (error: unknown) => error instanceof Error ? error.message : 'Try again in a moment.'

function loadDraft(): Draft {
  try {
    const saved = JSON.parse(localStorage.getItem(storageKey) ?? 'null') as Partial<Draft> | null
    if (saved && controls.every(({ key }) => typeof saved[key] === 'string')) return saved as Draft
  } catch { /* A fresh draft works when storage is unavailable. */ }
  return stockDraft
}

async function request<T>(url: string, options?: RequestInit): Promise<T> {
  const timeout = AbortSignal.timeout(5000)
  const signal = options?.signal ? AbortSignal.any([options.signal, timeout]) : timeout
  const response = await fetch(url, { ...options, signal, cache: 'no-store' })
  if (!response.ok) {
    const body = await response.json().catch(() => null) as { error?: string } | null
    throw new Error(body?.error ?? 'Quasar could not complete the request. Try again.')
  }
  return response.json() as Promise<T>
}

function Nozzle({ diameter }: { diameter: number }) {
  const id = useId()
  const halfHeight = diameter * 28
  return (
    <figure className="torch-nozzle">
      <svg viewBox="0 0 400 194" role="img" aria-labelledby={id}>
        <title id={id}>Side cross-section of the magnetic nozzle, {number(diameter)} meters in diameter</title>
        <defs>
          <linearGradient id={id + '-plasma'}>
            <stop stopColor="#60e9fb" stopOpacity=".55" />
            <stop offset="1" stopColor="#60e9fb" stopOpacity="0" />
          </linearGradient>
        </defs>
        <line x1="26" x2="365" y1="90" y2="90" stroke="#45616f" strokeDasharray="4 6" />
        <path d={`M72 84 C155 84 183 81 290 ${90 - halfHeight * 0.7} L365 67 L365 113 L290 ${90 + halfHeight * 0.7} C183 99 155 96 72 96 Z`}
          fill={`url(#${id}-plasma)`} />
        <path d={`M70 71 H120 Q175 71 290 ${90 - halfHeight} M70 109 H120 Q175 109 290 ${90 + halfHeight}`}
          fill="none" stroke="#97e5f3" strokeWidth="4" />
        <path d={`M300 ${90 - halfHeight} h13 v${2 * halfHeight} h-13`} fill="none" stroke="#c3a5fb" strokeWidth="2" />
        <text x="327" y="95" fill="#e3d2ff" fontSize="15">{number(diameter)} m</text>
        <text x="77" y="184" fill="#b7ccd6" fontSize="15">Reaction mass</text>
        <text x="259" y="184" fill="#b7ccd6" fontSize="15">Exhaust →</text>
      </svg>
      <figcaption><a href="#nozzleDiameterM">Magnetic nozzle diameter</a> controls the width shown here.</figcaption>
    </figure>
  )
}

function compare(value: number, reference: number, unit: string, digits = 2) {
  const difference = value - reference
  if (Math.abs(difference) < 0.5 * 10 ** -digits) return 'Same as stock'
  return `${number(Math.abs(difference), digits)} ${unit} ${difference > 0 ? 'more' : 'less'} than stock`
}

export default function TorchDesigner() {
  const [draft, setDraft] = useState(loadDraft)
  const [preview, setPreview] = useState<Readback<PreviewComparison>>()
  const [burn, setBurn] = useState<Readback<BurnComparison>>()
  const [busy, setBusy] = useState<'burn' | 'fit' | null>(null)
  const [fitStatus, setFitStatus] = useState<FitStatus | null>()
  const [confirmation, setConfirmation] = useState<FitStatus>()
  const [message, setMessage] = useState('')
  const [fitError, setFitError] = useState(false)
  const dialog = useRef<HTMLDialogElement>(null)
  const design: Design = {
    jetPowerGw: Number(draft.jetPowerGw),
    exhaustSpeedKmPerSecond: Number(draft.exhaustSpeedKmPerSecond),
    nozzleDiameterM: Number(draft.nozzleDiameterM),
  }
  const complete = controls.every(({ key }) => draft[key].trim() && Number.isFinite(design[key]))
  const key = complete ? new URLSearchParams(controls.map(({ key }) => [key, String(design[key])])).toString() : ''
  const ready = key && preview?.key === key ? preview : undefined
  const result = key && burn?.key === key ? burn : undefined

  useEffect(() => {
    try { localStorage.setItem(storageKey, JSON.stringify(draft)) } catch { /* Storage is optional. */ }
  }, [draft])

  useEffect(() => {
    if (!key) return
    const controller = new AbortController()
    const timer = setTimeout(() => {
      void request<PreviewComparison>(`/api/torch-designer/preview?${key}`, { signal: controller.signal })
        .then((data) => { if (!controller.signal.aborted) setPreview({ key, data }) })
        .catch((error: unknown) => { if (!controller.signal.aborted) setPreview({ key, error: errorText(error) }) })
    }, 150)
    return () => { clearTimeout(timer); controller.abort() }
  }, [key])

  useEffect(() => {
    const controller = new AbortController()
    let pending = false
    function refreshFitStatus() {
      if (pending || document.visibilityState === 'hidden') return
      pending = true
      void request<FitStatus>('/api/torch-designer/fit-status', { signal: controller.signal })
        .then((status) => { if (!controller.signal.aborted) setFitStatus(status) })
        .catch(() => { if (!controller.signal.aborted) setFitStatus(null) })
        .finally(() => { pending = false })
    }
    refreshFitStatus()
    const interval = setInterval(refreshFitStatus, 3000)
    return () => { controller.abort(); clearInterval(interval) }
  }, [])

  function changeDraft(next: Draft) {
    setDraft(next)
    setBurn(undefined)
    setMessage('')
  }

  async function runBurn() {
    setBusy('burn')
    setBurn(undefined)
    try {
      const data = await request<BurnComparison>(`/api/torch-designer/test-burn?${key}`)
      setBurn({ key, data })
    } catch (error) { setBurn({ key, error: errorText(error) }) }
    finally { setBusy(null) }
  }

  async function confirmFit() {
    setBusy('fit')
    setMessage('')
    try {
      const status = await request<FitStatus>('/api/torch-designer/fit-status')
      setFitStatus(status)
      if (status.reason) { setFitError(true); setMessage(status.reason); return }
      setConfirmation(status)
      dialog.current?.showModal()
    } catch (error) { setFitError(true); setMessage(errorText(error)) }
    finally { setBusy(null) }
  }

  async function fit() {
    const installed = ready?.data?.draft
    if (!confirmation || !installed) return
    dialog.current?.close()
    setBusy('fit')
    try {
      await request('/api/lab/commands', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          instanceId: confirmation.instanceId, revision: confirmation.revision,
          clientId: crypto.randomUUID(), operation: 'fit-designed-torch', torchDesign: design,
        }),
      })
      setFitError(false)
      setMessage(`Fitted the ${number(installed.thrustN / 1000)} kN fusion torch. Return to the System Lab to plan a course or save your campaign.`)
    } catch (error) {
      setFitError(true)
      setMessage(error instanceof TypeError || (error instanceof DOMException && error.name === 'TimeoutError')
        ? 'Fit outcome uncertain. Check Pathfinder’s propulsion and event history in the System Lab before trying again.'
        : errorText(error))
    } finally { setBusy(null) }
  }

  const data = ready?.data?.draft
  const reference = ready?.data?.stock
  const displayedDiameter = Number.isFinite(design.nozzleDiameterM) && design.nozzleDiameterM >= 1 && design.nozzleDiameterM <= 2.5
    ? design.nozzleDiameterM : stock.nozzleDiameterM

  return (
    <main className="torch-page">
      <header className="torch-heading">
        <a href="/">← Back to System Lab</a>
        <h1>Torch Designer</h1>
        <p>Tune a fusion drive for Pathfinder. Start with the stock torch, change one setting, then compare the result.</p>
      </header>
      <div className="torch-grid">
        <section className="torch-panel" aria-labelledby="design-title">
          <div className="torch-row">
            <h2 id="design-title">Drive settings</h2>
            <button disabled={!!busy} onClick={() => changeDraft(stockDraft)}>Reset to stock</button>
          </div>
          {controls.map(({ key: field, label, unit, min, max, step, help }) => (
            <div className="torch-control" key={field}>
              <label htmlFor={field}>{label}</label>
              <div className="torch-row">
                <input type="range" aria-label={label + ' slider'} aria-describedby={field + '-help'}
                  min={min} max={max} step={step} disabled={!!busy}
                  value={Math.max(min, Math.min(max, design[field] || stock[field]))}
                  onChange={(event) => changeDraft({ ...draft, [field]: event.target.value })} />
                <input id={field} type="number" inputMode="decimal" aria-describedby={field + '-help'}
                  min={min} max={max} step="any" disabled={!!busy} value={draft[field]}
                  onChange={(event) => changeDraft({ ...draft, [field]: event.target.value })} />
                <span>{unit}</span>
              </div>
              <p id={field + '-help'}>{help} Stock: {number(stock[field])} {unit}.</p>
            </div>
          ))}
          <Nozzle diameter={displayedDiameter} />
          <details>
            <summary>Model assumptions</summary>
            <p>Quasar assumes the ship supplies all requested power. Coil mass and power handling use simplified game rules. Flight previews use Pathfinder’s stock hull and a full 20,000 kg tank.</p>
          </details>
        </section>
        <section className="torch-panel" aria-labelledby="performance-title">
          <h2 id="performance-title">Drive performance</h2>
          <div role="status">
            {!complete ? <p>Enter a value for each setting.</p>
              : ready?.error ? <p className="torch-error">{ready.error}</p>
              : !data ? <p>Calculating drive performance…</p> : null}
          </div>
          {data && reference && (
            <>
              <p>Nozzle power limit: <strong>{number(Math.floor(data.maximumJetPowerGw * 100) / 100)} GW</strong></p>
              <dl className="torch-metrics">
                {([
                  ['Thrust', data.thrustN / 1000, reference.thrustN / 1000, 'kN', 2],
                  ['Reaction mass per second', data.performance.ratedMassFlowKgPerSecond, reference.performance.ratedMassFlowKgPerSecond, 'kg/s', 3],
                  ['Drive dry mass', data.driveDryMassKg, reference.driveDryMassKg, 'kg', 2],
                  ['Initial acceleration', data.performance.initialAccelerationMPerSecond2, reference.performance.initialAccelerationMPerSecond2, 'm/s²', 2],
                  ['Full-tank delta-v', data.performance.theoreticalDeltaVMPerSecond / 1000, reference.performance.theoreticalDeltaVMPerSecond / 1000, 'km/s', 2],
                ] as const).map(([label, value, reference, unit, digits]) => (
                  <div key={label}>
                    <dt>{label}</dt><dd>{number(value, digits)} {unit}<small>{compare(value, reference, unit, digits)}</small></dd>
                  </div>
                ))}
              </dl>
              <p className="torch-help">Delta-v estimates how much the ship can change its velocity using the reaction mass aboard.</p>
            </>
          )}
        </section>
      </div>
      <div className="torch-grid">
        <section className="torch-panel">
          <h2>Test burn</h2>
          <p>Fire both drives for 30 seconds on identical test ships near Earth. This test uses a fresh tank each time.</p>
          <button disabled={!data || !!busy} onClick={() => void runBurn()}>{busy === 'burn' ? 'Running test…' : 'Run test burn'}</button>
          <div aria-live="polite">
            {result?.error && <p className="torch-error">{result.error}</p>}
            {result?.data && (
              <>
                <table className="torch-results">
                  <thead><tr><th scope="col">30-second result</th><th scope="col">Stock</th><th scope="col">Draft</th></tr></thead>
                  <tbody>
                    <tr><th scope="row">Reaction mass used</th><td>{number(result.data.stock.reactionMassUsedKg, 3)} kg</td><td>{number(result.data.draft.reactionMassUsedKg, 3)} kg</td></tr>
                    <tr><th scope="row">Velocity change</th><td>{number(result.data.stock.velocityChangeKmPerSecond * 1000)} m/s</td><td>{number(result.data.draft.velocityChangeKmPerSecond * 1000)} m/s</td></tr>
                  </tbody>
                </table>
                <p>The draft’s reaction-mass use is {compare(result.data.draft.reactionMassUsedKg, result.data.stock.reactionMassUsedKg, 'kg', 3).toLowerCase()}. Velocity change measures what the burn added compared with coasting.</p>
              </>
            )}
          </div>
        </section>
        <section className="torch-panel">
          <h2>Campaign refit</h2>
          <p>Fit this drive to Pathfinder’s existing tank. Its remaining reaction mass stays aboard.</p>
          <button className="primary" disabled={!data || !!busy || !fitStatus || !!fitStatus.reason} onClick={() => void confirmFit()}>
            {busy === 'fit' ? 'Checking fit…' : 'Fit to Pathfinder'}
          </button>
          <p className="torch-help">{fitStatus === null ? 'Campaign unavailable. Quasar will retry shortly.'
            : fitStatus ? fitStatus.reason : 'Checking campaign availability…'}</p>
          {message && <p role="status" className={fitError ? 'torch-error' : 'torch-success'}>{message} {!fitError && <a href="/">Back to System Lab</a>}</p>}
        </section>
      </div>
      <dialog className="torch-dialog" ref={dialog} aria-labelledby="torch-confirm-title">
        <h2 id="torch-confirm-title">Fit this drive to Pathfinder?</h2>
        {data && <p>{number(data.thrustN / 1000)} kN thrust · {number(data.driveDryMassKg)} kg dry mass · {number(design.exhaustSpeedKmPerSecond)} km/s exhaust speed</p>}
        <div className="torch-row">
          <button autoFocus onClick={() => dialog.current?.close()}>Cancel</button>
          <button className="primary" onClick={() => void fit()}>Confirm refit</button>
        </div>
      </dialog>
    </main>
  )
}
