import { useEffect, useMemo, useRef, useState } from 'react'
import type { CourseLayer, FlightForecast, LabObject, Snapshot } from './lab'

const dayTicks = 864_000_000_000n

export function useFlightPath(snapshot: Snapshot | null, ship: LabObject | undefined, enabled: boolean) {
  const [result, setResult] = useState<{ key: string; data?: FlightForecast; tick?: string } | null>(null)
  const tick = snapshot?.world.elapsedTicks
  const requestTick = useRef(tick)
  useEffect(() => { requestTick.current = tick }, [tick])
  const instance = snapshot?.instanceId
  const shipId = ship?.id
  const version = ship?.flightPathVersion
  const key = enabled && instance && shipId && version ? JSON.stringify([instance, shipId, version]) : null
  const current = key && result?.key === key ? result : null
  const path = current?.data?.path
  const now = tick === undefined ? null : BigInt(tick)
  const end = path ? BigInt(path.endTicks) : null
  const needsRefresh = path && now !== null && end !== null &&
    (now >= end || now - BigInt(path.startTicks) >= 5n * dayTicks)
  const failedAt = current && !path ? current.tick : undefined
  // Stable between polls while one request is running. Large clock jumps can refresh immediately after it.
  const requestKey = key && (!current || needsRefresh || failedAt !== undefined && failedAt !== tick)
    ? key + (path?.startTicks ?? failedAt ?? '') : null
  useEffect(() => {
    if (!requestKey || !shipId || !key) return
    const controller = new AbortController()
    let started = false
    function start() {
      if (document.hidden) return
      if (started) return
      started = true
      void (async () => {
        try {
          const response = await fetch(`/api/lab/spacecraft/${encodeURIComponent(shipId)}/flight-path`, {
            cache: 'no-store',
            signal: AbortSignal.any([controller.signal, AbortSignal.timeout(5000)]),
          })
          if (!response.ok) throw new Error('Flight forecast unavailable')
          const data: FlightForecast = await response.json()
          if (!controller.signal.aborted) {
            const returnedKey = JSON.stringify([data.instanceId, data.shipId, data.pathVersion])
            setResult({ key: returnedKey, data })
          }
        } catch {
          if (!controller.signal.aborted) setResult({ key, tick: requestTick.current })
        }
      })()
    }
    const timer = window.setTimeout(start, 150)
    document.addEventListener('visibilitychange', start)
    return () => {
      clearTimeout(timer)
      document.removeEventListener('visibilitychange', start)
      controller.abort()
    }
  }, [requestKey, key, shipId])

  const position = ship?.position
  const velocity = ship?.velocityKmPerSecond
  const layer = useMemo<CourseLayer | null>(() => {
    if (!path || now === null || now < BigInt(path.startTicks) || !tick || !position || !velocity) return null
    const remaining = path.points.filter((point) => BigInt(point.atTicks) > now)
    if (!remaining.length) return null
    return {
      kind: 'forecast',
      trajectory: { points: [{ atTicks: tick, positionKm: position, velocityKmPerSecond: velocity }, ...remaining] },
      transitionTicks: new Set(path.gravityTransitions.map((transition) => transition.atTicks)),
    }
  }, [path, now, tick, position, velocity])
  return {
    layer,
    endTicks: layer ? path?.endTicks : undefined,
    unavailable: !!current && !path,
    loading: !!key && !current,
  }
}
