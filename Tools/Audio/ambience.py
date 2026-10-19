"""Ambience loops. Each is a steady noise bed (rendered a few seconds long and cross-faded into its own start)
plus scattered events (waves, gulls, birds, crickets, crackles, creaks) placed at seeded random times and folded
back onto the start, so the loop has no seam and no event is cut in half."""
import numpy as np
from dsp import (SR, ns, tt, rng, place, colored, white, lp, hp, bp, svf, additive, saw_partials, smooth_noise,
                 stft_shape, reverb, loop_wrap, loop_xfade, circular, normalize_rms, limit, end_ramp, modal, resonate)
from sfx import crackle, bubble, wood, thump
from instruments import anvil

LOOP = 40.0     # seconds
XF = 4.0        # seconds of cross-fade for the noise beds


def bed(fn, seed):
    """Render a bed of LOOP + XF seconds with fn(dur, seed) and turn it into a seamless loop."""
    x = fn(LOOP + XF, seed)
    return loop_xfade(x, ns(LOOP), ns(XF))


def events():
    return np.zeros(ns(LOOP + 12.0))


def fold(ev):
    return loop_wrap(ev, ns(LOOP))


def schedule(seed, mean_gap, jitter=0.5, start=0.5, end=LOOP):
    """Event times from `start` to `end`, gaps around mean_gap (+- jitter fraction)."""
    r = rng(seed)
    out, at = [], start + r.uniform(0, mean_gap)
    while at < end:
        out.append(at)
        at += mean_gap * r.uniform(1 - jitter, 1 + jitter)
    return out


# ---------------------------------------------------------------------------------------------- noise beds

def wind_noise(dur, seed, fc_lo=250, fc_hi=900, rate=0.15, amp_depth=0.6, whistle=0.0):
    """Wind: pink noise under a low-pass whose cutoff and loudness drift slowly together (gusts)."""
    x = colored(dur, seed, -3.0)
    gust = smooth_noise(dur + 1, rate, seed + 1)
    gust2 = smooth_noise(dur + 1, rate * 3.1, seed + 2)
    g = np.clip(0.65 * gust + 0.35 * gust2, 0, 1)

    def shape(times, freqs):
        k = np.interp(times, np.arange(len(g)) / SR, g)[:, None]
        fc = fc_lo + (fc_hi - fc_lo) * k
        lpf = 1 / np.sqrt(1 + (freqs[None, :] / fc) ** 4)
        hpf = 1 / np.sqrt(1 + (60 / np.maximum(freqs[None, :], 1)) ** 4)
        return (1 - amp_depth + amp_depth * k) * lpf * hpf
    out = stft_shape(x, shape)
    if whistle:
        wf = 500 + 400 * smooth_noise(dur, 0.1, seed + 3)
        w = svf(colored(dur, seed + 4, 0), wf, 25, 'bp') if dur < 5 else _whistle(dur, seed, wf)
        out += whistle * w * g[:len(out)] ** 2
    return out


def _whistle(dur, seed, wf):
    """Narrow moving resonance for gusty wind, done in the frequency domain (too long for a sample loop)."""
    x = colored(dur, seed + 4, 0)
    def shape(times, freqs):
        f0 = np.interp(times, np.arange(len(wf)) / SR, wf)[:, None]
        return np.exp(-0.5 * ((freqs[None, :] - f0) / 25) ** 2) * 4
    return stft_shape(x, shape)


def surf_rumble(dur, seed):
    x = colored(dur, seed, -6.0)
    return lp(x, 350) * (0.7 + 0.3 * smooth_noise(dur, 0.12, seed + 1))


def rain_bed(dur, seed):
    x = colored(dur, seed, -2.0)
    x = lp(hp(x, 500), 9000) * (0.9 + 0.1 * smooth_noise(dur, 0.2, seed + 1))
    rumble = lp(colored(dur, seed + 2, -6.0), 200) * 0.6
    return x + rumble


def room_tone(dur, seed):
    return lp(colored(dur, seed, -6.0), 220) * 0.8 + lp(colored(dur, seed + 1, -3.0), 1800) * 0.08


def fire_bed(dur, seed):
    roar = lp(colored(dur, seed, -6.0), 450) * (0.6 + 0.4 * smooth_noise(dur, 1.5, seed + 1))
    hiss = hp(colored(dur, seed + 2, -1.0), 5000) * 0.05 * (0.5 + smooth_noise(dur, 3, seed + 3))
    return roar + hiss


