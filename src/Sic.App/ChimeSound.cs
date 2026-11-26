using System.Runtime.InteropServices;
using Sic.Core.Alerts;

namespace Sic.App;

/// <summary>
/// Plays a <see cref="Sound"/> through winmm's PlaySound, from memory and without waiting. It plays in SIC's own audio session,
/// so the Windows volume mixer lists it under SIC; SND_SYSTEM would put it with the system sounds, which many people mute. A
/// sound's image is pinned the first time it plays and kept for the life of the process, because an asynchronous play reads it
/// until the sound has finished; a Reload makes new sounds, whose images are pinned in turn. PlaySound stops a sound of this
/// process still sounding before it starts the next.
/// </summary>
internal static class ChimeSound
{
    private const uint Async = 0x0001, NoDefault = 0x0002, Memory = 0x0004;
    private static readonly Dictionary<byte[], nint> Pinned = new(ReferenceEqualityComparer.Instance);

    /// <summary>Starts the sound and returns at once; false when no audio device took it.</summary>
    internal static bool Play(Sound sound)
    {
        var image = sound.Image;
        if (!Pinned.TryGetValue(image, out var address)) Pinned[image] = address = GCHandle.Alloc(image, GCHandleType.Pinned).AddrOfPinnedObject();
        return PlaySound(address, 0, Memory | Async | NoDefault);
    }

    /// <summary>
    /// Plays the chime's length of silence and waits for it to finish: whether the audio device takes the chime's format,
    /// without a sound, for the smoke. It blocks for the chime's length, so call it off the UI thread.
    /// </summary>
    internal static bool PlaySilence()
    {
        var silence = GCHandle.Alloc(ReadyChime.Render(0), GCHandleType.Pinned);
        try { return PlaySound(silence.AddrOfPinnedObject(), 0, Memory | NoDefault); }
        finally { silence.Free(); }
    }

    [DllImport("winmm.dll", EntryPoint = "PlaySoundW", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PlaySound(nint sound, nint module, uint flags);
}