# Quasar browser client

React and TypeScript provide inspection, time controls, and course planning.
PixiJS renders the 2D tactical map and Babylon.js renders the 3D map. Both
consume snapshots and trajectory samples from the authoritative C# simulation.
The Vite development server proxies `/api` to loopback 5080. The published
ASP.NET host serves both assets and API from one origin.

See the [playable workflow](../../README.md), [development setup](../../docs/DEVELOPMENT_SETUP.md),
and [frontend architecture](../../docs/FRONTEND_ARCHITECTURE.md).
`npm run check:renderers` checks loading and browser bundling.
`npm run test:maps` and `npm run test:coordinates` check navigation and projection.
Real-browser course tests use the disposable hosts described in the
[course plotting guide](../../docs/COURSE_PLOTTING.md).
