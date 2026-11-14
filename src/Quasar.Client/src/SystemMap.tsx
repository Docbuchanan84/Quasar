import { useEffect, useRef, useState } from 'react'
import { Application, Container, Graphics, Text } from 'pixi.js'
import { MapGesture, wheelPixels } from './mapGestures'
import { courseColor, objectColor, type MapProps } from './lab'
import { focusRadius, mapScale, orbitPath, showObject } from './mapCoordinates'
import MapScaleBar from './MapScaleBar'

export default function SystemMap(props: MapProps) {
  const host = useRef<HTMLDivElement>(null)
  const current = useRef(props)
  const controls = useRef<(action: string) => void>(() => {})
  const redraw = useRef<() => void>(() => {})
  const [error, setError] = useState('')
  const [kilometresPerPixel, setKilometresPerPixel] = useState(0)
  useEffect(() => {
    current.current = props
    redraw.current()
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
        // An older StrictMode/lazy initialization can finish after its replacement.
        // Boolean true also releases Pixi's global pools, invalidating the active map's batches.
        app.destroy({ removeView: true, releaseGlobalResources: false }, { children: true })
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
        panY = 0
      let lastZoom = 0
      let focusedId: string | null = null
      let origin = { x: 0, y: 0, z: 0 }
      const lines = new Graphics()
      scene.addChild(lines)
      const symbols = new Map<string, { marker: Graphics; label: Text }>()
      const gesture = new MapGesture()
      const pointers = gesture.pointers
      const clampZoom = (value: number) => Math.max(.008, Math.min(20000, value))
      // Keep the world point beneath the cursor/finger midpoint stationary.
      const zoomAt = (value: number, clientX: number, clientY: number) => {
        const rect = app.canvas.getBoundingClientRect()
        const x = clientX - rect.left - app.screen.width / 2
        const y = clientY - rect.top - app.screen.height / 2
        const ratio = clampZoom(value) / zoom
        panX = x - (x - panX) * ratio
        panY = y - (y - panY) * ratio
        zoom = clampZoom(value)
      }
      const screen = (x: number, y: number) => ({
        x: app.screen.width / 2 + panX + (x - origin.x) * mapScale * zoom,
        y: app.screen.height / 2 + panY - (y - origin.y) * mapScale * zoom,
      })
      let catalogueRoot: string | undefined
      function draw() {
        const { objects, selectedId, courses, references } = current.current
        const root = objects.find((o) => o.kind === 'star')?.id
        if (root !== catalogueRoot) {
          catalogueRoot = root
          controls.current('reset')
          return
        }
        const focused = objects.find((o) => o.id === focusedId)
        if (focused) origin = focused.position
        if (zoom !== lastZoom) {
          lastZoom = zoom
          setKilometresPerPixel(1 / (mapScale * zoom))
        }
        const span = Math.min(app.screen.width, app.screen.height) / (mapScale * zoom)
        const visible = (object: MapProps['objects'][number]) => showObject(object, selectedId, focusedId, span)
        lines.clear()
        if (references) {
          if (!objects.some((o) => o.orbit)) for (let n = -6; n <= 6; n++) {
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
          for (const object of objects.filter(visible)) {
            const parent = objects.find((o) => o.id === object.parentId)?.position
            const path = orbitPath(object)
            if (!path.length) continue
            for (const [i, point] of path.entries()) {
              const q = screen(point.x + (parent?.x ?? 0), point.y + (parent?.y ?? 0))
              if (i === 0) lines.moveTo(q.x, q.y)
              else lines.lineTo(q.x, q.y)
            }
            lines.stroke({ color: '#345065', width: 1 })
          }
          lines.circle(origin.x, origin.y, 3).stroke({ color: '#526778' })
        }
        for (const course of courses) {
          const color = courseColor(course.kind)
          const points = course.trajectory.points
          for (const [i, sample] of points.entries()) {
            const p = screen(sample.positionKm.x, sample.positionKm.y)
            if (i === 0 || (course.kind === 'preview' && i % 2 === 0))
              lines.moveTo(p.x, p.y)
            else lines.lineTo(p.x, p.y)
          }
          lines.stroke({ color, width: course.kind === 'preview' ? 2.5 : 1.5 })
          const encounter = points.find((sample) => sample.atTicks === course.trajectory.flyby?.atTicks)
          if (encounter) {
            const p = screen(encounter.positionKm.x, encounter.positionKm.y)
            lines.circle(p.x, p.y, 9).stroke({ color, width: 2 })
          }
          const departure = points[0]?.positionKm,
            arrival = points.at(-1)?.positionKm
          if (departure && arrival) {
            const d = screen(departure.x, departure.y),
              a = screen(arrival.x, arrival.y)
            lines.rect(d.x - 5, d.y - 5, 10, 10).stroke({ color, width: 2 })
            lines
              .poly([
                a.x,
                a.y - 12,
                a.x + 12,
                a.y,
                a.x,
                a.y + 12,
                a.x - 12,
                a.y,
              ])
              .stroke({ color, width: 2 })
            lines.circle(a.x, a.y, 18).stroke({ color, width: 1 })
          }
        }
        const ids = new Set(objects.map((o) => o.id))
        for (const [id, symbol] of symbols) {
          if (!ids.has(id)) {
            symbol.marker.destroy()
            symbol.label.destroy()
            symbols.delete(id)
          }
        }
        for (const object of objects) {
          const p = screen(object.position.x, object.position.y),
            color = objectColor(object.kind)
          let symbol = symbols.get(object.id)
          if (!symbol) {
            symbol = {
              marker: new Graphics(),
              label: new Text({
                text: '',
                style: {
                  fontFamily: 'monospace',
                  fontSize: 11,
                  fill: color,
                  lineHeight: 15,
                },
              }),
            }
            symbols.set(object.id, symbol)
            scene.addChild(symbol.marker, symbol.label)
          }
          const { marker, label } = symbol
          marker.clear()
          marker.visible = visible(object) && p.x >= -25 && p.y >= -25 &&
            p.x <= app.screen.width + 25 && p.y <= app.screen.height + 25
          label.visible = marker.visible
          if (!marker.visible) continue
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
          else if (object.kind === 'station')
            marker.rect(p.x - 5, p.y - 5, 10, 10).stroke({ color, width: 2 })
          else
            marker
              .circle(p.x, p.y, object.kind === 'star' ? 10 : 7)
              .stroke({ color, width: 2 })
              .fill({ color, alpha: 0.2 })
          if (object.id === selectedId)
            marker.circle(p.x, p.y, 18).stroke({ color: '#ffffff', width: 1.5 })
          label.text = `${object.name}\nZ ${(object.position.z / 1000).toFixed(0)}k km`
          label.position.set(
            Math.max(4, Math.min(p.x + 15, app.screen.width - 115)),
            object.kind === 'planet' ? p.y - 40 : p.y + 16,
          )
        }
        app.render()
      }
      controls.current = (action) => {
        if (action === 'reset') {
          focusedId = null
          origin = { x: 0, y: 0, z: 0 }
          zoom = current.current.objects.some((o) => o.orbit)
            ? clampZoom(Math.min(element.clientWidth, element.clientHeight) / 10000)
            : Math.min(element.clientWidth / 6, 110)
          panX = panY = 0
        } else if (action === 'focus') {
          const selected = current.current.objects.find((o) => o.id === current.current.selectedId)
          if (selected) {
            focusedId = selected.id
            panX = panY = 0
            zoom = clampZoom(Math.min(element.clientWidth, element.clientHeight) /
              (2.4 * mapScale * focusRadius(current.current.objects, selected.id)))
          }
        } else if (action === 'in') zoom = clampZoom(zoom * 1.3)
        else if (action === 'out') zoom = clampZoom(zoom / 1.3)
        draw()
      }
      controls.current('reset')
      const down = (e: PointerEvent) => {
        if (e.button !== 0) return
        app.canvas.setPointerCapture(e.pointerId)
        gesture.down(e.pointerId, e.clientX, e.clientY)
      }
      const move = (e: PointerEvent) => {
        const before = [...pointers.values()]
        const previous = gesture.move(e.pointerId, e.clientX, e.clientY)
        if (!previous || !gesture.dragging) return
        focusedId = null
        if (pointers.size === 2) {
          const [a, b] = before,
            [c, d] = [...pointers.values()]
          const oldDistance = Math.hypot(a.x - b.x, a.y - b.y)
          const distance = Math.hypot(c.x - d.x, c.y - d.y)
          if (oldDistance > 0 && distance > 0)
            zoomAt(
              (zoom * distance) / oldDistance,
              (a.x + b.x) / 2,
              (a.y + b.y) / 2,
            )
          panX += (c.x + d.x - a.x - b.x) / 2
          panY += (c.y + d.y - a.y - b.y) / 2
        } else if (pointers.size === 1 && !gesture.multiTouch) {
          panX += e.clientX - previous.x
          panY += e.clientY - previous.y
        }
        draw()
      }
      const up = (e: PointerEvent) => {
        if (gesture.up(e.pointerId, e.clientX, e.clientY) && e.button === 0) {
          const rect = app.canvas.getBoundingClientRect(),
            x = e.clientX - rect.left,
            y = e.clientY - rect.top
          let nearest: string | null = null,
            distance = 28
          for (const object of current.current.objects) {
            if (!symbols.get(object.id)?.marker.visible) continue
            const p = screen(object.position.x, object.position.y)
            const d = Math.hypot(x - p.x, y - p.y)
            if (d < distance) {
              nearest = object.id
              distance = d
            }
          }
          if (nearest) current.current.onSelect(nearest)
        }
        if (app.canvas.hasPointerCapture(e.pointerId))
          app.canvas.releasePointerCapture(e.pointerId)
      }
      const cancel = (e: Event) => {
        if (
          e.type === 'lostpointercapture' &&
          !pointers.has((e as PointerEvent).pointerId)
        )
          return
        gesture.cancel()
      }
      const wheel = (e: WheelEvent) => {
        e.preventDefault()
        zoomAt(
          zoom * Math.exp(-wheelPixels(e, element.clientHeight) * 0.001),
          e.clientX,
          e.clientY,
        )
        draw()
      }
      const lost = (e: Event) => {
        e.preventDefault()
        setError('Map graphics context lost. Switch views to reload.')
      }
      app.canvas.addEventListener('pointerdown', down)
      app.canvas.addEventListener('pointermove', move)
      app.canvas.addEventListener('pointerup', up)
      app.canvas.addEventListener('pointercancel', cancel)
      app.canvas.addEventListener('lostpointercapture', cancel)
      window.addEventListener('blur', cancel)
      app.canvas.addEventListener('wheel', wheel, { passive: false })
      app.canvas.addEventListener('webglcontextlost', lost)
      // State and camera events redraw; ordinary idle time performs no object serialization or rebuilds.
      redraw.current = () => {
        if (!document.hidden) draw()
      }
      const resize = new ResizeObserver(() => {
        app.resize()
        redraw.current()
      })
      resize.observe(element)
      document.addEventListener('visibilitychange', redraw.current)
      const visibility = redraw.current
      cleanup = () => {
        redraw.current = () => {}
        resize.disconnect()
        document.removeEventListener('visibilitychange', visibility)
        app.canvas.removeEventListener('pointerdown', down)
        app.canvas.removeEventListener('pointermove', move)
        app.canvas.removeEventListener('pointerup', up)
        app.canvas.removeEventListener('pointercancel', cancel)
        app.canvas.removeEventListener('lostpointercapture', cancel)
        window.removeEventListener('blur', cancel)
        app.canvas.removeEventListener('wheel', wheel)
        app.canvas.removeEventListener('webglcontextlost', lost)
        app.destroy({ removeView: true, releaseGlobalResources: false }, { children: true })
      }
      redraw.current()
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
      <p className="map-gestures">
        Drag or swipe to pan · wheel or pinch to zoom
      </p>
      <div className="map-hud">
        {!error && <MapScaleBar kilometresPerPixel={kilometresPerPixel} />}
        <div className="camera-controls" aria-label="2D camera controls">
          {[
            ['in', '+'],
            ['out', '−'],
            ['reset', 'Reset View'],
            ['focus', 'Focus Selected'],
          ].map(([action, label]) => (
            <button
              key={action}
              aria-label={
                action === 'reset' || action === 'focus'
                  ? label
                  : action === 'in'
                    ? 'Zoom in'
                    : 'Zoom out'
              }
              onClick={() => controls.current(action)}
            >
              {label}
            </button>
          ))}
        </div>
      </div>
    </>
  )
}
