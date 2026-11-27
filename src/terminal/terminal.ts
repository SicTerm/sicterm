import { Terminal, type ITheme } from '@xterm/xterm';
import { FitAddon } from '@xterm/addon-fit';
import { SearchAddon, type ISearchOptions } from '@xterm/addon-search';
import { UnicodeGraphemesAddon } from '@xterm/addon-unicode-graphemes';
import { WebLinksAddon } from '@xterm/addon-web-links';
import { KittyImages, type KittyAgainResult, type KittyImageAgainMessage, type KittyImageMessage, type KittyImagesCheck } from './kittyImages';
import { decodeBase64 } from './base64';
import placeholderFont from 'sic:placeholder-font';

type Bridge = {
  postMessage(message: unknown): void;
  postMessageWithAdditionalObjects(message: unknown, additionalObjects: ArrayLike<unknown>): void;
  addEventListener(type: 'message', listener: (event: MessageEvent) => void): void;
};
type Appearance = {
  theme?: Record<string, unknown>;
  fontFamily?: string;
  fontSize?: number;
  searchMatch?: string;
  searchActive?: string;
  accent?: string;
  cursorStyle?: string;
  cursorBlink?: boolean;
  scrollback?: number;
  copyOnSelect?: boolean;
  petImages?: boolean;
};
type Options = { cursorStyle: string; cursorInactiveStyle: string; cursorBlink: boolean; scrollback: number; copyOnSelect: boolean };
type CursorStyle = 'bar' | 'block' | 'underline';
type CursorCheck = { focused: boolean; classes: string; animation: string; reducedMotion: boolean };
type DropCheck = { accepted: boolean; outlined: boolean; outline: string; cleared: boolean };
type FindOptions = { backward?: boolean; incremental?: boolean };
type KeyModifiers = { ctrlKey?: boolean; shiftKey?: boolean; altKey?: boolean; code?: string };
type Neighbors = { left: boolean; right: boolean; up: boolean; down: boolean };
declare global {
  interface Window {
    chrome?: { webview?: Bridge };
    sic: {
      input(data: string): void;
      paste(text: string): void;
      selection(): string;
      columnOf(text: string): number;
      rowOf(text: string): number;
      windowsPty(): { backend?: string; buildNumber?: number };
      clearSelection(): void;
      clear(): void;
      screenText(): string;
      dimensions(): { columns: number; rows: number };
      focus(): void;
      find(query: string, options?: FindOptions): boolean;
      clearFind(): void;
      setAppearance(settings: Appearance): void;
      appearance(): { background: string; foreground: string };
      lineHeight(): number;
      font(): { family: string; loaded: boolean; cellWidth: number };
      key(name: string, modifiers?: KeyModifiers): boolean;
      neighbors(): Neighbors;
      drop(text: string): DropCheck;
      outlineDrop(on: boolean): void;
      options(): Options;
      cursor(): CursorCheck;
      render(data: string): void;
      selectAndRelease(text: string): string;
      programCopied(): boolean;
      forgetGesture(): void;
      pasteEvent(text: string): boolean;
      image(message: KittyImageMessage): Promise<boolean>;
      imageAgain(message: KittyImageAgainMessage): KittyAgainResult;
      deleteImage(id: number): void;
      petImagesOn(on: boolean): void;
      petImages(): KittyImagesCheck;
    };
  }
}

const bridge = window.chrome?.webview;
const post = (message: unknown): void => { bridge?.postMessage(message); };
window.addEventListener('error', event => post({ type: 'renderer-error', message: event.message }));
window.addEventListener('unhandledrejection', event => post({ type: 'renderer-error', message: String(event.reason) }));

