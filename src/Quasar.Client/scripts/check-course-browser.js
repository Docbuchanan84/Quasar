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
  const objects = page.getByRole('navigation', { name: 'System objects' })
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
  await route('Fastest').waitFor()
  assert(await route('Fastest').getAttribute('aria-pressed') === 'true', 'Fastest is selected by default')
  await route('Lowest maneuver cost').click()
  assert(await route('Lowest maneuver cost').getAttribute('aria-pressed') === 'true', 'Route selection switches')
  assert(jupiter.fastest.trajectory.travelSeconds < jupiter.lowestManeuverCost.trajectory.travelSeconds, 'Jupiter has a faster option')
  assert(same(before, await read()), 'Preview and route selection leave canonical state untouched')

  await page.setViewportSize({ width: 393, height: 852 })
  const moon = await choose('moon-moon', () => objects.getByRole('button', { name: /^Moon$/ }).click())
  await route('Fastest').waitFor()
  assert(await destination.inputValue() === 'moon-moon', 'Object list selects a moon')
  assert(await route('Fastest').getAttribute('aria-pressed') === 'true', 'New destination defaults to fastest')
  assert(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), 'Portrait has no horizontal overflow')
  await panel.screenshot({ path: 'output/playwright/course-phone-panel.png' })

  await page.setViewportSize({ width: 1440, height: 900 })
  await button('3D').click()
  await page.locator('.space-label').first().waitFor()
  await route('Lowest maneuver cost').click()
  assert(await issue.isEnabled(), 'Moon route remains reviewable in 3D')
  await issue.click()
  await panel.waitFor({ state: 'hidden' })
  const committed = await read()
  assert(committed.world.ship.destinationId === 'moon-moon', 'Order targets the Moon')
  assert(same(committed.world.trajectory, moon.lowestManeuverCost.trajectory), 'Order matches the selected preview')
  assert(committed.world.events.filter((e) => e.kind === 'order').length === 1, 'Only confirmation records an order')
  assert(errors.length === 0, errors.join(', '))
  return 'AUTOMATIC_COURSE_BROWSER_OK'
}
