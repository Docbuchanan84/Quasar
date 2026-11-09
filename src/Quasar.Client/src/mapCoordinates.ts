import type { Position } from './lab'

// Presentation only. Both views consume the server's XYZ kilometres without extrapolation.
export const mapScale = 1 / 1_000_000
export function toMapPosition(position: Position): Position {
  return { x: position.x * mapScale, y: position.y * mapScale, z: position.z * mapScale }
}
export function toTacticalPosition(position: Position) {
  const point = toMapPosition(position)
  return { x: point.x, y: -point.y }
}

// Illustrative lab circles, never orbital elements or authoritative physical positions.
// Use one geometry for both maps, including polar/zero offsets without division by zero.
export function referenceCircle(position: Position): Position[] {
  const radial = Math.hypot(position.x, position.y)
  const radius = Math.hypot(radial, position.z)
  if (radius === 0) return []
  const tangent = radial === 0
    ? { x: 1, y: 0, z: 0 }
    : { x: -position.y / radial, y: position.x / radial, z: 0 }
  return Array.from({ length: 97 }, (_, i) => {
    const angle = i / 96 * Math.PI * 2
    const c = Math.cos(angle), s = radius * Math.sin(angle)
    return { x: position.x * c + tangent.x * s, y: position.y * c + tangent.y * s, z: position.z * c }
  })
}


export function orbitPath(object: import('./lab').LabObject): Position[] {
  const orbit = object.orbit
  if (!orbit) return object.kind === 'planet' ? referenceCircle(object.position) : []
  const { semiMajorAxisKm: a, eccentricity: e, inclinationRadians: i,
    longitudeOfAscendingNodeRadians: node, argumentOfPeriapsisRadians: peri } = orbit
  return Array.from({ length: 97 }, (_, n) => {
    const anomaly = n / 96 * Math.PI * 2
    const x = a * (Math.cos(anomaly) - e)
    const y = a * Math.sqrt(1 - e * e) * Math.sin(anomaly)
    const u = x * Math.cos(peri) - y * Math.sin(peri)
    const v = x * Math.sin(peri) + y * Math.cos(peri)
    return {
      x: u * Math.cos(node) - v * Math.cos(i) * Math.sin(node),
      y: u * Math.sin(node) + v * Math.cos(i) * Math.cos(node),
      z: v * Math.sin(i),
    }
  })
}

export function focusRadius(objects: import('./lab').LabObject[], id: string) {
  return Math.max(20000, ...objects.filter((o) => o.parentId === id)
    .map((o) => o.orbit ? o.orbit.semiMajorAxisKm * (1 + o.orbit.eccentricity) : 0))
}

export function showObject(object: import('./lab').LabObject, selected: string, focused: string | null, spanKm: number) {
  if (object.id === selected || object.id === focused || object.kind === 'spacecraft' || !object.orbit) return true
  if (object.parentId === focused) return true
  return object.parentId === 'star-sun'
    ? object.kind === 'planet' || spanKm < 2e9
    : object.orbit.semiMajorAxisKm >= spanKm * .035
}