# ---------------------------------------------------------------------------------------------- events

def wave(seed, size=1.0):
    """One wave: the swell rolls in (dark, rising), breaks (bright, loud), then washes back up the sand and
    drains away (hiss and fizz fading, the low end leaving first)."""
    r = rng(seed)
    d = r.uniform(6.0, 8.0)
    tb = r.uniform(1.6, 2.4)                  # break time
    x = colored(d, seed, -3.0)

    def shape(times, freqs):
        t = np.clip(times, 0, d)[:, None]
        f = freqs[None, :]
        rise = np.clip(t / tb, 0, 1) ** 2
        after = np.clip(t - tb, 0, None)
        amp = np.where(t < tb, 0.15 + 0.85 * rise, np.exp(-after / (d * 0.32)))
        fc = np.where(t < tb, 250 + 1200 * rise, 1400 + 2600 * np.exp(-after / 0.6) - 400 * np.clip(after / (d - tb), 0, 1))
        hc = np.where(t < tb, 40, 40 + 500 * np.clip(after / (d - tb), 0, 1))
        lpf = 1 / np.sqrt(1 + (f / fc) ** 2)
        hpf = 1 / np.sqrt(1 + (hc / np.maximum(f, 1)) ** 2)
        return amp * lpf * hpf * size
    s = stft_shape(x, shape)
    # fizz of bubbles and sand as the wash drains
    fz = np.zeros(len(s))
    for k, at in enumerate(np.sort(r.uniform(tb, d - 0.3, 150))):
        place(fz, bubble(r.uniform(2500, 6000), 0.02, seed + k), at, r.uniform(0.02, 0.08) * np.exp(-(at - tb) / 2.0))
    out = (s + fz * size) * end_ramp(len(s), 0.3)
    out[:ns(0.3)] *= np.linspace(0, 1, ns(0.3))
    return out


def gull(seed, dist=1.0):
    """Herring-gull 'kee-ow' calls in a short burst, softened by distance."""
    r = rng(seed)
    out = np.zeros(ns(2.5))
    for k in range(int(r.integers(2, 5))):
        d = r.uniform(0.25, 0.4)
        u = np.linspace(0, 1, ns(d))
        f = np.interp(u, [0, 0.25, 1], [1300, 2100, 1250]) * r.uniform(0.92, 1.08)
        src = additive(f, d, saw_partials(8, 1.4))
        s = 0.8 * bp(src, 2200, 2) + 0.6 * bp(src, 3600, 3) + 0.15 * src
        s *= np.clip(u / 0.08, 0, 1) * np.clip((1 - u) / 0.3, 0, 1)
        place(out, s, k * r.uniform(0.35, 0.5), r.uniform(0.6, 1.0))
    out = lp(out, 6000 - 3500 * dist)
    return reverb(out, size=1.3, decay=0.8, wet=0.4 * dist, tail=1.0) * (1.2 - 0.7 * dist)


def bird(seed):
    """A short songbird phrase. Three kinds: rising chirps, a two-note whistle, a trill."""
    r = rng(seed)
    kind = int(r.integers(3))
    out = np.zeros(ns(2.0))
    if kind == 0:
        base = r.uniform(3000, 4200)
        for k in range(int(r.integers(3, 6))):
            d = r.uniform(0.05, 0.08)
            f = np.linspace(base, base * 1.45, ns(d))
            s = additive(f, d, [(1, 1), (2, 0.1)]) * np.hanning(ns(d))
            place(out, s, k * r.uniform(0.11, 0.15))
    elif kind == 1:
        hi = r.uniform(2600, 3200)
        for k, (f0, f1, d) in enumerate(((hi, hi * 1.02, 0.22), (hi * 0.78, hi * 0.72, 0.28))):
            f = np.linspace(f0, f1, ns(d))
            s = additive(f, d, [(1, 1), (2, 0.05)]) * np.sin(np.pi * np.linspace(0, 1, ns(d))) ** 0.7
            place(out, s, k * 0.3)
    else:
        d = r.uniform(0.4, 0.7)
        f = r.uniform(3800, 4800) * (1 - 0.1 * np.linspace(0, 1, ns(d)))
        am = 0.5 + 0.5 * np.sin(2 * np.pi * r.uniform(22, 30) * tt(d))
        s = additive(f, d, [(1, 1), (2, 0.08)]) * am ** 2 * np.sin(np.pi * np.linspace(0, 1, ns(d)))
        place(out, s, 0)
    return reverb(lp(out, 7000), size=1.0, decay=0.75, wet=0.2, tail=0.6) * r.uniform(0.4, 1.0)


