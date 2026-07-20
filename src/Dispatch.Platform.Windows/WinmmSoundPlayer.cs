using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Dispatch.Core.Audio;
using Microsoft.Extensions.Logging;

namespace Dispatch.Platform.Windows;

/// <summary>
/// Plays WAV audio through winmm, the multimedia API present on every Windows
/// install.
/// </summary>
/// <remarks>
/// <c>PlaySound</c> with <c>SND_MEMORY | SND_ASYNC</c> plays a WAV straight from
/// a byte array without touching disk and returns immediately, which is exactly
/// what a fire-and-forget intro cue wants. No package, no bundled engine — the
/// fade is baked into the audio data, so no volume control is needed either.
///
/// <para>
/// The played bytes are pinned for the lifetime of the sound. winmm reads the
/// buffer asynchronously, so letting the GC move or collect it mid-play would
/// crackle or crash; the handle is freed when the next sound starts or on
/// dispose.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WinmmSoundPlayer : ISoundPlayer, IDisposable
{
    private const uint SndAsync = 0x0001;
    private const uint SndMemory = 0x0004;
    private const uint SndPurge = 0x0040;

    private readonly ILogger<WinmmSoundPlayer> _logger;
    private readonly object _gate = new();
    private GCHandle _pinned;
    private bool _disposed;

    /// <summary>Constructs the player.</summary>
    public WinmmSoundPlayer(ILogger<WinmmSoundPlayer> logger)
    {
        _logger = logger;
        IsAvailable = OperatingSystem.IsWindows();
    }

    /// <inheritdoc />
    public bool IsAvailable { get; }

    /// <inheritdoc />
    public void Play(byte[] wavBytes)
    {
        if (!IsAvailable || _disposed || wavBytes is null || wavBytes.Length == 0)
        {
            return;
        }

        lock (_gate)
        {
            // Free whatever was pinned before, so a second sound replaces the
            // first rather than leaking its buffer.
            ReleasePinned();

            _pinned = GCHandle.Alloc(wavBytes, GCHandleType.Pinned);

            try
            {
                if (!NativeMethods.PlaySound(_pinned.AddrOfPinnedObject(), IntPtr.Zero, SndMemory | SndAsync))
                {
                    _logger.LogDebug("PlaySound returned false; the WAV may be malformed");
                    ReleasePinned();
                }
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                _logger.LogDebug(ex, "winmm is unavailable; sound disabled");
                ReleasePinned();
            }
        }
    }

    /// <inheritdoc />
    public void Stop()
    {
        if (!IsAvailable)
        {
            return;
        }

        lock (_gate)
        {
            try
            {
                NativeMethods.PlaySound(IntPtr.Zero, IntPtr.Zero, SndPurge);
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                // Nothing to stop if winmm is not there.
            }

            ReleasePinned();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            ReleasePinned();
        }
    }

    private void ReleasePinned()
    {
        if (_pinned.IsAllocated)
        {
            _pinned.Free();
        }
    }

    [SupportedOSPlatform("windows")]
    private static class NativeMethods
    {
        [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern bool PlaySound(IntPtr data, IntPtr module, uint flags);
    }
}
