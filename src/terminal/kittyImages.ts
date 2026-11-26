import type { IBufferCell, IBufferLine, Terminal } from '@xterm/xterm';
// Row and column diacritics by number, kitty's gen/rowcolumn-diacritics.txt (the table Hermes's agent/pet/render.py copies);
// the placeholder font (placeholderFont.mjs) is built from the same file.
import DIACRITICS from './rowColumnDiacritics.json';
import { decodeBase64 } from './base64';

/**
 * Images a program shows through kitty's Unicode placeholders (https://sw.kovidgoyal.net/kitty/graphics-protocol/#unicode-placeholders),
 * which is how Hermes draws its pet when `display.pet.render_mode` is `kitty`. xterm.js swallows the APC that carries the image,
 * so the host takes it out of the stream (Sic.Core KittyGraphicsSplitter) and posts the PNG here; the program prints ordinary
 * text cells (U+10EEEE, a row diacritic on a row's first cell, the image ID as the foreground colour) where the image goes,
 * and those reach xterm's buffer intact. This draws each image's slice over its cells on a canvas laid on xterm's screen.
 * xterm draws nothing for the cells themselves: the page's font list ends with a font of blank glyphs for U+10EEEE and the
 * diacritics (terminal.ts), so the cells show their own background, and only the image is drawn over it.
 *
 * Hermes sends the whole PNG of every frame, 5.5 times a second, cycling through a few frames per state. The host (Sic.Core
 * KittyImagePoster) gives each image it posts a key and sends a frame it already sent as a reference to that key; decoded
 * frames are kept here by key, so after the first loop nothing is decoded again. A reference to a frame no longer held is
 * asked for again (kitty-miss). The reasoning, measurements and limits are in docs/decisions/pet-images.md.
 */

/** What the host posts for one transmitted image: the key names the frame for later references; resent answers a miss. */
export type KittyImageMessage = { id: number; cols?: number; rows?: number; key?: number; resent?: boolean; png: string };
/** A frame sent again: show the one held under its key. */
export type KittyImageAgainMessage = { id: number; key: number };
/** What a reference did: shown at once, shown when its decode ends, asked of the host, or nothing (off, or a frame that failed). */
export type KittyAgainResult = 'shown' | 'pending' | 'missed' | 'off' | 'ignored';
/** The verification hook's view: what is held and what the last draw put on the screen, with running costs. */
export type KittyImagesCheck = {
  enabled: boolean;
  /** Images with a frame to show, and the key of that frame. */
  ids: number[];
  shown: Record<string, number>;
  /** Decoded frames held, on the screen or kept for a later reference. */
  frames: number;
  placementsDrawn: number;
  runsDrawn: number;
  /** The runs drawn with an image, in CSS pixels from the page's top left. */
  runs: { id: number; x: number; y: number; width: number; height: number }[];
  placeholderRows: number;
  decodedBytes: number;
  viewport: { width: number; height: number };
  /** xterm's screen, the cell grid's top left, in CSS pixels from the page's top left. */
  screen: { left: number; top: number };
  canvas: { width: number; height: number; hidden: boolean };
  cell: { width: number; height: number };
  devicePixelRatio: number;
  decodes: number; decodeMs: number; lastDecodeMs: number;
  againShown: number; misses: number;
  draws: number; drawMs: number; lastDrawMs: number;
  scans: number; scanMs: number; rowsScanned: number;
};

const PLACEHOLDER = 0x10eeee;
const PLACEHOLDER_TEXT = String.fromCodePoint(PLACEHOLDER);
const DIACRITIC_NUMBER = new Map(DIACRITICS.map((code, number) => [code, number]));

/**
 * At most this many images on the screen, this many decoded frames in all, and this many bytes of decoded pixels are held.
 * Frames kept only for a later reference go first, least recently shown first; then images, least recently drawn first.
 */
const MAX_IMAGES = 16;
const MAX_FRAMES = 64;
const MAX_BYTES = 32 * 1024 * 1024;
/** A PNG whose header claims more than this per side is not decoded: a few KiB of PNG can claim gigabytes of pixels. */
const MAX_SIDE = 4096;

const validId = (id: unknown): id is number => Number.isInteger(id) && (id as number) > 0 && (id as number) <= 0xffffffff;

/** One decoded frame of an image. */
type Frame = { id: number; bitmap: ImageBitmap; cols: number; rows: number; bytes: number };
/** Consecutive placeholder cells of one image row: viewport column x onward, image column col onward, length cells. */
type Run = { id: number; row: number; col: number; x: number; length: number };
const NONE: readonly Run[] = [];

