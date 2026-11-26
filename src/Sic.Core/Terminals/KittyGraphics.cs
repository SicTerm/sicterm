using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Sic.Core.Terminals;

/// <summary>
/// One kitty graphics protocol command (<c>ESC _ G keys ; payload ESC \</c>), its chunks joined: the control keys of its first
/// chunk and the base64 payload of every chunk, as sent. See https://sw.kovidgoyal.net/kitty/graphics-protocol/ and
/// docs/decisions/pet-images.md.
/// </summary>
public sealed class KittyGraphicsCommand
{
    /// <summary>The first eight bytes of every PNG (<c>89 50 4E 47 0D 0A 1A 0A</c>) as base64 begin with these characters.</summary>
    private static ReadOnlySpan<byte> PngBase64 => "iVBORw0KGgo"u8;

    private readonly byte[] _payload;
    private string? _payloadText;

    internal KittyGraphicsCommand(IReadOnlyDictionary<char, string> keys, byte[] payload)
    {
        Keys = keys;
        _payload = payload;
    }

    /// <summary>The control keys of the command's first chunk, each a single letter with its value as written.</summary>
    public IReadOnlyDictionary<char, string> Keys { get; }
    /// <summary>The base64 payload, every chunk's piece joined in order; empty when the command carries none.</summary>
    public string Payload => _payloadText ??= Encoding.ASCII.GetString(_payload);
    /// <summary>The payload's ASCII bytes, as <see cref="Payload"/> spells them; no string is made for a frame posted by reference.</summary>
    public ReadOnlySpan<byte> PayloadBytes => _payload;
    internal byte[] PayloadArray => _payload;

    /// <summary><c>a</c>: t or T transmit, p place, d delete, q query. A command without it is taken as a transmission.</summary>
    public char Action => Letter('a') ?? 't';
    /// <summary><c>i</c>, the image ID; 0 when the command names none.</summary>
    public uint ImageId => Keys.TryGetValue('i', out var value) && uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : 0;
    /// <summary><c>c</c> and <c>r</c>, the cell box the image is fitted to; 0 when not given.</summary>
    public int Columns => Number('c');
    public int Rows => Number('r');
    /// <summary><c>f</c>: 24 RGB, 32 RGBA (kitty's default), 100 PNG.</summary>
    public int Format => Keys.ContainsKey('f') ? Number('f') : 32;
    /// <summary><c>t</c>: d direct (the default; the payload is the data), f file, t temporary file, s shared memory.</summary>
    public char Medium => Letter('t') ?? 'd';
    /// <summary><c>U=1</c>: a virtual placement, shown wherever the program prints Unicode placeholder cells.</summary>
    public bool Virtual => Number('U') == 1;
    /// <summary><c>d</c>, what a delete removes; kitty's code treats a delete without it as <c>a</c>, everything on the screen.</summary>
    public char Delete => Letter('d') ?? 'a';

    private char? Letter(char key) => Keys.TryGetValue(key, out var value) && value.Length == 1 ? value[0] : null;
    private int Number(char key) => Keys.TryGetValue(key, out var value) && int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number) ? number : 0;

    /// <summary>
    /// What SIC shows for this command. A PNG sent directly with an image ID for a virtual placement (shown through Unicode
    /// placeholders, the only way SIC shows an image) is an image; a delete by ID, or of everything, is a delete. Nothing is read
    /// from a file or shared memory, whatever the program asks, and raw RGB(A) data, images placed at the cursor, queries and
    /// placements by cell are left alone; nothing is answered either, so a program that probes for graphics finds none (Hermes
    /// is told by its configuration instead).
    /// </summary>
    internal KittyPageAction PageAction => Action switch
    {
        't' or 'T' when Virtual && Medium == 'd' && Format == 100 && ImageId != 0 && PayloadBytes.StartsWith(PngBase64) => KittyPageAction.Image,
        'd' when Delete is 'i' or 'I' && ImageId != 0 => KittyPageAction.Delete,
        'd' when Delete is 'a' or 'A' => KittyPageAction.Delete,
        _ => KittyPageAction.None
    };

    /// <summary>The image a delete removes: its ID, or 0 for every image.</summary>
    internal uint DeletedId => Delete is 'i' or 'I' ? ImageId : 0;

    /// <summary>
    /// The message the terminal page takes for this command on its own, or null when SIC shows nothing for it (<see cref="PageAction"/>):
    /// <c>kitty-image</c> {id, cols, rows, png}, or <c>kitty-delete</c> {id}, 0 meaning every image. A pane posts through
    /// <see cref="KittyImagePoster"/>, which sends an image it already sent as a reference instead.
    /// </summary>
    public string? ToPageMessage() => PageAction switch
    {
        KittyPageAction.Image => KittyPageMessages.Image(ImageId, Columns, Rows, PayloadBytes),
        KittyPageAction.Delete => KittyPageMessages.Delete(DeletedId),
        _ => null
    };
}

