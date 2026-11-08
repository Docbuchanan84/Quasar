import { ArcRotateCamera } from '@babylonjs/core/Cameras/arcRotateCamera.js'
import { ArcRotateCameraPointersInput } from '@babylonjs/core/Cameras/Inputs/arcRotateCameraPointersInput.js'
import { Vector3 } from '@babylonjs/core/Maths/math.vector.js'
import type { Scene } from '@babylonjs/core/scene.js'
import { MapGesture } from './mapGestures.ts'

// Extend Babylon's input, retaining its capture, multitouch and movement math.
class MapCameraPointersInput extends ArcRotateCameraPointersInput {
  private readonly gesture: MapGesture

  constructor(gesture: MapGesture) {
    super()
    this.gesture = gesture
    this.useNaturalPinchZoom = true
    this.angularSensibilityX = this.angularSensibilityY = 360
  }

  override onTouch(
    ...args: Parameters<ArcRotateCameraPointersInput['onTouch']>
  ) {
    if (this.gesture.dragging && !this.gesture.multiTouch)
      super.onTouch(...args)
  }
}

export function createSpaceCamera(scene: Scene, gesture: MapGesture) {
  const camera = new ArcRotateCamera(
    'system-camera',
    Math.PI / 2,
    Math.PI / 3,
    8,
    Vector3.Zero(),
    scene,
  )
  // Quasar's tactical plane is XY; Z, rather than Babylon's default Y, is up.
  // Look from negative Y so positive X points right, as in the 2D home view.
  camera.upVector = new Vector3(0, 0, 1)
  camera.lowerRadiusLimit = 0.4
  camera.upperRadiusLimit = 30
  camera.lowerBetaLimit = 0.05
  camera.upperBetaLimit = Math.PI - 0.05
  camera.allowUpsideDown = false
  camera.minZ = 0.01
  camera.inertia = 0.45
  camera.panningInertia = 0
  camera.useInputToRestoreState = false
  camera.inputs.removeByType('ArcRotateCameraPointersInput')
  camera.inputs.add(new MapCameraPointersInput(gesture))
  // Keep Ctrl-left and right drag; make middle drag useful too.
  // Append: attachControl's compatibility bridge rewrites the FIRST bare pan entry.
  camera.movement.input.inputMap.push({
    source: 'pointer',
    button: 1,
    interaction: 'pan',
  })
  camera.wheelDeltaPercentage = 0.01
  return camera
}

export function stopCamera(camera: ArcRotateCamera) {
  camera.stopInterpolation()
  camera.inertialAlphaOffset = 0
  camera.inertialBetaOffset = 0
  camera.inertialRadiusOffset = 0
  camera.inertialPanningX = camera.inertialPanningY = 0
  camera.movement.resetRotationVelocity()
  camera.movement.resetPanVelocity()
  camera.movement.resetZoomVelocity()
}

export function frameCamera(
  camera: ArcRotateCamera,
  target: Vector3,
  reset = false,
) {
  stopCamera(camera)
  // The fourth argument preserves orbit angles/distance when changing pivot.
  camera.setTarget(target.clone(), false, true, true)
  if (reset) {
    camera.alpha = Math.PI / 2
    camera.beta = Math.PI / 3
    camera.radius = 8
  }
}

export function scaleCameraPan(camera: ArcRotateCamera, height: number) {
  // Screen-plane world units per CSS pixel at the pivot, at every zoom level.
  camera.panningSensibility =
    Math.max(1, height) / (2 * camera.radius * Math.tan(camera.fov / 2))
}
