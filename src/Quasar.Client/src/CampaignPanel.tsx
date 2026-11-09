import { useEffect, useRef, useState, type FormEvent } from 'react'
import {
  CampaignError,
  campaignRequest,
  checkpointName,
  formatSaveDate,
  type CampaignAction,
  type CampaignEntry,
  type CampaignStatus,
  type Checkpoint,
  type CheckpointList,
} from './campaigns'
import { formatTime } from './lab'

type Replacement = Extract<
  CampaignAction,
  { route: 'new' | 'load' | 'continue' }
>
type Confirmation = {
  action: Replacement
  target: string
  instanceId: string
}

type Result<T> = { data: T; error?: never } | { data?: never; error: string }

function useCampaignList<T>(route: string) {
  const [result, setResult] = useState<Result<T> | null>(null)
  useEffect(() => {
    const request = new AbortController()
    void campaignRequest<T>(
      route,
      AbortSignal.any([request.signal, AbortSignal.timeout(5000)]),
    )
      .then((data) => {
        if (!request.signal.aborted) setResult({ data })
      })
      .catch((error: unknown) => {
        if (!request.signal.aborted)
          setResult({
            error:
              error instanceof CampaignError
                ? error.message
                : 'Could not read campaign storage. Refresh the save list to try again.',
          })
      })
    return () => request.abort()
  }, [route])
  return result
}

function SaveDetails({ checkpoint }: { checkpoint: Checkpoint }) {
  return (
    <span className="save-details">
      <time dateTime={checkpoint.createdUtc}>
        {formatSaveDate(checkpoint.createdUtc)}
      </time>
      {' · Simulated '}
      {formatTime(checkpoint.simulationTicks)}
    </span>
  )
}

function CheckpointChoices({
  entry,
  disabled,
  onReplace,
}: {
  entry: CampaignEntry
  disabled: boolean
  onReplace: (action: Replacement, target: string) => void
}) {
  const result = useCampaignList<CheckpointList>(`/${entry.id}/checkpoints`)
  const name = entry.campaign?.displayName ?? 'Unavailable campaign'
  const list = result?.data
  return (
    <div className="checkpoint-choices">
      <button
        disabled={disabled}
        className="primary"
        onClick={() =>
          onReplace({ route: 'continue', campaignId: entry.id }, name)
        }
      >
        Continue
      </button>
      <p className="campaign-meta">
        Continue opens the latest readable save and can recover an earlier
        checkpoint or backup.
      </p>
      {!result && <p role="status">Reading checkpoints…</p>}
      {result?.error && (
        <p className="campaign-warning" role="alert">
          {result.error}
        </p>
      )}
      {!!list?.rejectedMetadata && (
        <p className="campaign-warning" role="alert">
          Some checkpoint records could not be read. Choose an earlier save or
          use Continue to attempt recovery.
        </p>
      )}
      {list?.checkpoints.length === 0 && !list.rejectedMetadata && (
        <p>No checkpoints for this campaign.</p>
      )}
      {list?.checkpoints.length ? (
        <ul className="save-list" aria-label="Campaign checkpoints">
          {list.checkpoints.map((checkpoint) => (
            <li key={checkpoint.id}>
              <div>
                <strong>{checkpointName(checkpoint)}</strong>
                <span className="campaign-meta">
                  {checkpoint.kind === 'named' ? 'Manual' : 'Recovery'} · #
                  {checkpoint.sequence}
                </span>
                <SaveDetails checkpoint={checkpoint} />
              </div>
              <button
                disabled={disabled}
                aria-label={`Load ${checkpointName(checkpoint)} #${checkpoint.sequence}`}
                onClick={() =>
                  onReplace(
                    {
                      route: 'load',
                      campaignId: entry.id,
                      checkpointId: checkpoint.id,
                    },
                    `${name} / ${checkpointName(checkpoint)} (#${checkpoint.sequence})`,
                  )
                }
              >
                Load
              </button>
            </li>
          ))}
        </ul>
      ) : null}
    </div>
  )
}

