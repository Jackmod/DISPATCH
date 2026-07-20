using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Dispatch.Core.Audio;
using Microsoft.Extensions.Logging;

namespace Dispatch.Platform.Windows;

/// <summary>
/// Speaks callsigns as a police-radio transmission: SAPI renders the words, then
/// the radio effect band-limits them and adds the mic click and roger beep.
/// </summary>
/// <remarks>
/// Driven through COM late binding rather than the <c>System.Speech</c> package,
/// which would pull a Windows-only reference into a project the rest of the app
/// links against for nothing SAPI does not already give.
///
/// <para>
/// The voice is rendered to a WAV rather than spoken straight to the device, so
/// <see cref="RadioEffect"/> can shape it into a scanner call before it plays
/// through the same <see cref="ISoundPlayer"/> the intro siren uses. Every
/// failure path — no engine, no voices, a locked device, COM refusing to
/// activate — falls back to a plain spoken line, and finally to silence, because
/// a callsign that will not read aloud is decoration, not a reason to fail.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class SapiCallsignVoice : ICallsignVoice, IDisposable
{
    // SpeechAudioFormatType.SAFT22kHz16BitMono and SpeechStreamFileMode.SSFMCreateForWrite.
    private const int Format22kHz16BitMono = 22;
    private const int CreateForWrite = 3;

    private readonly ILogger<SapiCallsignVoice> _logger;
    private readonly ISoundPlayer _player;
    private readonly object? _voice;
    private readonly object _gate = new();
    private bool _disposed;

    /// <summary>Constructs the voice, probing for a usable engine.</summary>
    public SapiCallsignVoice(ILogger<SapiCallsignVoice> logger, ISoundPlayer player)
    {
        _logger = logger;
        _player = player;

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

        // SAPI blocks while it synthesises, so this goes to the pool rather than
        // stalling the UI thread mid-click.
        return Task.Run(() => PlayRadio(words), cancellationToken);
    }

    private void PlayRadio(string words)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            var temp = Path.Combine(Path.GetTempPath(), $"dispatch-callsign-{Guid.NewGuid():N}.wav");

            try
            {
                if (RenderToFile(words, temp))
                {
                    var voice = WavAudio.Read(File.ReadAllBytes(temp));
                    var radio = RadioEffect.Apply(voice);
                    _player.Play(WavAudio.Write(radio));
                    return;
                }
            }
            catch (Exception ex) when (ex is COMException or MissingMethodException or FormatException
                                           or TargetInvocationException or IOException)
            {
                _logger.LogDebug(ex, "Radio callsign render failed; falling back to plain speech");
            }
            finally
            {
                TryDelete(temp);
            }

            SpeakPlain(words);
        }
    }

    /// <summary>Renders the words to a WAV file through a SAPI file stream. Returns success.</summary>
    private bool RenderToFile(string words, string path)
    {
        object? fileStream = null;
        try
        {
            var type = Type.GetTypeFromProgID("SAPI.SpFileStream");
            if (type is null)
            {
                return false;
            }

            fileStream = Activator.CreateInstance(type);
            if (fileStream is null)
            {
                return false;
            }

            var format = Get(fileStream, "Format");
            if (format is not null)
            {
                Set(format, "Type", Format22kHz16BitMono);
            }

            Invoke(fileStream, "Open", path, CreateForWrite, false);
            SetRef(_voice!, "AudioOutputStream", fileStream);

            // Flag 0 = synchronous: the file is complete when Speak returns.
            Invoke(_voice!, "Speak", words, 0);
            Invoke(fileStream, "Close");

            return File.Exists(path) && new FileInfo(path).Length > 44;
        }
        finally
        {
            if (fileStream is not null && Marshal.IsComObject(fileStream))
            {
                Marshal.FinalReleaseComObject(fileStream);
            }
        }
    }

    private void SpeakPlain(string words)
    {
        try
        {
            // 1 = async, 2 = purge queued speech, so repeated clicks do not stack.
            Invoke(_voice!, "Speak", words, 1 | 2);
        }
        catch (Exception ex) when (ex is COMException or MissingMethodException or TargetInvocationException)
        {
            _logger.LogDebug(ex, "SAPI refused to speak");
        }
    }

    private static object? Get(object o, string name) =>
        o.GetType().InvokeMember(name, BindingFlags.GetProperty, null, o, null);

    private static void Set(object o, string name, object value) =>
        o.GetType().InvokeMember(name, BindingFlags.SetProperty, null, o, [value]);

    private static void SetRef(object o, string name, object value) =>
        o.GetType().InvokeMember(name, BindingFlags.PutRefDispProperty, null, o, [value]);

    private static object? Invoke(object o, string name, params object[] args) =>
        o.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, o, args);

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
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

            if (_voice is not null && Marshal.IsComObject(_voice))
            {
                Marshal.FinalReleaseComObject(_voice);
            }
        }
    }
}