internal enum KittyPageAction { None, Image, Delete }

/// <summary>
/// The terminal page's kitty messages, written by hand rather than serialized: no reflection, and the base64 goes out as it is
/// (the default encoder would write every '+' as +). The payload is base64 by the splitter's check, so nothing in it needs
/// escaping.
/// </summary>
internal static class KittyPageMessages
{
    private static readonly JsonWriterOptions Options = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary><c>kitty-image</c> {id, cols, rows, key?, resent?, png}: an image to decode and show, kept under its key when it has one.</summary>
    internal static string Image(uint id, int columns, int rows, ReadOnlySpan<byte> png, long key = 0, bool resent = false)
    {
        var buffer = new ArrayBufferWriter<byte>(128 + png.Length);
        using (var writer = new Utf8JsonWriter(buffer, Options))
        {
            writer.WriteStartObject();
            writer.WriteString("type", "kitty-image");
            writer.WriteNumber("id", id);
            writer.WriteNumber("cols", Math.Max(0, columns));
            writer.WriteNumber("rows", Math.Max(0, rows));
            if (key > 0) writer.WriteNumber("key", key);
            if (resent) writer.WriteBoolean("resent", true);
            writer.WriteString("png", png);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary><c>kitty-image-again</c> {id, key}: show the image the page holds under the key.</summary>
    internal static string Again(uint id, long key) =>
        "{\"type\":\"kitty-image-again\",\"id\":" + id.ToString(CultureInfo.InvariantCulture) + ",\"key\":" + key.ToString(CultureInfo.InvariantCulture) + "}";

    /// <summary><c>kitty-delete</c> {id}: forget the image, or every image for 0.</summary>
    internal static string Delete(uint id) => "{\"type\":\"kitty-delete\",\"id\":" + id.ToString(CultureInfo.InvariantCulture) + "}";
}

/// <summary>
/// Turns one pane's kitty graphics commands into the terminal page's messages, and remembers the images it posted lately so
/// that one sent again crosses as a reference to the copy the page already decoded. Hermes sends the whole PNG of every frame
/// of its pet, 5.5 times a second, cycling through up to six frames per state; after a state's first loop only references
/// cross (docs/decisions/pet-images.md).
/// <para>
/// Every image posted gets a key, never reused. A repeat of one remembered, the same image ID, cell box and payload compared
/// byte for byte, is posted as <c>kitty-image-again</c> {id, key}. The page keeps decoded frames by key within its own caps; a
/// key it no longer holds it asks for (<see cref="Resend"/>), and the image goes again in full. At most
/// <see cref="DefaultCapacity"/> images and <see cref="DefaultByteLimit"/> bytes of payload are remembered, the least recently
/// posted forgotten first; a delete forgets its image's. Thread-safe: the output pump posts, the page's requests arrive on the
/// UI thread.
/// </para>
/// </summary>
public sealed class KittyImagePoster
{
    /// <summary>Hermes's pet has up to seven states of up to six frames; the states a session moves between fit with room to spare.</summary>
    public const int DefaultCapacity = 48;
    /// <summary>8 MiB of base64: every state of Hermes's pet at scale 1.0 is about 2.7 MB.</summary>
    public const int DefaultByteLimit = 8 * 1024 * 1024;

    private sealed record Remembered(long Key, uint Id, int Columns, int Rows, int Hash, byte[] Payload);

    private readonly Lock _gate = new();
    /// <summary>Least recently posted first.</summary>
    private readonly List<Remembered> _remembered = [];
    private readonly int _capacity;
    private readonly int _byteLimit;
    private long _bytes;
    private long _lastKey;

    public KittyImagePoster(int capacity = DefaultCapacity, int byteLimit = DefaultByteLimit)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        ArgumentOutOfRangeException.ThrowIfNegative(byteLimit);
        _capacity = capacity;
        _byteLimit = byteLimit;
    }

    /// <summary>How many images are remembered, and their payloads' bytes.</summary>
    public int Count { get { lock (_gate) return _remembered.Count; } }
    public long Bytes { get { lock (_gate) return _bytes; } }

    /// <summary>The page's message for <paramref name="command"/>, or null when SIC shows nothing for it.</summary>
    public string? ToPageMessage(KittyGraphicsCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        switch (command.PageAction)
        {
            case KittyPageAction.Delete:
                Forget(command.DeletedId);
                return KittyPageMessages.Delete(command.DeletedId);
            case KittyPageAction.Image:
            {
                var payload = command.PayloadArray;
                var (id, columns, rows) = (command.ImageId, Math.Max(0, command.Columns), Math.Max(0, command.Rows));
                var hash = new HashCode();
                hash.AddBytes(payload);
                var code = hash.ToHashCode();
                long key;
                lock (_gate)
                {
                    for (var index = _remembered.Count - 1; index >= 0; index--)
                    {
                        var entry = _remembered[index];
                        if (entry.Hash != code || entry.Id != id || entry.Columns != columns || entry.Rows != rows || !entry.Payload.AsSpan().SequenceEqual(payload)) continue;
                        _remembered.RemoveAt(index);
                        _remembered.Add(entry);
                        return KittyPageMessages.Again(id, entry.Key);
                    }
                    key = ++_lastKey;
                    // One larger than the whole budget is posted with its key but not remembered.
                    if (payload.Length <= _byteLimit && _capacity > 0)
                    {
                        _remembered.Add(new Remembered(key, id, columns, rows, code, payload));
                        _bytes += payload.Length;
                        while (_remembered.Count > _capacity || _bytes > _byteLimit)
                        {
                            _bytes -= _remembered[0].Payload.Length;
                            _remembered.RemoveAt(0);
                        }
                    }
                }
                return KittyPageMessages.Image(id, columns, rows, payload, key);
            }
            default:
                return null;
        }
    }

    /// <summary>
    /// The image remembered under <paramref name="key"/> in full, marked as resent (the page shows it only if that key is still
    /// the newest it was asked to show for the image), or null when it is no longer remembered.
    /// </summary>
    public string? Resend(long key)
    {
        Remembered? found = null;
        lock (_gate)
        {
            foreach (var entry in _remembered)
                if (entry.Key == key) { found = entry; break; }
        }
        return found is null ? null : KittyPageMessages.Image(found.Id, found.Columns, found.Rows, found.Payload, found.Key, resent: true);
    }

    /// <summary>Forgets every image, as when the page has dropped its own (Settings → Pet images off).</summary>
    public void Clear() => Forget(0);

    private void Forget(uint id)
    {
        lock (_gate)
        {
            _remembered.RemoveAll(entry => id == 0 || entry.Id == id);
            _bytes = _remembered.Sum(entry => (long)entry.Payload.Length);
        }
    }
}

/// <summary>
/// Takes kitty graphics commands out of a pseudoconsole's output before it reaches xterm.js, which swallows every APC sequence
/// unread. Text passes through unchanged and in order; any other APC, and anything malformed, is left in the text for xterm to
/// swallow as before. A command may arrive across any number of chunks, cut anywhere, and a transmission across any number of
/// <c>m=1</c> commands; the payload of one transmission is capped, and one that grows past the cap is dropped whole, its
/// remaining chunks with it. One instance per output stream; not thread-safe.
/// </summary>
public sealed class KittyGraphicsSplitter
{
    /// <summary>4 MiB of base64, a 3 MiB image. Hermes's pet frames are a few KiB at its default scale.</summary>
    public const int DefaultPayloadLimit = 4 * 1024 * 1024;
    /// <summary>The control keys of a real command fit in well under this; anything longer is not one.</summary>
    private const int ControlLimit = 256;
    private const byte Escape = 0x1B;
    private static readonly byte[] ApcStart = [Escape, (byte)'_'];
    private static readonly SearchValues<byte> Base64 = SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/="u8);
    private static readonly SearchValues<byte> PayloadStops = SearchValues.Create([Escape, 0x18, 0x1A]);

