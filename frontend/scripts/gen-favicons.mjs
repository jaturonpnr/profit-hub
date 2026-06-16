// One-off favicon generator: rasterizes public/favicon.svg (brand mark) into a
// full icon set. Run: npx -y -p sharp -p png-to-ico node scripts/gen-favicons.mjs
import { readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import sharp from 'sharp';
import pngToIco from 'png-to-ico';

const pub = join(dirname(fileURLToPath(import.meta.url)), '..', 'public');
const svg = readFileSync(join(pub, 'favicon.svg'));

const png = (size) => sharp(svg, { density: 384 }).resize(size, size).png().toBuffer();

const sizes = { 'favicon-16.png': 16, 'favicon-32.png': 32, 'favicon-48.png': 48,
  'apple-touch-icon.png': 180, 'icon-192.png': 192, 'icon-512.png': 512 };

for (const [name, size] of Object.entries(sizes)) {
  writeFileSync(join(pub, name), await png(size));
  console.log('wrote', name);
}

// Multi-resolution .ico from 16/32/48
const ico = await pngToIco([16, 32, 48].map((s) => join(pub, `favicon-${s}.png`)));
writeFileSync(join(pub, 'favicon.ico'), ico);
console.log('wrote favicon.ico');
