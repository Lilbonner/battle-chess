using System.Runtime.InteropServices;

namespace BattleChess;

// Синтезированные звуки + простой микшер через waveOut (звуки могут накладываться).
public sealed class SoundSystem : IDisposable
{
    const int Rate = 22050;
    readonly Dictionary<string, float[]> bank = new();
    readonly List<(float[] data, int pos, float vol)> voices = new();
    readonly object sync = new();
    readonly Random rnd = new(7);
    Thread thread;
    volatile bool running;
    public bool Enabled = true;

    public SoundSystem()
    {
        try
        {
            Build();
            running = true;
            thread = new Thread(Loop) { IsBackground = true, Name = "audio" };
            thread.Start();
        }
        catch { running = false; }
    }

    public void Play(string name, float vol = 1)
    {
        if (!Enabled || !running || !bank.TryGetValue(name, out var d)) return;
        lock (sync)
        {
            if (voices.Count > 24) voices.RemoveAt(0);
            voices.Add((d, 0, vol));
        }
    }

    public void Dispose() { running = false; thread?.Join(500); }

    public bool Has(string name) => bank.ContainsKey(name);

    // Воспроизведение с изменённой высотой: ratio > 1 — быстрее и выше.
    public void PlayRate(string name, float ratio, float vol = 1)
    {
        if (!bank.TryGetValue(name, out var src)) return;
        ratio = Math.Clamp(ratio, 0.25f, 6f);
        if (Math.Abs(ratio - 1) < 0.01f) { Play(name, vol); return; }
        string key = $"{name}@{ratio:0.00}";
        if (!bank.ContainsKey(key))
        {
            int n = (int)(src.Length / ratio);
            var b = new float[n];
            for (int i = 0; i < n; i++)
            {
                float s = i * ratio; int k = Math.Min((int)s, src.Length - 1), k2 = Math.Min(k + 1, src.Length - 1);
                b[i] = src[k] + (src[k2] - src[k]) * (s - k);
            }
            bank[key] = b;
        }
        Play(key, vol);
    }

    // Загрузка 8-битного моно WAV (оригинальные звуки) с пересчётом частоты в 22050 Гц.
    public void LoadWav(string name, string path, float gain = 0.9f)
    {
        try
        {
            using var r = new BinaryReader(File.OpenRead(path));
            r.ReadBytes(12);
            int rate = Rate, bits = 8; byte[] data = null;
            while (r.BaseStream.Position < r.BaseStream.Length - 8)
            {
                string id = new string(r.ReadChars(4)); int len = r.ReadInt32();
                if (id == "fmt ") { r.ReadInt16(); r.ReadInt16(); rate = r.ReadInt32(); r.ReadInt32(); r.ReadInt16(); bits = r.ReadInt16(); r.ReadBytes(len - 16); }
                else if (id == "data") { data = r.ReadBytes(len); break; }
                else r.ReadBytes(len);
            }
            if (data == null || bits != 8) return;
            int n = (int)((long)data.Length * Rate / rate);
            var b = new float[n];
            for (int i = 0; i < n; i++)
            {
                float src = i * (float)rate / Rate;
                int k = Math.Min((int)src, data.Length - 1), k2 = Math.Min(k + 1, data.Length - 1);
                float t = src - k;
                b[i] = ((data[k] - 128) * (1 - t) + (data[k2] - 128) * t) / 128f * gain;
            }
            bank[name] = b;
        }
        catch { }
    }

    // ---------------- синтез ----------------
    float Noise() => (float)(rnd.NextDouble() * 2 - 1);
    static float[] Buf(float sec) => new float[(int)(sec * Rate)];
    static float T(int i) => i / (float)Rate;

    float[] Thump(float sec, float f0, float f1, float vol, float noise = 0.3f)
    {
        var b = Buf(sec); float ph = 0, lp = 0;
        for (int i = 0; i < b.Length; i++)
        {
            float t = T(i), u = t / sec;
            ph += 2 * MathF.PI * (f0 + (f1 - f0) * u) / Rate;
            lp += (Noise() - lp) * 0.08f;
            b[i] = (MathF.Sin(ph) + lp * noise * 3) * MathF.Exp(-t * 6 / sec) * vol;
        }
        return b;
    }

