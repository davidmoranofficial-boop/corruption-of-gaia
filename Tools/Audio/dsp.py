"""Small numpy DSP kit for the sound generator: oscillators, envelopes, filters, plucked strings, noise,
reverb and loop helpers. Everything is mono float64 at SR samples per second, nominally in -1..1.

Recursive (IIR) filters are run in one of three ways, so pure numpy stays fast:
- fixed filters (one-pole, RBJ biquads) are turned into an impulse response once and applied by FFT convolution;
- feedback delays (Karplus-Strong strings, comb and allpass reverb stages) are run a whole delay-length block at a
  time, since each block only depends on the block before it;
- filters whose cutoff moves (whooshes) run sample by sample in plain Python; they are only used on short sounds.
Long noise beds that need a moving filter are shaped in the frequency domain instead (stft_shape).
"""
import math
from functools import lru_cache
import numpy as np

SR = 44100


# ---------------------------------------------------------------------------------------------- basics

def ns(dur):
    """Seconds to samples."""
    return max(1, int(round(dur * SR)))


def tt(dur):
    """Time axis in seconds for a sound of this length."""
    return np.arange(ns(dur)) / SR


def rng(seed):
    return np.random.default_rng(seed)


def db(x):
    return 20 * math.log10(max(x, 1e-12))


def undb(d):
    return 10 ** (d / 20)


def mtof(m):
    return 440.0 * 2 ** ((m - 69) / 12)


def place(buf, x, at, gain=1.0):
    """Mix x into buf starting at time `at` (seconds); clipped to the buffer."""
    i = int(round(at * SR))
    if i >= len(buf) or i + len(x) <= 0:
        return buf
    a, b = max(0, i), min(len(buf), i + len(x))
    buf[a:b] += gain * x[a - i:b - i]
    return buf


def pad_to(x, n):
    return np.concatenate([x, np.zeros(max(0, n - len(x)))])[:n]


def mix(*parts):
    n = max(len(p) for p in parts)
    out = np.zeros(n)
    for p in parts:
        out[:len(p)] += p
    return out


# ---------------------------------------------------------------------------------------------- oscillators

def phase_of(freq, n):
    """Running phase (radians) of a frequency that may be a number or a per-sample array."""
    f = np.broadcast_to(np.asarray(freq, dtype=float), (n,))
    return 2 * np.pi * np.cumsum(f) / SR


def sine(freq, dur, phase0=0.0):
    n = ns(dur)
    return np.sin(phase_of(freq, n) + phase0)


def additive(freq, dur, partials, phase0=None):
    """Sum of sines at freq*ratio with the given amplitudes; partials above Nyquist are dropped.
    `partials` is a list of (ratio, amp). freq may be a per-sample array (glides, vibrato)."""
    n = ns(dur)
    ph = phase_of(freq, n)
    fmax = float(np.max(freq)) if np.ndim(freq) else float(freq)
    out = np.zeros(n)
    for k, (r, a) in enumerate(partials):
        if fmax * r >= SR * 0.45:
            continue
        p0 = 0.0 if phase0 is None else phase0[k % len(phase0)]
        out += a * np.sin(ph * r + p0)
    return out


def saw_partials(count, roll=1.0):
    """Harmonic series with 1/n^roll amplitudes (a soft sawtooth)."""
    return [(k, 1.0 / k ** roll) for k in range(1, count + 1)]


def sweep(f0, f1, dur, curve='exp'):
    """A per-sample frequency glide from f0 to f1."""
    x = np.linspace(0, 1, ns(dur))
    if curve == 'exp':
        return f0 * (f1 / f0) ** x
    if curve == 'lin':
        return f0 + (f1 - f0) * x
    k = float(curve)                       # a number bends the glide: >1 slow start, <1 fast start
    return f0 * (f1 / f0) ** (x ** k)


def vibrato(base, dur, rate=5.0, depth_cents=15, delay=0.0, rise=0.3, seed=0):
    """Per-sample frequency with a vibrato that fades in after `delay` over `rise` seconds, with a little drift."""
    t = tt(dur)
    r = rng(seed)
    amt = np.clip((t - delay) / max(rise, 1e-3), 0, 1)
    wob = np.sin(2 * np.pi * rate * t + r.uniform(0, 6.28)) + 0.3 * np.sin(2 * np.pi * rate * 0.37 * t + r.uniform(0, 6.28))
    return base * 2 ** (depth_cents * amt * wob / 1200 / 1.3)


