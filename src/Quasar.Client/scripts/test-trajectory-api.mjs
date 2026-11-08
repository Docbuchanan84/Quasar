// Mutates only the explicitly supplied isolated test host; never point this at an owner's live world.
import assert from 'node:assert/strict'
import { randomUUID } from 'node:crypto'

const base = process.argv[2]
assert.ok(base, 'Supply an isolated server URL')
const read = async () => (await fetch(`${base}/api/lab`)).json()
const clientId = randomUUID()
async function command(operation, extra = {}) {
  const state = await read()
  const response = await fetch(`${base}/api/lab/commands`, {
    method: 'POST', headers: { Origin: base, 'Content-Type': 'application/json' },
    body: JSON.stringify({ instanceId: state.instanceId, revision: state.revision, clientId, operation, ...extra }),
  })
  const result = await response.json()
  assert.equal(response.status, 200, JSON.stringify(result))
  return result
}
const ship = (state) => state.world.objects.find(o => o.kind === 'spacecraft')
const before = await read()
const response = await fetch(`${base}/api/lab/trajectory-preview?destinationId=planet-iris&flightSeconds=21600`)
assert.equal(response.status, 200)
assert.equal(response.headers.get('cache-control'), 'no-store')
const preview = await response.json()
assert.deepEqual(await read(), before)
assert.equal(preview.points.length, 65)
assert.equal(preview.fuel.propellantMassKg, null)
assert.ok(preview.requiredDeltaVKmPerSecond > 0)
const ordered = await command('travel', { destinationId: 'planet-iris', flightSeconds: 21600 })
assert.deepEqual(ordered.world.trajectory, preview)
for (const point of preview.points) {
  const result = await (await fetch(`${base}/api/lab/spacecraft/state?atTicks=${point.atTicks}`)).json()
  assert.equal(result.atTicks, point.atTicks)
  assert.deepEqual(result.state.positionKm, point.positionKm)
  assert.deepEqual(result.state.velocityKmPerSecond, point.velocityKmPerSecond)
}
assert.deepEqual(await read(), ordered)
const cancelled = await command('cancel-travel')
assert.deepEqual(ship(cancelled), ship(ordered))
assert.equal(cancelled.world.trajectory, null)
assert.equal(cancelled.world.ship.status, 'Coasting')
for (const [path, status] of [
  ['/trajectory-preview?destinationId=missing', 400],
  ['/trajectory-preview?destinationId=planet-iris&flightSeconds=0', 400],
  ['/spacecraft/state?atTicks=no', 400],
  ['/spacecraft/state?atTicks=-1', 400],
]) assert.equal((await fetch(`${base}/api/lab${path}`)).status, status)
assert.deepEqual(await read(), cancelled)
const denied = await fetch(`${base}/api/lab/commands`, {
  method: 'POST', headers: { Origin: 'https://wrong.invalid', 'Content-Type': 'application/json' }, body: '{}',
})
assert.equal(denied.status, 403)
await command('travel', { destinationId: 'planet-nereid', flightSeconds: 21600 })
await command('configure', { durationSeconds: 2592000, mode: 'once' })
await command('play')
let arrived
for (let i = 0; i < 40; i++) {
  arrived = await read()
  if (!arrived.running) break
  await new Promise(resolve => setTimeout(resolve, 100))
}
assert.equal(arrived.running, false)
assert.equal(arrived.world.ship.status, 'Arrived')
assert.equal(arrived.world.elapsedTicks, 21600 * 10000000)
assert.equal(arrived.world.events.filter(e => e.kind === 'arrival').length, 1)
assert.deepEqual(ship(arrived).position, arrived.world.objects.find(o => o.id === 'planet-nereid').position)
console.log('Trajectory HTTP checks passed: isolated preview, committed samples/positions/velocities, exact timestamps, cancellation/replanning, errors, origin validation and headless arrival interruption.')
