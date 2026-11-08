// Playwright CLI run-code --filename, with Vite :5174 and a DISPOSABLE host :5094.
// This changes the isolated world's state. Never target the owner's published host.
// oxlint-disable-next-line no-unused-expressions -- CLI evaluates this function.
async (page) => {
  const results = [],
    errors = [],
    requests = []
  const check = (ok, message) => {
    if (!ok) throw new Error(message)
    results.push(message)
  }
  const read = () => page.evaluate(async () => (await fetch('/api/lab')).json())
  const same = (a, b) => JSON.stringify(a) === JSON.stringify(b)
  const button = (name) => page.getByRole('button', { name, exact: true })
  const issue = () =>
    button('Issue Order')
      .waitFor({ state: 'visible' })
      .then(async () => {
        await page.waitForFunction(
          () =>
            ![...document.querySelectorAll('button')].find(
              (b) => b.textContent === 'Issue Order',
            )?.disabled,
        )
      })
  await page.unroute('**/api/**')
  await page.route('**/api/**', async (route) => {
    const u = new URL(route.request().url())
    requests.push(u.pathname)
    const response = await route.fetch({
      url: 'http://127.0.0.1:5094' + u.pathname + u.search,
      headers: {
        ...route.request().headers(),
        origin: 'http://127.0.0.1:5094',
      },
    })
    await route.fulfill({ response })
  })
  page.on('pageerror', (error) => errors.push(error.message))
  await page.addInitScript(() => {
    const apps = []
    globalThis.__PIXI_APP_INIT__ = (app) => apps.push(app)
    // StrictMode may finish disposing an earlier async Pixi initialization later.
    Object.defineProperty(globalThis, 'testPixi', {
      configurable: true,
      get: () => apps.find((app) => app.stage && app.canvas?.isConnected),
    })
  })
  await page.goto('http://127.0.0.1:5174')
  await page.locator('canvas').waitFor()
  await page.waitForFunction(
    () => globalThis.testPixi?.stage?.children[0]?.children.length > 0,
  )
  await page.getByText('HOST CONNECTED', { exact: true }).waitFor()
  const before = await read()
  await button('Plot Course').click()
  // Map click chooses the destination, using current renderer geometry.
  const iris = await page.evaluate(() => {
    const app = globalThis.testPixi
    const label = app.stage.children[0].children.find((c) =>
      c.text?.startsWith('Iris\n'),
    )
    const rect = app.canvas.getBoundingClientRect()
    return { x: rect.x + label.x - 15, y: rect.y + label.y + 40 }
  })
  await page.mouse.click(iris.x, iris.y)
  await issue()
  check(
    (await page.getByLabel('Course destination').inputValue()) ===
      'planet-iris',
    '2D map selection chooses plot destination',
  )
  check(
    same(before, await read()),
    'Preview leaves clock, revision, position, velocity, order and history unchanged',
  )
  const requestCount = requests.filter((p) =>
    p.endsWith('/course-preview'),
  ).length
  const zoom = button('Zoom in')
  await zoom.click()
  await button('Reset View').click()
  await page.waitForTimeout(600)
  check(
    requests.filter((p) => p.endsWith('/course-preview')).length ===
      requestCount,
    'Camera operations do not recalculate trajectories',
  )
  await page.screenshot({ path: 'output/playwright/course-2d.png' })
  await button('Cancel preview').click()
  check(
    same(before, await read()),
    'Cancel preview leaves canonical state untouched',
  )
  await button('Pathfinder').click()
  await button('Plot Course').click()
  await page.getByLabel('Course destination').selectOption('planet-iris')
  await issue()
  await page.getByLabel('Flight duration (seconds)').fill('0')
  check(
    await button('Issue Order').isDisabled(),
    'Invalid adjustment immediately disables confirmation',
  )
  await page.getByLabel('Flight duration (seconds)').fill('28800')
  await issue()
  const planned = await page.evaluate(async () =>
    (
      await fetch(
        '/api/lab/course-preview?destinationId=planet-iris&flightSeconds=28800',
      )
    ).json(),
  )
  check(
    (
      await page.getByRole('region', { name: 'Course plotting' }).textContent()
    ).includes('28,800'),
    'Duration adjustment recalculates displayed course',
  )
  await button('3D').click()
  await page.locator('.space-label').first().waitFor()
  await page.evaluate(async () => {
    const { Engine } = await import(
      '/node_modules/.vite/deps/@babylonjs_core_Engines_engine.js'
    )
    globalThis.testScene = Engine.LastCreatedScene
  })
  const sceneData = () =>
    page.evaluate(() => {
      const scene = globalThis.testScene
      return Object.fromEntries(
        scene.meshes.map((m) => [m.name, m.position.asArray()]),
      )
    })
  let meshes = await sceneData()
  check(
    !!meshes['preview-trajectory'] &&
      !!meshes['preview-departure'] &&
      !!meshes['preview-arrival-future-destination'],
    '3D displays sampled preview and departure/future-arrival markers',
  )
  for (const object of before.world.objects) {
    check(
      meshes[object.id].every(
        (v, i) =>
          Math.abs(v - object.position[['x', 'y', 'z'][i]] / 1e6) < 1e-10,
      ),
      `3D ${object.name} uses authoritative XYZ`,
    )
  }
  const box = await page.locator('canvas').boundingBox()
  await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2)
  await page.mouse.down()
  await page.mouse.move(
    box.x + box.width / 2 + 60,
    box.y + box.height / 2 + 30,
    { steps: 10 },
  )
  await page.mouse.up()
  check(
    same(before, await read()),
    '3D camera movement changes no physical state',
  )
  await button('Reset View').click()
  await page.screenshot({ path: 'output/playwright/course-3d.png' })
  await button('Issue Order').click()
  await page
    .getByRole('region', { name: 'Course plotting' })
    .waitFor({ state: 'hidden' })
  const committed = await read()
  check(
    same(committed.world.trajectory, planned.trajectory),
    'Issue Order commits the calculated preview samples and delta-v',
  )
  check(
    committed.world.events.filter((e) => e.kind === 'order').length === 1,
    'Only explicit confirmation records an order',
  )
  await page.waitForTimeout(200)
  meshes = await sceneData()
  check(
    !!meshes['committed-trajectory'] && !meshes['preview-trajectory'],
    'Confirmation replaces preview styling with committed styling',
  )
  // Preview a replacement while retaining the current committed course.
  await button('Pathfinder').click()
  await button('Plot Course').click()
  await button('Nereid').click()
  await issue()
  await page.waitForTimeout(200)
  meshes = await sceneData()
  check(
    !!meshes['committed-trajectory'] && !!meshes['preview-trajectory'],
    'Committed and replacement-preview courses coexist distinctly',
  )
  await button('Cancel preview').click()
  check(
    same(committed, await read()),
    'Cancelling a replacement preserves the committed course',
  )
  await page
    .getByLabel('Advance interval', { exact: true })
    .selectOption('2592000')
  await button('▶ Play').click()
  await page.waitForFunction(() =>
    document.querySelector('.run-status')?.textContent.includes('Arrival'),
  )
  const arrived = await read()
  check(
    arrived.world.elapsedTicks === Number(planned.trajectory.arrivalTicks),
    'Large time advance stops at the exact previewed arrival',
  )
  check(
    arrived.world.trajectory === null,
    'Arrival removes completed course visualization',
  )
  await button('Pathfinder').click()
  await button('2D').click()
  await page.locator('canvas').waitFor()
  const text2d = await page.getByTestId('position-x').textContent()
  await button('3D').click()
  await page.locator('.space-label').first().waitFor()
  check(
    text2d === (await page.getByTestId('position-x').textContent()),
    '2D and 3D selection inspectors agree after time advancement',
  )
  // Responsive emulation is a layout check, not physical-iPhone qualification.
  await page.setViewportSize({ width: 393, height: 852 })
  await button('Plot Course').click()
  await page.getByLabel('Course destination').selectOption('planet-nereid')
  await issue()
  check(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= innerWidth,
    ),
    'Phone-width layout has no horizontal overflow',
  )
  await page
    .getByRole('region', { name: 'Course plotting' })
    .screenshot({ path: 'output/playwright/course-phone-panel.png' })
  await button('Cancel preview').click()
  check(errors.length === 0, `No browser exceptions: ${errors.join('; ')}`)
  return results
}