export class KittyImages {
  /** Decoded frames by key, least recently shown first. */
  private readonly frames = new Map<number, Frame>();
  /** Per image ID, the key of the frame on the screen; least recently drawn first. */
  private readonly shown = new Map<number, number>();
  /** Per image ID, the newest frame asked for: a decode that finishes after a newer frame was asked for is kept, not shown. */
  private readonly wanted = new Map<number, number>();
  /** Frames being decoded, by key, with their image ID; a delete or turning off removes them, and their decode is dropped. */
  private readonly pending = new Map<number, number>();
  /** Keys whose PNG did not decode, so a reference to one asks nothing of the host. */
  private readonly failed = new Set<number>();
  /** Keys for images given without one (the verification hook): negative, so never one of the host's. */
  private localKey = 0;
  private enabled = true;
  /** Placeholder runs per viewport row, as last read from the buffer. */
  private runs: (readonly Run[])[] = [];
  private viewportY = -1;
  private bufferType = '';
  private readonly canvas: HTMLCanvasElement;
  private readonly context: CanvasRenderingContext2D;
  private readonly screen: HTMLElement;
  private painted = false;
  private drawnGeometry = '';
  private drawRequested = false;
  private drawnRuns: KittyImagesCheck['runs'] = [];
  private stats = { placementsDrawn: 0, runsDrawn: 0, decodes: 0, decodeMs: 0, lastDecodeMs: 0, againShown: 0, misses: 0, draws: 0, drawMs: 0, lastDrawMs: 0, scans: 0, scanMs: 0, rowsScanned: 0 };

  constructor(private readonly terminal: Terminal, private readonly post: (message: unknown) => void) {
    const screen = terminal.element?.querySelector<HTMLElement>('.xterm-screen');
    const canvas = document.createElement('canvas');
    const context = canvas.getContext('2d');
    if (!screen || !context) throw new Error('The terminal has no screen to draw images on.');
    this.screen = screen;
    this.canvas = canvas;
    this.context = context;
    // xterm.css already places a canvas inside the screen at its top left. Above the rows, under the selection and the search
    // highlights (positioned later with z-index 1 and up), and never in the pointer's way.
    canvas.className = 'sic-images';
    canvas.setAttribute('aria-hidden', 'true');
    canvas.style.pointerEvents = 'none';
    canvas.style.zIndex = '0';
    canvas.hidden = true;
    screen.appendChild(canvas);
    // xterm renders in an animation frame and reports the rows it drew; reading and drawing here lands in the same frame as
    // the text, so an image never trails its cells when they move.
    terminal.onRender(({ start, end }) => this.scan(start, end));
    terminal.buffer.onBufferChange(() => this.scan(0, terminal.rows - 1, true));
    terminal.onResize(() => this.scan(0, terminal.rows - 1, true));
    // Display scaling changes (the window moved to another monitor, or the scale was changed) leave xterm's DOM rows to the
    // browser, so no render is reported; the canvas needs a backing store at the new ratio.
    const watchRatio = (): void => {
      matchMedia(`(resolution: ${window.devicePixelRatio || 1}dppx)`).addEventListener('change', () => { this.requestDraw(); watchRatio(); }, { once: true });
    };
    watchRatio();
  }

  /**
   * Settings → Pet images. Off, every frame is dropped, the canvas is cleared and hidden, nothing is read from the buffer, and
   * placeholder cells stay blank (the font draws nothing for them either way); on again, the screen is read at once and images
   * come with their next frame.
   */
  setEnabled(on: boolean): void {
    if (on === this.enabled) return;
    this.enabled = on;
    if (on) {
      this.viewportY = -1;
      this.scan(0, this.terminal.rows - 1, true);
      return;
    }
    this.remove(0);
    this.failed.clear();
    this.runs = [];
    this.draw();
  }

