import test from 'node:test'
import assert from 'node:assert/strict'
import { NullEngine } from '@babylonjs/core/Engines/nullEngine.js'
import { Scene } from '@babylonjs/core/scene.js'
import { Vector3 } from '@babylonjs/core/Maths/math.vector.js'
import { MapGesture, wheelPixels } from '../src/mapGestures.ts'
import {
  createSpaceCamera,
  frameCamera,
  scaleCameraPan,
} from '../src/spaceCamera.ts'

test('tap jitter never becomes navigation; release displacement also counts', () => {
  const g = new MapGesture()
  g.down(1, 20, 20)
  g.move(1, 23, 22)
  assert.equal(g.dragging, false)
  assert.equal(g.up(1, 23, 22), true)
  g.down(2, 0, 0)
  assert.equal(g.up(2, 10, 0), false)
})

test('drag remains a drag when it returns to its starting point', () => {
  const g = new MapGesture()
  g.down(1, 0, 0)
  g.move(1, 20, 0)
  g.move(1, 0, 0)
  assert.equal(g.up(1, 0, 0), false)
})

test('pinch handoff cannot become a tap/orbit; next gesture works', () => {
  const g = new MapGesture()
  g.down(1, 0, 0)
  g.down(2, 20, 0)
  assert.equal(g.up(2, 20, 0), false)
  g.move(1, 30, 0)
  assert.equal(g.multiTouch, true)
  assert.equal(g.up(1, 30, 0), false)
  g.down(3, 0, 0)
  assert.equal(g.multiTouch, false)
  assert.equal(g.up(3, 0, 0), true)
})

test('cancel and stale/unknown pointer releases never select', () => {
  const g = new MapGesture()
  g.down(1, 0, 0)
  g.cancel()
  assert.equal(g.move(1, 20, 0), undefined)
  assert.equal(g.up(1, 0, 0), false)
  g.down(2, 0, 0)
  assert.equal(g.up(99, 0, 0), false)
  assert.equal(g.up(2, 0, 0), true)
})

test('wheel units normalize pixel, line and page devices', () => {
  assert.equal(wheelPixels({ deltaY: 2, deltaMode: 0 }, 500), 2)
  assert.equal(wheelPixels({ deltaY: 2, deltaMode: 1 }, 500), 32)
  assert.equal(wheelPixels({ deltaY: 2, deltaMode: 2 }, 500), 1000)
})

function fixture(run) {
  const engine = new NullEngine()
  const scene = new Scene(engine)
  scene.useRightHandedSystem = true
  const gesture = new MapGesture()
  const camera = createSpaceCamera(scene, gesture)
  try {
    run(camera, gesture)
  } finally {
    scene.dispose()
    engine.dispose()
  }
}

test('camera maps Z-up, left orbit, right/middle/Ctrl-left pan', () =>
  fixture((c) => {
    c.attachControl(undefined, false)
    assert.deepEqual(c.upVector.asArray(), [0, 0, 1])
    assert.ok(
      Vector3.TransformCoordinates(new Vector3(1, 0, 0), c.getViewMatrix(true))
        .x > 0,
      'positive X points right in the home view, matching the 2D map',
    )
    const resolve = (button, ctrl = false) =>
      c.movement.input.resolveInteraction('pointer', {
        button,
        modifiers: { ctrl },
      }).interaction
    assert.equal(resolve(0), 'rotate')
    assert.equal(resolve(0, true), 'pan')
    assert.equal(resolve(1), 'pan')
    assert.equal(resolve(2), 'pan')
    assert.equal(c.inputs.attached.pointers.useNaturalPinchZoom, true)
  }))

test('Babylon pointer input suppresses jitter and post-pinch orbit', () =>
  fixture((c, g) => {
    const p = c.inputs.attached.pointers
    p.onButtonDown({ button: 0 })
    g.down(1, 0, 0)
    p.onTouch(null, 3, 0)
    assert.equal(c.movement.rotationAccumulatedPixels.length(), 0)
    g.move(1, 20, 0)
    p.onTouch(null, 20, 0)
    assert.ok(c.movement.rotationAccumulatedPixels.length() > 0)
    c.movement.resetRotationVelocity()
    g.down(2, 40, 0)
    g.up(2, 40, 0)
    p.onTouch(null, 20, 0)
    assert.equal(c.movement.rotationAccumulatedPixels.length(), 0)
  }))

test('focus preserves orbit and zoom; reset cancels pending motion', () =>
  fixture((c) => {
    c.alpha = 0.8
    c.beta = 1.4
    c.radius = 3
    frameCamera(c, new Vector3(2, -1, 0.5))
    assert.deepEqual(c.target.asArray(), [2, -1, 0.5])
    assert.deepEqual([c.alpha, c.beta, c.radius], [0.8, 1.4, 3])
    c.inertialAlphaOffset = 0.2
    c.inertialPanningY = 0.3
    c.movement.zoomAccumulatedPixels = 2
    c.movement.panAccumulatedPixels.set(1, 2, 3)
    frameCamera(c, Vector3.Zero(), true)
    c._checkInputs()
    assert.deepEqual(c.target.asArray(), [0, 0, 0])
    assert.deepEqual([c.alpha, c.beta, c.radius], [Math.PI / 2, Math.PI / 3, 8])
  }))

test('panning speed scales with zoom and viewport; radius/poles stay bounded', () =>
  fixture((c) => {
    c.radius = 8
    scaleCameraPan(c, 600)
    const normal = c.panningSensibility
    c.radius = 4
    scaleCameraPan(c, 600)
    assert.equal(c.panningSensibility, normal * 2)
    scaleCameraPan(c, 1200)
    assert.equal(c.panningSensibility, normal * 4)
    c.radius = -10
    c.beta = -10
    c._checkInputs()
    assert.equal(c.radius, c.lowerRadiusLimit)
    assert.equal(c.beta, c.lowerBetaLimit)
    c.radius = 100
    c.beta = 100
    c._checkInputs()
    assert.equal(c.radius, c.upperRadiusLimit)
    assert.equal(c.beta, c.upperBetaLimit)
  }))