def cricket(seed, length, gain):
    """One cricket chirping on its own rhythm through the whole loop."""
    r = rng(seed)
    carrier = r.uniform(3900, 5000)
    out = np.zeros(ns(length + 1))
    period = r.uniform(0.45, 0.85)
    pulses = int(r.integers(3, 5))
    pl, pg = 0.014, r.uniform(0.026, 0.034)
    one = np.sin(2 * np.pi * carrier * tt(pl)) * np.hanning(ns(pl))
    at = r.uniform(0, period)
    while at < length:
        for p in range(pulses):
            place(out, one, at + p * pg, gain * (0.8 + 0.2 * r.uniform()))
        at += period * r.uniform(0.95, 1.05)
    return out


def owl(seed):
    r = rng(seed)
    out = np.zeros(ns(3.0))
    pattern = ((0.0, 0.45), (0.9, 0.22), (1.18, 0.22), (1.46, 0.5))
    base = r.uniform(360, 400)
    for at, d in pattern:
        u = np.linspace(0, 1, ns(d))
        f = base * (1.03 - 0.06 * u)
        s = additive(f, d, [(1, 1), (2, 0.08), (3, 0.03)]) * np.sin(np.pi * u) ** 1.5
        s += 0.05 * bp(white(d, seed), base * 2, 2) * np.sin(np.pi * u)
        place(out, s, at)
    return reverb(lp(out, 2000), size=1.5, decay=0.85, wet=0.45, tail=1.5)


def creak(seed, dur=0.6):
    r = rng(seed)
    n = ns(dur)
    rate = r.uniform(30, 60) * (1 + 0.5 * np.sin(np.pi * np.linspace(0, 1, n)))
    ph = np.cumsum(rate) / SR
    pulses = np.zeros(n)
    idx = np.nonzero(np.diff(np.floor(ph)) > 0)[0]
    pulses[idx] = r.uniform(0.4, 1.0, len(idx))
    base = r.uniform(250, 450)
    s = resonate(pulses, [(base, 12, 1.0), (base * 2.2, 14, 0.6), (base * 4.7, 16, 0.3)])
    return s * np.sin(np.pi * np.linspace(0, 1, n)) ** 1.5


def hammer_far(seed):
    """A smith somewhere across the village: three to five strikes, dull and far."""
    r = rng(seed)
    out = np.zeros(ns(6.0))
    at = 0.0
    for k in range(int(r.integers(3, 6))):
        a = anvil(1150 * r.uniform(0.97, 1.03), r.uniform(0.6, 1.0), seed + k, dur=1.2, damp=0.6)
        place(out, a, at)
        at += r.uniform(0.75, 0.95)
    out = lp(hp(out, 500), 2500)
    return reverb(out, size=1.4, decay=0.85, wet=0.6, tail=1.5)


# ---------------------------------------------------------------------------------------------- the loops

def finish(x, rms_db=-20.0, ceiling=-6.6):
    x = circular(lambda v: hp(v, 30), x)
    x = x - np.mean(x)
    for _ in range(3):
        x = normalize_rms(x, rms_db)
        x = circular(lambda v: limit(v, ceiling, 0.005), x)
    return x


def amb_sea():
    ev = events()
    for i, at in enumerate(schedule(1000, 7.5, 0.3, start=0.0)):
        place(ev, wave(1001 + i, size=np.random.default_rng(1100 + i).uniform(0.7, 1.0)), at)
    for i, at in enumerate(schedule(1200, 14, 0.4, start=3)):
        place(ev, gull(1201 + i, dist=0.85), at, 0.8)
    return finish(fold(ev) + 0.5 * bed(surf_rumble, 1300) + 0.15 * bed(lambda d, s: wind_noise(d, s, 300, 1200), 1310))


