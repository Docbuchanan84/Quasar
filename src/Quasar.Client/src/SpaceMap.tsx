import { useEffect, useRef, useState } from 'react'
import { Engine } from '@babylonjs/core/Engines/engine'
import { Scene } from '@babylonjs/core/scene'
import { ArcRotateCamera } from '@babylonjs/core/Cameras/arcRotateCamera'
import { Vector3, Color3, Color4, Matrix } from '@babylonjs/core/Maths/math'
import { MeshBuilder } from '@babylonjs/core/Meshes/meshBuilder'
import { StandardMaterial } from '@babylonjs/core/Materials/standardMaterial'
import { PointerEventTypes } from '@babylonjs/core/Events/pointerEvents'
import type { Mesh } from '@babylonjs/core/Meshes/mesh'
import '@babylonjs/core/Culling/ray'
import { mapScale, objectColor, type MapProps } from './lab'

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
      'Three-dimensional system map. Drag to rotate, pinch to zoom, two fingers to pan.',
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
      const camera = new ArcRotateCamera(
        'system-camera',
        -Math.PI / 2,
        Math.PI / 3,
        8,
        Vector3.Zero(),
        scene,
      )
      camera.attachControl(canvas, true)
      camera.lowerRadiusLimit = 1
      camera.upperRadiusLimit = 30
      camera.wheelPrecision = 30
      camera.panningSensibility = 150
      camera.minZ = 0.01
      const meshes = new Map<string, Mesh>(),
        labels = new Map<string, HTMLSpanElement>()
      const point = (p: { x: number; y: number; z: number }) =>
        new Vector3(p.x * mapScale, p.y * mapScale, p.z * mapScale)
      for (const object of current.current.objects) {
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
        material.emissiveColor = Color3.FromHexString(objectColor(object.kind))
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
        const u = point(planet.position),
          radius = u.length(),
          unit = u.normalizeToNew(),
          v = Vector3.Cross(unit, new Vector3(0, 0, 1)).normalize()
        const points = Array.from({ length: 97 }, (_, i) => {
          const a = (i / 96) * Math.PI * 2
          return unit
            .scale(radius * Math.cos(a))
            .add(v.scale(radius * Math.sin(a)))
        })
        const guide = MeshBuilder.CreateLines(
          `${planet.id}-reference`,
          { points },
          scene,
        )
        guide.color = Color3.FromHexString('#345065')
        guide.isPickable = false
        guides.push(guide)
      }
      let route: Mesh | null = null,
        lastRoute = ''
      scene.onPointerObservable.add((info) => {
        if (info.type === PointerEventTypes.POINTERTAP) {
          const id = info.pickInfo?.pickedMesh?.metadata?.objectId
          if (id) current.current.onSelect(id)
        }
      })
      controls.current = (action) => {
        if (action === 'reset') {
          camera.setTarget(Vector3.Zero())
          camera.alpha = -Math.PI / 2
          camera.beta = Math.PI / 3
          camera.radius = 8
        } else if (action === 'in' || action === 'out')
          camera.radius = Math.max(
            1,
            Math.min(30, camera.radius * (action === 'in' ? 0.8 : 1.25)),
          )
        else if (action === 'rotate') camera.alpha += Math.PI / 8
        else
          camera.target.addInPlace(
            new Vector3(
              action === 'left' ? -0.3 : action === 'right' ? 0.3 : 0,
              action === 'up' ? 0.3 : action === 'down' ? -0.3 : 0,
              0,
            ),
          )
      }
      const render = () => {
        if (document.hidden) return
        const { objects, selectedId, destinationId, references } =
          current.current
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
        const ship = objects.find((o) => o.kind === 'spacecraft'),
          destination = objects.find((o) => o.id === destinationId)
        const routeKey = JSON.stringify([ship?.position, destinationId])
        if (routeKey !== lastRoute) {
          route?.dispose()
          route = null
          lastRoute = routeKey
          if (ship && destination) {
            const line = MeshBuilder.CreateLines(
              'movement-vector',
              { points: [point(ship.position), point(destination.position)] },
              scene,
            )
            line.color = Color3.FromHexString('#87e4bd')
            line.isPickable = false
            route = line
          }
        }
        scene.render()
        const viewport = camera.viewport.toGlobal(
          engine.getRenderWidth(),
          engine.getRenderHeight(),
        )
        for (const object of objects) {
          const projected = Vector3.Project(
            point(object.position),
            Matrix.Identity(),
            scene.getTransformMatrix(),
            viewport,
          )
          const label = labels.get(object.id)!
          const x =
            (projected.x * element.clientWidth) / engine.getRenderWidth()
          const y =
            (projected.y * element.clientHeight) / engine.getRenderHeight()
          label.hidden =
            projected.z < 0 ||
            projected.z > 1 ||
            x < 0 ||
            x > element.clientWidth ||
            y < 0 ||
            y > element.clientHeight
          label.style.left = `${Math.max(4, Math.min(element.clientWidth - 110, x + 14))}px`
          label.style.top = `${Math.max(4, Math.min(element.clientHeight - 60, y + (object.kind === 'planet' ? -26 : 16)))}px`
        }
      }
      const visibility = () => {
        if (document.hidden) engine.stopRenderLoop(render)
        else engine.runRenderLoop(render)
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
      <div className="camera-controls" aria-label="3D camera controls">
        {[
          ['in', '+'],
          ['out', '−'],
          ['rotate', '↻'],
          ['left', '←'],
          ['right', '→'],
          ['up', '↑'],
          ['down', '↓'],
          ['reset', 'Fit'],
        ].map(([action, label]) => (
          <button
            key={action}
            aria-label={`Map ${action}`}
            onClick={() => controls.current(action)}
          >
            {label}
          </button>
        ))}
      </div>
    </>
  )
}