function CampaignLibrary({
  activeId,
  disabled,
  onReplace,
}: {
  activeId?: string
  disabled: boolean
  onReplace: (action: Replacement, target: string) => void
}) {
  const result = useCampaignList<CampaignEntry[]>('')
  const [selectedId, setSelectedId] = useState('')
  const entries = result?.data
  const selected =
    entries?.find((entry) => entry.id === selectedId) ??
    entries?.find((entry) => entry.id === activeId) ??
    entries?.[0]
  if (!result) return <p role="status">Reading saved campaigns…</p>
  if (result.error)
    return (
      <p className="campaign-warning" role="alert">
        {result.error}
      </p>
    )
  if (!entries?.length || !selected) return <p>No saved campaigns found.</p>
  return (
    <>
      <label className="campaign-field">
        Saved campaign
        <select
          value={selected.id}
          onChange={(event) => setSelectedId(event.target.value)}
        >
          {entries.map(({ id, campaign }) => (
            <option key={id} value={id}>
              {campaign?.displayName ?? 'Unavailable campaign'} ·{' '}
              {id.slice(0, 8)}
              {id === activeId ? ' (active)' : ''}
            </option>
          ))}
        </select>
      </label>
      {selected.campaign && (
        <p className="campaign-meta">
          Last checkpoint written{' '}
          <time dateTime={selected.campaign.updatedUtc}>
            {formatSaveDate(selected.campaign.updatedUtc)}
          </time>
        </p>
      )}
      {selected.error && (
        <p className="campaign-warning" role="alert">
          {selected.error}
        </p>
      )}
      <CheckpointChoices
        key={selected.id}
        entry={selected}
        disabled={disabled}
        onReplace={onReplace}
      />
    </>
  )
}

