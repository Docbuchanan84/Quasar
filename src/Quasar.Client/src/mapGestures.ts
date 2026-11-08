type Point = { x: number; y: number }

// One gesture lasts until ALL pointers lift. A pinch must never end as a tap
// or turn into a one-finger orbit when the first finger is lifted.
export class MapGesture {
  readonly pointers = new Map<number, Point>()
  private start: Point = { x: 0, y: 0 }
  dragging = false
  multiTouch = false

  down(id: number, x: number, y: number) {
    if (!this.pointers.size) {
      this.start = { x, y }
      this.dragging = false
      this.multiTouch = false
    }
    this.pointers.set(id, { x, y })
    if (this.pointers.size > 1) this.multiTouch = this.dragging = true
  }

  move(id: number, x: number, y: number) {
    const previous = this.pointers.get(id)
    if (!previous) return
    this.pointers.set(id, { x, y })
    if (Math.hypot(x - this.start.x, y - this.start.y) > 6) this.dragging = true
    return previous
  }

  up(id: number, x: number, y: number) {
    const tracked = this.pointers.has(id)
    this.move(id, x, y)
    const tap = tracked && !this.dragging && !this.multiTouch
    this.pointers.delete(id)
    return tap
  }

  cancel() {
    this.pointers.clear()
    this.dragging = true
  }
}

export function wheelPixels(event: WheelEvent, height: number) {
  return (
    event.deltaY *
    (event.deltaMode === 1 ? 16 : event.deltaMode === 2 ? height : 1)
  )
}
