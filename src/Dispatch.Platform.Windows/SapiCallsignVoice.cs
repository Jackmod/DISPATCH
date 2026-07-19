using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Dispatch.Core.Audio;
using Microsoft.Extensions.Logging;

namespace Dispatch.Platform.Windows;

/// <summary>
/// Speaks callsigns through SAPI, the speech engine present on every Windows
/// install.
/// </summary>
/// <remarks>
/// Driven through COM late binding rather than the <c>System.Speech</c>
/// package. That package is a Windows-only dependency that would have to be
/// referenced from a project the rest of the app links against, and it brings
/// nothing here that <c>SAPI.SpVoice</c> does not already provide on the
/// machine.
///
/// <para>
/// Speech is decoration on one button. Every failure path â€” no engine, no
/// voices installed, audio device in use, COM refusing to activate â€” resolves
/// to reporting unavailable rather than throwing, because a callsign that will
/// not read aloud is not a reason to fail a screen.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class SapiCallsignVoice : ICallsignVoice, IDisposable
{
    private readonly ILogger<SapiCallsignVoice> _logger;
    private readonly object? _voice;
    private readonly object _gate = new();
    private bool _disposed;

    /// <summary>Constructs the voice, probing for a usable engine.</summary>
    public SapiCallsignVoice(ILogger<SapiCallsignVoice> logger)
    {
        _logger = logger;

        try
        {
            var type = Type.GetTypeFromProgID("SAPI.SpVoice");
            if (type is not null)
            {
                _voice = Activator.CreateInstance(type);
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or TypeLoadException
                                       or MissingMethodException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "SAPI is unavailable; callsign playback disabled");
        }

        IsAvailable = _voice is not null;
    }

    /// <inheritdoc />
    public bool IsAvailable { get; }

    /// <inheritdoc />
    public Task SpeakAsync(string callsign, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable || _disposed || string.IsNullOrWhiteSpace(callsign))
        {
            return Task.CompletedTask;
        }

        var words = RadioPhrasing.Speak(callsign);
        if (words.Length == 0)
        {
            return Task.CompletedTask;
        }

        // SAPI blocks the calling thread while it synthesises, so this goes to
        // the pool rather than stalling the UI thread mid-click.
        return Task.Run(
            () =>
            {
                lock (_gate)
                {
                    if (_disposed)
                    {
                        return;
                    }

                    try
                    {
                        // 1 = SVSFlagsAsync, 2 = SVSFPurgeBeforeSpeak. Together
                        // they queue this utterance and drop anything still
                        // playing, so repeated clicks do not stack up.
                        _voice!.GetType().InvokeMember(
                            "Speak",
                            System.Reflection.BindingFlags.InvokeMethod,
                            binder: null,
                            target: _voice,
                            args: [words, 1 | 2]);
                    }
                    catch (Exception ex) when (ex is COMException or MissingMethodException
                                                   or System.Reflection.TargetInvocationException)
                    {
                        _logger.LogDebug(ex, "SAPI refused to speak {Callsign}", callsign);
                    }
                }
            },
            cancellationToken);
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

            if (_voice is not null && Marshal.IsComObject(_voice))
            {
                Marshal.FinalReleaseComObject(_voice);
            }
        }
    }
}
