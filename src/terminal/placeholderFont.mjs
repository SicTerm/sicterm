// A font of blank glyphs for kitty's image placeholder, U+10EEEE, and the 297 combining marks kitty numbers rows and
// columns with (rowColumnDiacritics.json, kitty's gen/rowcolumn-diacritics.txt). No usual font has U+10EEEE, so the
// browser draws a missing-glyph box for it in the cell's foreground colour, the image ID's colour, and because it cannot
// attach a mark to a missing glyph it retries the whole cluster in the next font and ends up drawing a box for the mark
// as well. Listed after the chosen terminal font, this one draws nothing for exactly those characters, and the page
// draws the image over the cells (kittyImages.ts). Built into the page at build time (build.mjs); see
// docs/decisions/pet-images.md.
import { readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import opentype from 'opentype.js';

/**
 * The font as TrueType bytes, the same bytes on every build: opentype.js stamps the time of the build into the `head`
 * table, which would change the committed bundle on every PC's build, so the stamps are fixed and the checksums redone.
 * The placeholder keeps a monospace-like advance; the marks have none, as combining marks do.
 */
export async function buildPlaceholderFont() {
  const diacritics = JSON.parse(await readFile(fileURLToPath(new URL('./rowColumnDiacritics.json', import.meta.url)), 'utf8'));
  const { Font, Glyph, Path } = opentype;
  const blank = (name, unicode, advanceWidth) => new Glyph({ name, unicode, advanceWidth, path: new Path() });
  const glyphs = [blank('.notdef', 0, 600), blank('placeholder', 0x10eeee, 600), ...diacritics.map((code, index) => blank(`mark${index}`, code, 0))];
  const font = new Font({ familyName: 'SIC Placeholders', styleName: 'Regular', unitsPerEm: 1000, ascender: 800, descender: -200, glyphs });
  return withFixedStamps(new Uint8Array(font.toArrayBuffer()));
}

/** Zeroes `head`'s created and modified times (1904-01-01), then recomputes its table checksum and the font's adjustment. */
function withFixedStamps(bytes) {
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  const checksum = (offset, length) => {
    let sum = 0;
    for (let at = offset; at < offset + length; at += 4) {
      let word = 0;
      for (let byte = 0; byte < 4; byte++) word = (word << 8) | (at + byte < offset + length ? bytes[at + byte] : 0);
      sum = (sum + (word >>> 0)) >>> 0;
    }
    return sum;
  };
  const tables = view.getUint16(4);
  for (let index = 0; index < tables; index++) {
    const entry = 12 + index * 16;
    if (String.fromCharCode(...bytes.subarray(entry, entry + 4)) !== 'head') continue;
    const offset = view.getUint32(entry + 8), length = view.getUint32(entry + 12);
    view.setUint32(offset + 8, 0); // checkSumAdjustment, left out of both sums
    view.setBigInt64(offset + 20, 0n); // created
    view.setBigInt64(offset + 28, 0n); // modified
    view.setUint32(entry + 4, checksum(offset, length));
    view.setUint32(offset + 8, (0xb1b0afba - checksum(0, bytes.byteLength)) >>> 0);
    return bytes;
  }
  throw new Error('The placeholder font has no head table.');
}
