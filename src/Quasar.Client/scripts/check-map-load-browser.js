// Synthetic desktop load check via Playwright CLI; no orders or physical-state changes.
// Requires Vite :5174 and the disposable host :5094. Fixtures exist only in intercepted responses.
// oxlint-disable-next-line no-unused-expressions -- CLI evaluates this function.
async (page) => {
  const errors = []
  let planningRequests = 0
  page.on('pageerror', (e) => errors.push(e.message))
  await page.unroute('**/api/**')
  await page.route('**/api/**', async (route) => {
    const url = new URL(route.request().url())
    if (url.pathname.includes('preview')) planningRequests++
    if (route.request().method() !== 'GET')
      throw new Error('Load check must never issue commands')
    const response = await route.fetch({
      url: 'http://127.0.0.1:5094' + url.pathname + url.search,
    })
    if (url.pathname !== '/api/lab') return route.fulfill({ response })
    const snapshot = await response.json()
    const original = snapshot.world.objects.find((o) => o.kind === 'spacecraft')
    snapshot.world.objects.push(
      ...Array.from({ length: 996 }, (_, i) => ({
        ...original,
        id: `load-fixture-${i}`,
        name: `Fixture ${i}`,
        position: {
          x: ((i % 40) - 20) * 100000,
          y: (Math.floor(i / 40) - 12) * 100000,
          z: (i % 7) * 100000,
        },
      })),
    )
    await route.fulfill({ response, json: snapshot })
  })
  await page.addInitScript(() => {
    const apps = []
    globalThis.__PIXI_APP_INIT__ = (app) => apps.push(app)
    // StrictMode may finish disposing an earlier async Pixi initialization later.
    Object.defineProperty(globalThis, 'testPixi', {
      configurable: true,
      get: () => apps.find((app) => app.stage && app.canvas?.isConnected),
    })
  })
  await page.setViewportSize({ width: 1280, height: 950 })
  await page.goto('http://127.0.0.1:5174')
  await page.waitForFunction(
    () =>
      globalThis.testPixi?.stage?.children[0]?.children.filter((c) => c.text)
        .length === 1000,
  )
  await page.evaluate(() => {
    globalThis.retainedLabel =
      globalThis.testPixi.stage.children[0].children.find((c) => c.text)
  })
  let start = Date.now()
  await page.getByRole('button', { name: 'Zoom in', exact: true }).click()
  await page.getByRole('button', { name: 'Reset View', exact: true }).click()
  const pixiInteractionMs = Date.now() - start
  if (
    !(await page.evaluate(() =>
      globalThis.testPixi.stage.children[0].children.includes(
        globalThis.retainedLabel,
      ),
    ))
  )
    throw new Error('Camera redraw replaced retained labels')
  start = Date.now()
  await page.getByRole('button', { name: '3D', exact: true }).click()
  await page.waitForFunction(
    () => document.querySelectorAll('.space-label').length === 1000,
  )
  const babylonLoadMs = Date.now() - start
  start = Date.now()
  await page.getByRole('button', { name: 'Zoom in', exact: true }).click()
  await page.getByRole('button', { name: 'Reset View', exact: true }).click()
  const babylonInteractionMs = Date.now() - start
  if (errors.length || planningRequests)
    throw new Error(JSON.stringify({ errors, planningRequests }))
  return {
    objects: 1000,
    pixiInteractionMs,
    babylonLoadMs,
    babylonInteractionMs,
    planningRequests,
    errors,
    scope:
      'One synthetic desktop automation sample; not a sustained frame-rate, capacity or phone measurement.',
  }
}
