namespace Dispatch.Core.Audio;

/// <summary>Plays a short sound effect.</summary>
/// <remarks>
/// Platform-specific — audio output is a Windows API here — so the interface
/// lives in Core and the implementation in the Windows project. A silent stub
/// satisfies it elsewhere, keeping the intro and the UI testable on any machine.
/// </remarks>
public interface ISoundPlayer
{
    /// <summary>Whether sound can actually be played on this machine.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Plays a WAV held in memory, returning immediately. A second call
    /// replaces whatever is playing rather than layering over it.
    /// </summary>
    void Play(byte[] wavBytes);

    /// <summary>Stops whatever is playing.</summary>
    void Stop();
}

/// <summary>Used where audio is unavailable. Reports itself unavailable and does nothing.</summary>
public sealed class SilentSoundPlayer : ISoundPlayer
{
    /// <inheritdoc />
    public bool IsAvailable => false;

    /// <inheritdoc />
    public void Play(byte[] wavBytes)
    {
    }

    /// <inheritdoc />
    public void Stop()
    {
    }
}
