import { useEffect, useRef, useState } from 'react'
import { Engine } from '@babylonjs/core/Engines/engine'
import { Scene } from '@babylonjs/core/scene'
import { Vector3, Color3, Color4, Matrix } from '@babylonjs/core/Maths/math'
import { MeshBuilder } from '@babylonjs/core/Meshes/meshBuilder'
import { StandardMaterial } from '@babylonjs/core/Materials/standardMaterial'
import type { Mesh } from '@babylonjs/core/Meshes/mesh'
import type { LinesMesh } from '@babylonjs/core/Meshes/linesMesh'
import '@babylonjs/core/Culling/ray'
import { MapGesture } from './mapGestures'
import {
  createSpaceCamera,
  frameCamera,
  scaleCameraPan,
  stopCamera,
} from './spaceCamera'
import {
  courseColor,
  objectColor,
  type CourseLayer,
  type MapProps,
} from './lab'
import { referenceCircle, toMapPosition } from './mapCoordinates'

export default function SpaceMap(props: MapProps) {
  const host = useRef<HTMLDivElement>(null),
    current = useRef(props)
  const controls = useRef<(action: string) => void>(() => {})
  const [error, setError] = useState('')
  useEffect(() => {
    current.current = props
  }, [props])
  useEffect(() => {
    const element = host.current!,
      canvas = document.createElement('canvas')
    element.appendChild(canvas)
    canvas.setAttribute(
      'aria-label',
      'Three-dimensional system map. Drag to orbit; right, middle, or Ctrl-drag to pan. Touch: one finger orbits, two pan; pinch or wheel zooms.',
    )
    let cleanup = () => {}
    try {
      const engine = new Engine(canvas, true, {
        preserveDrawingBuffer: false,
        stencil: true,
      })
      engine.setHardwareScalingLevel(Math.max(1, devicePixelRatio / 2))
      const scene = new Scene(engine)
      scene.useRightHandedSystem = true
      scene.clearColor = new Color4(0.035, 0.075, 0.11, 1)
      const gesture = new MapGesture()
      const camera = createSpaceCamera(scene, gesture)
      let focusedId: string | null = null
      const down = (e: PointerEvent) => {
        if (e.button > 2) return
        gesture.down(e.pointerId, e.clientX, e.clientY)
        if (gesture.multiTouch || e.button !== 0 || e.ctrlKey) focusedId = null
        // Switching to two fingers must not retain one-finger orbit momentum.
        stopCamera(camera)
      }
      const move = (e: PointerEvent) => {
        gesture.move(e.pointerId, e.clientX, e.clientY)
        scaleCameraPan(camera, element.clientHeight)
      }
      const up = (e: PointerEvent) => {
        if (gesture.up(e.pointerId, e.clientX, e.clientY) && e.button === 0) {
          const rect = canvas.getBoundingClientRect()
          const pick = scene.pick(e.clientX - rect.left, e.clientY - rect.top)
          const id = pick?.pickedMesh?.metadata?.objectId
          if (id) current.current.onSelect(id)
        }
        if (gesture.multiTouch) stopCamera(camera)
      }
      const cancel = (e: Event) => {
        if (
          e.type === 'lostpointercapture' &&
          !gesture.pointers.has((e as PointerEvent).pointerId)
        )
          return
        gesture.cancel()
        stopCamera(camera)
      }
      const contextMenu = (e: Event) => e.preventDefault()
      // Capture tracks intent before Babylon consumes input; Babylon still moves the camera.
      canvas.addEventListener('pointerdown', down, true)
      canvas.addEventListener('pointermove', move, true)
      canvas.addEventListener('pointerup', up, true)
      canvas.addEventListener('pointercancel', cancel, true)
      canvas.addEventListener('lostpointercapture', cancel)
      canvas.addEventListener('contextmenu', contextMenu)
      window.addEventListener('blur', cancel)
      camera.attachControl(canvas, false)
      const meshes = new Map<string, Mesh>(),
        labels = new Map<string, HTMLSpanElement>()
      const point = (p: { x: number; y: number; z: number }) => {
        const scaled = toMapPosition(p)
        return new Vector3(scaled.x, scaled.y, scaled.z)
      }
      const syncObjects = (objects: MapProps['objects']) => {
        const ids = new Set(objects.map((o) => o.id))
        for (const [id, mesh] of meshes) {
          if (!ids.has(id)) {
            mesh.material?.dispose()
            mesh.dispose()
            meshes.delete(id)
            labels.get(id)?.remove()
            labels.delete(id)
          }
        }
        for (const object of objects) {
          if (meshes.has(object.id)) {
            labels.get(object.id)!.textContent = object.name
            continue
          }
          const mesh =
            object.kind === 'spacecraft'
              ? MeshBuilder.CreateCylinder(
                  object.id,
                  {
                    diameterTop: 0,
                    diameterBottom: 0.12,
                    height: 0.2,
                    tessellation: 4,
                  },
                  scene,
                )
              : MeshBuilder.CreateSphere(
                  object.id,
                  {
                    diameter: object.kind === 'star' ? 0.22 : 0.15,
                    segments: 12,
                  },
                  scene,
                )
          const material = new StandardMaterial(`${object.id}-symbol`, scene)
          material.emissiveColor = Color3.FromHexString(
            objectColor(object.kind),
          )
          material.disableLighting = true
          mesh.material = material
          mesh.metadata = { objectId: object.id }
          meshes.set(object.id, mesh)
          // Screen-space labels remain legible on phones without a GUI package or texture per name.
          const label = document.createElement('span')
          label.className = 'space-label'
          label.textContent = object.name
          label.style.color = objectColor(object.kind)
          label.setAttribute('aria-hidden', 'true')
          element.appendChild(label)
          labels.set(object.id, label)
        }
      }
      syncObjects(current.current.objects)
      let lastObjects = current.current.objects
      const guides: Mesh[] = []
      const axis = MeshBuilder.CreateLineSystem(
        'axes',
        {
          lines: [
            [new Vector3(-3, 0, 0), new Vector3(3, 0, 0)],
            [new Vector3(0, -3, 0), new Vector3(0, 3, 0)],
            [new Vector3(0, 0, -3), new Vector3(0, 0, 3)],
          ],
        },
        scene,
      )
      axis.color = Color3.FromHexString('#345065')
      axis.isPickable = false
      guides.push(axis)
      for (const planet of current.current.objects.filter(
        (o) => o.kind === 'planet',
      )) {
        const points = referenceCircle(planet.position).map(point)
        if (points.length === 0) continue
        const guide = MeshBuilder.CreateLines(
          `${planet.id}-reference`,
          { points },
          scene,
        )
        guide.color = Color3.FromHexString('#345065')
        guide.isPickable = false
        guides.push(guide)
      }
      let routes: Mesh[] = []
      let lastCourses: CourseLayer[] | null = null
      let lastLabelObjects: MapProps['objects'] | null = null
      let lastView = -1,
        lastProjection = -1
      let lastWidth = 0,
        lastHeight = 0
      controls.current = (action) => {
        if (action === 'reset') {
          focusedId = null
          frameCamera(camera, Vector3.Zero(), true)
        } else if (action === 'focus') {
          const selected = current.current.objects.find(
            (o) => o.id === current.current.selectedId,
          )
          if (selected) {
            focusedId = selected.id
            frameCamera(camera, point(selected.position))
          }
        } else if (action === 'in' || action === 'out') {
          stopCamera(camera)
          camera.radius = Math.max(
            camera.lowerRadiusLimit!,
            Math.min(
              camera.upperRadiusLimit!,
              camera.radius * (action === 'in' ? 0.8 : 1.25),
            ),
          )
        }
      }
      const render = () => {
        if (document.hidden) return
        const { objects, selectedId, courses, references } = current.current
        if (objects !== lastObjects) {
          syncObjects(objects)
          lastObjects = objects
        }
        if (focusedId !== selectedId) focusedId = null
        const focused = objects.find((o) => o.id === focusedId)
        // Follow a moving focused object without changing orbit angles or zoom.
        // Pan/two-finger navigation releases follow; selection never moves the view.
        if (focused) camera.target.copyFrom(point(focused.position))
        scaleCameraPan(camera, element.clientHeight)
        for (const object of objects) {
          const mesh = meshes.get(object.id)!
          mesh.position.copyFrom(point(object.position))
          mesh.showBoundingBox = object.id === selectedId
          // Maintain readable symbols as the camera moves; sizes are presentation only.
          const size = Math.max(
            0.7,
            ((Vector3.Distance(camera.position, mesh.position) / 8) * 600) /
              element.clientHeight,
          )
          mesh.scaling.setAll(size)
        }
        for (const guide of guides) guide.setEnabled(references)
        if (courses !== lastCourses) {
          for (const mesh of routes) {
            mesh.material?.dispose()
            mesh.dispose()
          }
          routes = []
          lastCourses = courses
          for (const course of courses) {
            const points = course.trajectory.points.map((sample) =>
              point(sample.positionKm),
            )
            if (points.length < 2) continue
            const line =
              course.kind === 'preview'
                ? MeshBuilder.CreateDashedLines(
                    'preview-trajectory',
                    { points, dashSize: 3, gapSize: 2, dashNb: 64 },
                    scene,
                  )
                : MeshBuilder.CreateLines(
                    'committed-trajectory',
                    { points },
                    scene,
                  )
            line.color = Color3.FromHexString(courseColor(course.kind))
            line.isPickable = false
            routes.push(line)
            for (const [phase, position] of [
              ['departure', points[0]],
              ['arrival', points.at(-1)!],
            ] as const) {
              const marker =
                phase === 'departure'
                  ? MeshBuilder.CreateBox(
                      `${course.kind}-${phase}`,
                      { size: 0.1 },
                      scene,
                    )
                  : MeshBuilder.CreateLines(
                      `${course.kind}-${phase}-future-destination`,
                      {
                        points: [
                          new Vector3(0.2, 0, 0),
                          new Vector3(0, 0.2, 0),
                          new Vector3(-0.2, 0, 0),
                          new Vector3(0, -0.2, 0),
                          new Vector3(0.2, 0, 0),
                          new Vector3(0, 0, 0.2),
                          new Vector3(-0.2, 0, 0),
                          new Vector3(0, 0, -0.2),
                          new Vector3(0.2, 0, 0),
                        ],
                      },
                      scene,
                    )
              marker.position.copyFrom(position)
              marker.isPickable = false
              if (phase === 'arrival') {
                const arrivalMarker = marker as LinesMesh
                arrivalMarker.color = line.color
                routes.push(marker)
                continue
              }
              const material = new StandardMaterial(
                `${course.kind}-${phase}-material`,
                scene,
              )
              material.emissiveColor = line.color
              material.disableLighting = true
              material.wireframe = true
              marker.material = material
              routes.push(marker)
            }
          }
        }
        scene.render()
        const viewport = camera.viewport.toGlobal(
          engine.getRenderWidth(),
          engine.getRenderHeight(),
        )
        const viewVersion = camera.getViewMatrix().updateFlag
        const projectionVersion = camera.getProjectionMatrix().updateFlag
        const width = element.clientWidth,
          height = element.clientHeight
        if (
          objects === lastLabelObjects &&
          viewVersion === lastView &&
          projectionVersion === lastProjection &&
          width === lastWidth &&
          height === lastHeight
        )
          return
        lastLabelObjects = objects
        lastView = viewVersion
        lastProjection = projectionVersion
        lastWidth = width
        lastHeight = height
        // Read layout once before writing labels; interleaving reads/writes per object forces repeated layout.
        for (const object of objects) {
          const projected = Vector3.Project(
            point(object.position),
            Matrix.Identity(),
            scene.getTransformMatrix(),
            viewport,
          )
          const label = labels.get(object.id)!
          const x = (projected.x * width) / engine.getRenderWidth()
          const y = (projected.y * height) / engine.getRenderHeight()
          label.hidden =
            projected.z < 0 ||
            projected.z > 1 ||
            x < 0 ||
            x > width ||
            y < 0 ||
            y > height
          label.style.left = `${Math.max(4, Math.min(width - 110, x + 14))}px`
          label.style.top = `${Math.max(4, Math.min(height - 60, y + (object.kind === 'planet' ? -26 : 16)))}px`
        }
      }
      const visibility = () => {
        if (document.hidden) {
          cancel(new Event('visibilitychange'))
          engine.stopRenderLoop(render)
        } else engine.runRenderLoop(render)
      }
      const resize = new ResizeObserver(() => engine.resize())
      resize.observe(element)
      const lost = (e: Event) => {
        e.preventDefault()
        setError('3D graphics context lost. Switch views to reload.')
      }
      canvas.addEventListener('webglcontextlost', lost)
      document.addEventListener('visibilitychange', visibility)
      engine.runRenderLoop(render)
      cleanup = () => {
        canvas.removeEventListener('pointerdown', down, true)
        canvas.removeEventListener('pointermove', move, true)
        canvas.removeEventListener('pointerup', up, true)
        canvas.removeEventListener('pointercancel', cancel, true)
        canvas.removeEventListener('lostpointercapture', cancel)
        canvas.removeEventListener('contextmenu', contextMenu)
        window.removeEventListener('blur', cancel)
        resize.disconnect()
        document.removeEventListener('visibilitychange', visibility)
        canvas.removeEventListener('webglcontextlost', lost)
        engine.stopRenderLoop()
        scene.dispose()
        engine.dispose()
        for (const label of labels.values()) label.remove()
      }
    } catch {
      // Engine creation can fail on devices without WebGL; report after effect setup finishes.
      queueMicrotask(() =>
        setError(
          '3D graphics unavailable. Switch to 2D or use the object list.',
        ),
      )
    }
    return () => {
      cleanup()
      canvas.remove()
    }
  }, [])
  return (
    <>
      <div className="map-canvas" ref={host} />
      {error && (
        <p className="map-error" role="alert">
          {error}
        </p>
      )}
      <p className="map-gestures">
        Drag to orbit · right/middle/Ctrl-drag or two fingers to pan ·
        wheel/pinch to zoom
      </p>
      <div className="camera-controls" aria-label="3D camera controls">
        {[
          ['in', '+'],
          ['out', '−'],
          ['focus', 'Focus Selected'],
          ['reset', 'Reset View'],
        ].map(([action, label]) => (
          <button
            key={action}
            aria-label={
              action === 'reset'
                ? 'Reset View'
                : action === 'focus'
                  ? 'Focus Selected Object'
                  : action === 'in'
                    ? 'Zoom in'
                    : 'Zoom out'
            }
            disabled={
              action === 'focus' &&
              !props.objects.some((o) => o.id === props.selectedId)
            }
            onClick={() => controls.current(action)}
          >
            {label}
          </button>
        ))}
      </div>
    </>
  )
}
