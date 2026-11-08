export type Position = { x: number; y: number; z: number }
export type Trajectory = {
  departureTicks: string
  arrivalTicks: string
  travelSeconds: number
  requiredDeltaVKmPerSecond: number
  fuel: { propellantMassKg: number | null; unavailableReason: string | null }
  points: {
    atTicks: string
    positionKm: Position
    velocityKmPerSecond: Position
  }[]
}
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
    trajectory: Trajectory | null
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
  courses: CourseLayer[]
  objects: LabObject[]
  selectedId: string
  references: boolean
  onSelect: (id: string) => void
}
export type CoursePreview = {
  previewId: string
  instanceId: string
  revision: number
  shipId: string
  destinationId: string
  sampledDistanceKm: number
  warnings: string[]
  trajectory: Trajectory
}
export type CourseLayer = {
  kind: 'preview' | 'committed'
  trajectory: Trajectory
}
export const courseColor = (kind: CourseLayer['kind']) =>
  kind === 'preview' ? '#efc46b' : '#87e4bd'
export function formatTime(ticks: number | string) {
  const seconds = BigInt(ticks) / 10_000_000n
  return `D${(seconds / 86400n)
    .toString()
    .padStart(
      3,
      '0',
    )} ${[(seconds / 3600n) % 24n, (seconds / 60n) % 60n, seconds % 60n].map((v) => v.toString().padStart(2, '0')).join(':')}`
}
export const objectColor = (kind: string) =>
  kind === 'star' ? '#efc46b' : kind === 'planet' ? '#7ab6d5' : '#87e4bd'