    private enum State
    {
        /// <summary>Plain text.</summary>
        Text,
        /// <summary>An ESC ended the last chunk; held until the next byte says whether an APC begins.</summary>
        Escape,
        /// <summary>ESC _ seen (and held); a G makes it a kitty graphics command.</summary>
        ApcIntroducer,
        /// <summary>Inside the control keys, up to the ';' or the end of the command.</summary>
        Control,
        /// <summary>Inside the payload.</summary>
        Payload,
        /// <summary>Inside a command being dropped (malformed, or over the cap), up to its end.</summary>
        Discard,
        /// <summary>An ESC inside a command: a backslash after it ends the command, anything else cuts it short.</summary>
        StringEscape
    }

    private readonly int _payloadLimit;
    /// <summary>The text of the chunk being split, kept between calls so a chunk inside a large image allocates nothing.</summary>
    private readonly ArrayBufferWriter<byte> _text = new(8192);
    private readonly byte[] _control = new byte[ControlLimit];
    private int _controlLength;
    private State _state = State.Text;
    /// <summary>Whether the command being read is dropped at its end (set when it turns out malformed or over the cap).</summary>
    private bool _dropping;
    /// <summary>Whether the command being read had its keys parsed (at its ';'); a command without a payload parses them at its end.</summary>
    private bool _keysParsed;
    /// <summary>The keys of the transmission under way (its first chunk's), null when none is.</summary>
    private Dictionary<char, string>? _pending;
    /// <summary>Whether the chunk being read says more follow (<c>m=1</c>).</summary>
    private bool _more;
    private byte[] _payload = [];
    private int _payloadLength;