# ---------------------------------------------------------------------------------------------- envelopes

def adsr(dur, a=0.01, d=0.1, s=0.7, r=0.1, curve=3.0):
    """Attack / decay / sustain / release envelope stretched over `dur` seconds (release included)."""
    n = ns(dur)
    t = np.arange(n) / SR
    env = np.empty(n)
    rel_start = max(dur - r, a + d * 0.2)
    e_att = np.clip(t / max(a, 1e-4), 0, 1)
    e_dec = s + (1 - s) * np.exp(-curve * np.clip(t - a, 0, None) / max(d, 1e-4))
    env = np.where(t < a, e_att, e_dec)
    lvl = env[min(n - 1, int(rel_start * SR))]
    rel = lvl * np.clip(1 - (t - rel_start) / max(r, 1e-4), 0, 1) ** 2
    return np.where(t < rel_start, env, rel)


def perc(dur, attack=0.002, decay=0.2):
    """Fast attack then exponential decay (time constant `decay` seconds); ends near zero."""
    t = tt(dur)
    e = np.clip(t / max(attack, 1e-5), 0, 1) * np.exp(-np.clip(t - attack, 0, None) / decay)
    return e * end_ramp(len(t), 0.01)


def end_ramp(n, fade=0.01):
    """1 everywhere except a short linear fade to 0 at the end."""
    k = min(n, max(1, int(fade * SR)))
    e = np.ones(n)
    e[-k:] = np.linspace(1, 0, k)
    return e


def swell(dur, peak=0.5, p=2.0):
    """Smooth rise to a peak at fraction `peak` of the sound, then fall: a raised-cosine-ish bump."""
    x = np.linspace(0, 1, ns(dur))
    up = np.clip(x / peak, 0, 1)
    dn = np.clip((1 - x) / (1 - peak), 0, 1)
    return (np.sin(np.pi / 2 * np.minimum(up, 1)) ** p) * np.where(x < peak, 1, np.sin(np.pi / 2 * dn) ** p)


def fade(x, fin=0.002, fout=0.02):
    x = x.copy()
    a, b = min(len(x), ns(fin)), min(len(x), ns(fout))
    x[:a] *= np.linspace(0, 1, a) ** 2
    x[len(x) - b:] *= np.linspace(1, 0, b) ** 2
    return x


def smooth_noise(dur, rate, seed, lo=0.0, hi=1.0):
    """Slow random wander (control signal) between lo and hi, with about `rate` changes per second."""
    n = ns(dur)
    r = rng(seed)
    k = max(6, int(dur * rate) + 5)
    pts = r.uniform(0, 1, k)
    x = np.linspace(0, k - 4, n)
    i = np.floor(x).astype(int)
    f = x - i
    # Catmull-Rom between random points: smooth, no corners
    p0, p1, p2, p3 = pts[i], pts[i + 1], pts[i + 2], pts[i + 3]
    v = 0.5 * ((2 * p1) + (-p0 + p2) * f + (2 * p0 - 5 * p1 + 4 * p2 - p3) * f ** 2 + (-p0 + 3 * p1 - 3 * p2 + p3) * f ** 3)
    return lo + (hi - lo) * np.clip(v, 0, 1)


# ---------------------------------------------------------------------------------------------- noise

def white(dur, seed):
    return rng(seed).standard_normal(ns(dur)) * 0.3


def colored(dur, seed, slope_db=-3.0, lo=20.0, hi=20000.0):
    """Gaussian noise with a spectral tilt (dB per octave): -3 pink, -6 brown; band-limited to lo..hi."""
    n = ns(dur)
    spec = np.fft.rfft(rng(seed).standard_normal(n))
    f = np.fft.rfftfreq(n, 1 / SR)
    g = np.where(f > 0, (np.maximum(f, 1.0) / 1000.0) ** (slope_db / 6.0206), 0)
    g *= np.clip((f - lo * 0.7) / (lo * 0.3), 0, 1) * np.clip((hi * 1.3 - f) / (hi * 0.3), 0, 1)
    x = np.fft.irfft(spec * g, n)
    return x / (np.std(x) + 1e-12) * 0.3


