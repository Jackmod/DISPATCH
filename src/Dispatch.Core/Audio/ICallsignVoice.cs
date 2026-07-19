namespace Dispatch.Core.Audio;

/// <summary>
/// Reads a callsign back the way dispatch would say it.
/// </summary>
/// <remarks>
/// Platform-specific by nature — speech synthesis is a Windows API here — so
/// the interface lives in Core and the implementation in the Windows project.
/// A stub satisfies it elsewhere, which keeps the officer screen testable on
/// any machine.
/// </remarks>
public interface ICallsignVoice
{
    /// <summary>Whether speech is actually available on this machine.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Speaks a callsign such as <c>1 ADAM 7</c>. Returns as soon as the
    /// utterance is queued; it does not block for the duration of the audio.
    /// </summary>
    Task SpeakAsync(string callsign, CancellationToken cancellationToken = default);
}

/// <summary>
/// Turns a callsign into the words a dispatcher would actually say.
/// </summary>
/// <remarks>
/// Kept in Core, and separate from the speech API, because the phrasing rules
/// are domain knowledge rather than platform detail — and because they are the
/// part worth testing. A synthesiser handed "1 ADAM 7" says "one adam seven"
/// only by luck; handed "10 ADAM 24" it will say "ten adam twenty-four", where
/// radio procedure is "ten adam two four".
/// </remarks>
public static class RadioPhrasing
{
    private static readonly string[] Digits =
        ["zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "niner"];

    /// <summary>
    /// Expands a callsign into spoken form.
    /// </summary>
    /// <example><c>10 ADAM 24</c> becomes <c>one zero adam two four</c>.</example>
    public static string Speak(string callsign)
    {
        if (string.IsNullOrWhiteSpace(callsign))
        {
            return string.Empty;
        }

        var spoken = new List<string>();

        foreach (var token in callsign.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.All(char.IsDigit))
            {
                // Digit by digit, which is how numbers go out over the air.
                spoken.AddRange(token.Select(digit => Digits[digit - '0']));
            }
            else
            {
                spoken.Add(token.ToLowerInvariant());
            }
        }

        return string.Join(' ', spoken);
    }
}

/// <summary>
/// Used where speech is unavailable. Reports itself unavailable so callers can
/// hide the control rather than offering a button that does nothing.
/// </summary>
public sealed class SilentCallsignVoice : ICallsignVoice
{
    /// <inheritdoc />
    public bool IsAvailable => false;

    /// <inheritdoc />
    public Task SpeakAsync(string callsign, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
