import assert from 'node:assert/strict'
import { fileURLToPath } from 'node:url'
import { NullEngine } from '@babylonjs/core/Engines/nullEngine.js'
import { Scene } from '@babylonjs/core/scene.js'
import { Container, Graphics } from 'pixi.js'
import { build } from 'vite'

// Exercise package loading and cleanup without requiring a browser or GPU.
const engine = new NullEngine()
const scene = new Scene(engine)
const map = new Container()
try {
  assert.equal(scene.getEngine(), engine)
  map.addChild(new Graphics().rect(0, 0, 10, 10).fill(0xffffff))
  assert.equal(map.children.length, 1)
} finally {
  map.destroy({ children: true })
  scene.dispose()
  engine.dispose()
}

// The placeholder app does not import the renderers yet. Bundle a separate
// in-memory browser entry so verification still checks their Vite compatibility.
const entry = fileURLToPath(new URL('../__quasar_renderer_check__.js', import.meta.url))
const resolvedEntry = '\0quasar-renderer-check'
await build({
  root: fileURLToPath(new URL('../', import.meta.url)),
  configFile: false,
  logLevel: 'warn',
  plugins: [{
    name: 'quasar-renderer-check',
    enforce: 'pre',
    resolveId(id) {
      if (id.replaceAll('\\', '/') === entry.replaceAll('\\', '/')) return resolvedEntry
    },
    load(id) {
      if (id === resolvedEntry) {
        return `
          export { Engine } from '@babylonjs/core/Engines/engine.js';
          export { Scene } from '@babylonjs/core/scene.js';
          export { Application, Container, Graphics } from 'pixi.js';
          export { version as reactVersion } from 'react';
        `
      }
    },
  }],
  build: {
    write: false,
    minify: false,
    lib: { entry, formats: ['es'] },
  },
})

console.log('Renderer package loading, scene cleanup, and browser bundling passed.')
console.log('This headless check does not verify browser GPU rendering or map performance.')