    public KittyGraphicsSplitter(int payloadLimit = DefaultPayloadLimit)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(payloadLimit);
        _payloadLimit = payloadLimit;
    }

    /// <summary>
    /// True between commands, with no byte held back: the stream can go on to the terminal without this splitter from here, as
    /// it does once Settings → Pet images is turned off. Inside a command the rest of it must still come through here, or its
    /// payload would reach the terminal as text.
    /// </summary>
    public bool Idle => _state == State.Text;

    /// <summary>
    /// Splits one chunk of output: returns the text for the terminal, which is <paramref name="chunk"/> itself when it holds no
    /// part of a command (no copy), and adds every command completed in it to <paramref name="commands"/> in order. Up to two
    /// bytes at the end of a chunk (ESC, or ESC _) are held until the next chunk shows whether a command starts there; xterm
    /// would wait for the next byte at that point as well.
    /// </summary>
    public byte[] Split(byte[] chunk, ICollection<KittyGraphicsCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        ArgumentNullException.ThrowIfNull(commands);
        if (_state == State.Text && (chunk.Length == 0 || (chunk[^1] != Escape && chunk.AsSpan().IndexOf(ApcStart) < 0))) return chunk;

        var text = _text;
        text.ResetWrittenCount();
        var input = chunk.AsSpan();
        var index = 0;
        while (index < input.Length)
        {
            switch (_state)
            {
                case State.Text:
                {
                    var rest = input[index..];
                    var start = rest.IndexOf(ApcStart);
                    if (start >= 0)
                    {
                        // An ESC right before the command's ESC is void in xterm's parser (ESC restarts an escape); left in the
                        // text it would join whatever follows the command.
                        text.Write(rest[..(start > 0 && rest[start - 1] == Escape ? start - 1 : start)]);
                        index += start + 2;
                        _state = State.ApcIntroducer;
                    }
                    else if (rest[^1] == Escape)
                    {
                        text.Write(rest[..^1]);
                        index = input.Length;
                        _state = State.Escape;
                    }
                    else
                    {
                        text.Write(rest);
                        index = input.Length;
                    }
                    break;
                }
                case State.Escape:
                    // A second ESC voids the held one, as it does in xterm's parser, and is held in its place.
                    if (input[index] == Escape) index++;
                    else if (input[index] == (byte)'_') { index++; _state = State.ApcIntroducer; }
                    else { text.Write([Escape]); _state = State.Text; }
                    break;
                case State.ApcIntroducer:
                    if (input[index] == (byte)'G') { index++; BeginCommand(); }
                    // An ESC ends the empty APC before it and starts an escape of its own.
                    else if (input[index] == Escape) { index++; _state = State.Escape; }
                    else { text.Write(ApcStart); _state = State.Text; }
                    break;
                case State.Control:
                {
                    var value = input[index];
                    if (value == Escape) { index++; _state = State.StringEscape; break; }
                    if (value is 0x18 or 0x1A) { AbortCommand(); break; }
                    index++;
                    if (value == (byte)';')
                    {
                        _keysParsed = true;
                        if (StartPayload()) _state = State.Payload;
                        else DropCommand();
                        break;
                    }
                    if (_controlLength == ControlLimit) { DropCommand(); break; }
                    _control[_controlLength++] = value;
                    break;
                }
                case State.Payload:
                {
                    var rest = input[index..];
                    var stop = rest.IndexOfAny(PayloadStops);
                    var piece = stop < 0 ? rest : rest[..stop];
                    if (piece.IndexOfAnyExcept(Base64) >= 0 || !AppendPayload(piece)) { DropCommand(); break; }
                    index += piece.Length;
                    if (stop < 0) break;
                    if (rest[stop] == Escape) { index++; _state = State.StringEscape; }
                    else AbortCommand();
                    break;
                }
                case State.Discard:
                {
                    var rest = input[index..];
                    var stop = rest.IndexOfAny(PayloadStops);
                    if (stop < 0) { index = input.Length; break; }
                    index += stop;
                    if (input[index] == Escape) { index++; _state = State.StringEscape; }
                    else AbortCommand();
                    break;
                }
                case State.StringEscape:
                    if (input[index] == (byte)'\\')
                    {
                        index++;
                        EndCommand(commands);
                        _state = State.Text;
                    }
                    else
                    {
                        // ESC inside a string ends it and starts an escape sequence, as in xterm's parser: the command is lost
                        // and the ESC is read again with the byte after it, which may begin the next command.
                        AbortCommand();
                        _state = State.Escape;
                    }
                    break;
            }
        }
        return text.WrittenCount == 0 ? [] : text.WrittenSpan.ToArray();
    }

