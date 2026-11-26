using System.Text;
using System.Text.Json;
using Sic.Core.Terminals;
using Xunit;

namespace Sic.Core.Tests;

public sealed class KittyGraphicsTests
{
    private const string Esc = "\u001b";
    private const string St = "\u001b\\";
    /// <summary>A 1×1 PNG.</summary>
    private const string Png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);
    private static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes);

    /// <summary>A transmission written the way Hermes writes one (agent/pet/render.py, _kitty_apc): 4096-character chunks, keys on the first.</summary>
    private static string Apc(string keys, string payload)
    {
        var pieces = new List<string>();
        for (var start = 0; start < payload.Length; start += 4096) pieces.Add(payload.Substring(start, Math.Min(4096, payload.Length - start)));
        if (pieces.Count == 0) pieces.Add("");
        var builder = new StringBuilder();
        for (var index = 0; index < pieces.Count; index++)
            builder.Append(Esc + "_G" + (index == 0 ? keys + "," : "") + "m=" + (index == pieces.Count - 1 ? 0 : 1) + ";" + pieces[index] + St);
        return builder.ToString();
    }

    private static string Frame(uint id, string payload) => Apc($"a=T,U=1,i={id},c=8,r=4,f=100,q=2", payload);

    /// <summary>A PNG-looking payload of the given length: the PNG signature's base64, then filler from the base64 alphabet.</summary>
    private static string LongPng(int length) => Png[..11] + new string('A', length - 11);

    /// <summary>Feeds the chunks in order and returns all text and every command.</summary>
    private static (string Text, List<KittyGraphicsCommand> Commands) Feed(KittyGraphicsSplitter splitter, IEnumerable<byte[]> chunks)
    {
        var text = new List<byte>();
        var commands = new List<KittyGraphicsCommand>();
        foreach (var chunk in chunks) text.AddRange(splitter.Split(chunk, commands));
        return (Encoding.UTF8.GetString(text.ToArray()), commands);
    }

    [Fact]
    public void AChunkWithoutACommandIsReturnedAsItIsWithoutACopy()
    {
        var splitter = new KittyGraphicsSplitter();
        var commands = new List<KittyGraphicsCommand>();
        var chunk = Bytes("plain " + Esc + "[31mred" + Esc + "[0m " + Esc + "]0;title\u0007 " + Esc + "P1$r" + St + " 🔌 done\r\n");
        Assert.Same(chunk, splitter.Split(chunk, commands));
        var empty = Array.Empty<byte>();
        Assert.Same(empty, splitter.Split(empty, commands));
        Assert.Empty(commands);
    }

    [Fact]
    public void ACommandIsTakenOutAndTheTextAroundItKeptInOrder()
    {
        var (text, commands) = Feed(new KittyGraphicsSplitter(), [Bytes("before" + Frame(42, Png) + "after" + Esc + "[0m")]);
        Assert.Equal("beforeafter" + Esc + "[0m", text);
        var command = Assert.Single(commands);
        Assert.Equal('T', command.Action);
        Assert.Equal(42u, command.ImageId);
        Assert.Equal((8, 4), (command.Columns, command.Rows));
        Assert.Equal(100, command.Format);
        Assert.True(command.Virtual);
        Assert.Equal(Png, command.Payload);
    }

    [Fact]
    public void ATransmissionOverSeveralChunksIsJoinedIntoOneCommand()
    {
        var payload = LongPng(10_000);
        var stream = Frame(7, payload);
        Assert.Equal(3, stream.Split(Esc + "_G").Length - 1);
        var (text, commands) = Feed(new KittyGraphicsSplitter(), [Bytes(stream)]);
        Assert.Equal("", text);
        Assert.Equal(payload, Assert.Single(commands).Payload);
        Assert.Equal(7u, commands[0].ImageId);
    }

    [Fact]
    public void ACommandCutAtAnyByteIsTakenTheSame()
    {
        var payload = LongPng(9_000);
        var stream = Bytes("a" + Esc + "[1m" + Frame(3, payload) + "b" + Esc + "_Ga=d,d=i,i=3,q=2" + St + "c" + Esc + "[0m");
        for (var cut = 0; cut <= stream.Length; cut++)
        {
            var (text, commands) = Feed(new KittyGraphicsSplitter(), [stream[..cut], stream[cut..]]);
            Assert.Equal("a" + Esc + "[1mbc" + Esc + "[0m", text);
            Assert.Equal(2, commands.Count);
            Assert.Equal(payload, commands[0].Payload);
            Assert.Equal('d', commands[1].Action);
        }
        var (byteText, byteCommands) = Feed(new KittyGraphicsSplitter(), stream.Select(value => new[] { value }));
        Assert.Equal("a" + Esc + "[1mbc" + Esc + "[0m", byteText);
        Assert.Equal(2, byteCommands.Count);
        Assert.Equal(payload, byteCommands[0].Payload);
    }

    [Fact]
    public void AnEscapeEndingAChunkIsHeldUntilTheNextChunkSaysWhatItBegins()
    {
        var splitter = new KittyGraphicsSplitter();
        var commands = new List<KittyGraphicsCommand>();
        Assert.Equal("abc", Text(splitter.Split(Bytes("abc" + Esc), commands)));
        Assert.Equal(Esc + "[31mred", Text(splitter.Split(Bytes("[31mred"), commands)));
        Assert.Equal("x", Text(splitter.Split(Bytes("x" + Esc + "_"), commands)));
        Assert.Equal(Esc + "_Xother" + St, Text(splitter.Split(Bytes("Xother" + St), commands)));
        Assert.Empty(commands);
    }

    [Theory]
    [InlineData("x" + Esc + "_Xsome other APC" + St + "y")]
    [InlineData("x" + Esc + "_Z" + St + "y")]
    [InlineData(Esc + "_tmux;" + Esc + Esc + "[31m" + St)]
    [InlineData(Esc + "^privacy message" + St + Esc + "Xstart of string" + St)]
    public void AnyOtherApcPassesThroughUntouched(string stream)
    {
        var (text, commands) = Feed(new KittyGraphicsSplitter(), [Bytes(stream)]);
        Assert.Equal(stream, text);
        Assert.Empty(commands);
    }

    [Fact]
    public void ATransmissionOverTheCapIsDroppedWholeAndTheNextOneIsTaken()
    {
        var splitter = new KittyGraphicsSplitter(payloadLimit: 8_192);
        var (text, commands) = Feed(splitter, [Bytes("one" + Frame(1, LongPng(12_000)) + "two"), Bytes(Frame(2, Png) + "three")]);
        Assert.Equal("onetwothree", text);
        Assert.Equal(2u, Assert.Single(commands).ImageId);
    }

    [Fact]
    public void ALargeTransmissionAndASmallOneAfterItAreBothTaken()
    {
        var splitter = new KittyGraphicsSplitter();
        var (_, commands) = Feed(splitter, [Bytes(Frame(1, LongPng(600_000))), Bytes(Frame(2, Png))]);
        Assert.Equal(new uint[] { 1, 2 }, commands.Select(command => command.ImageId).ToArray());
        Assert.Equal(600_000, commands[0].Payload.Length);
        Assert.Equal(Png, commands[1].Payload);
    }

    [Fact]
    public void AnEmptyApcBeforeACommandDoesNotSwallowTheTextAfterIt()
    {
        // ESC _ ESC _ G…: the first APC is ended by the second's ESC; left in the text it would swallow "after" in xterm.
        var (text, commands) = Feed(new KittyGraphicsSplitter(), [Bytes("x" + Esc + "_" + Frame(5, Png) + "after")]);
        Assert.Equal("xafter", text);
        Assert.Single(commands);
    }

    [Theory]
    [InlineData(Esc + "_Ga=T,i=;AAAA" + St)]
    [InlineData(Esc + "_Ga=T,i=1,f=100;iVBO*not base64*" + St)]
    [InlineData(Esc + "_Gnot keys;AAAA" + St)]
    [InlineData(Esc + "_Gm=0;AAAA" + St)]
    [InlineData(Esc + "_Gm=1;AAAA" + St + Esc + "_Gm=0;AAAA" + St)]
    [InlineData(Esc + "_Ga=T,i=1,f=100,m=1;iVBO" + "\u0018" + Esc + "_Gm=0;AAAA" + St)]
    public void MalformedOrOrphanedCommandsAreDroppedWithoutText(string stream)
    {
        var (text, commands) = Feed(new KittyGraphicsSplitter(), [Bytes(stream)]);
        Assert.Empty(commands);
        Assert.Equal(stream.Contains('\u0018') ? "\u0018" : "", text);
    }

    [Fact]
    public void OverlongControlKeysAreNotACommand()
    {
        var (text, commands) = Feed(new KittyGraphicsSplitter(), [Bytes(Esc + "_G" + string.Join(',', Enumerable.Repeat("a=T", 200)) + ";" + Png + St + "ok")]);
        Assert.Empty(commands);
        Assert.Equal("ok", text);
    }

    [Fact]
    public void AStrayEscapeCutsACommandShortAndTheNextCommandIsStillTaken()
    {
        var stream = Esc + "_Ga=T,i=1,f=100,U=1;iVBO" + Esc + Frame(2, Png) + "after";
        var (text, commands) = Feed(new KittyGraphicsSplitter(), [Bytes(stream)]);
        Assert.Equal("after", text);
        Assert.Equal(2u, Assert.Single(commands).ImageId);
    }

    [Fact]
    public void AnEscapeVoidedByTheCommandsOwnIsNotLeftToJoinTheTextAfterIt()
    {
        var (text, commands) = Feed(new KittyGraphicsSplitter(), [Bytes("x" + Esc + Frame(5, Png) + "after")]);
        Assert.Equal("xafter", text);
        Assert.Single(commands);
        var (heldText, heldCommands) = Feed(new KittyGraphicsSplitter(), [Bytes("x" + Esc), Bytes(Frame(5, Png) + "after")]);
        Assert.Equal("xafter", heldText);
        Assert.Single(heldCommands);
    }

    [Fact]
    public void TheTextOfTheRowsAroundAnImageIsUntouched()
    {
        // Hermes prints the placeholder grid as ordinary text: U+10EEEE with a row diacritic on each row's first cell.
        var rows = "\u001b[38;2;0;18;52m\U0010EEEE̅\U0010EEEE\U0010EEEE\r\n\U0010EEEE̍\U0010EEEE\U0010EEEE\u001b[39m";
        var (text, commands) = Feed(new KittyGraphicsSplitter(), [Bytes(Frame(4660, Png) + rows)]);
        Assert.Equal(rows, text);
        Assert.Single(commands);
    }

    [Fact]
    public void ATransmittedPngBecomesAnImageMessageForThePage()
    {
        var (_, commands) = Feed(new KittyGraphicsSplitter(), [Bytes(Frame(4660, Png))]);
        var text = Assert.Single(commands).ToPageMessage()!;
        Assert.Contains("\"png\":\"" + Png + "\"", text);
        using var json = JsonDocument.Parse(text);
        var message = json.RootElement;
        Assert.Equal("kitty-image", message.GetProperty("type").GetString());
        Assert.Equal(4660u, message.GetProperty("id").GetUInt32());
        Assert.Equal(8, message.GetProperty("cols").GetInt32());
        Assert.Equal(4, message.GetProperty("rows").GetInt32());
        Assert.Equal(Png, message.GetProperty("png").GetString());
    }

    [Theory]
    [InlineData("a=d,d=i,i=7,q=2", 7u)]
    [InlineData("a=d,d=I,i=7", 7u)]
    [InlineData("a=d", 0u)]
    [InlineData("a=d,d=A", 0u)]
    public void DeletesByIdOrOfEverythingBecomeDeleteMessages(string keys, uint id)
    {
        var (text, commands) = Feed(new KittyGraphicsSplitter(), [Bytes(Esc + "_G" + keys + St)]);
        Assert.Equal("", text);
        using var json = JsonDocument.Parse(Assert.Single(commands).ToPageMessage()!);
        Assert.Equal("kitty-delete", json.RootElement.GetProperty("type").GetString());
        Assert.Equal(id, json.RootElement.GetProperty("id").GetUInt32());
    }

    [Fact]
    public void TheSplitterIsIdleOnlyBetweenCommands()
    {
        var splitter = new KittyGraphicsSplitter();
        var commands = new List<KittyGraphicsCommand>();
        Assert.True(splitter.Idle);
        var frame = Bytes(Frame(9, LongPng(6_000)));
        splitter.Split(frame[..100], commands);
        Assert.False(splitter.Idle);
        splitter.Split(frame[100..], commands);
        Assert.True(splitter.Idle);
        splitter.Split(Bytes("text" + Esc), commands);
        Assert.False(splitter.Idle);
        Assert.Equal(Esc + "[0m", Text(splitter.Split(Bytes("[0m"), commands)));
        Assert.True(splitter.Idle);
        Assert.Single(commands);
    }

    /// <summary>A pet's frames as Hermes sends them: whole PNGs for one image ID, each a different payload of the same length.</summary>
    private static string[] PetFrames(int count, int length = 9_000) =>
        Enumerable.Range(0, count).Select(index => Png[..11] + new string((char)('B' + index), length - 11)).ToArray();

    private static KittyGraphicsCommand Command(string stream) => Assert.Single(Feed(new KittyGraphicsSplitter(), [Bytes(stream)]).Commands);

    private static (string Type, uint Id, long Key, bool HasPng, bool Resent) Read(string message)
    {
        using var json = JsonDocument.Parse(message);
        var root = json.RootElement;
        return (root.GetProperty("type").GetString()!, root.GetProperty("id").GetUInt32(),
            root.TryGetProperty("key", out var key) ? key.GetInt64() : 0, root.TryGetProperty("png", out _), root.TryGetProperty("resent", out _));
    }

    [Fact]
    public void AfterTheFirstLoopOfAPetsFramesOnlyReferencesArePosted()
    {
        var poster = new KittyImagePoster();
        var frames = PetFrames(6);
        var posted = new List<string>();
        for (var tick = 0; tick < 6 * 5; tick++) posted.Add(poster.ToPageMessage(Command(Frame(25561, frames[tick % 6])))!);
        var first = posted.Take(6).Select(Read).ToArray();
        Assert.All(first, message => Assert.Equal(("kitty-image", 25561u, true, false), (message.Type, message.Id, message.HasPng, message.Resent)));
        Assert.Equal(new long[] { 1, 2, 3, 4, 5, 6 }, first.Select(message => message.Key).ToArray());
        for (var tick = 6; tick < posted.Count; tick++)
        {
            var again = Read(posted[tick]);
            Assert.Equal(("kitty-image-again", 25561u, (long)(tick % 6 + 1), false), (again.Type, again.Id, again.Key, again.HasPng));
            Assert.True(posted[tick].Length < 80, posted[tick]);
        }
        Assert.Equal(6, poster.Count);
        Assert.Equal(6L * 9_000, poster.Bytes);
    }

    [Fact]
    public void TheFullImageMessageCarriesTheKeyAndTheBox()
    {
        var message = new KittyImagePoster().ToPageMessage(Command(Frame(4660, Png)))!;
        Assert.Contains("\"png\":\"" + Png + "\"", message);
        using var json = JsonDocument.Parse(message);
        var root = json.RootElement;
        Assert.Equal((8, 4, 1L), (root.GetProperty("cols").GetInt32(), root.GetProperty("rows").GetInt32(), root.GetProperty("key").GetInt64()));
        Assert.False(root.TryGetProperty("resent", out _));
    }

    [Fact]
    public void TheSamePngForAnotherImageOrCellBoxOrADifferentPayloadIsPostedInFull()
    {
        var poster = new KittyImagePoster();
        var payload = PetFrames(1)[0];
        Assert.Equal(1, Read(poster.ToPageMessage(Command(Frame(1, payload)))!).Key);
        Assert.Equal(2, Read(poster.ToPageMessage(Command(Frame(2, payload)))!).Key);
        Assert.Equal(3, Read(poster.ToPageMessage(Command(Apc("a=T,U=1,i=1,c=9,r=4,f=100,q=2", payload)))!).Key);
        // Same length, same start, one byte different at the end.
        var changed = Read(poster.ToPageMessage(Command(Frame(1, payload[..^1] + "Z")))!);
        Assert.Equal(("kitty-image", 4L), (changed.Type, changed.Key));
        var again = Read(poster.ToPageMessage(Command(Frame(1, payload)))!);
        Assert.Equal(("kitty-image-again", 1L), (again.Type, again.Key));
    }

    [Fact]
    public void ADeleteForgetsItsImagesSoTheNextFrameGoesInFullWithANewKey()
    {
        var poster = new KittyImagePoster();
        var frames = PetFrames(2);
        poster.ToPageMessage(Command(Frame(1, frames[0])));
        poster.ToPageMessage(Command(Frame(2, frames[1])));
        var deleted = Read(poster.ToPageMessage(Command(Esc + "_Ga=d,d=i,i=1,q=2" + St))!);
        Assert.Equal(("kitty-delete", 1u), (deleted.Type, deleted.Id));
        Assert.Equal(1, poster.Count);
        var full = Read(poster.ToPageMessage(Command(Frame(1, frames[0])))!);
        Assert.Equal(("kitty-image", 3L), (full.Type, full.Key));
        Assert.Equal("kitty-image-again", Read(poster.ToPageMessage(Command(Frame(2, frames[1])))!).Type);
        var everything = Read(poster.ToPageMessage(Command(Esc + "_Ga=d" + St))!);
        Assert.Equal(("kitty-delete", 0u), (everything.Type, everything.Id));
        Assert.Equal((0, 0L), (poster.Count, poster.Bytes));
        var after = Read(poster.ToPageMessage(Command(Frame(2, frames[1])))!);
        Assert.Equal(("kitty-image", 4L), (after.Type, after.Key));
    }

    [Fact]
    public void TheLeastRecentlyPostedImageIsForgottenPastTheCapacityOrTheByteLimit()
    {
        var frames = PetFrames(4, length: 1_000);
        var byCount = new KittyImagePoster(capacity: 3);
        foreach (var frame in frames[..3]) byCount.ToPageMessage(Command(Frame(1, frame)));
        // Frame 0 posted again is the most recent; frame 3 then pushes out frame 1, the least recent.
        Assert.Equal("kitty-image-again", Read(byCount.ToPageMessage(Command(Frame(1, frames[0])))!).Type);
        Assert.Equal("kitty-image", Read(byCount.ToPageMessage(Command(Frame(1, frames[3])))!).Type);
        Assert.Equal(3, byCount.Count);
        Assert.Equal("kitty-image-again", Read(byCount.ToPageMessage(Command(Frame(1, frames[0])))!).Type);
        Assert.Equal("kitty-image", Read(byCount.ToPageMessage(Command(Frame(1, frames[1])))!).Type);

        var byBytes = new KittyImagePoster(byteLimit: 2_500);
        foreach (var frame in frames[..3]) byBytes.ToPageMessage(Command(Frame(1, frame)));
        Assert.Equal((2, 2_000L), (byBytes.Count, byBytes.Bytes));
        Assert.Equal("kitty-image", Read(byBytes.ToPageMessage(Command(Frame(1, frames[0])))!).Type);
        // One larger than the whole budget goes in full every time and pushes nothing out.
        var large = PetFrames(1, length: 3_000)[0];
        Assert.Equal(("kitty-image", "kitty-image"), (Read(byBytes.ToPageMessage(Command(Frame(1, large)))!).Type, Read(byBytes.ToPageMessage(Command(Frame(1, large)))!).Type));
        Assert.Equal(2, byBytes.Count);
    }

    [Fact]
    public void AResendIsTheRememberedImageMarkedResentAndNothingOnceItIsForgotten()
    {
        var poster = new KittyImagePoster(capacity: 1);
        var frames = PetFrames(2);
        poster.ToPageMessage(Command(Frame(7, frames[0])));
        var resent = poster.Resend(1)!;
        Assert.Equal(("kitty-image", 7u, 1L, true, true), Read(resent));
        Assert.Contains("\"png\":\"" + frames[0] + "\"", resent);
        Assert.Contains("\"cols\":8,\"rows\":4", resent);
        poster.ToPageMessage(Command(Frame(7, frames[1])));
        Assert.Null(poster.Resend(1));
        Assert.Null(poster.Resend(99));
    }

    [Fact]
    public void ClearForgetsEveryImageAndKeysAreNeverReused()
    {
        var poster = new KittyImagePoster();
        var frames = PetFrames(2);
        poster.ToPageMessage(Command(Frame(1, frames[0])));
        poster.ToPageMessage(Command(Frame(2, frames[1])));
        poster.Clear();
        Assert.Equal((0, 0L), (poster.Count, poster.Bytes));
        var full = Read(poster.ToPageMessage(Command(Frame(1, frames[0])))!);
        Assert.Equal(("kitty-image", 3L), (full.Type, full.Key));
        Assert.Null(poster.ToPageMessage(Command(Esc + "_Ga=q,i=31,s=1,v=1,f=24;AAAA" + St)));
    }

    [Theory]
    [InlineData("a=d,d=p,x=1,y=1", "")]
    [InlineData("a=d,d=i", "")]
    [InlineData("a=q,i=31,s=1,v=1,f=24", "AAAA")]
    [InlineData("a=T,U=1,i=1,f=100,t=f", "L3RtcC9zZWNyZXQ=")]
    [InlineData("a=T,U=1,i=1,f=32,s=1,v=1", "AAAAAA==")]
    [InlineData("a=T,U=1,f=100", Png)]
    [InlineData("a=T,U=1,i=1,f=100", "AAAAAAAAAAAA")]
    [InlineData("a=T,i=1,f=100,c=4,r=2", Png)]
    [InlineData("a=p,U=1,i=1,c=4,r=2", "")]
    public void CommandsSicDoesNotShowGiveNoMessage(string keys, string payload)
    {
        var (_, commands) = Feed(new KittyGraphicsSplitter(), [Bytes(Esc + "_G" + keys + ";" + payload + St)]);
        Assert.Null(Assert.Single(commands).ToPageMessage());
    }
}
