// Run only against a fresh disposable campaign. This scenario commits a Callisto order.
// oxlint-disable-next-line no-unused-expressions -- Playwright CLI evaluates this function.
async (page) => {
  const assert = (condition, message) => { if (!condition) throw new Error(message) }
  const button = name => page.getByRole('button', { name, exact: true })
  const panel = page.getByRole('region', { name: 'Course plotting' })
  const read = () => page.evaluate(async () => (await fetch('/api/lab')).json())
  const errors = []
  page.on('pageerror', error => errors.push(error.message))
  await page.getByText('HOST CONNECTED', { exact: true }).waitFor()
  const before = await read()
  await button('Plot Course').click()
  const initialResponse = page.waitForResponse(response => new URL(response.url()).searchParams.get('destinationId') === 'moon-callisto')
  const began = Date.now()
  await page.getByLabel('Course destination').selectOption('moon-callisto')
  await page.getByRole('slider', { name: 'Travel time', exact: true }).waitFor()
  const initial = await (await initialResponse).json()
  assert(Date.now() - began < 8000, 'Initial Callisto response stays inside the client timeout')
  assert(initial.fastest, 'Callisto has a verified route')
  assert(initial.fastest.trajectory.gravityTransitions.some(transition => transition.bodyId === 'planet-jupiter'), 'The computer plans Jupiter entry')
  await panel.getByRole('heading', { name: 'Automatic gravity transitions' }).waitFor()
  assert(JSON.stringify(before) === JSON.stringify(await read()), 'Planning does not alter the campaign')

  const reserveResponse = page.waitForResponse(response => new URL(response.url()).searchParams.get('reservePercent') === '95')
  const reserve = page.getByRole('slider', { name: 'Propellant reserve', exact: true })
  await reserve.press('End')
  for (let step = 0; step < 4; step++) await reserve.press('ArrowLeft')
  const protectedRoute = await (await reserveResponse).json()
  const protectedPreview = protectedRoute.tuned ?? protectedRoute.fastest
  assert(protectedPreview.trajectory.brakingMarginPercent >= 95, 'Travel time adjusts to protect the requested reserve')
  assert(protectedPreview.trajectory.travelSeconds > initial.fastest.trajectory.travelSeconds, 'The route slows down to spend less propellant')

  const reserveLock = page.waitForResponse(response => new URL(response.url()).searchParams.get('reservePercent') === '95')
  await button('Lock propellant reserve').click()
  await reserveLock

  const lockResponse = page.waitForResponse(response => new URL(response.url()).searchParams.get('lockFlightTime') === 'true')
  await button('Lock travel time').click()
  const locked = await (await lockResponse).json()
  assert(locked.tuned.trajectory.brakingMarginPercent >= 95, 'Locking holds the reviewed time and protected reserve')
  const impossibleResponse = page.waitForResponse(response => new URL(response.url()).searchParams.get('preferredFlightSeconds') === '1')
  await page.getByRole('slider', { name: 'Travel time', exact: true }).press('Home')
  assert(!(await (await impossibleResponse).json()).fastest, 'An impossible one-second trip cannot issue an order')
  await panel.getByText(/No verified course meets the locked travel time/).waitFor()
  assert(await button('Issue Order').isDisabled(), 'Conflicting locks disable confirmation')

  const unlockResponse = page.waitForResponse(response => {
    const url = new URL(response.url())
    return url.searchParams.get('reservePercent') === '95' && !url.searchParams.has('lockFlightTime')
  })
  await button('Unlock travel time').click()
  const unlocked = await (await unlockResponse).json()
  const reviewed = unlocked.tuned ?? unlocked.fastest
  assert(reviewed.trajectory.brakingMarginPercent >= 95, 'Unlocking time restores a viable protected route')
  await panel.getByText('Travel time adjusted to the nearest executable route found.', { exact: true }).waitFor()
  await page.setViewportSize({ width: 393, height: 852 })
  assert(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), 'Course controls fit portrait width')
  await panel.screenshot({ path: 'output/playwright/callisto-course-phone.png' })
  await page.setViewportSize({ width: 1440, height: 900 })
  await button('3D').click()
  await page.locator('.space-label').first().waitFor()
  await button('Issue Order').click()
  await panel.waitFor({ state: 'hidden' })
  const committed = await read()
  assert(JSON.stringify(committed.world.trajectory) === JSON.stringify(reviewed.trajectory), 'Confirmation executes the reviewed course')
  assert(committed.world.ship.propulsion.availablePropellantKg === 20000, 'Fuel is consumed only as time advances')
  assert(errors.length === 0, errors.join(', '))
  return 'CROSS_PRIMARY_LINKED_BROWSER_OK'
}