  /**
   * Decodes and keeps a frame; the frame it replaces stays on the screen until this one is ready. A frame resent at the page's
   * request is shown only if it is still the newest asked for. True when it was taken.
   */
  async add(message: KittyImageMessage): Promise<boolean> {
    const id = message?.id;
    if (!this.enabled || !validId(id) || typeof message.png !== 'string' || message.png.length === 0) return false;
    const key = Number.isSafeInteger(message.key) && message.key! > 0 ? message.key! : --this.localKey;
    const cols = Number.isInteger(message.cols) && message.cols! > 0 ? Math.min(message.cols!, 1000) : 0;
    const rows = Number.isInteger(message.rows) && message.rows! > 0 ? Math.min(message.rows!, 1000) : 0;
    if (message.resent !== true) this.wanted.set(id, key);
    if (this.frames.has(key)) return this.show(id, key);
    if (this.pending.has(key)) return false;
    this.pending.set(key, id);
    const started = performance.now();
    let bitmap: ImageBitmap;
    try {
      const bytes = decodeBase64(message.png);
      // IHDR: width and height big-endian at bytes 16 and 20.
      const view = new DataView(bytes.buffer);
      if (bytes.length < 24 || view.getUint32(16) > MAX_SIDE || view.getUint32(20) > MAX_SIDE) throw new RangeError('Image too large');
      bitmap = await createImageBitmap(new Blob([bytes], { type: 'image/png' }));
    } catch {
      if (this.pending.get(key) === id) {
        this.pending.delete(key);
        if (this.failed.size >= 256) this.failed.clear();
        this.failed.add(key);
      }
      return false;
    }
    // Deleted, or turned off, while it decoded.
    if (this.pending.get(key) !== id) { bitmap.close(); return false; }
    this.pending.delete(key);
    const elapsed = performance.now() - started;
    this.stats.decodes++;
    this.stats.decodeMs += elapsed;
    this.stats.lastDecodeMs = elapsed;
    this.frames.set(key, { id, bitmap, cols, rows, bytes: bitmap.width * bitmap.height * 4 });
    if (this.wanted.get(id) === key) this.show(id, key);
    this.evict();
    return true;
  }

  /** Shows a frame sent again from the frames held; one no longer held is asked of the host, which sends it in full. */
  again(message: KittyImageAgainMessage): KittyAgainResult {
    const id = message?.id, key = message?.key;
    if (!this.enabled) return 'off';
    if (!validId(id) || !Number.isSafeInteger(key) || key <= 0) return 'ignored';
    this.wanted.set(id, key);
    if (this.show(id, key)) { this.stats.againShown++; return 'shown'; }
    if (this.pending.has(key)) return 'pending';
    if (this.failed.has(key)) return 'ignored';
    this.stats.misses++;
    this.post({ type: 'kitty-miss', id, key });
    return 'missed';
  }

  /** Forgets an image's frames, or every image's for ID 0; its cells show their background until it comes again. */
  remove(id: number): void {
    for (const [key, frame] of this.frames) {
      if (id !== 0 && frame.id !== id) continue;
      frame.bitmap.close();
      this.frames.delete(key);
    }
    for (const [key, owner] of this.pending) if (id === 0 || owner === id) this.pending.delete(key);
    if (id === 0) { this.shown.clear(); this.wanted.clear(); } else { this.shown.delete(id); this.wanted.delete(id); }
    this.requestDraw();
  }

  check(): KittyImagesCheck {
    const { width, height } = this.cellSize();
    const screen = this.screen.getBoundingClientRect();
    return {
      enabled: this.enabled,
      ids: [...this.shown.keys()],
      shown: Object.fromEntries(this.shown),
      frames: this.frames.size,
      placeholderRows: this.runs.filter(runs => runs.length > 0).length,
      decodedBytes: [...this.frames.values()].reduce((sum, frame) => sum + frame.bytes, 0),
      runs: this.drawnRuns,
      viewport: { width: window.innerWidth, height: window.innerHeight },
      screen: { left: screen.left, top: screen.top },
      canvas: { width: this.canvas.width, height: this.canvas.height, hidden: this.canvas.hidden === true },
      cell: { width, height },
      devicePixelRatio: window.devicePixelRatio || 1,
      ...this.stats
    };
  }

  /** Puts a held frame on the screen for its image. False when the key is not held. */
  private show(id: number, key: number): boolean {
    const frame = this.frames.get(key);
    if (!frame) return false;
    this.frames.delete(key);
    this.frames.set(key, frame);
    if (this.shown.get(id) !== key) {
      this.shown.delete(id);
      this.shown.set(id, key);
      this.requestDraw();
    }
    return true;
  }

  private evict(): void {
    let bytes = 0;
    for (const frame of this.frames.values()) bytes += frame.bytes;
    const over = (): boolean => this.frames.size > MAX_FRAMES || bytes > MAX_BYTES || this.shown.size > MAX_IMAGES;
    const drop = (key: number): void => {
      const frame = this.frames.get(key);
      if (!frame) return;
      frame.bitmap.close();
      this.frames.delete(key);
      bytes -= frame.bytes;
    };
    if (!over()) return;
    const onScreen = new Set(this.shown.values());
    for (const key of [...this.frames.keys()]) {
      if (this.frames.size <= MAX_FRAMES && bytes <= MAX_BYTES) break;
      if (!onScreen.has(key)) drop(key);
    }
    // Then whole images, least recently drawn first; one larger than the whole budget is not kept either.
    for (const [id, key] of [...this.shown]) {
      if (!over()) break;
      for (const [other, frame] of [...this.frames]) if (frame.id === id && other !== key) drop(other);
      drop(key);
      this.shown.delete(id);
    }
  }

