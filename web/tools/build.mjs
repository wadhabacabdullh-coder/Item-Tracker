// Bundles the browser game and inlines it into dist/index.html (artifact pages only load scripts from CDNs).
import { build } from 'esbuild';
import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';

mkdirSync('dist', { recursive: true });
const out = await build({ entryPoints: ['src/game/main.ts'], bundle: true, format: 'iife', target: 'es2020', minify: true, write: false, legalComments: 'none' });
const js = out.outputFiles[0].text.replace(/<\/script/gi, '<\\/script');
const html = readFileSync('index.html', 'utf8').replace('/*__GAME_JS__*/', () => js);
writeFileSync('dist/index.html', html);
console.log(`dist/index.html ${(html.length / 1024).toFixed(0)} KB`);
