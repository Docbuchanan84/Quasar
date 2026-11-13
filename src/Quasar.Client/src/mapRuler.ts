const kilometresPerAu = 149_597_870.7
const maxBarWidth = 140
const formatDistance = new Intl.NumberFormat()

export function mapRuler(kilometresPerPixel: number) {
  const maxKilometres = maxBarWidth * kilometresPerPixel
  if (!Number.isFinite(maxKilometres) || maxKilometres <= 0) return null

  const unitKilometres = maxKilometres >= kilometresPerAu ? kilometresPerAu : 1
  const maxDistance = maxKilometres / unitKilometres
  // Take the largest 1, 2 or 5 × 10ⁿ distance that fits in the available pixels.
  const magnitude = 10 ** Math.floor(Math.log10(maxDistance))
  const step = maxDistance >= 5 * magnitude ? 5 : maxDistance >= 2 * magnitude ? 2 : 1
  const distance = step * magnitude
  const unit = unitKilometres === 1 ? 'km' : 'AU'
  return {
    label: `${formatDistance.format(distance)} ${unit}`,
    width: distance * unitKilometres / kilometresPerPixel,
  }
}