    float[] Metal(float sec, float[] freqs, float vol, float decay = 6)
    {
        var b = Buf(sec);
        for (int i = 0; i < b.Length; i++)
        {
            float t = T(i), s = 0;
            for (int k = 0; k < freqs.Length; k++) s += MathF.Sin(2 * MathF.PI * freqs[k] * t) / (k + 1);
            float click = i < 300 ? Noise() * (1 - i / 300f) : 0;
            b[i] = (s * 0.5f + click) * MathF.Exp(-t * decay) * vol;
        }
        return b;
    }

    float[] Whoosh(float sec, float vol, float from = 0.02f, float to = 0.35f)
    {
        var b = Buf(sec); float lp = 0, lp2 = 0;
        for (int i = 0; i < b.Length; i++)
        {
            float u = i / (float)b.Length, k = from + (to - from) * u;
            lp += (Noise() - lp) * k; lp2 += (lp - lp2) * k;
            b[i] = (lp - lp2) * MathF.Sin(MathF.PI * u) * vol * 3;
        }
        return b;
    }

    float[] Rumble(float sec, float vol, float cut = 0.02f)
    {
        var b = Buf(sec); float lp = 0;
        for (int i = 0; i < b.Length; i++)
        {
            float t = T(i);
            lp += (Noise() - lp) * cut;
            b[i] = lp * 6 * MathF.Exp(-t * 3 / sec) * vol;
        }
        return b;
    }

    float[] Sweep(float sec, float f0, float f1, float vol, float vib = 0, bool saw = false)
    {
        var b = Buf(sec); float ph = 0;
        for (int i = 0; i < b.Length; i++)
        {
            float t = T(i), u = t / sec;
            float f = f0 * MathF.Pow(f1 / f0, u) * (1 + vib * MathF.Sin(t * 40));
            ph += f / Rate;
            float w = saw ? (ph % 1) * 2 - 1 : MathF.Sin(2 * MathF.PI * ph);
            float env = Math.Min(1, t * 40) * (1 - u);
            b[i] = w * env * vol;
        }
        return b;
    }

    float[] Notes(float[] freqs, float each, float tail, float vol)
    {
        var b = Buf(each * freqs.Length + tail);
        for (int n = 0; n < freqs.Length; n++)
        {
            int start = (int)(n * each * Rate);
            for (int i = start; i < b.Length; i++)
            {
                float t = T(i - start);
                float s = MathF.Sin(2 * MathF.PI * freqs[n] * t) + 0.3f * MathF.Sin(4 * MathF.PI * freqs[n] * t);
                b[i] += s * MathF.Exp(-t * 3.5f) * Math.Min(1, t * 200) * vol;
            }
        }
        return b;
    }

    static float[] Mix(params float[][] parts)
    {
        var b = new float[parts.Max(p => p.Length)];
        foreach (var p in parts) for (int i = 0; i < p.Length; i++) b[i] += p[i];
        return b;
    }

    static float[] Delay(float[] a, float sec)
    {
        int off = (int)(sec * Rate);
        var b = new float[a.Length + off];
        Array.Copy(a, 0, b, off, a.Length);
        return b;
    }

