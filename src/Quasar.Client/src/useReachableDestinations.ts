import { useEffect, useRef, useState } from 'react'
import { isCourseDestination, type Snapshot } from './lab'
import { courseSearchParameters, type PlotInputs } from './useCoursePreview'

const empty = new Set<string>()

export function useReachableDestinations(snapshot: Snapshot | null, inputs: PlotInputs | null, online: boolean) {
  const [attempt, setAttempt] = useState(0)
  const [result, setResult] = useState<{ key: string; ids: Set<string>; done: boolean; incomplete: boolean } | null>(null)
  const completedKey = useRef<string | null>(null)
  const instanceId = snapshot?.instanceId
  const revision = snapshot?.revision
  const active = !!snapshot && !snapshot.running && online && !!inputs
  const query = inputs ? courseSearchParameters(inputs).toString() : ''
  const origin = snapshot?.world.objects.find((object) => object.id === inputs?.shipId)
  const distance = (object: NonNullable<typeof origin>) => origin
    ? Math.hypot(object.position.x - origin.position.x, object.position.y - origin.position.y, object.position.z - origin.position.z)
    : 0
  const targets = active ? snapshot.world.objects.filter(isCourseDestination).sort((a, b) => distance(a) - distance(b)) : []
  // Keep nearby moons and stations from waiting behind the full planetary search.
  const queues = JSON.stringify([
    targets.filter((target) => target.kind === 'planet').map((target) => target.id),
    targets.filter((target) => target.kind !== 'planet').map((target) => target.id),
  ])
  const key = active ? JSON.stringify([instanceId, revision, query, queues, attempt]) : null

  useEffect(() => {
    if (!key || completedKey.current === key || instanceId === undefined || revision === undefined) return
    completedKey.current = null
    const controller = new AbortController()
    const ids = new Set<string>()
    let incomplete = false
    const parameters = new URLSearchParams(query)
    parameters.set('instanceId', instanceId)
    parameters.set('revision', String(revision))

    const check = async (queue: string[]) => {
      for (const destinationId of queue) {
        if (controller.signal.aborted) return
        try {
          const response = await fetch(
            `/api/lab/course-availability?${parameters}&destinationId=${encodeURIComponent(destinationId)}`,
            {
              cache: 'no-store',
              signal: AbortSignal.any([controller.signal, AbortSignal.timeout(10000)]),
            },
          )
          if (!response.ok) throw new Error('Destination check failed.')
          const verdict = await response.json() as { instanceId: string; revision: number; reachable: boolean }
          if (verdict.instanceId !== instanceId || verdict.revision !== revision) throw new Error('Departure changed.')
          if (verdict.reachable && !controller.signal.aborted) {
            ids.add(destinationId)
            setResult({ key, ids: new Set(ids), done: false, incomplete })
          }
        } catch {
          if (!controller.signal.aborted) incomplete = true
        }
      }
    }

    const timer = window.setTimeout(() => {
      const work = JSON.parse(queues) as string[][]
      void Promise.all(work.map(check)).then(() => {
        if (controller.signal.aborted) return
        if (!incomplete) completedKey.current = key
        setResult({ key, ids, done: true, incomplete })
      })
    }, 250)
    return () => {
      clearTimeout(timer)
      controller.abort()
    }
  }, [key, instanceId, revision, query, queues])

  const current = result?.key === key ? result : null
  return {
    active,
    ids: current?.ids ?? empty,
    loading: active && !current?.done,
    incomplete: !!current?.done && current.incomplete,
    recalculate: () => setAttempt((value) => value + 1),
  }
}