def amb_day():
    ev = events()
    for i, at in enumerate(schedule(2000, 4.5, 0.6)):
        place(ev, bird(2001 + i), at, 0.7)
    return finish(fold(ev) + bed(lambda d, s: hp(wind_noise(d, s, 300, 1600, 0.15, 0.6), 150), 2100)
                  + 0.25 * bed(lambda d, s: hp(colored(d, s, -1.0), 3000) * smooth_noise(d, 0.3, s + 1), 2110))


def amb_night():
    ev = events()
    for i in range(6):
        g = (0.35, 0.3, 0.22, 0.15, 0.12, 0.1)[i]
        ev[:ns(LOOP + 1)] += pad_len(cricket(3000 + i, LOOP, g), ns(LOOP + 1))
    for i, at in enumerate((11.0, 31.5)):
        place(ev, owl(3100 + i), at, 0.5 if i == 0 else 0.35)
    ev = lp(ev, 9000)
    return finish(fold(ev) + 0.7 * bed(lambda d, s: wind_noise(d, s, 200, 700, 0.1, 0.5), 3200), rms_db=-21.0)


def pad_len(x, n):
    return np.concatenate([x, np.zeros(max(0, n - len(x)))])[:n]


def amb_rain():
    ev = events()
    r = rng(4000)
    # light drops everywhere: tiny bright bubbles
    for k in range(int(LOOP * 220)):
        at = r.uniform(0, LOOP)
        place(ev, bubble(r.uniform(2000, 5500), 0.015, 0, rise=1.0), at, r.uniform(0.01, 0.05))
    # heavier drips off leaves
    taps = np.zeros(ns(LOOP + 1))
    for k in range(int(LOOP * 18)):
        i = int(r.uniform(0, LOOP) * SR)
        taps[i] += r.uniform(0.2, 1.0)
    ev[:len(taps)] += 0.25 * resonate(taps, [(900, 2, 1.0), (1800, 2, 0.6), (3300, 2, 0.4)])
    return finish(fold(ev) + bed(rain_bed, 4100))


def amb_wind():
    w = bed(lambda d, s: wind_noise(d, s, 180, 2200, 0.22, 0.85, whistle=0.06), 5000)
    low = bed(lambda d, s: lp(colored(d, s, -6.0), 120) * (0.5 + smooth_noise(d, 0.2, s + 1)), 5100)
    return finish(w + 0.6 * low)


def amb_fire():
    ev = events()
    ev[:ns(LOOP + 0.06)] += pad_len(crackle(LOOP, 9, 6000, 1.0), ns(LOOP + 0.06))
    for i, at in enumerate(schedule(6100, 5.0, 0.7)):
        place(ev, crackle(0.4, 30, 6101 + i, 1.2), at)          # a log settling: a burst of snaps
    return finish(fold(lp(ev, 9000)) * 0.6 + bed(fire_bed, 6200))


def amb_indoor():
    ev = events()
    ev[:ns(LOOP + 0.06)] += 0.25 * pad_len(crackle(LOOP, 4, 7000, 1.0), ns(LOOP + 0.06))
    for i, at in enumerate(schedule(7100, 12, 0.4, start=3)):
        place(ev, creak(7101 + i, rng(7150 + i).uniform(0.4, 0.9)), at, 0.08)
    ev = lp(ev, 5000)
    return finish(fold(ev) + bed(room_tone, 7200) + 0.3 * bed(fire_bed, 7210), rms_db=-23.0)


def amb_village():
    ev = events()
    for i, at in enumerate(schedule(8000, 8.5, 0.3, start=0.0)):
        place(ev, lp(wave(8001 + i, size=0.6), 1400), at)
    for i, at in enumerate(schedule(8100, 7, 0.5, start=1)):
        place(ev, gull(8101 + i, dist=float(rng(8150 + i).uniform(0.5, 0.95))), at, 0.3)
    for i, at in enumerate((6.0, 26.0)):
        place(ev, hammer_far(8201 + i), at, 0.12)
    return finish(fold(ev) + 0.4 * bed(surf_rumble, 8300) + 0.2 * bed(lambda d, s: wind_noise(d, s, 300, 1200), 8310))


LOOPS = {'amb_sea': amb_sea, 'amb_day': amb_day, 'amb_night': amb_night, 'amb_rain': amb_rain, 'amb_wind': amb_wind,
         'amb_fire': amb_fire, 'amb_indoor': amb_indoor, 'amb_village': amb_village}
