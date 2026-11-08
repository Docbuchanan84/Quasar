# Quasar browser client

React/TypeScript/Vite development shell only. See
[development setup](../../docs/DEVELOPMENT_SETUP.md) for commands.
Approved frontend roles: React for UI, Babylon.js for 3D, and PixiJS for 2D maps.
Renderer packages are installed; see [frontend architecture](../../docs/FRONTEND_ARCHITECTURE.md).
`npm run check:renderers` checks package loading and browser bundling headlessly.
The skeleton checks the real same-origin `/api/health` endpoint. Vite proxies
`/api` to loopback 5080 during development; the published ASP.NET host serves
both client and API. See [hosting](../../docs/HOSTING.md).
No simulation state, map views, or time controls are implemented.