    void Build()
    {
        bank["step"] = Thump(0.07f, 120, 60, 0.18f, 0.5f);
        bank["stomp"] = Mix(Thump(0.18f, 70, 35, 0.5f, 0.6f), Rumble(0.25f, 0.15f));
        var clop = Metal(0.06f, new[] { 900f, 1400f }, 0.25f, 60);
        bank["gallop"] = Mix(clop, Delay(clop, 0.11f), Delay(clop, 0.3f), Delay(clop, 0.41f));
        bank["land"] = Mix(Thump(0.15f, 90, 40, 0.4f), clop, Delay(clop, 0.05f));
        bank["clang"] = Metal(0.6f, new[] { 523f, 1307f, 2213f, 3301f, 4517f }, 0.45f, 7);
        bank["stab"] = Mix(Whoosh(0.18f, 0.5f), Delay(Thump(0.15f, 140, 60, 0.4f, 0.8f), 0.12f));
        bank["hit"] = Mix(Whoosh(0.2f, 0.4f), Delay(Mix(Thump(0.25f, 110, 40, 0.5f, 1f), Metal(0.3f, new[] { 700f, 1650f }, 0.2f, 12)), 0.15f));
        bank["slash"] = Mix(Whoosh(0.25f, 0.6f, 0.05f, 0.6f), Delay(Metal(0.35f, new[] { 1800f, 2600f, 3900f }, 0.18f, 10), 0.1f));
        bank["thunder"] = Mix(Whoosh(0.12f, 1.2f, 0.6f, 0.9f), Rumble(1.6f, 0.6f, 0.015f), Thump(0.5f, 60, 30, 0.4f, 1));
        bank["smash"] = Mix(Thump(0.7f, 110, 30, 0.7f, 1.2f), Rumble(1.0f, 0.5f, 0.03f));
        bank["charge"] = Sweep(0.55f, 220, 880, 0.18f, 0.03f);
        bank["magic"] = Mix(Notes(new[] { 1046f, 1318f, 1568f, 2093f }, 0.05f, 0.3f, 0.12f), Sweep(0.4f, 900, 1800, 0.08f, 0.05f));
        bank["zap"] = Mix(Sweep(0.35f, 1600, 160, 0.25f, 0, true), Whoosh(0.3f, 0.4f, 0.4f, 0.1f));
        bank["crunch"] = Mix(Rumble(0.5f, 0.4f, 0.25f), Thump(0.3f, 80, 30, 0.4f, 1));
        var ping = new List<float[]>();
        for (int i = 0; i < 9; i++)
            ping.Add(Delay(Metal(0.25f, new[] { 2500f + rnd.Next(2500), 5200f + rnd.Next(2000) }, 0.1f, 18), i * 0.06f + (float)rnd.NextDouble() * 0.03f));
        bank["shatter"] = Mix(ping.ToArray());
        var burn = Buf(1.2f); float lp = 0;
        for (int i = 0; i < burn.Length; i++)
        {
            float t = T(i);
            lp += (Noise() - lp) * 0.3f;
            float crackle = rnd.NextDouble() < 0.002 ? 1 : 0;
            burn[i] = (lp * 0.25f + crackle * Noise()) * (1 - t / 1.2f) * 0.6f;
        }
        bank["burn"] = Mix(burn, Sweep(0.3f, 400, 100, 0.12f));
        bank["die"] = Sweep(0.7f, 240, 90, 0.16f, 0.02f, true);
        bank["thud"] = Thump(0.25f, 90, 35, 0.45f, 0.8f);
        bank["poof"] = Whoosh(0.3f, 0.6f, 0.3f, 0.02f);
        bank["promote"] = Notes(new[] { 523f, 659f, 784f, 1046f }, 0.09f, 0.5f, 0.15f);
        bank["check"] = Notes(new[] { 880f, 660f }, 0.12f, 0.25f, 0.2f);
        bank["win"] = Mix(Notes(new[] { 523f, 659f, 784f, 1046f, 784f, 1046f }, 0.13f, 0.9f, 0.13f),
                          Delay(Notes(new[] { 523f }, 0.01f, 1.0f, 0.08f), 0.8f), Delay(Notes(new[] { 659f }, 0.01f, 1.0f, 0.08f), 0.8f));
        bank["lose"] = Notes(new[] { 440f, 415f, 392f, 330f }, 0.22f, 0.8f, 0.14f);
        bank["whoosh"] = Whoosh(0.45f, 0.7f, 0.05f, 0.5f);
        bank["wind"] = Mix(Whoosh(1.2f, 0.6f, 0.02f, 0.2f), Delay(Whoosh(0.9f, 0.4f, 0.1f, 0.3f), 0.3f));
        bank["ice"] = Mix(Notes(new[] { 1760f, 2093f, 2637f, 3136f }, 0.04f, 0.4f, 0.08f), Whoosh(0.5f, 0.25f, 0.5f, 0.8f));
        bank["rumble"] = Mix(Rumble(1.4f, 0.6f, 0.012f), Thump(0.6f, 50, 25, 0.4f, 1));
        bank["stone"] = Mix(Rumble(0.8f, 0.3f, 0.1f), Sweep(0.6f, 200, 80, 0.12f, 0, true));
        bank["boing"] = Sweep(0.5f, 260, 620, 0.2f, 0.12f);
        bank["laser"] = Mix(Sweep(0.6f, 2200, 700, 0.14f, 0.02f, true), Sweep(0.6f, 1100, 350, 0.1f));
        bank["explode"] = Mix(Thump(0.9f, 90, 25, 0.8f, 1.6f), Rumble(1.2f, 0.7f, 0.05f), Whoosh(0.2f, 0.8f, 0.6f, 0.9f));
        bank["throw"] = Whoosh(0.3f, 0.5f, 0.1f, 0.7f);
        bank["move"] = Metal(0.08f, new[] { 600f, 1100f }, 0.3f, 50);
        bank["select"] = Metal(0.05f, new[] { 1500f }, 0.12f, 70);
        bank["error"] = Sweep(0.15f, 140, 120, 0.15f, 0, true);
    }

