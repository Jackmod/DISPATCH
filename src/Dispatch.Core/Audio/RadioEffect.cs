namespace Dispatch.Core.Audio;

/// <summary>
/// Turns a plain voice line into a police-radio transmission: band-limited,
/// compressed, with a mic click opening it and the roger beep closing it.
/// </summary>
/// <remarks>
/// The sound of a scanner call is three things layered — the voice squeezed into
/// a narrow radio band so it goes thin and tinny, a bed of static under it, and
/// the two-tone key-up click and the "roger" beep that bookend a real over-the-air
/// transmission. None of it needs a sampled asset: the band-pass shapes the voice,
/// and the clicks, hiss and beep are generated. The result reads unmistakably as
/// "dispatch" without a single copyrighted byte.
/// </remarks>
public static class RadioEffect
{
    /// <summary>Applies the full radio treatment to a voice line.</summary>
    public static PcmAudio Apply(PcmAudio voice)
    {
        ArgumentNullException.ThrowIfNull(voice);

        var rate = voice.SampleRate;
        var body = BandLimit(voice.Samples, rate);
        Compress(body);
        AddHiss(body, 0.05);

        var click = KeyUpClick(rate);
        var beep = RogerBeep(rate);
        var gapA = Silence(rate, 0.04);
        var gapB = Silence(rate, 0.06);

        // click · short gap · voice · short gap · roger beep
        var output = Concat(click, gapA, body, gapB, beep);
        Normalise(output, 0.9);

        return new PcmAudio(output, rate);
    }

    /// <summary>A band-pass roughly 300–3000 Hz, the classic comms band, via a HP→LP cascade.</summary>
    private static short[] BandLimit(short[] input, int rate)
    {
        var samples = ToFloat(input);
        HighPass(samples, 300f, rate);
        LowPass(samples, 3000f, rate);
        return FromFloat(samples);
    }

    private static void HighPass(float[] x, float cutoff, int rate)
    {
        // One-pole high-pass: y[n] = a*(y[n-1] + x[n] - x[n-1]).
        var dt = 1f / rate;
        var rc = 1f / (2f * MathF.PI * cutoff);
        var a = rc / (rc + dt);

        var prevX = x.Length > 0 ? x[0] : 0f;
        var prevY = 0f;

        for (var i = 0; i < x.Length; i++)
        {
            var cur = x[i];
            prevY = a * (prevY + cur - prevX);
            prevX = cur;
            x[i] = prevY;
        }
    }

    private static void LowPass(float[] x, float cutoff, int rate)
    {
        // One-pole low-pass: y[n] = y[n-1] + a*(x[n] - y[n-1]).
        var dt = 1f / rate;
        var rc = 1f / (2f * MathF.PI * cutoff);
        var a = dt / (rc + dt);

        var prevY = 0f;
        for (var i = 0; i < x.Length; i++)
        {
            prevY += a * (x[i] - prevY);
            x[i] = prevY;
        }
    }

    /// <summary>Soft-clips to add the squashed, driven grit of a radio.</summary>
    private static void Compress(short[] x)
    {
        for (var i = 0; i < x.Length; i++)
        {
            var v = x[i] / 32768f * 2.2f;            // drive
            v = MathF.Tanh(v);                        // soft clip
            x[i] = (short)Math.Clamp(v * 26000f, short.MinValue, short.MaxValue);
        }
    }

    private static void AddHiss(short[] x, double amount)
    {
        // Deterministic pseudo-noise: no Random (unavailable to the workflow
        // runtime and unwanted for reproducibility), just a cheap hash per index.
        for (var i = 0; i < x.Length; i++)
        {
            var n = (Hash((uint)i) / (double)uint.MaxValue - 0.5) * 2.0 * amount * 32767.0;
            x[i] = (short)Math.Clamp(x[i] + n, short.MinValue, short.MaxValue);
        }
    }

    /// <summary>The short broadband click of a mic keying up.</summary>
    private static short[] KeyUpClick(int rate)
    {
        var length = (int)(rate * 0.02);
        var click = new short[length];
        for (var i = 0; i < length; i++)
        {
            var env = 1.0 - i / (double)length;
            var n = (Hash((uint)(i + 7919)) / (double)uint.MaxValue - 0.5) * 2.0;
            click[i] = (short)(n * env * env * 12000);
        }

        return click;
    }

    /// <summary>The "roger" beep — a ~1200 Hz tone with a soft envelope.</summary>
    private static short[] RogerBeep(int rate)
    {
        var length = (int)(rate * 0.13);
        var beep = new short[length];
        const double freq = 1200.0;

        for (var i = 0; i < length; i++)
        {
            var t = i / (double)rate;
            var p = i / (double)length;
            var env = Math.Sin(Math.PI * p);          // fade in and out
            beep[i] = (short)(Math.Sin(2 * Math.PI * freq * t) * env * 15000);
        }

        return beep;
    }

    private static short[] Silence(int rate, double seconds) => new short[(int)(rate * seconds)];

    private static short[] Concat(params short[][] parts)
    {
        var total = parts.Sum(p => p.Length);
        var result = new short[total];
        var at = 0;
        foreach (var part in parts)
        {
            part.CopyTo(result, at);
            at += part.Length;
        }

        return result;
    }

    private static void Normalise(short[] x, double peak)
    {
        var max = 1;
        foreach (var s in x)
        {
            max = Math.Max(max, Math.Abs((int)s));
        }

        var gain = peak * short.MaxValue / max;
        if (gain >= 1.0)
        {
            return;
        }

        for (var i = 0; i < x.Length; i++)
        {
            x[i] = (short)(x[i] * gain);
        }
    }

    private static float[] ToFloat(short[] x)
    {
        var f = new float[x.Length];
        for (var i = 0; i < x.Length; i++)
        {
            f[i] = x[i] / 32768f;
        }

        return f;
    }

    private static short[] FromFloat(float[] x)
    {
        var s = new short[x.Length];
        for (var i = 0; i < x.Length; i++)
        {
            s[i] = (short)Math.Clamp(x[i] * 32768f, short.MinValue, short.MaxValue);
        }

        return s;
    }

    private static uint Hash(uint i)
    {
        // A small integer hash for reproducible noise.
        i ^= i >> 16;
        i *= 0x7feb352d;
        i ^= i >> 15;
        i *= 0x846ca68b;
        i ^= i >> 16;
        return i;
    }
}
