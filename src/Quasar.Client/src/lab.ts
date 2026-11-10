export type Position = { x: number; y: number; z: number }
export type Trajectory = {
  departureTicks: string
  arrivalTicks: string
  travelSeconds: number
  requiredDeltaVKmPerSecond: number
  fuel: { propellantMassKg: number | null; unavailableReason: string | null }
  arrivalKind?: string | null
  arrival?: {
    kind: 'approach' | 'orbit'
    targetRadiusKm: number
    primaryBodyId: string
    positionErrorKm: number
    velocityErrorKmPerSecond: number
  } | null
  burnSchedule?: { phase: string; startTicks: string; durationSeconds: number; propellantKg: number }[] | null
  brakingMarginKg?: number | null
  flyby?: { bodyId: string; bodyName: string; atTicks: string; closestApproachKm: number } | null
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
  radiusKm?: number
  orbit?: {
    semiMajorAxisKm: number
    eccentricity: number
    inclinationRadians: number
    longitudeOfAscendingNodeRadians: number
    argumentOfPeriapsisRadians: number
  } | null
}
export const isCourseDestination = (object: LabObject) =>
  object.kind === 'planet' || object.kind === 'moon'

type LabEvent = {
  sequence: number
  atTicks: string
  kind: string
  objectId: string
  message: string
}

export type Snapshot = {
  instanceId: string
  revision: number
  running: boolean
  stopReason: string
  durationSeconds: number
  mode: 'once' | 'continuous'
  interrupt?: LabEvent | null
  world: {
    trajectory: Trajectory | null
    elapsedTicks: string
    referenceEpoch?: string | null
    objects: LabObject[]
    ship: {
      status: string
      speedKmPerSecond: number
      destinationId: string | null
      arrivalTicks: string | null
      remainingDistanceKm: number
      burnEndTicks?: string | null
      propulsion?: {
        dryMassKg: number
        wetMassKg: number
        availablePropellantKg: number
        ratedMassFlowKgPerSecond: number
        initialAccelerationMPerSecond2: number
        theoreticalDeltaVMPerSecond: number
      } | null
    }
    events: LabEvent[]
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
export type AutomaticCoursePreview = {
  fastest: CoursePreview | null
  lowestManeuverCost: CoursePreview | null
  infeasibleReasons: string[]
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
  kind === 'star' ? '#efc46b' : kind === 'planet' ? '#7ab6d5' : kind === 'station' ? '#e99dcd' : kind === 'spacecraft' ? '#87e4bd' : '#b6bccf'
