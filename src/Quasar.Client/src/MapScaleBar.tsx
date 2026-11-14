import { mapRuler } from './mapRuler'

export default function MapScaleBar({ kilometresPerPixel, atFocusDepth = false }: {
  kilometresPerPixel: number
  atFocusDepth?: boolean
}) {
  const ruler = mapRuler(kilometresPerPixel)
  if (!ruler) return null

  return (
    <div className="map-scale" role="group" aria-label="Map scale">
      <span>{ruler.label}{atFocusDepth && ' at focus depth'}</span>
      <span className="map-scale-line" style={{ width: ruler.width }} aria-hidden="true" />
    </div>
  )
}
