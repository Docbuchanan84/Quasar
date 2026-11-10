import { useEffect, useMemo, useState } from 'react'
import type { AutomaticCoursePreview, Snapshot } from './lab'

export type PlotInputs = {
  shipId: string
  destinationId: string
  arrivalKind: 'approach' | 'orbit'
  preferredFlightSeconds?: number
  reservePercent?: number
  lockFlightTime?: boolean
  lockReserve?: boolean
}

// Preview requests are reads. Input/state changes hide old results immediately and abort late responses.
export function useCoursePreview(
  snapshot: Snapshot | null,
  inputs: PlotInputs | null,
  online: boolean,
) {
  const [attempt, setAttempt] = useState(0)
  const [result, setResult] = useState<{
    key: object
    routes?: AutomaticCoursePreview
    error?: string
  } | null>(null)
  const instanceId = snapshot?.instanceId,
    revision = snapshot?.revision
  const ready = !!snapshot && !snapshot.running && online && !!inputs?.destinationId
  // A return to earlier inputs still needs a fresh preview token.
  const key = useMemo(
    () => ready ? { instanceId, revision, inputs, attempt } : null,
    [ready, instanceId, revision, inputs, attempt],
  )
  const destinationId = inputs?.destinationId,
    shipId = inputs?.shipId,
    arrivalKind = inputs?.arrivalKind
  const preferredFlightSeconds = inputs?.preferredFlightSeconds
  const reservePercent = inputs?.reservePercent
  const lockFlightTime = inputs?.lockFlightTime
  useEffect(() => {
    if (!key) return
    const controller = new AbortController()
    const timer = window.setTimeout(() => {
      void (async () => {
        try {
          const response = await fetch(
            `/api/lab/course-preview?${new URLSearchParams({
              destinationId: destinationId!,
              mode: 'auto',
              arrivalKind: arrivalKind!,
              ...(preferredFlightSeconds === undefined ? {} : { preferredFlightSeconds: String(preferredFlightSeconds) }),
              ...(reservePercent === undefined ? {} : { reservePercent: String(reservePercent) }),
              ...(lockFlightTime ? { lockFlightTime: 'true' } : {}),
            })}`,
            {
              cache: 'no-store',
              signal: AbortSignal.any([
                controller.signal,
                AbortSignal.timeout(10000),
              ]),
            },
          )
          const data = await response.json()
          if (!response.ok)
            throw new Error(data.error ?? 'Course planning failed.')
          const routes = data as AutomaticCoursePreview
          const stale = [routes.fastest, routes.lowestManeuverCost, routes.tuned].some(
            (preview) => preview && (
              preview.instanceId !== instanceId ||
              preview.revision !== revision ||
              preview.shipId !== shipId ||
              preview.destinationId !== destinationId
            ),
          )
          if (stale)
            throw new Error(
              'State changed during planning. Refresh state and recalculate.',
            )
          if (!controller.signal.aborted) setResult({ key, routes })
        } catch (error) {
          if (!controller.signal.aborted)
            setResult({
              key,
              error:
                error instanceof Error
                  ? error.message
                  : 'Course planning failed.',
            })
        }
      })()
    }, 250)
    return () => {
      clearTimeout(timer)
      controller.abort()
    }
  }, [key, destinationId, instanceId, revision, shipId, arrivalKind, preferredFlightSeconds, reservePercent, lockFlightTime])
  const current = result?.key === key ? result : null
  return {
    routes: current?.routes ?? null,
    error: current?.error,
    loading: ready && !current,
    recalculate: () => setAttempt((value) => value + 1),
  }
}