    // ---------------- waveOut ----------------
    [StructLayout(LayoutKind.Sequential)]
    struct WaveFormat { public short wFormatTag, nChannels; public int nSamplesPerSec, nAvgBytesPerSec; public short nBlockAlign, wBitsPerSample, cbSize; }

    [StructLayout(LayoutKind.Sequential)]
    struct WaveHdr { public IntPtr lpData; public uint dwBufferLength, dwBytesRecorded; public IntPtr dwUser; public uint dwFlags, dwLoops; public IntPtr lpNext, reserved; }

    [DllImport("winmm.dll")] static extern int waveOutOpen(out IntPtr h, int dev, ref WaveFormat fmt, IntPtr cb, IntPtr inst, int flags);
    [DllImport("winmm.dll")] static extern int waveOutPrepareHeader(IntPtr h, IntPtr hdr, int size);
    [DllImport("winmm.dll")] static extern int waveOutUnprepareHeader(IntPtr h, IntPtr hdr, int size);
    [DllImport("winmm.dll")] static extern int waveOutWrite(IntPtr h, IntPtr hdr, int size);
    [DllImport("winmm.dll")] static extern int waveOutReset(IntPtr h);
    [DllImport("winmm.dll")] static extern int waveOutClose(IntPtr h);

    void Loop()
    {
        const int N = 4, Samples = 1024;
        var fmt = new WaveFormat { wFormatTag = 1, nChannels = 1, nSamplesPerSec = Rate, wBitsPerSample = 16, nBlockAlign = 2, nAvgBytesPerSec = Rate * 2 };
        if (waveOutOpen(out var h, -1, ref fmt, IntPtr.Zero, IntPtr.Zero, 0) != 0) { running = false; return; }
        int hdrSize = Marshal.SizeOf<WaveHdr>();
        int flagsOff = (int)Marshal.OffsetOf<WaveHdr>(nameof(WaveHdr.dwFlags));
        var hdrs = new IntPtr[N]; var datas = new IntPtr[N];
        var mix = new float[Samples]; var pcm = new short[Samples];
        try
        {
            for (int i = 0; i < N; i++)
            {
                datas[i] = Marshal.AllocHGlobal(Samples * 2);
                hdrs[i] = Marshal.AllocHGlobal(hdrSize);
                Marshal.StructureToPtr(new WaveHdr { lpData = datas[i], dwBufferLength = Samples * 2, dwFlags = 1 /*DONE*/ }, hdrs[i], false);
            }
            bool[] prepared = new bool[N];
            while (running)
            {
                bool wrote = false;
                for (int i = 0; i < N; i++)
                {
                    if ((Marshal.ReadInt32(hdrs[i], flagsOff) & 1) == 0) continue;
                    if (prepared[i]) waveOutUnprepareHeader(h, hdrs[i], hdrSize);
                    Render(mix);
                    for (int k = 0; k < Samples; k++) pcm[k] = (short)(Math.Clamp(mix[k], -1f, 1f) * 30000);
                    Marshal.Copy(pcm, 0, datas[i], Samples);
                    Marshal.StructureToPtr(new WaveHdr { lpData = datas[i], dwBufferLength = Samples * 2 }, hdrs[i], false);
                    waveOutPrepareHeader(h, hdrs[i], hdrSize);
                    prepared[i] = true;
                    waveOutWrite(h, hdrs[i], hdrSize);
                    wrote = true;
                }
                if (!wrote) Thread.Sleep(5);
            }
            waveOutReset(h);
            for (int i = 0; i < N; i++) if (prepared[i]) waveOutUnprepareHeader(h, hdrs[i], hdrSize);
        }
        finally
        {
            waveOutClose(h);
            for (int i = 0; i < N; i++) { Marshal.FreeHGlobal(datas[i]); Marshal.FreeHGlobal(hdrs[i]); }
        }
    }

    void Render(float[] mix)
    {
        Array.Clear(mix);
        lock (sync)
        {
            for (int v = voices.Count - 1; v >= 0; v--)
            {
                var (d, pos, vol) = voices[v];
                int n = Math.Min(mix.Length, d.Length - pos);
                for (int i = 0; i < n; i++) mix[i] += d[pos + i] * vol;
                pos += n;
                if (pos >= d.Length) voices.RemoveAt(v); else voices[v] = (d, pos, vol);
            }
        }
        for (int i = 0; i < mix.Length; i++) mix[i] = MathF.Tanh(mix[i]);
    }
}
