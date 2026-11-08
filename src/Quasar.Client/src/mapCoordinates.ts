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
