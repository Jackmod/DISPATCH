using Dispatch.Core.Audio;
using FluentAssertions;
using Xunit;

namespace Dispatch.Core.Tests.Audio;

/// <summary>
/// Radio procedure, not text-to-speech configuration. A synthesiser handed
/// "10 ADAM 24" says "ten adam twenty-four"; over the air it is "one zero adam
/// two four", and getting that wrong is the difference between the callsign
/// sounding right and sounding like a robot reading a spreadsheet.
/// </summary>
public sealed class RadioPhrasingTests
{
    [Theory]
    [InlineData("1 ADAM 7", "one adam seven")]
    [InlineData("2 LINCOLN 14", "two lincoln one four")]
    [InlineData("10 ADAM 24", "one zero adam two four")]
    [InlineData("7 KING 3", "seven king three")]
    public void Numbers_are_spoken_digit_by_digit(string callsign, string expected) =>
        RadioPhrasing.Speak(callsign).Should().Be(expected);

    [Fact]
    public void Nine_is_spoken_as_niner()
    {
        // Standard voice procedure: "nine" and "five" are easily confused over
        // a noisy channel, so nine is always "niner".
        RadioPhrasing.Speak("9 ADAM 9").Should().Be("niner adam niner");
    }

    [Fact]
    public void Zero_is_spoken()
    {
        RadioPhrasing.Speak("10 BOY 20").Should().Be("one zero boy two zero");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_input_produces_nothing(string callsign) =>
        RadioPhrasing.Speak(callsign).Should().BeEmpty();

    [Fact]
    public void Extra_whitespace_is_ignored()
    {
        RadioPhrasing.Speak("  1   ADAM   7  ").Should().Be("one adam seven");
    }

    [Fact]
    public void Words_are_lowercased_for_the_synthesiser()
    {
        // Left uppercase, some engines spell acronyms out letter by letter.
        RadioPhrasing.Speak("1 ADAM 7").Should().NotContain("ADAM");
    }

    [Fact]
    public void The_silent_voice_reports_itself_unavailable()
    {
        // Callers hide the playback button on this, rather than offering one
        // that silently does nothing.
        var voice = new SilentCallsignVoice();

        voice.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task The_silent_voice_accepts_anything_without_throwing()
    {
        var voice = new SilentCallsignVoice();

        var speak = async () => await voice.SpeakAsync("1 ADAM 7");

        await speak.Should().NotThrowAsync();
    }
}
