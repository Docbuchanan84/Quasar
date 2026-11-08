// Run with Playwright CLI run-code --filename from the repository root.
// Requires Vite :5174 and an ISOLATED System Lab host :5094. Never use a live game.
// oxlint-disable-next-line no-unused-expressions -- Playwright CLI evaluates this function.
async (page) => {
  const results = []
  const check = (condition, message) => {
    if (!condition) throw new Error(message)
    results.push(message)
  }
  const close = (a, b, epsilon = 0.01) => Math.abs(a - b) < epsilon
  const wait = () => page.waitForTimeout(180)
  await page.unroute('**/api/**')
  await page.route('**/api/**', async (route) => {
    const url = new URL(route.request().url())
    const response = await route.fetch({
      url: 'http://127.0.0.1:5094' + url.pathname + url.search,
      headers: {
        ...route.request().headers(),
        origin: 'http://127.0.0.1:5094',
      },
    })
    await route.fulfill({ response })
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
  const errors = []
  page.on('pageerror', (error) => errors.push(error.message))
  await page.setViewportSize({ width: 1280, height: 950 })
  await page.goto('http://127.0.0.1:5174')
  await page.locator('canvas').waitFor()
  await page.waitForFunction(
    () => globalThis.testPixi?.stage?.children[0]?.children.length > 0,
  )
  const labels2d = async () => {
    await page.waitForFunction(() => globalThis.testPixi?.stage?.children[0]?.children.length > 0)
    return page.evaluate(() =>
      Object.fromEntries(
        globalThis.testPixi.stage.children[0].children
          .filter((c) => c.text)
          .map((c) => [c.text.split('\n')[0], { x: c.x, y: c.y }]),
      ),
    )
  }
  const bounds = () => page.locator('canvas').boundingBox()
  const drag = async (x, y, dx, dy, button = 'left') => {
    await page.mouse.move(x, y)
    await page.mouse.down({ button })
    await page.mouse.move(x + dx, y + dy, { steps: 12 })
    await page.mouse.up({ button })
    await wait()
  }
  const reset = async () => {
    await page.getByRole('button', { name: 'Reset View', exact: true }).click()
    await wait()
  }
  const selected = () =>
    page.locator('.object-list button[aria-pressed=true]').textContent()
  const original = await labels2d()
  let box = await bounds()
  await page.mouse.move(
    box.x + original.Helios.x - 15,
    box.y + original.Helios.y - 16,
  )
  await page.mouse.down()
  await page.mouse.move(
    box.x + original.Helios.x - 12,
    box.y + original.Helios.y - 14,
  )
  await page.mouse.up()
  await wait()
  check(
    (await selected()).includes('Helios'),
    '2D click with jitter selects object',
  )
  check(
    JSON.stringify(await labels2d()) === JSON.stringify(original),
    '2D tap jitter does not pan',
  )
  await drag(box.x + box.width / 2, box.y + box.height / 2, 60, 35)
  const panned = await labels2d()
  check(panned.Helios.x > original.Helios.x + 45, '2D mouse drag pans')
  check(
    (await selected()).includes('Helios'),
    '2D drag does not change selection',
  )
  await reset()
  check(
    JSON.stringify(await labels2d()) === JSON.stringify(original),
    '2D reset restores home',
  )
  await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2)
  await page.mouse.wheel(0, -150)
  await wait()
  const zoomed = await labels2d()
  check(
    Math.abs(zoomed.Iris.y - zoomed.Helios.y) >
      Math.abs(original.Iris.y - original.Helios.y),
    '2D mouse wheel zooms',
  )
  await reset()
  await page.getByRole('checkbox', { name: 'Reference guides' }).uncheck()
  await page.getByRole('checkbox', { name: 'Reference guides' }).check()

  await page.getByRole('button', { name: '3D', exact: true }).click()
  await page.locator('.space-label').first().waitFor()
  await wait()
  // Read Babylon's own dev-module registry; no test/debug hooks in production code.
  await page.evaluate(async () => {
    const { Engine } = await import(
      '/node_modules/.vite/deps/@babylonjs_core_Engines_engine.js'
    )
    globalThis.testScene = Engine.LastCreatedScene
  })
  const camera = () =>
    page.evaluate(() => {
      const c = globalThis.testScene.activeCamera
      return {
        alpha: c.alpha,
        beta: c.beta,
        radius: c.radius,
        target: c.target.asArray(),
      }
    })
  const sameAngles = (a, b) => close(a.alpha, b.alpha) && close(a.beta, b.beta)
  const home = await camera()
  box = await bounds()
  await drag(box.x + box.width / 2, box.y + box.height / 2, 100, 30)
  const orbited = await camera()
  check(!sameAngles(orbited, home), '3D left drag orbits')
  check((await selected()).includes('Helios'), '3D orbit does not select')
  await reset()
  check(
    JSON.stringify(await camera()) === JSON.stringify(home),
    '3D reset clears motion and restores home',
  )
  for (const button of ['right', 'middle', 'ctrl']) {
    if (button === 'ctrl') await page.keyboard.down('Control')
    await drag(
      box.x + box.width / 2,
      box.y + box.height / 2,
      70,
      30,
      button === 'ctrl' ? 'left' : button,
    )
    if (button === 'ctrl') await page.keyboard.up('Control')
    const pan = await camera()
    check(
      sameAngles(pan, home) && Math.hypot(...pan.target) > 0.1,
      `3D ${button} drag pans without orbit`,
    )
    await reset()
  }
  const helios = await page.evaluate(() => {
    const s = globalThis.testScene,
      c = s.activeCamera
    const v = s.getMeshByName('star-helios').position
    const { x, y } = v.constructor.Project(
      v,
      s.getWorldMatrix?.() ?? s.getMeshByName('star-helios').getWorldMatrix(),
      s.getTransformMatrix(),
      c.viewport.toGlobal(
        s.getEngine().getRenderWidth(),
        s.getEngine().getRenderHeight(),
      ),
    )
    const canvas = document.querySelector('canvas'),
      r = canvas.getBoundingClientRect()
    return {
      x: r.x + (x * r.width) / s.getEngine().getRenderWidth(),
      y: r.y + (y * r.height) / s.getEngine().getRenderHeight(),
    }
  })
  await page.getByRole('button', { name: 'Pathfinder', exact: true }).click()
  await drag(helios.x, helios.y, 3, 2)
  check(
    (await selected()).includes('Helios') && sameAngles(await camera(), home),
    '3D jitter selects without orbit',
  )
  await page.getByRole('button', { name: 'Iris', exact: true }).click()
  const beforeFocus = await camera()
  await page.getByRole('button', { name: 'Focus Selected Object' }).click()
  await wait()
  const focus = await camera()
  check(
    focus.target.every((v, i) => close(v, [1.2, 0.4, 0.2][i])) &&
      sameAngles(focus, beforeFocus) &&
      close(focus.radius, beforeFocus.radius),
    '3D focus changes pivot while preserving orbit and zoom',
  )
  await drag(box.x + box.width / 2, box.y + box.height / 2, 60, 10)
  check(
    (await camera()).target.every((v, i) => close(v, focus.target[i])),
    '3D orbit retains selected pivot',
  )
  await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2)
  await page.mouse.wheel(0, -120)
  await wait()
  check((await camera()).radius < focus.radius, '3D wheel zooms')
  await reset()
  check(
    JSON.stringify(await camera()) === JSON.stringify(home),
    '3D reset recovers focused/zoomed view',
  )
  await page.screenshot({
    path: 'output/playwright/map-desktop.png',
    fullPage: true,
  })

  // Chromium CDP sends native touch streams (not synthetic DOM pointer events).
  const cdp = await page.context().newCDPSession(page)
  await cdp.send('Emulation.setTouchEmulationEnabled', {
    enabled: true,
    maxTouchPoints: 5,
  })
  await page.setViewportSize({ width: 393, height: 852 })
  await page.locator('canvas').scrollIntoViewIfNeeded()
  await wait()
  const touch = (type, points) =>
    cdp.send('Input.dispatchTouchEvent', {
      type,
      touchPoints: points.map(([id, x, y]) => ({ id, x, y })),
    })
  const gesture = async (points, dx, dy, spread = 0) => {
    await touch('touchStart', points)
    for (let i = 1; i <= 12; i++) {
      await touch(
        'touchMove',
        points.map(([id, x, y], j) => [
          id,
          x + (dx * i) / 12 + ((j ? spread : -spread) * i) / 12,
          y + (dy * i) / 12,
        ]),
      )
      await page.waitForTimeout(16)
    }
    await touch('touchEnd', [])
    await wait()
  }
  box = await bounds()
  const x = box.x + box.width / 2,
    y = box.y + box.height / 2
  const touchHome = await camera()
  check(
    (await page.locator('.camera-controls button').count()) === 4,
    '3D redundant directions/rotate removed; zoom/focus/reset retained',
  )
  await page.getByRole('button', { name: 'Iris', exact: true }).click()
  await gesture([[1, x, y]], 2, 2)
  check(
    (await selected()).includes('Helios') &&
      sameAngles(await camera(), touchHome),
    '3D native touch tap selects without orbit',
  )
  await gesture([[1, x, y]], 55, 15)
  check(!sameAngles(await camera(), touchHome), '3D one-finger touch orbits')
  await reset()
  await gesture(
    [
      [1, x - 35, y],
      [2, x + 35, y],
    ],
    30,
    20,
  )
  const touchPan = await camera()
  check(
    sameAngles(touchPan, touchHome) && Math.hypot(...touchPan.target) > 0.1,
    '3D two-finger touch pans without orbit',
  )
  await reset()
  await gesture(
    [
      [1, x - 35, y],
      [2, x + 35, y],
    ],
    0,
    0,
    20,
  )
  check(
    (await camera()).radius < touchHome.radius &&
      sameAngles(await camera(), touchHome),
    '3D pinch zooms without rotation',
  )
  await reset()
  await touch('touchStart', [
    [1, x - 30, y],
    [2, x + 30, y],
  ])
  await touch('touchMove', [
    [1, x - 40, y],
    [2, x + 40, y],
  ])
  await touch('touchEnd', [[1, x - 40, y]])
  const handoff = await camera()
  await touch('touchMove', [[1, x + 30, y + 30]])
  await touch('touchEnd', [])
  await wait()
  check(
    sameAngles(await camera(), handoff),
    '3D lifting one finger after pinch does not orbit',
  )
  await reset()
  await touch('touchStart', [[1, x, y]])
  await touch('touchCancel', [])
  await gesture([[1, x, y]], 30, 10)
  check(
    !sameAngles(await camera(), touchHome),
    '3D touch cancellation allows the next gesture',
  )
  await reset()
  await page.getByRole('button', { name: 'Pathfinder', exact: true }).click()
  await page.getByRole('button', { name: 'Focus Selected Object' }).click()
  const priorShipX = await page.evaluate(
    () => globalThis.testScene.getMeshByName('ship-pathfinder').position.x,
  )
  await page.getByRole('button', { name: 'Plot Course', exact: true }).click()
  await page.getByLabel('Course destination').selectOption('planet-nereid')
  await page.waitForFunction(
    () =>
      ![...document.querySelectorAll('button')].find(
        (b) => b.textContent === 'Issue Order',
      )?.disabled,
  )
  await page.getByRole('button', { name: 'Issue Order', exact: true }).click()
  await page
    .getByRole('combobox', { name: 'Advance interval' })
    .selectOption('3600')
  await page.getByRole('button', { name: '▶ Play', exact: true }).click()
  await page.waitForFunction(
    (before) =>
      Math.abs(
        globalThis.testScene.getMeshByName('ship-pathfinder').position.x -
          before,
      ) > 0.000001,
    priorShipX,
  )
  const moving = await camera()
  const ship = await page.evaluate(() =>
    globalThis.testScene.getMeshByName('ship-pathfinder').position.asArray(),
  )
  check(
    moving.target.every((v, i) => close(v, ship[i])),
    '3D focused pivot follows authoritative ship movement',
  )
  check(
    await page.evaluate(
      () => !!globalThis.testScene.getMeshByName('committed-trajectory'),
    ),
    'Travel order and 3D course overlay remain functional',
  )
  await page.getByRole('checkbox', { name: 'Reference guides' }).uncheck()
  await wait()
  check(
    await page.evaluate(
      () => !globalThis.testScene.getMeshByName('axes').isEnabled(),
    ),
    '3D reference overlay visibility remains functional',
  )
  await page.getByRole('checkbox', { name: 'Reference guides' }).check()
  await reset()
  // Movement above is intentionally confined to the disposable :5094 host.
  await page.screenshot({
    path: 'output/playwright/map-touch-3d.png',
    fullPage: true,
  })

  await page.getByRole('button', { name: '2D', exact: true }).click()
  await page.waitForFunction(
    () => globalThis.testPixi?.stage?.children[0]?.children.length > 0,
  )
  await page.locator('canvas').scrollIntoViewIfNeeded()
  await wait()
  box = await bounds()
  const tx = box.x + box.width / 2,
    ty = box.y + box.height / 2
  const touch2d = await labels2d()
  await gesture([[1, tx, ty]], 2, 2)
  check((await selected()).includes('Helios'), '2D native touch tap selects')
  check(
    await page.evaluate(() =>
      globalThis.testPixi.stage.children[0].children[0].context.instructions.some(
        (i) => i.action === 'stroke' && i.data.style.color === 0x87e4bd,
      ),
    ),
    '2D travel vector renders after switching views',
  )
  await page.getByRole('button', { name: 'Pathfinder', exact: true }).click()
  await gesture([[1, tx, ty]], 30, 20)
  check(
    (await labels2d()).Helios.x > touch2d.Helios.x + 20,
    '2D one-finger swipe pans',
  )
  await reset()
  await gesture(
    [
      [1, tx - 35, ty],
      [2, tx + 35, ty],
    ],
    20,
    15,
  )
  check(
    (await labels2d()).Helios.x > touch2d.Helios.x + 10,
    '2D two-finger swipe pans',
  )
  await reset()
  await gesture(
    [
      [1, tx - 35, ty],
      [2, tx + 35, ty],
    ],
    0,
    0,
    15,
  )
  const pinch2d = await labels2d()
  check(
    Math.abs(pinch2d.Pathfinder.y - pinch2d.Helios.y) >
      Math.abs(touch2d.Pathfinder.y - touch2d.Helios.y),
    '2D native touch pinch zooms',
  )
  check(
    (await selected()).includes('Pathfinder'),
    '2D touch navigation retains selection',
  )
  const beforeHandoff = await labels2d()
  await touch('touchStart', [
    [1, tx - 30, ty],
    [2, tx + 30, ty],
  ])
  await touch('touchEnd', [[1, tx - 30, ty]])
  await touch('touchMove', [[1, tx + 30, ty + 30]])
  await touch('touchEnd', [])
  await wait()
  check(
    JSON.stringify(await labels2d()) === JSON.stringify(beforeHandoff) &&
      (await selected()).includes('Pathfinder'),
    '2D pinch handoff cannot pan or select',
  )
  await reset()
  await touch('touchStart', [[1, tx, ty]])
  await touch('touchCancel', [])
  await gesture([[1, tx, ty]], 25, 15)
  check(
    (await labels2d()).Helios.x > touch2d.Helios.x + 15,
    '2D touch cancellation allows the next gesture',
  )
  await reset()
  await page.screenshot({
    path: 'output/playwright/map-touch-2d.png',
    fullPage: true,
  })
  check(
    (await page.locator('.camera-controls button').count()) === 3,
    '2D redundant directions removed; zoom/reset retained',
  )
  await cdp.detach()
  for (let cycle = 0; cycle < 3; cycle++) {
    await page.reload()
    await page.waitForFunction(() => globalThis.testPixi?.stage?.children[0]?.children.length > 0)
    await page.setViewportSize({ width: cycle % 2 ? 393 : 800, height: 852 })
    await page.getByRole('button', { name: 'Reset View', exact: true }).click()
    await wait()
    if (await page.getByRole('alert').count()) throw new Error('Map failed after remount/resize')
  }
  check(true, 'Repeated StrictMode remount and resize preserve active Pixi graphics')
  check(errors.length === 0, 'No browser runtime errors: ' + errors.join('; '))
  return { passed: results.length, checks: results }
}
