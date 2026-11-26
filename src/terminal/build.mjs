import { build } from 'esbuild';
import { mkdir, copyFile, readFile, writeFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import { buildPlaceholderFont } from './placeholderFont.mjs';

const root = path.dirname(fileURLToPath(import.meta.url));
const output = path.resolve(root, '../Sic.App/Assets/terminal');
await mkdir(output, { recursive: true });
// The placeholder font goes into the bundle as bytes (`import font from 'sic:placeholder-font'`), built fresh each time.
const placeholderFont = {
  name: 'placeholder-font',
  setup(plugin) {
    plugin.onResolve({ filter: /^sic:placeholder-font$/ }, () => ({ path: 'placeholder-font', namespace: 'sic' }));
    plugin.onLoad({ filter: /^placeholder-font$/, namespace: 'sic' }, async () => ({ contents: await buildPlaceholderFont(), loader: 'binary' }));
  }
};
await build({ entryPoints: [path.join(root, 'terminal.ts')], outfile: path.join(output, 'terminal.js'), bundle: true, format: 'esm', target: 'es2022', minify: true, sourcemap: false, legalComments: 'linked', plugins: [placeholderFont] });
await copyFile(path.join(root, 'terminal.html'), path.join(output, 'terminal.html'));
const css = await readFile(path.join(root, 'node_modules/@xterm/xterm/css/xterm.css'), 'utf8') + '\n' + await readFile(path.join(root, 'terminal.css'), 'utf8');
await writeFile(path.join(output, 'terminal.css'), css, 'utf8');
let notices = '# Terminal dependencies\n\n';
for (const name of ['@xterm/xterm', '@xterm/addon-fit', '@xterm/addon-search', '@xterm/addon-web-links', '@xterm/addon-unicode-graphemes']) {
  notices += `## ${name}\n\n` + await readFile(path.join(root, 'node_modules', name, 'LICENSE'), 'utf8') + '\n\n';
}
// The fonts Settings offers beyond Windows' own (fonts/README.md), copied as published, each with its licence in the notices.
const fonts = path.join(root, 'fonts');
await mkdir(path.join(output, 'fonts'), { recursive: true });
for (const [family, file] of [['JetBrains Mono', 'JetBrainsMono'], ['Fira Code', 'FiraCode'], ['IBM Plex Mono', 'IBMPlexMono'], ['Victor Mono', 'VictorMono']]) {
  for (const weight of ['Regular', 'Bold']) await copyFile(path.join(fonts, `${file}-${weight}.woff2`), path.join(output, 'fonts', `${file}-${weight}.woff2`));
  notices += `## ${family} (font)\n\n` + await readFile(path.join(fonts, `${file}-OFL.txt`), 'utf8') + '\n\n';
}
await writeFile(path.join(output, 'THIRD-PARTY-NOTICES.md'), notices, 'utf8');
console.log('Built local terminal assets:', output);
