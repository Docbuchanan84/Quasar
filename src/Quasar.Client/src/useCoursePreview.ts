import { useEffect, useState } from 'react'
import type { CoursePreview, Snapshot } from './lab'

export type PlotInputs = {
  shipId: string
  destinationId: string
  flightSeconds: string
}

// Preview requests are reads. Input/state changes hide old results immediately and abort late responses.
export function useCoursePreview(
  snapshot: Snapshot | null,
  inputs: PlotInputs | null,
  online: boolean,
) {
  const [attempt, setAttempt] = useState(0)
  const [result, setResult] = useState<{
    key: string
    preview?: CoursePreview
    error?: string
  } | null>(null)
  const seconds = Number(inputs?.flightSeconds)
  const valid =
    Number.isInteger(seconds) && seconds >= 1 && seconds <= 315576000
  const ready =
    !!snapshot &&
    !snapshot.running &&
    online &&
    !!inputs?.destinationId &&
    valid
  const key = ready
    ? JSON.stringify([
        snapshot!.instanceId,
        snapshot!.revision,
        inputs,
        attempt,
      ])
    : ''
  const instanceId = snapshot?.instanceId,
    revision = snapshot?.revision
  const destinationId = inputs?.destinationId,
    shipId = inputs?.shipId
  useEffect(() => {
    if (!key) return
    const controller = new AbortController()
    const timer = window.setTimeout(() => {
      void (async () => {
        try {
          const response = await fetch(
            `/api/lab/course-preview?${new URLSearchParams({
              destinationId: destinationId!,
              flightSeconds: String(seconds),
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
          const preview = data as CoursePreview
          if (
            preview.instanceId !== instanceId ||
            preview.revision !== revision ||
            preview.shipId !== shipId ||
            preview.destinationId !== destinationId
          )
            throw new Error(
              'State changed during planning. Refresh state and recalculate.',
            )
          if (!controller.signal.aborted) setResult({ key, preview })
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
  }, [key, destinationId, seconds, instanceId, revision, shipId])
  const current = result?.key === key ? result : null
  return {
    preview: current?.preview ?? null,
    error: current?.error,
    loading: ready && !current,
    valid,
    recalculate: () => setAttempt((value) => value + 1),
  }
}
