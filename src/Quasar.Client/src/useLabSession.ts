import { useCallback, useEffect, useRef, useState } from 'react'
import { type Snapshot } from './lab'
import {
  CampaignError,
  campaignRequest,
  checkpointName,
  type CampaignAction,
  type CampaignSession,
  type CampaignStatus,
  type Checkpoint,
} from './campaigns'

type Message = { text: string; error?: boolean }

export function useLabSession(
  onInstanceChange: (next: Snapshot | null) => void,
) {
  const [snapshot, setSnapshot] = useState<Snapshot | null>(null)
  const [status, setStatus] = useState<CampaignStatus | null>(null)
  const [online, setOnline] = useState(false)
  const [pending, setPending] = useState(false)
  const [notice, setNotice] = useState('')
  const [campaignMessage, setCampaignMessage] = useState<Message | null>(null)
  const latest = useRef<Snapshot | null>(null)
  const latestStatus = useRef<CampaignStatus | null>(null)
  const clientId = useRef<string | null>(null)
  const busy = useRef(false)
  const polling = useRef(false)
  // A poll started before a mutation must never reinstall the old world.
  const generation = useRef(0)

  const accept = useCallback(
    (next: Snapshot | null) => {
      const previous = latest.current
      if (previous?.instanceId !== next?.instanceId) {
        clientId.current = next ? crypto.randomUUID() : null
        onInstanceChange(next)
      }
      if (
        !next ||
        previous?.instanceId !== next.instanceId ||
        next.revision > previous.revision
      ) {
        // The maps share the same sampled path. Keep its identity stable across ticks.
        if (
          next &&
          previous?.instanceId === next.instanceId &&
          JSON.stringify(previous.world.trajectory) ===
            JSON.stringify(next.world.trajectory)
        )
          next.world.trajectory = previous.world.trajectory
        latest.current = next
        setSnapshot(next)
      }
    },
    [onInstanceChange],
  )

  const refresh = useCallback(
    async (duringMutation = false) => {
      if (!duringMutation && (polling.current || busy.current)) return
      const requestGeneration = generation.current
      if (!duringMutation) polling.current = true
      try {
        const nextStatus = await campaignRequest<CampaignStatus>('/status')
        let next: Snapshot | null = null
        if (nextStatus.campaign) {
          const response = await fetch('/api/lab', {
            cache: 'no-store',
            signal: AbortSignal.timeout(5000),
          })
          if (!response.ok) throw new Error('Host state unavailable')
          next = (await response.json()) as Snapshot
          if (next.instanceId !== nextStatus.instanceId)
            throw new Error('Campaign changed while refreshing')
        }
        if (requestGeneration !== generation.current) return
        accept(next)
        // A paused, unchanged campaign needs no React update or map redraw.
        if (
          JSON.stringify(latestStatus.current) !== JSON.stringify(nextStatus)
        ) {
          latestStatus.current = nextStatus
          setStatus(nextStatus)
        }
        setOnline(true)
      } catch {
        if (requestGeneration === generation.current) setOnline(false)
      } finally {
        if (!duringMutation) polling.current = false
      }
    },
    [accept],
  )

  useEffect(() => {
    const resume = () => {
      if (!document.hidden) void refresh()
    }
    const invalidate = () => {
      ++generation.current
    }
    const offline = () => {
      invalidate()
      setOnline(false)
    }
    const timer = window.setInterval(resume, 500)
    const initial = window.setTimeout(resume, 0)
    document.addEventListener('visibilitychange', resume)
    window.addEventListener('online', resume)
    window.addEventListener('offline', offline)
    return () => {
      invalidate()
      clearTimeout(initial)
      clearInterval(timer)
      document.removeEventListener('visibilitychange', resume)
      window.removeEventListener('online', resume)
      window.removeEventListener('offline', offline)
    }
  }, [refresh])

  function beginMutation() {
    busy.current = true
    ++generation.current
    setPending(true)
  }

  async function finishMutation() {
    await refresh(true)
    busy.current = false
    setPending(false)
  }

  async function command(
    operation: string,
    extra: Record<string, unknown> = {},
  ) {
    const state = latest.current
    if (!online || !state || busy.current) return
    beginMutation()
    setNotice('')
    try {
      const response = await fetch('/api/lab/commands', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          instanceId: state.instanceId,
          revision: state.revision,
          clientId: clientId.current,
          operation,
          ...extra,
        }),
        signal: AbortSignal.timeout(5000),
      })
      const result = await response.json()
      if (!response.ok) setNotice(result.error ?? 'Command rejected.')
      else accept(result)
    } catch {
      setNotice(
        'Command outcome uncertain. Check the refreshed state before trying again.',
      )
    } finally {
      await finishMutation()
    }
  }

  async function campaignAction(
    { route, ...fields }: CampaignAction,
    expectedInstance: string,
  ) {
    const current = latestStatus.current
    if (!online || !current || busy.current) return false
    if (current.instanceId !== expectedInstance) {
      setCampaignMessage({
        error: true,
        text: 'The active session changed. Review the current campaign before trying again.',
      })
      return false
    }
    beginMutation()
    setCampaignMessage(null)
    let submitted = false
    try {
      // Continuous processing can advance between polls. Check the version
      // immediately before sending, without renewing a confirmation for another world.
      const version = await campaignRequest<CampaignStatus>('/status')
      if (version.instanceId !== expectedInstance)
        throw new CampaignError(
          'The active session changed. Review the current campaign before trying again.',
        )
      submitted = true
      const result = await campaignRequest<Checkpoint | CampaignSession>(
        `/${route}`,
        AbortSignal.timeout(15000),
        { instanceId: expectedInstance, revision: version.revision, ...fields },
      )
      if ('kind' in result) {
        setCampaignMessage({ text: `Saved “${checkpointName(result)}”.` })
      } else {
        // Hold all lab commands until a fresh authoritative snapshot arrives.
        accept(null)
        const replacement = { ...result, autosave: version.autosave }
        latestStatus.current = replacement
        setStatus(replacement)
        setNotice('')
        setCampaignMessage({
          text:
            route === 'new'
              ? 'Campaign created. Time is paused.'
              : 'Campaign loaded. Time is paused.',
        })
      }
      return true
    } catch (error) {
      setCampaignMessage({
        error: true,
        text:
          error instanceof CampaignError
            ? error.message
            : submitted
              ? 'Campaign operation outcome uncertain. Check the refreshed campaign and saves before trying again.'
              : 'Could not check the current session. Refresh state before trying again.',
      })
      return false
    } finally {
      await finishMutation()
    }
  }

  return {
    snapshot,
    status,
    online,
    pending,
    notice,
    campaignMessage,
    refresh,
    command,
    campaignAction,
  }
}
