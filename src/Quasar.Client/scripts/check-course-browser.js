// Playwright CLI run-code --filename, against a disposable host with a fresh Solar System campaign.
// The desktop phase commits an order. Never run this against the owner's published host.
// oxlint-disable-next-line no-unused-expressions -- CLI evaluates this function.
async (page) => {
  const assert = (condition, message) => {
    if (!condition) throw new Error(message)
  }
  const read = () => page.evaluate(async () => (await fetch('/api/lab')).json())
  const same = (a, b) => JSON.stringify(a) === JSON.stringify(b)
  const button = (name) => page.getByRole('button', { name, exact: true })
  const panel = page.getByRole('region', { name: 'Course plotting' })
  const destination = page.getByLabel('Course destination')
  const issue = button('Issue Order')
  const arrival = page.getByLabel('Arrival condition')
  const route = (name) => panel.getByRole('button', { name: new RegExp('^' + name) })
  const errors = []
  page.on('pageerror', (error) => errors.push(error.message))

  await page.getByText('HOST CONNECTED', { exact: true }).waitFor()
  const before = await read()
  assert(!before.world.trajectory, 'Start with a fresh disposable campaign')
  await button('Plot Course').click()

  const choose = async (id, select) => {
    const response = page.waitForResponse((r) => {
      const url = new URL(r.url())
      return url.pathname === '/api/lab/course-preview' &&
        url.searchParams.get('mode') === 'auto' &&
        url.searchParams.get('destinationId') === id
    })
    await select()
    assert(await issue.isDisabled(), 'Old preview must clear as soon as the destination changes')
    const completed = await response
    assert(completed.ok(), 'Automatic preview should succeed')
    return completed.json()
  }

  const jupiter = await choose('planet-jupiter', () => destination.selectOption('planet-jupiter'))
  assert(jupiter.fastest === null, 'Unsupported Jupiter route remains infeasible')
  await panel.getByText(/No executable|Insufficient propellant|gravity|boundary/).first().waitFor()
  assert(await issue.isDisabled(), 'Infeasible results cannot be ordered')

  const earth = await choose('planet-earth', () => destination.selectOption('planet-earth'))
  await route('Fastest').waitFor()
  assert(await route('Fastest').getAttribute('aria-pressed') === 'true', 'Fastest is selected by default')
  assert(earth.fastest.trajectory.brakingMarginKg > 0, 'Starting torch retains reaction mass after braking')
  await panel.getByText(/^Braking margin.*kg.*%/).waitFor()
  await route('Lowest maneuver cost').click()
  assert(await route('Lowest maneuver cost').getAttribute('aria-pressed') === 'true', 'Route selection switches')
  assert(earth.fastest.trajectory.travelSeconds < earth.lowestManeuverCost.trajectory.travelSeconds, 'Earth has a faster option')
  await panel.getByText('Estimated burn schedule').waitFor()
  await panel.getByText(/Braking margin/).waitFor()
  assert(await panel.getByRole('alert').count() === 0, 'Sufficient reserve clears the warning')
  assert(same(before, await read()), 'Preview and route selection leave canonical state untouched')

  await page.setViewportSize({ width: 393, height: 852 })
  await choose('planet-earth', () => arrival.selectOption('orbit'))
  await panel.getByText(/Cross-primary capture/).waitFor()
  assert(await issue.isDisabled(), 'Unsupported capture cannot be ordered')
  const reviewed = await choose('planet-earth', () => arrival.selectOption('approach'))
  await route('Fastest').waitFor()
  assert(await route('Fastest').getAttribute('aria-pressed') === 'true', 'New arrival condition defaults to fastest')
  assert(reviewed.fastest.trajectory.brakingMarginPercent > 1, 'Starting torch has a useful Earth approach reserve')
  assert(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), 'Portrait has no horizontal overflow')
  await panel.screenshot({ path: 'output/playwright/course-phone-panel.png' })

  await page.setViewportSize({ width: 1440, height: 900 })
  await button('3D').click()
  await page.locator('.space-label').first().waitFor()
  await route('Lowest maneuver cost').click()
  assert(await issue.isEnabled(), 'Earth route remains reviewable in 3D')
  await issue.click()
  await panel.waitFor({ state: 'hidden' })
  const committed = await read()
  assert(committed.world.ship.destinationId === 'planet-earth', 'Order targets Earth')
  assert(same(committed.world.trajectory, reviewed.lowestManeuverCost.trajectory), 'Order matches the selected preview')
  assert(committed.world.events.filter((e) => e.kind === 'order').length === 1, 'Only confirmation records an order')
  await page.getByLabel('Advance interval').selectOption('2592000')
  await page.getByLabel('Processing mode').selectOption('continuous')
  for (const kind of ['course-departure-cutoff', 'course-braking-start', 'course-approach-complete']) {
    await button('▶ Play').click()
    await page.waitForFunction(async (expected) => {
      const state = await (await fetch('/api/lab')).json()
      return !state.running && state.interrupt?.kind === expected
    }, kind)
  }
  await page.locator('.clock').getByText('Velocity-matched approach complete', { exact: true }).waitFor()
  await page.locator('.event-kind.course-approach-complete').filter({ hasText: /^Velocity-matched approach complete$/ }).waitFor()
  assert((await read()).world.ship.arrivalKind === 'approach', 'Completion preserves the approach outcome')
  assert(errors.length === 0, errors.join(', '))
  return 'AUTOMATIC_COURSE_BROWSER_OK'
}
