import { useEffect, useRef, useState } from 'react'
import { Application, Container, Graphics, Text } from 'pixi.js'
import { objectColor, type MapProps } from './lab'
import { referenceCircle, toTacticalPosition } from './mapCoordinates'

export default function SystemMap(props: MapProps) {
  const host = useRef<HTMLDivElement>(null)
  const current = useRef(props)
  const controls = useRef<(action: string) => void>(() => {})
  const [error, setError] = useState('')
  useEffect(() => {
    current.current = props
  }, [props])
  useEffect(() => {
    const element = host.current!
    let disposed = false
    const app = new Application()
    let cleanup = () => {}
    void (async () => {
      await app.init({
        background: '#09131c',
        resizeTo: element,
        antialias: true,
        resolution: Math.min(devicePixelRatio, 2),
        autoDensity: true,
        preference: 'webgl',
      })
      if (disposed) {
        app.destroy(true, { children: true })
        return
      }
      element.appendChild(app.canvas)
      app.stop()
      app.canvas.setAttribute(
        'aria-label',
        'Tactical system map. Drag to pan, pinch to zoom. Select objects here or in the object list.',
      )
      const scene = new Container()
      app.stage.addChild(scene)
      let zoom = 100,
        panX = 0,
        panY = 0,
        lastPinch = 0
      let lastDraw = ''
      const pointers = new Map<number, { x: number; y: number }>()
      let start = { x: 0, y: 0 },
        moved = false
      const screen = (x: number, y: number) => {
        const point = toTacticalPosition({ x, y, z: 0 })
        return {
          x: app.screen.width / 2 + panX + point.x * zoom,
          y: app.screen.height / 2 + panY + point.y * zoom,
        }
      }
      function draw() {
        const key = JSON.stringify([
          current.current.objects,
          current.current.selectedId,
          current.current.destinationId,
          current.current.trajectory,
          current.current.references,
          zoom,
          panX,
          panY,
          app.screen.width,
          app.screen.height,
        ])
        if (key === lastDraw) return
        lastDraw = key
        for (const child of scene.removeChildren())
          child.destroy({ children: true })
        const { objects, selectedId, trajectory, references } =
          current.current
        const lines = new Graphics()
        scene.addChild(lines)
        if (references) {
          for (let n = -6; n <= 6; n++) {
            const a = screen(n * 1e6, -6e6),
              b = screen(n * 1e6, 6e6),
              c = screen(-6e6, n * 1e6),
              d = screen(6e6, n * 1e6)
            lines
              .moveTo(a.x, a.y)
              .lineTo(b.x, b.y)
              .moveTo(c.x, c.y)
              .lineTo(d.x, d.y)
              .stroke({ color: '#203443', width: 1 })
          }
          const origin = screen(0, 0)
          for (const planet of objects.filter((o) => o.kind === 'planet')) {
            for (const [i, point] of referenceCircle(planet.position).entries()) {
              const q = screen(point.x, point.y)
              if (i === 0) lines.moveTo(q.x, q.y)
              else lines.lineTo(q.x, q.y)
            }
            lines.stroke({ color: '#345065', width: 1 })
          }
          lines.circle(origin.x, origin.y, 3).stroke({ color: '#526778' })
        }
        if (trajectory) {
          for (const [i, sample] of trajectory.points.entries()) {
            const p = screen(sample.positionKm.x, sample.positionKm.y)
            if (i === 0) lines.moveTo(p.x, p.y)
            else lines.lineTo(p.x, p.y)
          }
          lines.stroke({ color: '#87e4bd', width: 1.5 })
        }
        for (const object of objects) {
          const p = screen(object.position.x, object.position.y),
            color = objectColor(object.kind)
          const marker = new Graphics()
          if (object.kind === 'spacecraft')
            marker
              .poly([
                p.x,
                p.y - 9,
                p.x + 8,
                p.y + 7,
                p.x,
                p.y + 3,
                p.x - 8,
                p.y + 7,
              ])
              .fill(color)
          else
            marker
              .circle(p.x, p.y, object.kind === 'star' ? 10 : 7)
              .stroke({ color, width: 2 })
              .fill({ color, alpha: 0.2 })
          if (object.id === selectedId)
            marker.circle(p.x, p.y, 18).stroke({ color: '#ffffff', width: 1.5 })
          scene.addChild(marker)
          const label = new Text({
            text: `${object.name}\nZ ${(object.position.z / 1000).toFixed(0)}k km`,
            style: {
              fontFamily: 'monospace',
              fontSize: 11,
              fill: color,
              lineHeight: 15,
            },
          })
          label.position.set(
            Math.max(4, Math.min(p.x + 15, app.screen.width - 115)),
            object.kind === 'planet' ? p.y - 40 : p.y + 16,
          )
          scene.addChild(label)
        }
        app.render()
      }
      controls.current = (action) => {
        if (action === 'reset') {
          zoom = Math.min(element.clientWidth / 6, 110)
          panX = 0
          panY = 0
        } else if (action === 'in') zoom = Math.min(800, zoom * 1.3)
        else if (action === 'out') zoom = Math.max(15, zoom / 1.3)
        else {
          panX += action === 'left' ? 50 : action === 'right' ? -50 : 0
          panY += action === 'up' ? 50 : action === 'down' ? -50 : 0
        }
        draw()
      }
      controls.current('reset')
      const down = (e: PointerEvent) => {
        app.canvas.setPointerCapture(e.pointerId)
        pointers.set(e.pointerId, { x: e.clientX, y: e.clientY })
        start = { x: e.clientX, y: e.clientY }
        moved = false
        lastPinch = 0
      }
      const move = (e: PointerEvent) => {
        const previous = pointers.get(e.pointerId)
        if (!previous) return
        pointers.set(e.pointerId, { x: e.clientX, y: e.clientY })
        if (Math.hypot(e.clientX - start.x, e.clientY - start.y) > 5)
          moved = true
        if (pointers.size === 2) {
          moved = true
          const [a, b] = [...pointers.values()],
            distance = Math.hypot(a.x - b.x, a.y - b.y)
          if (lastPinch)
            zoom = Math.max(15, Math.min(800, (zoom * distance) / lastPinch))
          lastPinch = distance
        } else {
          panX += e.clientX - previous.x
          panY += e.clientY - previous.y
        }
        draw()
      }
      const up = (e: PointerEvent) => {
        if (!moved && pointers.size === 1 && e.type !== 'pointercancel') {
          const rect = app.canvas.getBoundingClientRect(),
            x = e.clientX - rect.left,
            y = e.clientY - rect.top
          const hits = current.current.objects
            .map((o) => {
              const p = screen(o.position.x, o.position.y)
              return { id: o.id, d: Math.hypot(x - p.x, y - p.y) }
            })
            .sort((a, b) => a.d - b.d)
          if (hits[0]?.d < 28) current.current.onSelect(hits[0].id)
        }
        pointers.delete(e.pointerId)
        lastPinch = 0
      }
      const wheel = (e: WheelEvent) => {
        e.preventDefault()
        zoom = Math.max(15, Math.min(800, zoom * Math.exp(-e.deltaY * 0.001)))
        draw()
      }
      const lost = (e: Event) => {
        e.preventDefault()
        setError('Map graphics context lost. Switch views to reload.')
      }
      app.canvas.addEventListener('pointerdown', down)
      app.canvas.addEventListener('pointermove', move)
      app.canvas.addEventListener('pointerup', up)
      app.canvas.addEventListener('pointercancel', up)
      app.canvas.addEventListener('wheel', wheel, { passive: false })
      app.canvas.addEventListener('webglcontextlost', lost)
      // Four objects need redraws only on state/input/resize, not a permanent frame loop.
      const redraw = () => {
        draw()
      }
      const interval = window.setInterval(() => {
        if (!document.hidden) redraw()
      }, 250)
      cleanup = () => {
        clearInterval(interval)
        app.canvas.removeEventListener('pointerdown', down)
        app.canvas.removeEventListener('pointermove', move)
        app.canvas.removeEventListener('pointerup', up)
        app.canvas.removeEventListener('pointercancel', up)
        app.canvas.removeEventListener('wheel', wheel)
        app.canvas.removeEventListener('webglcontextlost', lost)
        app.destroy(true, { children: true })
      }
      redraw()
    })().catch(() => {
      if (!disposed)
        setError('2D graphics unavailable. Use the object list or try 3D.')
    })
    return () => {
      disposed = true
      cleanup()
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
      <div className="camera-controls" aria-label="2D camera controls">
        {[
          ['in', '+'],
          ['out', '−'],
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
