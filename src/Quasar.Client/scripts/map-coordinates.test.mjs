import assert from 'node:assert/strict'
import test from 'node:test'
import { referenceCircle, toMapPosition, toTacticalPosition } from '../src/mapCoordinates.ts'
import { mapRuler } from '../src/mapRuler.ts'

test('XY projection agrees with XYZ map units and never changes server coordinates', () => {
  for (const input of [{ x: 1_200_000, y: 400_000, z: 200_000 },
    { x: -2_000_000, y: 1_300_000, z: -600_000 }, { x: 0, y: 0, z: 7000 }]) {
    const position = Object.freeze(input)
    const xyz = toMapPosition(position)
    const xy = toTacticalPosition(position)
    assert.deepEqual(xy, { x: xyz.x, y: -xyz.y })
    assert.ok(Math.abs(xyz.z - position.z / 1_000_000) < 1e-12)
    assert.notEqual(xyz, position)
  }
  assert.deepEqual(toMapPosition({ x: 1_000_000, y: -2_000_000, z: 3_000_000 }), { x: 1, y: -2, z: 3 })
})

test('both maps use identical inclined reference geometry, including polar and zero cases', () => {
  for (const position of [{ x: 1_200_000, y: 400_000, z: 200_000 }, { x: 0, y: 0, z: 7000 }]) {
    const points = referenceCircle(Object.freeze(position))
    assert.equal(points.length, 97)
    assert.deepEqual(points[0], position)
    const radius = Math.hypot(position.x, position.y, position.z)
    for (const point of points) {
      assert.ok(Math.abs(Math.hypot(point.x, point.y, point.z) - radius) < 1e-8)
      assert.deepEqual(toTacticalPosition(point), { x: toMapPosition(point).x, y: -toMapPosition(point).y })
    }
  }
  assert.deepEqual(referenceCircle({ x: 0, y: 0, z: 0 }), [])
})

test('scale bars fit in the overlay and show the distance represented by their width', () => {
  const au = 149_597_870.7
  for (const [kilometresPerPixel, distance, unit] of [
    [25, 2_000, 'km'],
    [50, 5_000, 'km'],
    [100, 10_000, 'km'],
    [au / 141, 100_000_000, 'km'],
    [au / 140, 1, 'AU'],
    [au / 20, 5, 'AU'],
    [125_000_000, 100, 'AU'],
  ]) {
    const { label, width } = mapRuler(kilometresPerPixel)
    assert.equal(label, `${distance.toLocaleString()} ${unit}`)
    assert.ok(width >= 56 && width <= 140)
    assert.ok(Math.abs(width * kilometresPerPixel / (distance * (unit === 'AU' ? au : 1)) - 1) < 1e-12)
  }
  for (const invalidScale of [0, -1, Infinity, NaN]) {
    assert.equal(mapRuler(invalidScale), null)
  }
})
