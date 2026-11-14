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
  brakingMarginPercent?: number
  criticalBrakingReserve?: boolean
  gravityTransitions?: { bodyId: string; bodyName: string; atTicks: string }[]
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
  flightPathVersion?: string
  orbit?: {
    semiMajorAxisKm: number
    eccentricity: number
    inclinationRadians: number
    longitudeOfAscendingNodeRadians: number
    argumentOfPeriapsisRadians: number
  } | null
}
export const isCourseDestination = (object: LabObject) =>
  object.kind !== 'spacecraft'

type LabEvent = {
  sequence: number
  atTicks: string
  kind: string
  objectId: string
  message: string
  arrivalLabel?: string
}

export const sailcraftId = 'ship-sundiver'
export type SailStatus = {
  status: 'Coasting' | 'Sailing'
  primaryBodyId: string
  speedKmPerSecond: number
  solarDistanceAu: number
  massKg: number
  areaSquareMeters: number
  faceOnAccelerationMPerSecond2: number
  angleDegrees: number | null
  trimEndTicks: string | null
}
export type SailFlyby = {
  bodyId: string
  bodyName: string
  closestAltitudeKm: number
  solarSpeedChangeKmPerSecond: number | null
}
export type SailPreview = {
  instanceId: string
  revision: number
  previewId: string | null
  coast: Pick<Trajectory, 'points'> | null
  trim: Pick<Trajectory, 'points'> | null
  coastFlybys: SailFlyby[]
  trimFlybys: SailFlyby[]
  coastWarning: string | null
}
export type FlightForecast = {
  instanceId: string
  shipId: string
  pathVersion: string
  path: {
    startTicks: string
    endTicks: string
    points: Trajectory['points']
    gravityTransitions: { bodyId: string; bodyName: string; atTicks: string }[]
  }
}

export type Snapshot = {
  instanceId: string
  revision: number
  running: boolean
  stopReason: string
  durationSeconds: number
  mode: 'once' | 'continuous'
  interrupt?: LabEvent | null
  flightNotice?: LabEvent | null
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
    solarSail?: SailStatus | null
    canLaunchSailcraft?: boolean
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
  tuned?: CoursePreview | null
}

export type CourseLayer = {
  kind: 'preview' | 'committed' | 'sail-coast' | 'sail-trim' | 'forecast'
  trajectory: Pick<Trajectory, 'points' | 'flyby'>
  transitionTicks?: ReadonlySet<string>
}
export const courseColor = (kind: CourseLayer['kind']) => ({
  preview: '#efc46b', committed: '#87e4bd', 'sail-coast': '#9abafa', 'sail-trim': '#f2aadd', forecast: '#69e2f4',
})[kind]
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
