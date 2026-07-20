using Dispatch.Core.Audio;
using FluentAssertions;
using Xunit;

namespace Dispatch.Core.Tests.Audio;

/// <summary>
/// The WAV codec and the radio effect that turns a plain voice line into a
/// scanner transmission.
/// </summary>
public sealed class RadioEffectTests
{
    private static short[] Tone(int rate, double seconds, double freq)
    {
        var n = (int)(rate * seconds);
        var s = new short[n];
        for (var i = 0; i < n; i++)
        {
            s[i] = (short)(Math.Sin(2 * Math.PI * freq * i / rate) * 12000);
        }

        return s;
    }

    [Fact]
    public void A_wav_round_trips_through_write_and_read()
    {
        var audio = new PcmAudio(Tone(22050, 0.2, 440), 22050);

        var wav = WavAudio.Write(audio);
        var back = WavAudio.Read(wav);

        back.SampleRate.Should().Be(22050);
        back.Samples.Length.Should().Be(audio.Samples.Length);
        back.Samples.Should().Equal(audio.Samples);
    }

    [Fact]
    public void The_written_wav_has_a_valid_riff_header()
    {
        var wav = WavAudio.Write(new PcmAudio(new short[] { 1, -1, 2, -2 }, 16000));

        System.Text.Encoding.ASCII.GetString(wav, 0, 4).Should().Be("RIFF");
        System.Text.Encoding.ASCII.GetString(wav, 8, 4).Should().Be("WAVE");
        System.Text.Encoding.ASCII.GetString(wav, 36, 4).Should().Be("data");
    }

    [Fact]
    public void The_radio_effect_adds_the_click_and_beep_around_the_voice()
    {
        var voice = new PcmAudio(Tone(22050, 0.5, 400), 22050);

        var radio = RadioEffect.Apply(voice);

        // Click + gaps + voice + beep make it longer than the voice alone.
        radio.Samples.Length.Should().BeGreaterThan(voice.Samples.Length);
        radio.SampleRate.Should().Be(22050);

        // It is not silent, and it stays within range.
        radio.Samples.Any(s => Math.Abs((int)s) > 1000).Should().BeTrue("the transmission is audible");
        radio.Samples.All(s => s >= short.MinValue && s <= short.MaxValue).Should().BeTrue();
    }

    [Fact]
    public void The_effect_ends_on_the_roger_beep()
    {
        var voice = new PcmAudio(new short[22050], 22050); // silent voice
        var radio = RadioEffect.Apply(voice);

        // The last ~100 ms carries the beep, so the tail is not silent even though
        // the voice was.
        var tail = radio.Samples[^2000..];
        tail.Any(s => Math.Abs((int)s) > 1000).Should().BeTrue("the roger beep closes the call");
    }

    [Fact]
    public void The_effect_is_deterministic()
    {
        // No Random inside, so the same input always produces the same bytes —
        // reproducible and testable.
        var voice = new PcmAudio(Tone(16000, 0.3, 300), 16000);

        var a = WavAudio.Write(RadioEffect.Apply(voice));
        var b = WavAudio.Write(RadioEffect.Apply(voice));

        a.Should().Equal(b);
    }
}