    private void BeginCommand()
    {
        _controlLength = 0;
        _dropping = false;
        _keysParsed = false;
        _more = false;
        _state = State.Control;
    }

    /// <summary>The command being read is malformed or over the cap: the rest of it is skipped, and the transmission it belongs to is dropped.</summary>
    private void DropCommand()
    {
        DropPending();
        _dropping = true;
        _state = State.Discard;
    }

    /// <summary>A command cut short by CAN, SUB or a stray ESC: it is dropped, and so is the transmission it belonged to.</summary>
    private void AbortCommand()
    {
        DropPending();
        _state = State.Text;
    }

    /// <summary>
    /// The control keys are read: decides whether this chunk starts a transmission or continues the one under way, and keeps its
    /// payload accordingly. False when the keys are malformed or the chunk continues nothing.
    /// </summary>
    private bool StartPayload()
    {
        if (!TryParseKeys(_control.AsSpan(0, _controlLength), out var keys)) { DropPending(); return false; }
        _more = keys.TryGetValue('m', out var more) && more == "1";
        var continuation = keys.Keys.All(key => key is 'm' or 'q');
        if (continuation) return _pending is not null;
        // A new first chunk abandons whatever transmission was under way.
        _pending = keys;
        _payloadLength = 0;
        return true;
    }

    private bool AppendPayload(ReadOnlySpan<byte> piece)
    {
        if (piece.IsEmpty) return true;
        if (_payloadLength + piece.Length > _payloadLimit) { DropPending(); return false; }
        if (_payloadLength + piece.Length > _payload.Length)
            Array.Resize(ref _payload, Math.Min(_payloadLimit, Math.Max(_payloadLength + piece.Length, Math.Max(4096, _payload.Length * 2))));
        piece.CopyTo(_payload.AsSpan(_payloadLength));
        _payloadLength += piece.Length;
        return true;
    }

    private void EndCommand(ICollection<KittyGraphicsCommand> commands)
    {
        if (_dropping) return;
        // A command without a ';' carries keys and no payload (a delete, say).
        if (!_keysParsed && !StartPayload()) return;
        if (_more || _pending is null) return;
        commands.Add(new KittyGraphicsCommand(_pending, _payloadLength == 0 ? [] : _payload.AsSpan(0, _payloadLength).ToArray()));
        DropPending();
    }

    private void DropPending()
    {
        _pending = null;
        _payloadLength = 0;
        // A large image's buffer is not kept for the life of the pane.
        if (_payload.Length > 256 * 1024) _payload = [];
    }

    /// <summary><c>k=v,k=v</c>: each key one ASCII letter, each value letters, digits or a leading minus.</summary>
    private static bool TryParseKeys(ReadOnlySpan<byte> control, out Dictionary<char, string> keys)
    {
        keys = new Dictionary<char, string>();
        if (control.IsEmpty) return true;
        foreach (var range in control.Split((byte)','))
        {
            var pair = control[range];
            if (pair.Length < 3 || !char.IsAsciiLetter((char)pair[0]) || pair[1] != (byte)'=') return false;
            var value = pair[2..];
            for (var position = 0; position < value.Length; position++)
            {
                var character = (char)value[position];
                if (!char.IsAsciiLetterOrDigit(character) && !(character == '-' && position == 0 && value.Length > 1)) return false;
            }
            keys[(char)pair[0]] = Encoding.ASCII.GetString(value);
        }
        return true;
    }
}
