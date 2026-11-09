;async (page) => {
  const button = (name) => page.getByRole('button', { name, exact: true })
  const text = (value) => page.getByText(value, { exact: true })
  const clock = (value) =>
    page.locator('.clock strong').filter({ hasText: value })
  const click = (name) => button(name).click()
  const show = (locator) => locator.waitFor({ state: 'visible' })
  const hide = (locator) => locator.waitFor({ state: 'hidden' })
  const save = async (label) => {
    await click('Named Save')
    await page.getByLabel('Save name').fill(label)
    await click('Save Checkpoint')
    await show(text(`Saved “${label}”.`))
  }
  const load = async (label) => {
    await click(label)
    await show(text('Replace active campaign?'))
    await click('Replace and Load')
  }
  const noOverflow = async (width, height) => {
    await page.setViewportSize({ width, height })
    if (
      await page.evaluate(
        () => document.documentElement.scrollWidth > window.innerWidth,
      )
    )
      throw new Error(`Horizontal overflow at ${width} × ${height}`)
  }
  const lab = async () => {
    const response = await page.request.get(
      new URL('/api/lab', page.url()).href,
    )
    if (!response.ok()) throw new Error(`Lab returned ${response.status()}`)
    return response.json()
  }
  const phase = new URL(page.url()).hash.slice(1)

  if (phase === 'create') {
    await show(text('No saved campaigns found.'))
    await hide(button('▶ Play'))
    await button('New Game').tap()
    await page.getByLabel('Campaign name').fill('Pathfinder test')
    await button('Create Campaign').tap()
    await show(text('Campaign created. Time is paused.'))
    await show(page.locator('.map-viewport canvas'))
    await show(text('Autosave off'))
    await click('Plot Course')
    await page.getByLabel('Course destination').selectOption('planet-iris')
    await show(text('PREVIEW · NOT ORDERED'))
    await click('Issue Order')
    for (const time of ['01:00:00', '02:00:00', '03:00:00']) {
      await click('▶ Play')
      await show(clock(time))
    }
    await show(text('Unsaved progress'))
    await save('Midflight')
    await show(text('No unsaved progress'))
    await click('Load Game')
    await show(button('Load Midflight #2'))
    await show(page.getByText('Recovery checkpoint', { exact: true }))
    await noOverflow(320, 690)
    await noOverflow(874, 402)
    await noOverflow(402, 874)
    if (
      !(await button('Load Midflight #2').evaluate((element) => {
        const rect = element.getBoundingClientRect()
        return rect.width >= 44 && rect.height >= 44
      }))
    )
      throw new Error('Load touch target is smaller than 44 × 44 CSS pixels')
  } else if (phase === 'resume') {
    await show(clock('03:00:00'))
    await show(button('▶ Play'))
    await click('▶ Play')
    await show(clock('04:00:00'))
    await click('New Game')
    await page.getByLabel('Campaign name').fill('Do not create')
    await click('Create Campaign')
    await show(text('Replace active campaign?'))
    await click('Keep Playing')
    await show(clock('04:00:00'))
    await click('Close')
    await click('Plot Course')
    await page.getByLabel('Course destination').selectOption('planet-iris')
    await show(text('PREVIEW · NOT ORDERED'))
    await click('Load Game')
    await load('Continue')
    await show(text('Campaign loaded. Time is paused.'))
    await hide(text('PREVIEW · NOT ORDERED'))
    await show(clock('03:00:00'))
    await show(button('▶ Play'))
    await show(text('Destination: Iris'))
  } else if (phase === 'arrival') {
    await noOverflow(1440, 900)
    await show(clock('03:00:00'))
    await click('3D')
    await show(page.locator('.map-viewport canvas'))
    await click('2D')
    await show(page.locator('.map-viewport canvas'))
    await click('Cancel committed course')
    await hide(button('Cancel committed course'))
    await click('Save Game')
    await show(text('Saved “Manual save”.'))
    await click('Load Game')
    await load('Load Midflight #2')
    await show(button('Cancel committed course'))
    await page.getByLabel('Advance interval').selectOption('86400')
    await click('▶ Play')
    await show(clock('06:00:00'))
    await show(page.locator('.event-kind.arrival'))
    await save('Arrival')
    await click('Load Game')
    await load('Load Arrival #4')
    await show(page.locator('.event-kind.arrival'))
    await show(clock('06:00:00'))
    await save('Corrupt test checkpoint')
  } else if (phase === 'recover') {
    await show(
      text(
        'Recovered an earlier checkpoint. Unreadable newer saves skipped: 1.',
      ),
    )
    await show(clock('06:00:00'))
    const before = await lab()
    await click('Load Game')
    await load('Load Corrupt test checkpoint #5')
    await show(text('Campaign or checkpoint data is corrupt or incompatible.'))
    const after = await lab()
    if (JSON.stringify(before) !== JSON.stringify(after))
      throw new Error('Failed load changed the active session or world')
    await load('Continue')
    await show(text('Campaign loaded. Time is paused.'))
    await show(clock('06:00:00'))
    await click('Load Game')
    await load('Load Manual save #3')
    await show(clock('03:00:00'))
    await hide(button('Cancel committed course'))
    await show(page.locator('.event-kind').filter({ hasText: 'cancel' }))
  } else if (phase === 'uncertain') {
    const routePattern = '**/api/campaigns/save-named'
    let attempts = 0
    await page.route(routePattern, async (route) => {
      attempts++
      const response = await route.fetch()
      if (response.status() !== 201)
        throw new Error('Expected real durable save')
      await route.abort('failed')
    })
    await click('Named Save')
    await page.getByLabel('Save name').fill('Lost response')
    await click('Save Checkpoint')
    await show(page.getByText(/campaign operation outcome uncertain/i))
    await hide(text('Saved “Lost response”.'))
    await click('Load Game')
    await show(button('Load Lost response #6'))
    if (attempts !== 1) throw new Error('The save request was repeated')
    await page.unroute(routePattern)
  } else if (phase === 'incompatible') {
    await show(page.getByText(/last played campaign could not be recovered/i))
    await show(text('Campaign metadata could not be read.'))
    await hide(text('No saved campaigns found.'))
    await hide(button('▶ Play'))
    await click('Continue')
    await show(text('Campaign or checkpoint data is corrupt or incompatible.'))
    await show(button('New Game'))
  } else {
    throw new Error(`Unknown campaign test phase: ${phase}`)
  }
  return `CAMPAIGN_BROWSER_OK ${phase}`
}