export default function CampaignPanel({
  status,
  online,
  pending,
  message,
  onAction,
}: {
  status: CampaignStatus | null
  online: boolean
  pending: boolean
  message: { text: string; error?: boolean } | null
  onAction: (
    action: CampaignAction,
    expectedInstance: string,
  ) => Promise<boolean>
}) {
  const [view, setView] = useState<'new' | 'save' | 'load' | null>(null)
  const [name, setName] = useState('')
  const [listVersion, setListVersion] = useState(0)
  const [confirmation, setConfirmation] = useState<Confirmation | null>(null)
  const cancelRef = useRef<HTMLButtonElement>(null)
  const active = status?.campaign
  const openView = view ?? (status && !active ? 'load' : null)
  const disabled = !online || pending || !status

  useEffect(() => {
    if (confirmation) cancelRef.current?.focus()
  }, [confirmation])

  function open(next: typeof view) {
    setName('')
    setConfirmation(null)
    setView(next)
  }

  async function submit(action: CampaignAction, instanceId: string) {
    setConfirmation(null)
    if (await onAction(action, instanceId)) open(null)
  }

  function replace(action: Replacement, target: string) {
    if (!status || disabled) return
    if (active)
      setConfirmation({ action, target, instanceId: status.instanceId })
    else void submit(action, status.instanceId)
  }

  function submitName(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const label = name.trim()
    if (!label || !status || disabled) return
    if (openView === 'new') replace({ route: 'new', displayName: label }, label)
    else void submit({ route: 'save-named', label }, status.instanceId)
  }

  return (
    <section className="campaign-panel" aria-label="Campaign management">
      <div className="campaign-bar">
        <div>
          <h1>{active?.displayName ?? 'Campaigns'}</h1>
          {active && (
            <p className="campaign-meta">
              {status.dirty ? 'Unsaved progress' : 'No unsaved progress'}
            </p>
          )}
        </div>
        <div className="campaign-actions">
          <button
            disabled={disabled}
            aria-expanded={openView === 'new'}
            onClick={() => open('new')}
          >
            New Game
          </button>
          {active && (
            <>
              <button
                disabled={disabled}
                onClick={() =>
                  void submit({ route: 'save' }, status.instanceId)
                }
              >
                Save Game
              </button>
              <button
                disabled={disabled}
                aria-expanded={openView === 'save'}
                onClick={() => open('save')}
              >
                Named Save
              </button>
            </>
          )}
          <button
            disabled={disabled}
            aria-expanded={openView === 'load'}
            onClick={() => open('load')}
          >
            Load Game
          </button>
        </div>
      </div>

      {status && (
        <p className="campaign-meta">
          {status.autosave.enabled
            ? `Autosave on · every ${status.autosave.intervalSeconds} s when changed`
            : 'Autosave off'}
        </p>
      )}
      {status?.lastCheckpoint && (
        <p className="campaign-meta">
          {status.lastCheckpoint.sequence === active?.headSequence
            ? 'Last saved state'
            : 'Loaded checkpoint'}
          {' · '}
          {checkpointName(status.lastCheckpoint)}
          {' · '}
          <SaveDetails checkpoint={status.lastCheckpoint} />
        </p>
      )}
      {status?.autosaveError && (
        <p className="campaign-warning" role="alert">
          {status.autosaveError}
        </p>
      )}
      {status?.recoveryError && (
        <p className="campaign-warning" role="alert">
          {status.recoveryError} Use Continue to try recovery, or choose an
          earlier checkpoint below.
        </p>
      )}
      {!!status?.rejectedCheckpoints && (
        <p className="campaign-warning" role="status">
          Recovered an earlier checkpoint. Unreadable newer saves skipped:{' '}
          {status.rejectedCheckpoints}.
        </p>
      )}
      {status?.recoveredFromCampaignId && (
        <p className="campaign-warning" role="status">
          Recovered from a verified backup into a separate campaign.
        </p>
      )}
      {message && (
        <p
          className={message.error ? 'campaign-warning' : 'campaign-result'}
          role={message.error ? 'alert' : 'status'}
        >
          {message.text}
        </p>
      )}
      {message?.error && openView === 'load' && (
        <p className="campaign-meta">
          Refresh saves, choose an earlier checkpoint, or use Continue to
          attempt recovery.
        </p>
      )}
      {pending && openView && <p role="status">Waiting for the host…</p>}

      {confirmation ? (
        <section
          className="campaign-editor campaign-confirm"
          aria-labelledby="campaign-confirm-title"
        >
          <h2 id="campaign-confirm-title">Replace active campaign?</h2>
          <p>
            {confirmation.action.route === 'new' ? 'Start' : 'Open'} “
            {confirmation.target}” and replace “{active?.displayName}”. Any
            unsaved progress in the active game will be discarded. Existing
            saves are kept.
          </p>
          {status?.instanceId !== confirmation.instanceId && (
            <p role="alert">
              The active session changed. Cancel and review it before trying
              again.
            </p>
          )}
          <div className="campaign-actions">
            <button ref={cancelRef} onClick={() => setConfirmation(null)}>
              Keep Playing
            </button>
            <button
              className="primary"
              disabled={
                disabled || status?.instanceId !== confirmation.instanceId
              }
              onClick={() =>
                void submit(confirmation.action, confirmation.instanceId)
              }
            >
              {confirmation.action.route === 'new'
                ? 'Start New Game'
                : 'Replace and Load'}
            </button>
          </div>
        </section>
      ) : null}
      {openView && (
        <div className="campaign-editor" hidden={!!confirmation}>
          <div className="campaign-bar">
            <h2>
              {openView === 'load'
                ? 'Saved campaigns'
                : openView === 'new'
                  ? 'New Game'
                  : 'Create Named Save'}
            </h2>
            <div className="campaign-actions">
              {openView === 'load' && (
                <button
                  disabled={disabled}
                  onClick={() => setListVersion((value) => value + 1)}
                >
                  Refresh saves
                </button>
              )}
              {(active || openView !== 'load') && (
                <button onClick={() => open(null)}>Close</button>
              )}
            </div>
          </div>
          {openView === 'load' ? (
            <CampaignLibrary
              key={listVersion}
              activeId={active?.id}
              disabled={disabled}
              onReplace={replace}
            />
          ) : (
            <form className="campaign-form" onSubmit={submitName}>
              <label className="campaign-field">
                {openView === 'new' ? 'Campaign name' : 'Save name'}
                <input
                  type="text"
                  required
                  maxLength={120}
                  value={name}
                  onChange={(event) => setName(event.target.value)}
                />
              </label>
              <button className="primary" disabled={disabled || !name.trim()}>
                {openView === 'new' ? 'Create Campaign' : 'Save Checkpoint'}
              </button>
            </form>
          )}
        </div>
      )}
    </section>
  )
}
