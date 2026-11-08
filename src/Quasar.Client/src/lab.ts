export type Position = { x: number; y: number; z: number }
export type LabObject = {
  id: string
  name: string
  kind: string
  position: Position
  velocityKmPerSecond: Position | null
  parentId: string | null
}
export type Snapshot = {
  instanceId: string
  revision: number
  running: boolean
  stopReason: string
  durationSeconds: number
  mode: 'once' | 'continuous'
  world: {
    elapsedTicks: number
    objects: LabObject[]
    ship: {
      status: string
      speedKmPerSecond: number
      destinationId: string | null
      arrivalTicks: number | null
      remainingDistanceKm: number
    }
    events: {
      sequence: number
      atTicks: number
      kind: string
      objectId: string
      message: string
    }[]
  }
}
export type MapProps = {
  objects: LabObject[]
  selectedId: string
  destinationId: string | null
  references: boolean
  onSelect: (id: string) => void
}
export function formatTime(ticks: number) {
  const seconds = Math.floor(ticks / 10_000_000)
  return `D${Math.floor(seconds / 86400)
    .toString()
    .padStart(
      3,
      '0',
    )} ${[Math.floor(seconds / 3600) % 24, Math.floor(seconds / 60) % 60, seconds % 60].map((v) => v.toString().padStart(2, '0')).join(':')}`
}
export const objectColor = (kind: string) =>
  kind === 'star' ? '#efc46b' : kind === 'planet' ? '#7ab6d5' : '#87e4bd'