  private requestDraw(): void {
    if (this.drawRequested) return;
    this.drawRequested = true;
    requestAnimationFrame(() => { this.drawRequested = false; this.draw(); });
  }

  /**
   * Reads the rows xterm just drew, or the whole viewport when it scrolled, switched buffers or resized, and draws again when
   * any row's placeholders changed. A row without U+10EEEE costs one string search. Nothing is read while turned off.
   */
  private scan(start: number, end: number, all = false): void {
    if (!this.enabled) return;
    const started = performance.now();
    const buffer = this.terminal.buffer.active;
    const rows = this.terminal.rows;
    if (all || buffer.viewportY !== this.viewportY || buffer.type !== this.bufferType || this.runs.length !== rows) {
      this.viewportY = buffer.viewportY;
      this.bufferType = buffer.type;
      this.runs = Array.from({ length: rows }, () => NONE);
      start = 0;
      end = rows - 1;
      all = true;
    }
    const cell = buffer.getNullCell();
    let changed = all;
    for (let y = Math.max(0, start); y <= Math.min(end, rows - 1); y++) {
      const line = buffer.getLine(buffer.viewportY + y);
      const runs = line ? this.read(line, cell) : NONE;
      if (runs.length === 0 && this.runs[y].length === 0) continue;
      this.runs[y] = runs;
      changed = true;
    }
    // A new font or display scaling can keep every run where it was and still move every cell; only a painted overlay asks.
    if (!changed && this.painted && this.geometry() !== this.drawnGeometry) changed = true;
    this.stats.scans++;
    this.stats.rowsScanned += Math.max(0, Math.min(end, rows - 1) - Math.max(0, start) + 1);
    this.stats.scanMs += performance.now() - started;
    if (changed) this.draw();
  }

  private geometry(): string {
    return `${this.screen.clientWidth}x${this.screen.clientHeight}/${this.terminal.cols}x${this.terminal.rows}@${window.devicePixelRatio || 1}`;
  }

  /** The placeholder runs of one buffer line, with kitty's rules for cells whose row or column diacritic is left out. */
  private read(line: IBufferLine, reuse: IBufferCell): readonly Run[] {
    if (!line.translateToString(true).includes(PLACEHOLDER_TEXT)) return NONE;
    const runs: Run[] = [];
    let left: { color: number; row: number; col: number; high: number } | null = null;
    for (let x = 0; x < line.length; x++) {
      const cell = line.getCell(x, reuse);
      if (!cell) break;
      const chars = cell.getChars();
      if (chars.codePointAt(0) !== PLACEHOLDER) { left = null; continue; }
      // The ID: 24 bits of a truecolor foreground, or a 256-colour index; a third diacritic gives its top byte.
      const color = cell.isFgRGB() || cell.isFgPalette() ? cell.getFgColor() : 0;
      const marks: number[] = [];
      for (let index = 2; index < chars.length;) {
        const code = chars.codePointAt(index)!;
        marks.push(DIACRITIC_NUMBER.get(code) ?? -1);
        index += code > 0xffff ? 2 : 1;
      }
      if (marks.includes(-1)) { left = null; continue; }
      const previous = left !== null && left.color === color ? left : null;
      let row: number, col: number, high: number;
      if (marks.length === 0) [row, col, high] = previous ? [previous.row, previous.col + 1, previous.high] : [0, 0, 0];
      else if (marks.length === 1) {
        row = marks[0];
        [col, high] = previous && previous.row === row ? [previous.col + 1, previous.high] : [0, 0];
      } else if (marks.length === 2) {
        [row, col] = marks;
        high = previous && previous.row === row && previous.col === col - 1 ? previous.high : 0;
      } else [row, col, high] = marks;
      left = { color, row, col, high };
      const id = (high & 0xff) * 0x1000000 + color;
      const last = runs[runs.length - 1];
      if (last && last.id === id && last.row === row && last.x + last.length === x && last.col + last.length === col) last.length++;
      else runs.push({ id, row, col, x, length: 1 });
    }
    return runs;
  }

