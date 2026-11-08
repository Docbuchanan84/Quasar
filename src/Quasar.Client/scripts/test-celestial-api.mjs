// Run only against an isolated lab, e.g. node scripts/test-celestial-api.mjs http://127.0.0.1:5091
import assert from 'node:assert/strict'
import { toMapPosition, toTacticalPosition } from '../src/mapCoordinates.ts'

const base = process.argv[2]
assert.ok(base, 'Supply an isolated server URL')
const before = await (await fetch(`${base}/api/lab`)).json()
for (const body of before.world.objects.filter(o => o.kind !== 'spacecraft')) {
  for (const atTicks of ['0', '-9223372036854775808', '9223372036854775807']) {
    const response = await fetch(`${base}/api/lab/celestial/${body.id}/state?atTicks=${atTicks}`)
    assert.equal(response.status, 200)
    assert.equal(response.headers.get('cache-control'), 'no-store')
    const result = await response.json()
    assert.equal(result.atTicks, atTicks)
    // Fixed lab fixture; orbital propagation is covered headlessly by xUnit.
    assert.deepEqual(result.positionKm, body.position)
    assert.deepEqual(result.velocityKmPerSecond, body.velocityKmPerSecond)
    const xyz = toMapPosition(result.positionKm)
    assert.deepEqual(toTacticalPosition(result.positionKm), { x: xyz.x, y: -xyz.y })
  }
}
for (const [path, status] of [
  ['/missing/state?atTicks=0', 404], ['/planet-iris/state?atTicks=no', 400],
  ['/planet-iris/state?atTicks=9223372036854775808', 400], ['/planet-iris/state', 400],
]) assert.equal((await fetch(`${base}/api/lab/celestial${path}`)).status, status)
assert.deepEqual(await (await fetch(`${base}/api/lab`)).json(), before)
console.log('Celestial HTTP queries passed: exact signed ticks, errors, no-store, shared map projection, unchanged world/revision/history.')