# ---------------------------------------------------------------------------------------------- filters

def fftconv(x, h):
    """Linear convolution. Long signals with a much shorter kernel use overlap-add in 50%-hop blocks."""
    n = len(x) + len(h) - 1
    step = max(1024, 1 << (len(h) - 1).bit_length())
    if len(x) < 4 * step:
        m = 1 << (n - 1).bit_length()
        return np.fft.irfft(np.fft.rfft(x, m) * np.fft.rfft(h, m), m)[:n]
    B = 2 * step
    nb = -(-len(x) // step)
    frames = np.zeros(nb * step)
    frames[:len(x)] = x
    Y = np.fft.irfft(np.fft.rfft(frames.reshape(nb, step), B, axis=1) * np.fft.rfft(h, B)[None, :], B, axis=1)
    out = np.zeros((nb + 1) * step)
    out[:nb * step] += Y[:, :step].ravel()
    out[step:] += Y[:, step:].ravel()
    return out[:n]


@lru_cache(maxsize=512)
def _impulse(b, a, maxlen):
    """Impulse response of a (b, a) recursive filter, run until it has died away."""
    b = list(b) + [0.0] * (len(a) - len(b))
    order = len(a) - 1
    xs = [0.0] * (order + 1)
    ys = [0.0] * (order + 1)
    out = []
    quiet = 0
    for i in range(maxlen):
        x0 = 1.0 if i == 0 else 0.0
        xs = [x0] + xs[:-1]
        y0 = sum(b[k] * xs[k] for k in range(order + 1)) - sum(a[k] * ys[k - 1] for k in range(1, order + 1))
        ys = [y0] + ys[:-1]
        out.append(y0)
        quiet = quiet + 1 if abs(y0) < 1e-7 else 0
        if i > 64 and quiet > 64:
            break
    return np.array(out)


def lfilter(b, a, x, maxlen=SR * 2):
    """Apply a recursive filter (normalised so a[0] == 1) by convolving with its impulse response.
    The output has the same length as the input (tails are cut; pad first if you want them)."""
    a0 = a[0]
    b = tuple(float(v) / a0 for v in b)
    a = tuple(float(v) / a0 for v in a)
    h = _impulse(b, a, int(maxlen))
    return fftconv(x, h)[:len(x)]


def onepole_lp(x, fc):
    p = math.exp(-2 * math.pi * fc / SR)
    return lfilter((1 - p,), (1, -p), x)


def onepole_hp(x, fc):
    return x - onepole_lp(x, fc)


def biquad_coefs(kind, fc, q=0.707, gain_db=0.0):
    """RBJ audio-EQ-cookbook biquads: lp, hp, bp (constant 0 dB peak), notch, peak, lowshelf, highshelf."""
    fc = min(fc, SR * 0.49)
    w = 2 * math.pi * fc / SR
    cw, sw = math.cos(w), math.sin(w)
    al = sw / (2 * q)
    A = 10 ** (gain_db / 40)
    if kind == 'lp':
        b = ((1 - cw) / 2, 1 - cw, (1 - cw) / 2); a = (1 + al, -2 * cw, 1 - al)
    elif kind == 'hp':
        b = ((1 + cw) / 2, -(1 + cw), (1 + cw) / 2); a = (1 + al, -2 * cw, 1 - al)
    elif kind == 'bp':
        b = (al, 0, -al); a = (1 + al, -2 * cw, 1 - al)
    elif kind == 'notch':
        b = (1, -2 * cw, 1); a = (1 + al, -2 * cw, 1 - al)
    elif kind == 'peak':
        b = (1 + al * A, -2 * cw, 1 - al * A); a = (1 + al / A, -2 * cw, 1 - al / A)
    elif kind in ('lowshelf', 'highshelf'):
        sq = 2 * math.sqrt(A) * al
        if kind == 'lowshelf':
            b = (A * ((A + 1) - (A - 1) * cw + sq), 2 * A * ((A - 1) - (A + 1) * cw), A * ((A + 1) - (A - 1) * cw - sq))
            a = ((A + 1) + (A - 1) * cw + sq, -2 * ((A - 1) + (A + 1) * cw), (A + 1) + (A - 1) * cw - sq)
        else:
            b = (A * ((A + 1) + (A - 1) * cw + sq), -2 * A * ((A - 1) + (A + 1) * cw), A * ((A + 1) + (A - 1) * cw - sq))
            a = ((A + 1) - (A - 1) * cw + sq, 2 * ((A - 1) - (A + 1) * cw), (A + 1) - (A - 1) * cw - sq)
    else:
        raise ValueError(kind)
    return tuple(round(v, 12) for v in b), tuple(round(v, 12) for v in a)


def biquad(x, kind, fc, q=0.707, gain_db=0.0):
    b, a = biquad_coefs(kind, fc, q, gain_db)
    return lfilter(b, a, x)


def lp(x, fc, q=0.707):
    return biquad(x, 'lp', fc, q)


def hp(x, fc, q=0.707):
    return biquad(x, 'hp', fc, q)


def bp(x, fc, q=1.0):
    return biquad(x, 'bp', fc, q)


def resonate(x, modes):
    """Run an excitation through parallel resonant band-passes: modes = [(freq, q, gain), ...]."""
    out = np.zeros(len(x))
    for f, q, g in modes:
        if f < SR * 0.45:
            out += g * bp(x, f, q)
    return out


def svf(x, fc, q=0.7, mode='bp'):
    """State-variable filter whose cutoff may move per sample (Simper/Cytomic TPT form).
    Plain Python loop: use on short sounds only."""
    n = len(x)
    fc = np.broadcast_to(np.asarray(fc, dtype=float), (n,))
    g = np.tan(np.pi * np.clip(fc, 10, SR * 0.45) / SR)
    k = 1.0 / q
    a1 = 1 / (1 + g * (g + k))
    a2 = g * a1
    a3 = g * a2
    ic1 = ic2 = 0.0
    out = np.empty(n)
    xl = x.tolist(); a1l = a1.tolist(); a2l = a2.tolist(); a3l = a3.tolist()
    sel = {'lp': 0, 'bp': 1, 'hp': 2}[mode]
    for i in range(n):
        v0 = xl[i]
        v3 = v0 - ic2
        v1 = a1l[i] * ic1 + a2l[i] * v3
        v2 = ic2 + a2l[i] * ic1 + a3l[i] * v3
        ic1 = 2 * v1 - ic1
        ic2 = 2 * v2 - ic2
        out[i] = v2 if sel == 0 else (v1 if sel == 1 else v0 - k * v1 - v2)
    return out


def stft_shape(x, gain_fn, hop=512):
    """Filter a (long) signal with a gain that changes over time and frequency.
    gain_fn(times[F], freqs[B]) -> array[F, B] of linear gains. Hann windows at 4x overlap, overlap-added."""
    win_n = hop * 4
    w = np.hanning(win_n + 1)[:-1]
    n = len(x)
    frames = int(math.ceil(n / hop)) + 4
    xp = np.concatenate([np.zeros(win_n), x, np.zeros(win_n + frames * hop - n)])
    idx = np.arange(frames)[:, None] * hop + np.arange(win_n)[None, :]
    spec = np.fft.rfft(xp[idx] * w, axis=1)
    times = (np.arange(frames) * hop + win_n / 2 - win_n) / SR
    freqs = np.fft.rfftfreq(win_n, 1 / SR)
    spec *= gain_fn(times, freqs)
    seg = np.fft.irfft(spec, win_n, axis=1) * w
    out = np.zeros(len(xp))
    np.add.at(out, idx, seg)
    norm = np.zeros(len(xp))
    np.add.at(norm, idx, np.broadcast_to(w * w, seg.shape))
    out /= np.maximum(norm, 1e-3)
    return out[win_n:win_n + n]


# ---------------------------------------------------------------------------------------------- strings and resonators

def karplus(freq, dur, seed=0, bright=0.6, decay=4.0, pick=0.12, body=None):
    """Karplus-Strong plucked string.
    bright: 0..1, how sharp the pluck is (low-passes the noise burst that excites the string).
    decay: seconds for the note to fall by 60 dB (sets the loop gain).
    pick: pluck position as a fraction of the string (comb notch gives the 'plucked near the bridge' tone).
    The loop filter is a 2-tap average convolved with linear interpolation (3 taps), which keeps the tuning
    exact to a fraction of a sample. Runs a whole period per numpy step."""
    n = ns(dur)
    D = SR / freq
    N = int(math.floor(D - 0.5))
    f = D - 0.5 - N
    w0, w1, w2 = 0.5 * (1 - f), 0.5, 0.5 * f
    g = 10 ** (-3 * D / (decay * SR))            # per-period loss so the note dies 60 dB in `decay` s
    r = rng(seed)
    exc = r.uniform(-1, 1, N)
    # soften the excitation: one-pole low-pass, coefficient from brightness
    c = 0.05 + 0.9 * (1 - bright)
    for _ in range(2):
        e2 = np.empty_like(exc)
        acc = 0.0
        for i, v in enumerate(exc):
            acc = (1 - c) * v + c * acc
            e2[i] = acc
        exc = e2
    exc -= exc.mean()
    p = max(1, int(round(pick * N)))
    exc = exc - np.roll(exc, p)                  # pick-position comb
    exc /= (np.max(np.abs(exc)) + 1e-9)
    y = np.zeros(n + N + 2)
    off = N + 2                                  # y[off + i] is output sample i
    x = np.zeros(n + N + 2)
    x[off:off + N] = exc[:max(0, min(N, n))]
    for s in range(off, n + off, N):
        e = min(s + N, n + off)
        L = e - s
        y[s:e] = x[s:e] + g * (w0 * y[s - N:s - N + L] + w1 * y[s - N - 1:s - N - 1 + L] + w2 * y[s - N - 2:s - N - 2 + L])
    out = y[off:off + n]
    out -= np.mean(out[:N])
    out = onepole_hp(out, 30)
    if body:
        out = out * 0.6 + resonate(out, body)
    return out * end_ramp(n, 0.02)


def modal(freq, dur, modes, seed=0, strike=0.002, noise=0.0):
    """Struck object: a sum of decaying sines. modes = [(ratio, amp, decay_seconds), ...].
    A tiny random detune per mode keeps repeated hits from sounding identical."""
    n = ns(dur)
    t = np.arange(n) / SR
    r = rng(seed)
    out = np.zeros(n)
    for ratio, amp, dec in modes:
        f = freq * ratio * (1 + r.uniform(-0.002, 0.002))
        if f >= SR * 0.45:
            continue
        out += amp * np.sin(2 * np.pi * f * t + r.uniform(0, 6.28)) * np.exp(-t / dec)
    out *= np.clip(t / strike, 0, 1)
    if noise:
        out += noise * hp(white(dur, seed + 1), 2000) * np.exp(-t / 0.01)
    return out * end_ramp(n, 0.01)


# ---------------------------------------------------------------------------------------------- reverb

def comb(x, delay, fb, damp=0.3):
    """Feedback comb y[n] = x[n] + fb * ((1-damp) y[n-M] + damp y[n-M-1]), run one delay-length block at a time."""
    M = int(delay)
    n = len(x)
    y = np.zeros(n + M + 1)
    xp = np.concatenate([np.zeros(M + 1), x])
    for s in range(M + 1, n + M + 1, M):
        e = min(s + M, n + M + 1)
        L = e - s
        y[s:e] = xp[s:e] + fb * ((1 - damp) * y[s - M:s - M + L] + damp * y[s - M - 1:s - M - 1 + L])
    return y[M + 1:]


def allpass(x, delay, g=0.5):
    """Schroeder allpass y[n] = -g x[n] + x[n-M] + g y[n-M], block at a time."""
    M = int(delay)
    n = len(x)
    xp = np.concatenate([np.zeros(M), x])
    y = np.zeros(n + M)
    for s in range(M, n + M, M):
        e = min(s + M, n + M)
        L = e - s
        y[s:e] = -g * xp[s:e] + xp[s - M:s - M + L] + g * y[s - M:s - M + L]
    return y[M:]


def reverb(x, size=1.0, decay=0.84, damp=0.35, wet=0.25, predelay=0.012, tail=1.5, lowcut=150.0, dry=1.0):
    """Freeverb-style mono reverb: 8 parallel damped combs into 4 series allpasses.
    size scales the delay lengths (room size), decay is comb feedback, wet the send level.
    Returns dry + wet, extended by `tail` seconds so the ring-out is kept."""
    x = np.concatenate([x, np.zeros(ns(tail))])
    send = hp(x, lowcut) if lowcut else x
    send = np.concatenate([np.zeros(ns(predelay)), send])[:len(x)]
    combs = [1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617]
    acc = np.zeros(len(x))
    for d in combs:
        acc += comb(send, int(d * size), decay, damp)
    acc /= len(combs)
    for d in (556, 441, 341, 225):
        acc = allpass(acc, int(d * size), 0.5)
    return dry * x + wet * acc


# ---------------------------------------------------------------------------------------------- levels and finishing

def peak(x):
    return float(np.max(np.abs(x))) if len(x) else 0.0


def rms(x):
    return float(np.sqrt(np.mean(x * x))) if len(x) else 0.0


def normalize_peak(x, target_db):
    return x * (undb(target_db) / (peak(x) + 1e-12))


def normalize_rms(x, target_db):
    return x * (undb(target_db) / (rms(x) + 1e-12))


def sliding_max(x, half):
    """max(x[i-half .. i+half]) for every i, by repeated doubling (log2 passes)."""
    out = x.copy()
    span = 1
    total = 2 * half + 1
    # build max over windows of length `span` starting at i, doubling span
    cur = x.copy()
    w = 1
    while w * 2 <= total:
        nxt = cur.copy()
        nxt[:-w] = np.maximum(cur[:-w], cur[w:])
        cur = nxt
        w *= 2
    # combine two overlapping windows of length w to cover `total`
    rest = total - w
    win = cur.copy()
    if rest > 0:
        win[:-rest] = np.maximum(cur[:-rest], cur[rest:])
    # win[i] = max over x[i .. i+total-1]; shift so it is centred
    out = np.concatenate([np.full(half, win[0]), win])[:len(x)]
    return out


def limit(x, ceiling_db, look=0.004):
    """Look-ahead peak limiter: gain is smoothed so it is already down when the peak arrives."""
    thr = undb(ceiling_db)
    half = ns(look)
    env = sliding_max(np.abs(x), half)
    g = np.minimum(1.0, thr / np.maximum(env, 1e-9))
    k = half  # moving average of width `half` keeps every gain at or below what each sample needs
    c = np.cumsum(np.concatenate([np.zeros(1), g]))
    lo = np.clip(np.arange(len(g)) - k // 2, 0, len(g))
    hi = np.clip(np.arange(len(g)) + k // 2 + 1, 0, len(g))
    g = (c[hi] - c[lo]) / (hi - lo)
    return x * g


def finish_sfx(x, peak_db=-3.0, fout=0.03, gate_db=-55.0):
    """Trim leading/trailing near-silence, fade both edges, remove DC, normalise the peak."""
    x = np.asarray(x, dtype=float)
    x = x - np.mean(x)
    p = peak(x)
    thr = p * undb(gate_db)
    loud = np.nonzero(np.abs(x) > thr)[0]
    x = x[max(0, loud[0] - 16):loud[-1] + 1]
    x = fade(x, 0.0015, min(fout, len(x) / SR * 0.3))
    x = x - np.mean(x) * np.hanning(len(x)) / 0.5   # pull any residual DC out without touching the edges
    return normalize_peak(x, peak_db)


# ---------------------------------------------------------------------------------------------- loops

def loop_wrap(x, L):
    """Fold everything after sample L back onto the start (for scheduled events and ring-outs)."""
    out = np.zeros(L)
    for s in range(0, len(x), L):
        seg = x[s:s + L]
        out[:len(seg)] += seg
    return out


def loop_xfade(x, L, T):
    """Seamless loop from a render of length >= L + T: the extra T samples fade out over the head as it fades in
    (equal power, for uncorrelated noise). Sample L-1 then flows straight into sample 0."""
    assert len(x) >= L + T
    out = x[:L].copy()
    a = np.linspace(0, 1, T)
    fin, fo = np.sin(a * np.pi / 2), np.cos(a * np.pi / 2)
    out[:T] = x[:T] * fin + x[L:L + T] * fo
    return out


def circular(fn, x):
    """Apply a time-invariant process (reverb, filter, limiter) to a loop as if it repeated forever."""
    L = len(x)
    y = fn(np.concatenate([x, x, x]))
    return y[L:2 * L]