  /** xterm sizes its screen to exactly the cell grid, so the cell is the screen's size over the grid's. */
  private cellSize(): { width: number; height: number } {
    return { width: this.screen.clientWidth / Math.max(1, this.terminal.cols), height: this.screen.clientHeight / Math.max(1, this.terminal.rows) };
  }

  /**
   * Draws the image's slice for every run, and nothing else: the cells keep whatever background xterm gave them, which is
   * right whatever the program did to the terminal's colours (Hermes's skin sets the default background with OSC 11), and the
   * font draws nothing for the placeholder characters. Each image is fitted to its placement's box with its aspect ratio
   * kept and centred, as kitty fits it, and drawn whole under a clip of the run's cells, so rows join without seams.
   */
  private draw(): void {
    const started = performance.now();
    const { canvas, context } = this;
    if (!this.enabled || !this.runs.some(runs => runs.length > 0)) {
      if (this.painted) {
        context.clearRect(0, 0, canvas.width, canvas.height);
        canvas.hidden = true;
        this.painted = false;
      }
      this.stats.placementsDrawn = 0;
      this.stats.runsDrawn = 0;
      this.drawnRuns = [];
      return;
    }
    const ratio = window.devicePixelRatio || 1;
    const width = this.screen.clientWidth, height = this.screen.clientHeight;
    const pixelWidth = Math.round(width * ratio), pixelHeight = Math.round(height * ratio);
    if (canvas.width !== pixelWidth || canvas.height !== pixelHeight) {
      canvas.width = pixelWidth;
      canvas.height = pixelHeight;
      canvas.style.width = width + 'px';
      canvas.style.height = height + 'px';
    } else {
      context.clearRect(0, 0, pixelWidth, pixelHeight);
    }
    canvas.hidden = false;
    this.painted = true;
    this.drawnGeometry = this.geometry();
    context.imageSmoothingEnabled = true;
    context.imageSmoothingQuality = 'high';
    const cell = this.cellSize();
    const cellWidth = cell.width * ratio, cellHeight = cell.height * ratio;
    // Each image under a clip of its run's cells rounded to whole pixels, so neighbouring rows meet without overlap.
    const screen = this.screen.getBoundingClientRect();
    const drawn = new Set<number>();
    const drawnRuns: KittyImagesCheck['runs'] = [];
    let runsDrawn = 0;
    for (let y = 0; y < this.runs.length; y++) {
      const top = Math.round(y * cellHeight), bottom = Math.round((y + 1) * cellHeight);
      for (const run of this.runs[y]) {
        const left = Math.round(run.x * cellWidth), right = Math.round((run.x + run.length) * cellWidth);
        const key = this.shown.get(run.id);
        const image = key === undefined ? undefined : this.frames.get(key);
        if (!image) continue;
        const { bitmap } = image;
        const cols = image.cols || Math.max(1, Math.ceil(bitmap.width / cell.width));
        const rows = image.rows || Math.max(1, Math.ceil(bitmap.height / cell.height));
        if (run.row >= rows || run.col >= cols) continue;
        const boxWidth = cols * cellWidth, boxHeight = rows * cellHeight;
        const scale = Math.min(boxWidth / bitmap.width, boxHeight / bitmap.height);
        const drawWidth = Math.round(bitmap.width * scale), drawHeight = Math.round(bitmap.height * scale);
        const originX = Math.round((run.x - run.col) * cellWidth + (boxWidth - drawWidth) / 2);
        const originY = Math.round((y - run.row) * cellHeight + (boxHeight - drawHeight) / 2);
        context.save();
        context.beginPath();
        context.rect(left, top, right - left, bottom - top);
        context.clip();
        context.drawImage(bitmap, originX, originY, drawWidth, drawHeight);
        context.restore();
        drawn.add(run.id);
        runsDrawn++;
        if (drawnRuns.length < 64) {
          drawnRuns.push({ id: run.id, x: screen.left + run.x * cell.width, y: screen.top + y * cell.height, width: run.length * cell.width, height: cell.height });
        }
      }
    }
    // Least recently drawn first: an image on the screen is the last to be evicted.
    for (const id of drawn) {
      const key = this.shown.get(id)!;
      this.shown.delete(id);
      this.shown.set(id, key);
    }
    const elapsed = performance.now() - started;
    this.drawnRuns = drawnRuns;
    this.stats.placementsDrawn = drawn.size;
    this.stats.runsDrawn = runsDrawn;
    this.stats.draws++;
    this.stats.drawMs += elapsed;
    this.stats.lastDrawMs = elapsed;
  }
}
