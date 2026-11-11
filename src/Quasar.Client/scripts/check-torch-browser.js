// Disposable same-origin host with a fresh campaign. The scenario uses the starting torch and orders the test ship.
// oxlint-disable-next-line no-unused-expressions -- Playwright CLI evaluates this function.
async (page) => {
  const assert = (condition, message) => { if (!condition) throw new Error(message) }
  const button = (name) => page.getByRole('button', { name, exact: true })
  const panel = page.getByRole('region', { name: 'Course plotting' })
  const read = () => page.evaluate(async () => (await fetch('/api/lab')).json())
  const errors = []
  page.on('pageerror', (error) => errors.push(error.message))
  await page.getByText('HOST CONNECTED', { exact: true }).waitFor()
  assert((await read()).world.ship.propulsion.availablePropellantKg === 20000, 'New games start with a fueled torch without a refit')
  await page.getByLabel('Advance interval').selectOption('3600')
  for (const at of ['36000000000', '72000000000']) {
    await button('▶ Play').click()
    await page.waitForFunction(async (ticks) => {
      const snapshot = await (await fetch('/api/lab')).json()
      return !snapshot.running && snapshot.world.elapsedTicks === ticks
    }, at)
  }
  await button('Plot Course').click()
  const firstResponse = page.waitForResponse(response => new URL(response.url()).pathname === '/api/lab/course-preview')
  await page.getByLabel('Course destination').selectOption('planet-mars')
  const initialResponse = await firstResponse
  assert(!new URL(initialResponse.url()).searchParams.has('maneuverBudget'), 'Planning uses the spacecraft capability')
  const initial = await initialResponse.json()
  assert(initial.fastest.trajectory.requiredDeltaVKmPerSecond > 25, 'Torch course exceeds the obsolete test budget')
  await panel.getByText('Tune course', { exact: true }).waitFor()

  const locked = page.waitForResponse(response => new URL(response.url()).searchParams.get('lockFlightTime') === 'true')
  await button('Lock travel time').click()
  assert((await locked).ok(), 'Locking a verified duration succeeds')
  const conflict = page.waitForResponse(response => {
    const url = new URL(response.url())
    return url.searchParams.get('reservePercent') === '95' && url.searchParams.get('lockFlightTime') === 'true'
  })
  const reserve = page.getByRole('slider', { name: 'Propellant reserve', exact: true })
  await reserve.press('End')
  for (let step = 0; step < 4; step++) await reserve.press('ArrowLeft')
  await conflict
  const reserveLocked = page.waitForResponse(response => new URL(response.url()).searchParams.get('reservePercent') === '95')
  await button('Lock propellant reserve').click()
  await reserveLocked
  const exactSecond = page.waitForResponse(response => new URL(response.url()).searchParams.get('preferredFlightSeconds') === '1')
  await page.getByRole('slider', { name: 'Travel time', exact: true }).press('Home')
  const impossible = await (await exactSecond).json()
  assert(!impossible.fastest, 'Locked time cannot silently spend the protected reserve')
  await panel.getByText(/No verified course meets the locked travel time/).waitFor()
  assert(await button('Issue Order').isDisabled(), 'Conflicting constraints prevent confirmation')

  const adjustable = page.waitForResponse(response => {
    const url = new URL(response.url())
    return url.searchParams.get('reservePercent') === '95' && !url.searchParams.has('lockFlightTime')
  })
  await button('Unlock travel time').click()
  const adjusted = await (await adjustable).json()
  assert(adjusted.tuned.trajectory.brakingMarginPercent >= 95, 'Unlocked time adjusts to preserve 95% reserve')
  assert(adjusted.tuned.trajectory.travelSeconds > initial.fastest.trajectory.travelSeconds, 'The fuel constraint makes this route slower')
  await button('Issue Order').waitFor({ state: 'visible' })
  await panel.getByText('Travel time adjusted to the nearest executable route found.', { exact: true }).waitFor()

  const snap = page.waitForResponse(response => new URL(response.url()).searchParams.get('preferredFlightSeconds') === '1')
  await page.getByRole('slider', { name: 'Travel time', exact: true }).press('Home')
  const nearest = await (await snap).json()
  assert(nearest.tuned.trajectory.brakingMarginPercent >= 95, 'Time adjustment respects the locked reserve')
  await panel.getByText('Travel time adjusted to the nearest executable route found.', { exact: true }).waitFor()
  await page.setViewportSize({ width: 393, height: 852 })
  assert(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), 'Linked controls fit portrait width')
  await panel.screenshot({ path: 'output/playwright/torch-linked-phone.png' })
  await page.setViewportSize({ width: 1440, height: 900 })
  await button('3D').click()
  await page.locator('.space-label').first().waitFor()
  await button('Issue Order').click()
  await panel.waitFor({ state: 'hidden' })
  const committed = await read()
  assert(JSON.stringify(committed.world.trajectory) === JSON.stringify(nearest.tuned.trajectory), 'Order commits the tuned trajectory exactly')
  assert(committed.world.ship.propulsion.availablePropellantKg === 20000, 'Preview and commit do not consume propellant before time advances')
  assert(errors.length === 0, errors.join(', '))
  return 'TORCH_LINKED_COURSE_BROWSER_OK'
}