const HEX = /^#[0-9a-f]{6}([0-9a-f]{2})?$/i;
const query = new URLSearchParams(location.search);
const initialBackground = HEX.test('#' + (query.get('bg') ?? '')) ? '#' + query.get('bg') : '#141414';
const initialForeground = HEX.test('#' + (query.get('fg') ?? '')) ? '#' + query.get('fg') : '#cccccc';
// The font and size from Settings, in the address, so the terminal is built in them and the grid it reports ready with is the
// pane's own; sent only afterwards, as they used to be, a font other than the page's made every start resize the pseudoconsole.
const requestedFont = query.get('font') ?? '';
const initialFontFamily = requestedFont.length > 0 && requestedFont.length <= 128 ? requestedFont : '"Cascadia Mono", "Cascadia Code", Consolas, monospace';
const requestedSize = Number(query.get('size'));
const initialFontSize = Number.isInteger(requestedSize) && requestedSize >= 8 && requestedSize <= 48 ? requestedSize : 14;
let theme: ITheme = { background: initialBackground, foreground: initialForeground, cursor: initialForeground };
let themeSent = '';
let searchMatch = '#264f78';
let copyOnSelect = false;
let searchActive = '#4cc2ff';
const firstFamily = (family: string): string => '"' + (family.split(',')[0] ?? '').trim().replace(/["']/g, '') + '"';

const paintBackground = (color: string): void => {
  document.documentElement.style.backgroundColor = color;
  document.documentElement.style.setProperty('--terminal-background', color);
  document.body.style.backgroundColor = color;
};
paintBackground(initialBackground);

// A pane without keyboard focus draws the inactive style, which xterm leaves as a hollow box whatever the chosen cursor, so a
// cursor changed in Settings (where the focus is) would not show until the pane was clicked. The inactive cursor keeps the
// chosen shape instead, never blinking; a block stays hollow, the usual sign of a terminal that is not taking keys.
const inactiveCursor = (style: CursorStyle): 'outline' | CursorStyle => style === 'block' ? 'outline' : style;

// Kitty's image placeholder, U+10EEEE, and the combining marks that number its rows and columns have no glyph in any usual
// font. The browser would draw a missing-glyph box for each in the cell's foreground colour (the image ID's), two boxes for a
// row's first cell since it cannot attach the mark to a missing glyph, and xterm's DOM rows sit a pixel or so off the cell
// grid, so painting over them never quite covered them (docs/decisions/pet-images.md). A font of blank glyphs for exactly
// those characters, built into the page (placeholderFont.mjs) and listed after the chosen font, draws nothing for them; the
// image overlay draws over the cells' own background. Loaded before the terminal measures its font, so no width is cached
// for the boxes. Should the font fail to load, the cells show as boxes under the image, as they did before.
const PLACEHOLDER_FONT = 'SIC Placeholders';
const withPlaceholderFont = (family: string): string => `${family}, "${PLACEHOLDER_FONT}"`;
try {
  document.fonts.add(await new FontFace(PLACEHOLDER_FONT, placeholderFont).load());
} catch (error) {
  post({ type: 'renderer-error', message: 'The placeholder font did not load: ' + String(error) });
}
// The chosen font's faces, as setFont loads them, before the terminal measures its cells: a face of the page's own
// (terminal.css) loads only when asked for, and one Windows has resolves at once.
try {
  const face = `${initialFontSize}px ${firstFamily(initialFontFamily)}`;
  await Promise.all([document.fonts.load(face), document.fonts.load('bold ' + face)]);
} catch { /* The fallback measures the cells, as a font that fails to load from Settings leaves it. */ }

const terminal = new Terminal({
  cursorBlink: true,
  cursorStyle: 'bar',
  cursorInactiveStyle: inactiveCursor('bar'),
  fontFamily: withPlaceholderFont(initialFontFamily),
  fontSize: initialFontSize,
  lineHeight: 1,
  scrollback: 10000,
  allowProposedApi: true, // the search addon highlights matches through registerDecoration, still a proposed API in xterm 6
  overviewRuler: { width: 10 },
  theme
});
// Whether Settings has the cursor blink on, for the reduced-motion rule in terminal.css: a blink a program asks for itself
// (DECSCUSR) is let through only when Settings allows one. The host sends the setting with the first appearance.
document.body.classList.add('cursor-blink');
const fit = new FitAddon();
const search = new SearchAddon();
terminal.loadAddon(fit);
terminal.loadAddon(search);
// Character widths as the console and the agents count them: an emoji such as 🔌 or 📖 takes two columns, and so does a symbol
// followed by VS16 (⚠️). xterm's own table is Unicode 6, which gives every emoji one column, so each row holding one was a
// column short of what ConPTY's console host and the program had written, and a later repaint of part of that row landed a
// column off (October 6, 2026: Hermes's tool list overwritten with letters out of place). See docs/decisions/terminal-text-width.md.
terminal.loadAddon(new UnicodeGraphemesAddon());
terminal.loadAddon(new WebLinksAddon((event, uri) => {
  if (event.ctrlKey && /^https?:\/\//i.test(uri)) post({ type: 'link', uri });
}));
terminal.open(document.getElementById('terminal')!);
// Hermes's pet through kitty's Unicode placeholders; the host takes the images out of the stream (Settings → Pet images,
// told before the first output). See kittyImages.ts.
const images = new KittyImages(terminal, post);
terminal.onData(data => post({ type: 'input', data }));
terminal.onResize(({ cols, rows }) => post({ type: 'resize', columns: cols, rows }));
terminal.onBell(() => post({ type: 'bell' }));
terminal.onTitleChange(title => post({ type: 'title', title: title.slice(0, 200) }));
search.onDidChangeResults(({ resultIndex, resultCount }) => post({ type: 'search-results', index: resultIndex, count: resultCount }));

// Output tells the host the agent may be working (docs/decisions/agent-presence.md); throttled so a burst posts once. Output
// inside the throttle window posts once when it ends, so a burst just after a post is not lost: the redraw that answers a focus
// report posts at once, and the host takes it for an echo, not work; the work that follows within the window must still reach it.
// Each post says how long ago the latest output was, so the host dates the output and not the post.
let lastActivity = 0;
let lastOutput = 0;
let trailingActivity: number | undefined;
const postActivity = (): void => {
  lastActivity = performance.now();
  post({ type: 'activity', age: Math.round(lastActivity - lastOutput) });
};
const noteActivity = (): void => {
  lastOutput = performance.now();
  const since = lastOutput - lastActivity;
  if (since >= 750) { postActivity(); return; }
  trailingActivity ??= window.setTimeout(() => { trailingActivity = undefined; postActivity(); }, 750 - since);
};

// Which sides of this pane another pane of its tab lies on, sent by the host whenever the tab's tree changes; the strip's side is
// always set, up since the tab strip lies above the top row, or down with the tab bar at the bottom. Alt+Arrow toward one of them
// moves the focus there. Toward the tab's other edges, or in a tab of one pane, the key reaches the terminal, as Windows Terminal
// passes it on, so an agent's own Alt+Left, Alt+Right and Alt+Down (word moves in Codex and Hermes, Codex's prompt stack) still
// work wherever no pane lies that way; with the tab bar at the bottom, Alt+Up takes Alt+Down's place there.
let neighbors: Neighbors = { left: false, right: false, up: false, down: false };
const sides = new Map<string, keyof Neighbors>([['ArrowLeft', 'left'], ['ArrowRight', 'right'], ['ArrowUp', 'up'], ['ArrowDown', 'down']]);

// Application chords. Plain Ctrl+T/W/K reach the agents' own interfaces; Ctrl+Tab and Ctrl+digits
// have no terminal meaning, so they are safe to take. Ctrl+C copies only when text is selected.
// The function keys, as requested, even where a program has its own use for them: F1 shows the keyboard reference
// (PSReadLine's ShowCommandHelp gives way), F2 renames the pane of a split tab or the tab of one pane (PSReadLine's
// SwitchPredictionView and Codex's warnings give way), Ctrl+F2 renames the tab, F5 resumes all tabs (Ctrl+F5 this workspace's
// alone), F6 consolidates them and F11 fills the display. With any other modifier they still reach the terminal.
const shortcutFor = (event: KeyboardEvent): string | null => {
  const { ctrlKey: ctrl, shiftKey: shift, altKey: alt, key, code } = event;
  const upper = key.length === 1 ? key.toUpperCase() : key;
  if (!shift && !alt && !ctrl) {
    if (key === 'F1') return 'help';
    if (key === 'F2') return 'rename';
    if (key === 'F5') return 'resume-all';
    if (key === 'F6') return 'consolidate';
    if (key === 'F11') return 'full-screen';
  }
  if (key === 'F2' && ctrl && !shift && !alt) return 'rename-tab';
  if (key === 'F5' && ctrl && !shift && !alt) return 'resume-workspace';
  if (ctrl && !alt && key === 'Tab') return shift ? 'previous-tab' : 'next-tab';
  if (ctrl && shift && !alt) {
    switch (upper) {
      case 'T': return 'new-tab';
      case 'W': return 'close-tab';
      case 'D': return 'duplicate-tab';
      case 'R': return 'resume-all';
      case 'F': return 'find';
      case 'E': return 'open-folder';
      case 'S': return 'toggle-panel';
      case 'A': return 'workspace-list';
      case 'Z': return 'zoom';
      case 'C': return 'copy';
      case 'V': return 'paste';
      case 'K': return 'search';
    }
  }
  if (ctrl && !shift && !alt) {
    if (/^[1-9]$/.test(key)) return 'tab-' + key;
    if (upper === 'C' && terminal.hasSelection()) return 'copy';
    // Ctrl+V pastes as Ctrl+Shift+V does, as in Windows Terminal. The host sends ^V on to the program when the clipboard has
    // no text or files (an image only), so an agent that reads the clipboard itself on Ctrl+V still gets the key.
    if (upper === 'V') return 'paste-or-key';
  }
  // Shift+Insert pastes too, as in Windows Terminal, through the host like the rest; left to the browser, it reached xterm's own
  // paste, which sends the clipboard on unfiltered.
  if (shift && !ctrl && !alt && key === 'Insert') return 'paste';
  // Alt+1…9 as well, the modifier the pane keys use; PSReadLine's and prompt_toolkit's Alt+digit repeat counts give way. The
  // digit row only (event.code), so Alt with the numeric keypad still types Windows' Alt codes.
  if (alt && !ctrl && !shift && /^Digit[1-9]$/.test(code)) return 'tab-' + code.slice(5);
  // Ctrl+Alt+1…9 picks a workspace, Ctrl+Alt+Left and Right step through them, and with Shift the tab goes along. On a layout
  // where Ctrl+Alt is AltGr the digit row types nothing anyway; a program's own Ctrl+Alt+Arrow gives way.
  if (ctrl && alt && !shift && /^Digit[1-9]$/.test(code)) return 'workspace-' + code.slice(5);
  if (ctrl && alt && (key === 'ArrowLeft' || key === 'ArrowRight')) return (shift ? 'move-tab-' : '') + (key === 'ArrowRight' ? 'next-workspace' : 'previous-workspace');
  // Windows Terminal's pair: Plus splits into columns (a new pane to the right), Minus into rows (below).
  if (alt && shift && !ctrl && (key === '+' || key === '=')) return 'split';
  if (alt && shift && !ctrl && (key === '-' || key === '_')) return 'split-down';
  // Windows Terminal's pane keys, taken only toward a pane.
  const side = sides.get(key);
  if (alt && !shift && !ctrl && side !== undefined && neighbors[side]) return 'focus-' + side;
  return null;
};
terminal.attachCustomKeyEventHandler(event => {
  if (event.type !== 'keydown') return true;
  const name = shortcutFor(event);
  if (name === null) return true;
  event.preventDefault();
  if (name === 'copy') {
    post({ type: 'shortcut', name, text: terminal.getSelection() });
    terminal.clearSelection();
  } else {
    post({ type: 'shortcut', name });
  }
  return false;
});

// When the user last pressed a key or a mouse button in this pane, or dragged with one held: a program's copy that follows such
// an action within moments was asked for, and an agent says "copied" itself. Captured on the window, ahead of xterm.
let lastGesture = -Infinity;
const noteGesture = (): void => { lastGesture = performance.now(); };
for (const type of ['keydown', 'mousedown', 'mouseup'] as const) window.addEventListener(type, noteGesture, true);
window.addEventListener('mousemove', event => { if (event.buttons !== 0) noteGesture(); }, true);

// A program's own copy (OSC 52, "ESC ] 52 ; <targets> ; <base64> BEL"): Claude Code and Codex select with the mouse
// themselves while mouse reporting is on, and put the text on the clipboard this way, as Windows Terminal lets them. Writes
// only: a read ("?") is never answered, so nothing on the clipboard reaches the program. Windows has one clipboard, so the
// targets are ignored. Anything a pane prints can send one, so the host is told how long after the user's last action in the
// pane it came (none: null), and tells of a copy nobody asked for (docs/decisions/clipboard.md). A copy the host could not take
// in one message (it takes 1 MB) is refused and reported. programCopied says whether the program has copied since the last
// press of the left button, so the menu's Copy offers that text only for the selection just made.
const PROGRAM_COPY_LIMIT = 1_000_000;
let programCopied = false;
terminal.parser.registerOscHandler(52, data => {
  const separator = data.indexOf(';');
  const payload = data.slice(separator + 1);
  if (separator < 0 || payload === '' || payload === '?') return true;
  if (payload.length > PROGRAM_COPY_LIMIT * 2) { post({ type: 'program-copy-refused', length: Math.floor(payload.length * 3 / 4) }); return true; }
  let text: string;
  try { text = new TextDecoder('utf-8', { fatal: true }).decode(decodeBase64(payload)); }
  catch { return true; }
  if (text === '') return true;
  if (JSON.stringify(text).length > PROGRAM_COPY_LIMIT) { post({ type: 'program-copy-refused', length: text.length }); return true; }
  programCopied = true;
  const since = performance.now() - lastGesture;
  post({ type: 'program-copy', text, sinceGesture: Number.isFinite(since) ? Math.round(since) : null });
  return true;
});
window.addEventListener('mousedown', event => { if (event.button === 0) programCopied = false; }, true);

// A program's own state (OSC 7501, the Program Status Protocol: "ESC ] 7501 ; key=value:key=value ST"), which Claude Code,
// Codex and OpenCode report, working, blocked, done, idle or error, in place of the titles and the output the host otherwise
// judges them by (docs/decisions/program-status.md). The body goes to the host as it came, within the protocol's limit, and the
// host reads and checks it (ProgramStatus.Parse); nothing in it is shown as text. A program may first ask whether the terminal
// listens ("?") and report only when answered, as Claude Code does: the answer is the same body back, through the input channel,
// which the host tells from typing by its ESC. Answered for every pane, so an agent started by hand in a PowerShell pane is
// heard too. Claude Code, once answered, leaves its status glyph off its title, so the answer and the reading go together.
const PROGRAM_STATUS_LIMIT = 4096;
terminal.parser.registerOscHandler(7501, data => {
  if (data === '?') post({ type: 'input', data: '\u001b]7501;?\u001b\\' });
  else if (data.length > 0 && data.length <= PROGRAM_STATUS_LIMIT) post({ type: 'program-status', body: data });
  return true;
});

// Every paste goes through the host, which takes control characters out as Windows Terminal does and, in a PowerShell pane,
// asks before sending several lines (docs/decisions/clipboard.md). A paste the browser makes itself would reach xterm, which
// sends the clipboard on as it is, so it stops here and the host pastes the clipboard instead, as for Ctrl+V.
window.addEventListener('paste', event => {
  event.preventDefault();
  event.stopPropagation();
  post({ type: 'shortcut', name: 'paste' });
}, true);

// The host draws the context menu in XAML; coordinates are CSS pixels, which match the control's DIPs.
document.addEventListener('contextmenu', event => {
  event.preventDefault();
  // With mouse reporting on (Claude Code, OpenCode, Codex), a plain drag goes to the program, which selects in its own way:
  // Copy takes the text the program copied for that selection, or a terminal selection made with Shift+drag, and the menu
  // says so when there is neither.
  post({ type: 'contextmenu', x: event.clientX, y: event.clientY, hasSelection: terminal.hasSelection(),
    mouseReporting: terminal.modes.mouseTrackingMode !== 'none', programCopied });
});
// Right-click opens that menu and nothing else. A program that turns on mouse reporting would also get the right button from
// xterm, and Claude Code pastes on its release, so the press and release stop here, before xterm sees them. The contextmenu
// event is separate and still fires.
for (const type of ['mousedown', 'mouseup'] as const) {
  window.addEventListener(type, event => { if (event.button === 2) event.stopPropagation(); }, true);
}

// Copy on select: once, when the mouse button is released over a selection, never on each step of the drag. A double- or
// triple-click selects on mousedown, so its release copies the word or line too. The selection stays, as Windows Terminal leaves
// it. The last text sent is kept for the verification hook.
let lastCopied = '';
document.addEventListener('mouseup', event => {
  if (event.button !== 0 || !copyOnSelect || !terminal.hasSelection()) return;
  const text = terminal.getSelection();
  if (!text) return;
  lastCopied = text;
  post({ type: 'shortcut', name: 'copy', text });
});

// Files and folders dragged from Explorer, or text: an accent outline while the drag is over the terminal, and on drop the
// host pastes the paths quoted for PowerShell, never with Enter. A real drag from Explorer did not reach these listeners
// (October 4, 2026: a not-allowed cursor and no outline), as WebView2 under WinUI's visual hosting expects the host to forward
// it; TerminalView takes such a drop itself and lights the outline through window.sic. Only the host can read a dropped File's full path, so the File
// objects themselves go across. dragenter and dragleave fire for every child element crossed; the depth says whether the drag
// is still somewhere inside.
const droppable = (event: DragEvent): boolean => {
  const types = event.dataTransfer?.types ?? [];
  return types.includes('Files') || types.includes('text/plain');
};
let dragDepth = 0;
const outlineDrop = (on: boolean): void => { document.body.classList.toggle('drop-target', on); };
document.addEventListener('dragenter', event => {
  if (!droppable(event)) return;
  dragDepth++;
  outlineDrop(true);
});
document.addEventListener('dragleave', event => {
  if (!droppable(event) || --dragDepth > 0) return;
  dragDepth = 0;
  outlineDrop(false);
});
document.addEventListener('dragover', event => {
  event.preventDefault();
  if (event.dataTransfer) event.dataTransfer.dropEffect = droppable(event) ? 'copy' : 'none';
});
document.addEventListener('drop', event => {
  event.preventDefault();
  dragDepth = 0;
  outlineDrop(false);
  const transfer = event.dataTransfer;
  if (!transfer) return;
  if (transfer.files.length > 0) {
    bridge?.postMessageWithAdditionalObjects({ type: 'drop' }, transfer.files);
    return;
  }
  const text = transfer.getData('text/plain');
  if (text) post({ type: 'drop', text });
});

const isHex = (value: unknown): value is string => typeof value === 'string' && HEX.test(value);
const decorations = (): ISearchOptions['decorations'] => ({
  matchBackground: searchMatch, matchOverviewRuler: searchMatch,
  activeMatchBackground: searchActive, activeMatchColorOverviewRuler: searchActive
});

// A font of the page's own (terminal.css) loads only when asked for, and the terminal measures its cells when the font is set:
// set before the font had loaded, the cells would be measured in the fallback. So the faces are loaded first, regular and bold;
// a font Windows has needs no loading and is set at once. Only the latest request is applied.
let fontRequest = 0;
const setFont = (family: string): void => {
  const request = ++fontRequest;
  const face = `${terminal.options.fontSize ?? 14}px ${firstFamily(family)}`;
  void Promise.all([document.fonts.load(face), document.fonts.load('bold ' + face)]).catch(() => undefined).then(() => {
    if (request !== fontRequest) return;
    terminal.options.fontFamily = withPlaceholderFont(family);
    fit.fit();
  });
};

const setAppearance = (settings: Appearance): void => {
  if (settings.theme && isHex(settings.theme.background)) {
    const next: Record<string, string> = {};
    for (const [name, value] of Object.entries(settings.theme)) if (isHex(value)) next[name] = value;
    // The same colors again (every appearance carries them, and the palette is fixed) are not set: xterm takes any new theme
    // object as a change and repaints every cell for it.
    const sent = JSON.stringify(next);
    if (sent !== themeSent) {
      themeSent = sent;
      theme = next as ITheme;
      terminal.options.theme = theme;
      paintBackground(theme.background!);
    }
  }
  if (isHex(settings.searchMatch)) searchMatch = settings.searchMatch.slice(0, 7);
  if (isHex(settings.searchActive)) searchActive = settings.searchActive.slice(0, 7);
  if (isHex(settings.accent)) document.documentElement.style.setProperty('--terminal-accent', settings.accent.slice(0, 7));
  if (typeof settings.fontSize === 'number' && settings.fontSize >= 8 && settings.fontSize <= 48) terminal.options.fontSize = settings.fontSize;
  if (typeof settings.fontFamily === 'string' && settings.fontFamily.length <= 128) setFont(settings.fontFamily);
  if (settings.cursorStyle === 'bar' || settings.cursorStyle === 'block' || settings.cursorStyle === 'underline') {
    terminal.options.cursorStyle = settings.cursorStyle;
    terminal.options.cursorInactiveStyle = inactiveCursor(settings.cursorStyle);
  }
  if (typeof settings.cursorBlink === 'boolean') {
    terminal.options.cursorBlink = settings.cursorBlink;
    document.body.classList.toggle('cursor-blink', settings.cursorBlink);
  }
  if (typeof settings.scrollback === 'number' && Number.isInteger(settings.scrollback) && settings.scrollback >= 1000 && settings.scrollback <= 50000) terminal.options.scrollback = settings.scrollback;
  if (typeof settings.copyOnSelect === 'boolean') copyOnSelect = settings.copyOnSelect;
  if (typeof settings.petImages === 'boolean') images.setEnabled(settings.petImages);
  fit.fit();
};

bridge?.addEventListener('message', event => {
  const message = event.data as { type?: string; data?: string; buildNumber?: number } & Appearance & Partial<Neighbors>;
  if (message.type === 'output' && typeof message.data === 'string') {
    terminal.write(decodeBase64(message.data));
    noteActivity();
  } else if (message.type === 'focus') {
    terminal.focus();
  } else if (message.type === 'settings') {
    setAppearance(message);
  } else if (message.type === 'neighbors') {
    neighbors = { left: message.left === true, right: message.right === true, up: message.up === true, down: message.down === true };
  } else if (message.type === 'pty' && typeof message.buildNumber === 'number') {
    // The console host under this pane, sent before its first output. A ConPTY host adds the rows of a taller pane at the
    // bottom of its buffer and leaves its cursor where it was; told so, xterm does the same. Otherwise it pulls scrollback down
    // into them, every line then sits rows below where the host has it, and the host's next absolute move (its echo of a typed
    // line, PSReadLine's redraw) lands mid-screen over old lines (docs/decisions/console-host.md). From build 21376 xterm also
    // reflows and keeps its line-wrapping guesses for older ConPTY off.
    terminal.options.windowsPty = { backend: 'conpty', buildNumber: message.buildNumber };
  } else if (message.type === 'kitty-image') {
    void images.add(event.data as KittyImageMessage);
  } else if (message.type === 'kitty-image-again') {
    images.again(event.data as KittyImageAgainMessage);
  } else if (message.type === 'kitty-delete') {
    images.remove(Number((event.data as { id?: unknown }).id) || 0);
  } else if (message.type === 'pet-images') {
    images.setEnabled((event.data as { on?: unknown }).on === true);
  }
});

window.sic = {
  input: data => terminal.input(data, true),
  // Every paste, filtered by the host (TerminalView.PasteAsync); xterm adds the bracketed paste markers when the program asked for them.
  paste: text => terminal.paste(text),
  selection: () => terminal.getSelection(),
  clearSelection: () => terminal.clearSelection(),
  clear: () => terminal.clear(),
  // Logical lines: a row the terminal wrapped onto the next is joined back, so text that wraps at a narrow pane's edge still
  // reads as written. At 150% scaling a pane of a six-way split wrapped a 37-character reply, and the smoke never found it.
  screenText: () => {
    const buffer = terminal.buffer.active;
    const lines: string[] = [];
    for (let row = 0; row < buffer.length; row++) {
      const line = buffer.getLine(row);
      const text = line?.translateToString(buffer.getLine(row + 1)?.isWrapped !== true) ?? '';
      if (line?.isWrapped && lines.length > 0) lines[lines.length - 1] += text;
      else lines.push(text);
    }
    return lines.join('\n');
  },
  dimensions: () => ({ columns: terminal.cols, rows: terminal.rows }),
  // Verification hook: the column the text starts at on the last buffer line that holds it, -1 when none does. A wide
  // character's second cell holds no text, so the columns come from the cells, not from the line's string.
  columnOf: text => {
    const buffer = terminal.buffer.active;
    for (let row = buffer.length - 1; row >= 0; row--) {
      const line = buffer.getLine(row);
      if (!line) continue;
      let joined = '';
      const cells: number[] = [];
      for (let column = 0; column < line.length; column++) {
        const chars = line.getCell(column)?.getChars() ?? '';
        for (let index = 0; index < chars.length; index++) cells.push(column);
        joined += chars;
      }
      const at = joined.indexOf(text);
      if (at >= 0) return cells[at];
    }
    return -1;
  },
  // Verification hook: the screen row (0 at the top of the viewport) of the last buffer line holding the text, negative when
  // that line is in the scrollback, and -1000 when no line holds it.
  rowOf: text => {
    const buffer = terminal.buffer.active;
    for (let row = buffer.length - 1; row >= 0; row--) {
      if (buffer.getLine(row)?.translateToString(true).includes(text)) return row - buffer.baseY;
    }
    return -1000;
  },
  windowsPty: () => ({ ...terminal.options.windowsPty }),
  focus: () => terminal.focus(),
  find: (text, options) => {
    if (!text) { search.clearDecorations(); return false; }
    const searchOptions: ISearchOptions = { caseSensitive: false, regex: false, incremental: options?.incremental === true, decorations: decorations() };
    return options?.backward === true ? search.findPrevious(text, searchOptions) : search.findNext(text, searchOptions);
  },
  clearFind: () => { search.clearDecorations(); terminal.clearSelection(); },
  setAppearance,
  appearance: () => ({ background: theme.background ?? initialBackground, foreground: theme.foreground ?? initialForeground }),
  lineHeight: () => terminal.options.lineHeight ?? 1,
  // Verification hook: the font the terminal draws with, whether its faces have loaded (always so for a font Windows has), and
  // the width of an M in it as the page measures it.
  font: () => {
    const family = terminal.options.fontFamily ?? '';
    const face = `${terminal.options.fontSize ?? 14}px ${firstFamily(family)}`;
    const context = document.createElement('canvas').getContext('2d')!;
    context.font = face + ', monospace';
    return { family, loaded: document.fonts.check(face) && document.fonts.check('bold ' + face), cellWidth: context.measureText('M').width };
  },
  options: () => ({
    cursorStyle: terminal.options.cursorStyle ?? 'bar', cursorInactiveStyle: terminal.options.cursorInactiveStyle ?? 'outline',
    cursorBlink: terminal.options.cursorBlink === true, scrollback: terminal.options.scrollback ?? 0, copyOnSelect
  }),
  // Verification hook: the cursor as drawn, its classes, the animation the page's styles leave on it, and whether the page
  // is being told to reduce motion.
  cursor: () => {
    const element = document.querySelector('.xterm-rows .xterm-cursor');
    return {
      focused: document.querySelector('.xterm-rows')?.classList.contains('xterm-focus') === true,
      classes: element?.className ?? '',
      animation: element ? getComputedStyle(element).animationName : '',
      reducedMotion: matchMedia('(prefers-reduced-motion: reduce)').matches
    };
  },
  // Verification hook: renders text as if the program had printed it, for control sequences the fixture cannot send.
  render: data => terminal.write(data),
  // Verification hook: whether the program has copied (OSC 52) since the last press of the left button.
  programCopied: () => programCopied,
  // Verification hook: as if the user had not pressed a key or a button in this pane, so a copy that follows is one nobody asked for.
  forgetGesture: () => { lastGesture = -Infinity; },
  // Verification hook: a paste event carrying the text, on xterm's textarea, as the browser fires one; true when the page
  // stopped it before xterm could paste the text itself.
  pasteEvent: text => {
    const data = new DataTransfer();
    data.setData('text/plain', text);
    const target = terminal.textarea ?? document.body;
    return !target.dispatchEvent(new ClipboardEvent('paste', { clipboardData: data, bubbles: true, cancelable: true }));
  },
  // Verification hook: selects the last buffer line containing the text, then releases the mouse over the terminal the way a
  // drag ends, through the page's own mouseup listener. Returns what that listener sent to be copied.
  selectAndRelease: text => {
    const buffer = terminal.buffer.active;
    for (let row = buffer.length - 1; row >= 0; row--) {
      const line = buffer.getLine(row)?.translateToString(true) ?? '';
      const column = line.indexOf(text);
      if (column < 0) continue;
      terminal.select(column, row, text.length);
      lastCopied = '';
      (document.querySelector('.xterm-screen') ?? document.body).dispatchEvent(new MouseEvent('mouseup', { bubbles: true, button: 0 }));
      terminal.clearSelection();
      return lastCopied;
    }
    return '';
  },
  // Verification hook: a keydown through xterm's own key pipeline; true when an application chord took it.
  key: (name, modifiers) => !(terminal.textarea?.dispatchEvent(new KeyboardEvent('keydown', {
    key: name, code: modifiers?.code ?? '', bubbles: true, cancelable: true,
    ctrlKey: modifiers?.ctrlKey === true, shiftKey: modifiers?.shiftKey === true, altKey: modifiers?.altKey === true
  })) ?? true),
  // Verification hook: the sides Alt+Arrow is taken toward, as the page last heard them.
  neighbors: () => ({ ...neighbors }),
  // Verification hook: a text drop through the page's own drag listeners, which a drop from another app reaches the same way.
  // Accepted means dragover was cancelled; Chromium ignores dropEffect on a DataTransfer that no real drag created.
  drop: text => {
    const transfer = new DataTransfer();
    transfer.setData('text/plain', text);
    const target = document.querySelector('.xterm-screen') ?? document.body;
    const fire = (type: string): boolean => !target.dispatchEvent(new DragEvent(type, { dataTransfer: transfer, bubbles: true, cancelable: true }));
    fire('dragenter');
    const accepted = fire('dragover');
    const outlined = document.body.classList.contains('drop-target');
    const outline = getComputedStyle(document.body, '::after').borderTopColor;
    fire('drop');
    return { accepted, outlined, outline, cleared: !document.body.classList.contains('drop-target') };
  },
  // The host's own drop target (TerminalView) shows the same outline as a drag that reaches the page.
  outlineDrop: on => outlineDrop(on),
  // Verification hooks: an image, a frame sent again and a delete as the host would post them, the switch, and what is held and drawn.
  image: message => images.add(message),
  imageAgain: message => images.again(message),
  deleteImage: id => images.remove(id),
  petImagesOn: on => images.setEnabled(on),
  petImages: () => images.check()
};

// Refit once the size settles: the panel slides and the window is dragged over many frames, and every fit resizes the
// pseudoconsole, which makes an agent's interface repaint in full each time.
let fitTimer: number | undefined;
new ResizeObserver(() => {
  if (fitTimer !== undefined) clearTimeout(fitTimer);
  fitTimer = window.setTimeout(() => {
    fitTimer = undefined;
    if (document.documentElement.clientWidth > 0 && document.documentElement.clientHeight > 0) fit.fit();
  }, 40);
}).observe(document.getElementById('terminal')!);
await document.fonts.ready;
// Ready on the first frame, or on a short timer when no frame comes: a page loaded while the window is hidden (minimized
// or in the tray) runs no animation frames until it is shown, so a ready that waited for one never came and the host gave
// up on the start. A hidden page's timers still run, though Chromium may slow them down.
let reported = false;
const reportReady = (): void => {
  if (reported) return;
  reported = true;
  if (document.documentElement.clientWidth > 0 && document.documentElement.clientHeight > 0) fit.fit();
  terminal.focus();
  post({ type: 'ready', columns: terminal.cols, rows: terminal.rows });
};
requestAnimationFrame(reportReady);
window.setTimeout(reportReady, 100);
