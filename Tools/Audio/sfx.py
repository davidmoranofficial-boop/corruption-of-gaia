"""Sound effects. Each function returns raw audio; build.py trims, fades and normalises it to the level given in
the SFX table at the bottom. Sounds are layered from a few building blocks defined first: wooden and metal modal
hits, noise whooshes through a moving band-pass, grain clouds (rustle, crunch, crackle), bubbles, and a small
source-filter voice (sawtooth-ish pulse through moving formant filters) for grunts and animal calls."""
import numpy as np
from dsp import (SR, ns, tt, rng, place, mix, mtof, sine, additive, saw_partials, sweep, adsr, perc, swell, end_ramp,
                 white, colored, lp, hp, bp, biquad, onepole_lp, onepole_hp, svf, resonate, modal, karplus, reverb,
                 smooth_noise, fade, pad_to)
from instruments import (midi, harp, lute, lute_body, strum, bass, flute, fiddle, pad, frame_drum, anvil, bell, chime,
                         chord_notes)

# ---------------------------------------------------------------------------------------------- building blocks


def wood(f, dur=0.12, seed=0, decay=0.03, noise=0.3, ratios=(1, 2.31, 3.95, 5.6)):
    """Struck wood: a few inharmonic modes that die quickly, plus a click."""
    amps = (1.0, 0.5, 0.3, 0.15)
    modes = [(r, a, decay / (1 + 0.6 * i)) for i, (r, a) in enumerate(zip(ratios, amps))]
    return modal(f, dur, modes, seed=seed, strike=0.0008, noise=noise)


def thump(f0=90, f1=50, dur=0.25, decay=0.08, seed=0, noise=0.4, noise_fc=500):
    """Low body thump: a sine dropping in pitch plus low-passed noise."""
    t = tt(dur)
    f = f1 + (f0 - f1) * np.exp(-t / (decay * 0.5))
    s = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t / decay)
    s += noise * lp(white(dur, seed), noise_fc) * np.exp(-t / (decay * 0.4)) * 3
    return s * np.clip(t / 0.0015, 0, 1) * end_ramp(len(t), 0.01)


def whoosh(dur, f0, f1, q=1.2, peak_at=0.5, seed=0, f2=None, color=-3.0):
    """Noise through a band-pass that sweeps f0 -> f1 (-> f2), loudest at `peak_at`."""
    n = ns(dur)
    x = colored(dur, seed, color)
    u = np.linspace(0, 1, n)
    if f2 is None:
        fc = f0 * (f1 / f0) ** u
    else:                                   # up to f1 at the peak, then on to f2 (log-frequency glide)
        fc = np.exp(np.interp(u, [0, peak_at, 1], np.log([f0, f1, f2])))
    return svf(x, fc, q, 'bp') * swell(dur, peak_at, 1.5) * 2.5


def grains(dur, rate, seed, fc=3000, q=1.0, glen=0.004, env=None, amp_spread=0.5, fc_spread=0.3):
    """A cloud of tiny noise clicks (rustle, crunch, crackle, tearing): `rate` grains per second.
    env(t) shapes the density/loudness over time."""
    n = ns(dur)
    r = rng(seed)
    out = np.zeros(n)
    t = np.arange(n) / SR
    density = env(t) if env is not None else np.ones(n)
    count = int(rate * dur * 1.5) + 1
    times = np.sort(r.uniform(0, dur, count))
    keep = r.uniform(0, 1, count) < np.interp(times, t, density) / max(1e-9, density.max()) * (1 / 1.5)
    gl = ns(glen)
    g = np.hanning(gl * 2)[gl:] if gl > 2 else np.ones(1)
    base = r.standard_normal(n)
    for tm, k in zip(times, keep):
        if not k:
            continue
        i = int(tm * SR)
        a = np.exp(r.normal(0, 0.6 * amp_spread)) * np.interp(tm, t, density)
        seg = base[i:i + gl] * g[:len(base[i:i + gl])]
        out[i:i + len(seg)] += a * seg
    if fc:
        # spread the colour of the grains by blending two band-passes
        out = bp(out, fc * (1 - fc_spread / 2), q) + bp(out, fc * (1 + fc_spread), q)
    return out


def bubble(f, dur=0.08, seed=0, rise=1.0, amp=1.0):
    """A single bubble: a damped sine whose pitch rises as it decays (Minnaert resonance closing up)."""
    t = tt(dur)
    tau = max(0.006, 3.0 / f ** 0.6)
    fr = f * (1 + rise * 0.6 * t / dur)
    return amp * np.sin(2 * np.pi * np.cumsum(fr) / SR) * np.exp(-t / tau) * np.clip(t / 0.0006, 0, 1)


def bubbles(dur, rate, f_lo, f_hi, seed, env=None, f_track=None):
    """Many random bubbles; f_track(t) in 0..1 can push the pitch range up over time (a vessel filling)."""
    r = rng(seed)
    out = np.zeros(ns(dur) + ns(0.2))
    count = int(rate * dur)
    for i in range(count):
        tm = r.uniform(0, dur)
        k = f_track(tm) if f_track else 0.0
        f = np.exp(r.uniform(np.log(f_lo), np.log(f_hi))) * (1 + 1.5 * k)
        a = r.uniform(0.2, 1.0) * (env(tm) if env else 1.0)
        place(out, bubble(f, 0.1, rise=r.uniform(0.5, 1.5)), tm, a)
    return out


def crackle(dur, rate, seed, loud=1.0):
    """Fire crackle: sparse sharp snaps of varying size, some with a short resonant ping."""
    r = rng(seed)
    out = np.zeros(ns(dur) + ns(0.05))
    count = int(rate * dur)
    for i in range(count):
        tm = r.uniform(0, dur)
        big = r.uniform() < 0.15
        L = r.uniform(0.002, 0.012 if big else 0.005)
        g = white(L, int(r.integers(1 << 30))) * np.exp(-tt(L) / (L * 0.3))
        g = hp(np.concatenate([g, np.zeros(ns(0.01))]), r.uniform(800, 3000))
        place(out, g, tm, loud * r.uniform(0.3, 1.0) * (2.2 if big else 1.0))
    return out


def voice(dur, f0, formants, seed=0, breath=0.2, rough=0.0, jitter=0.0, roll=1.0, env=None):
    """Source-filter voice. f0: per-sample array or number. formants: list of (freq or per-sample array, q, gain).
    rough: random amplitude flutter (growl); jitter: random pitch wobble (fraction)."""
    n = ns(dur)
    r = rng(seed)
    f = np.broadcast_to(np.asarray(f0, dtype=float), (n,)).copy()
    if jitter:
        f *= 1 + jitter * (smooth_noise(dur, 40, seed + 1) * 2 - 1)
    src = additive(f, dur, saw_partials(int(min(60, 9000 / max(60, f.min()))), roll))
    if rough:
        flutter = lp(r.standard_normal(n), 60)
        flutter /= np.std(flutter) + 1e-9
        src *= np.clip(1 + rough * flutter, 0, None)
    src += breath * white(dur, seed + 2) * 3
    out = np.zeros(n)
    for fc, q, g in formants:
        out += g * svf(src, fc, q, 'bp')
    e = env if env is not None else adsr(dur, 0.02, 0.1, 0.8, 0.1)
    return out * e


def metal_clang(f, dur=1.2, seed=0, ratios=(1, 2.71, 5.15, 8.3, 11.6), decay=0.6, noise=0.8):
    modes = [(r_, a, decay / (1 + 0.5 * i)) for i, (r_, a) in enumerate(zip(ratios, (1.0, 0.6, 0.45, 0.25, 0.12)))]
    return modal(f, dur, modes, seed=seed, strike=0.0004, noise=noise)


def to_len(x, dur):
    return pad_to(x, ns(dur))


def verb(x, wet=0.2, size=0.7, decay=0.8, tail=0.6):
    return reverb(x, size=size, decay=decay, wet=wet, tail=tail)


# ---------------------------------------------------------------------------------------------- UI

def ui_click():
    return mix(wood(1500, 0.07, seed=1, decay=0.012, noise=0.15), 0.4 * wood(2900, 0.04, seed=2, decay=0.004, noise=0))


def ui_open():
    out = np.zeros(ns(0.4))
    place(out, hp(whoosh(0.2, 500, 2600, q=1.0, peak_at=0.7, seed=3), 250), 0, 0.4)
    place(out, wood(720, 0.15, seed=4, decay=0.035, noise=0.2), 0.16)
    return out


def ui_close():
    out = np.zeros(ns(0.4))
    place(out, wood(560, 0.15, seed=5, decay=0.03, noise=0.2), 0)
    place(out, hp(whoosh(0.22, 2400, 450, q=1.0, peak_at=0.3, seed=6), 250), 0.02, 0.4)
    return out


def ui_tick():
    return mix(karplus(mtof(88), 0.12, seed=7, bright=0.5, decay=0.12, pick=0.2), 0.3 * wood(2400, 0.03, 8, 0.004, 0))


def blip():
    """One letter of someone talking: a soft, rounded little tone. The game pitches it per speaker (voice blips)."""
    t = np.arange(ns(0.06)) / SR
    f = 330.0
    w = 0.55 * np.sin(2 * np.pi * f * t) + 0.3 * np.sign(np.sin(2 * np.pi * f * t)) + 0.15 * np.sin(4 * np.pi * f * t)
    env = np.minimum(1.0, t / 0.004) * np.exp(-t / 0.022)
    return lp(w * env, 2200)


def ui_confirm():
    out = np.zeros(ns(0.6))
    place(out, harp(midi('A5'), 0.5, 0.7, 9), 0)
    place(out, harp(midi('E6'), 0.55, 0.8, 10), 0.075)
    return lute_body(out) * 0.5 + out * 0.5


def ui_deny():
    s = mix(thump(140, 95, 0.22, 0.05, seed=11, noise=0.3, noise_fc=400), 0.5 * wood(210, 0.2, seed=12, decay=0.04, noise=0))
    return lp(s, 1200)


def toast():
    out = np.zeros(ns(1.3))
    place(out, chime(midi('E6'), 1.2, 0.6, 13), 0)
    place(out, chime(midi('B6'), 1.1, 0.45, 14), 0.09)
    return verb(out, 0.25)


def page():
    dur = 0.4
    t = tt(dur)
    env = lambda tm: swell_at(tm, dur, 0.35)
    s = grains(dur, 900, 15, fc=3500, q=0.7, glen=0.003, env=env)
    s += 0.5 * grains(dur, 120, 16, fc=1800, q=1.2, glen=0.006, env=env)
    s += 0.15 * hp(white(dur, 17), 2500) * swell(dur, 0.4)
    return s


def swell_at(tm, dur, peak_at):
    x = np.clip(np.asarray(tm) / dur, 0, 1)
    return np.where(x < peak_at, np.sin(np.pi / 2 * x / peak_at), np.cos(np.pi / 2 * (x - peak_at) / (1 - peak_at))) ** 2 + 1e-3


def quest_new():
    out = np.zeros(ns(2.0))
    for i, (n, at, v) in enumerate((('D5', 0.0, 0.7), ('F#5', 0.16, 0.75), ('A5', 0.32, 0.9))):
        place(out, harp(midi(n), 1.6, v, 20 + i), at)
    place(out, harp(midi('D3'), 1.8, 0.5, 24), 0)
    place(out, harp(midi('A3'), 1.6, 0.35, 25), 0.32)
    return verb(out, 0.25, 0.9)


def quest_done():
    out = np.zeros(ns(2.6))
    seq = (('G5', 0.0), ('B5', 0.12), ('D6', 0.24), ('G6', 0.40))
    for i, (n, at) in enumerate(seq):
        place(out, lute(midi(n), 1.4, 0.75 + 0.05 * i, 30 + i, bright=0.75), at)
    for dt, a in strum(chord_notes('G', 55, 5), 0.40, 0.55, 40, gap=0.012):
        place(out, a, dt)
    place(out, bass(midi('G2'), 1.6, 0.6, 46), 0.40)
    place(out, bell(midi('G6'), 2.0, 0.35, 47), 0.42)
    out = lute_body(out) * 0.6 + out * 0.4
    return verb(out, 0.25, 0.9)


def levelup():
    out = np.zeros(ns(2.0))
    for i, n in enumerate(('C5', 'E5', 'G5', 'C6', 'E6')):
        place(out, harp(midi(n), 1.4, 0.65 + 0.06 * i, 50 + i), i * 0.07)
        place(out, lute(midi(n) - 12, 1.0, 0.35, 60 + i), i * 0.07)
    place(out, bass(midi('C3'), 1.4, 0.6, 66), 0.28)
    # shimmer: high chimes trembling over the top, and a rising airy sparkle
    for i, n in enumerate(('G6', 'C7', 'E7', 'G7')):
        c = chime(midi(n), 1.2, 0.22, 70 + i) * (1 + 0.5 * np.sin(2 * np.pi * 9 * tt(1.2)))
        place(out, c, 0.3 + i * 0.05)
    place(out, whoosh(0.9, 3000, 9000, q=2.0, peak_at=0.4, seed=75), 0.25, 0.25)
    return verb(out, 0.3, 1.0, 0.85, 1.0)


def skill_up():
    out = np.zeros(ns(1.4))
    place(out, chime(midi('E6'), 1.0, 0.6, 80), 0)
    place(out, chime(midi('A6'), 1.2, 0.7, 81), 0.12)
    place(out, harp(midi('A5'), 1.0, 0.3, 82), 0.12)
    return verb(out, 0.25)


def coin():
    out = np.zeros(ns(0.6))
    r = rng(90)
    for i, at in enumerate((0.0, 0.07, 0.15)):
        f = r.uniform(2600, 3400)
        c = modal(f, 0.4, [(1, 1, 0.12), (1.47, 0.6, 0.1), (2.41, 0.5, 0.07), (3.3, 0.3, 0.05)], seed=91 + i,
                  strike=0.0003, noise=0.5)
        place(out, c, at, 1.0 - 0.25 * i)
    return out


def pickup():
    dur = 0.25
    s = grains(dur, 500, 95, fc=1600, q=0.8, glen=0.005, env=lambda tm: swell_at(tm, dur, 0.3)) * 0.8
    s += 0.2 * lp(white(dur, 96), 1500) * swell(dur, 0.3)
    out = np.zeros(ns(0.35))
    place(out, s, 0)
    t = tt(0.06)
    pop = np.sin(2 * np.pi * np.cumsum(sweep(380, 900, 0.06)) / SR) * np.exp(-t / 0.018)
    place(out, pop, 0.13, 0.6)
    return out


def eat():
    out = np.zeros(ns(1.1))
    for i, at in enumerate((0.0, 0.33, 0.62)):
        d = 0.11 - 0.015 * i
        c = grains(d, 2500, 100 + i, fc=2200, q=0.7, glen=0.0025, env=lambda tm, d=d: swell_at(tm, d, 0.15))
        c = mix(c, 0.4 * thump(110, 70, 0.12, 0.03, seed=110 + i, noise=0.5, noise_fc=600))
        place(out, c, at, 1.0 - 0.2 * i)
    # chewing between bites: soft, low, wet
    chew = lp(grains(1.0, 120, 115, fc=700, q=0.8, glen=0.012), 1500)
    chew /= np.max(np.abs(chew)) + 1e-9
    out += 0.12 * pad_to(chew, len(out)) * np.clip(tt(1.1) / 0.1, 0, 1)[:len(out)]
    return out


def drink():
    out = np.zeros(ns(1.3))
    for i, at in enumerate((0.0, 0.36, 0.72)):
        d = 0.18
        t = tt(d)
        g = np.sin(2 * np.pi * np.cumsum(sweep(260 - 15 * i, 170, d)) / SR) * np.exp(-t / 0.05) * np.clip(t / 0.01, 0, 1)
        g += 0.6 * bp(white(d, 120 + i), 400, 2.0) * np.exp(-t / 0.04)
        place(out, g, at, 1.0)
        place(out, bubbles(0.15, 60, 500, 1400, 125 + i), at + 0.05, 0.25)
    out += 0.06 * lp(white(1.3, 129), 900) * swell(1.3, 0.5)
    return out


def door():
    out = np.zeros(ns(1.4))
    # creak: stick-slip pulses whose rate wanders, coloured by the door's wooden modes
    dur = 0.85
    rate = 45 + 40 * np.sin(np.pi * np.linspace(0, 1, ns(dur))) ** 1.5 + 8 * smooth_noise(dur, 6, 130)
    ph = np.cumsum(rate) / SR
    pulses = np.zeros(ns(dur))
    idx = np.nonzero(np.diff(np.floor(ph)) > 0)[0]
    r = rng(131)
    pulses[idx] = r.uniform(0.5, 1.0, len(idx))
    creak = resonate(pulses, [(320, 12, 1.0), (690, 14, 0.8), (1450, 16, 0.6), (2600, 18, 0.35)])
    creak *= swell(dur, 0.4, 1.0)
    creak /= np.max(np.abs(creak)) + 1e-9
    place(out, creak, 0, 0.45)
    # thud as it shuts, and the latch
    place(out, thump(85, 55, 0.35, 0.09, seed=132, noise=0.6, noise_fc=300), 0.9, 1.0)
    place(out, wood(190, 0.3, 133, decay=0.06, noise=0.2), 0.9, 0.7)
    place(out, metal_clang(2200, 0.15, 134, decay=0.03, noise=0.3), 0.93, 0.15)
    return out


def chop():
    out = np.zeros(ns(0.6))
    place(out, whoosh(0.09, 400, 1600, q=1.0, peak_at=0.9, seed=140), 0, 0.25)
    hitpt = 0.085
    place(out, hp(white(0.006, 141), 1500) * np.linspace(1, 0, ns(0.006)), hitpt, 1.0)
    place(out, wood(240, 0.4, 142, decay=0.07, noise=0.4, ratios=(1, 1.83, 2.9, 4.4)), hitpt, 1.3)
    place(out, bp(white(0.05, 143), 1800, 1.5) * np.exp(-tt(0.05) / 0.01), hitpt + 0.004, 0.8)
    place(out, thump(120, 70, 0.2, 0.04, 144, noise=0.3), hitpt, 0.6)
    return out


def water_fill():
    dur = 1.7
    t = tt(dur)
    k = np.clip(t / dur, 0, 1)
    stream = colored(dur, 150, -2.0)
    fc = 600 + 900 * k                                       # the air column above the water shortens: pitch rises
    gurgle = svf(stream, fc * (1 + 0.15 * (smooth_noise(dur, 18, 151) - 0.5)), 3.0, 'bp')
    splash = hp(stream, 2500) * 0.25
    env = np.clip(t / 0.08, 0, 1) * np.clip((dur - t) / 0.25, 0, 1)
    s = (gurgle * 1.6 + splash) * env
    b = bubbles(dur - 0.15, 90, 350, 900, 152, env=lambda tm: 1.0, f_track=lambda tm: tm / dur)
    return lp(pad_to(s, len(b)) + 0.35 * b, 7000)


def campfire():
    dur = 1.6
    t = tt(dur)
    out = np.zeros(ns(dur))
    roar = colored(dur, 160, -6.0)
    roar = svf(roar, sweep(200, 1400, dur, 0.4), 0.8, 'lp') * (np.clip(t / 0.25, 0, 1) * np.exp(-np.clip(t - 0.3, 0, None) / 0.6))
    out += roar * 1.6
    place(out, whoosh(0.5, 300, 2500, 0.9, 0.7, seed=161), 0, 0.4)
    c = pad_to(crackle(dur - 0.1, 22, 162, 0.6), len(out))
    out += c * np.clip((t - 0.1) / 0.2, 0, 1) * np.exp(-np.clip(t - 0.5, 0, None) / 0.7)
    return out


def bandage():
    dur = 0.5
    t = tt(dur)
    env = lambda tm: np.clip(np.asarray(tm) / 0.05, 0, 1) * (0.6 + 0.4 * np.asarray(tm) / dur) * (np.asarray(tm) < dur - 0.02)
    s = grains(dur, 3500, 170, fc=2600, q=0.8, glen=0.0015, env=env)
    s += 0.2 * grains(dur, 400, 171, fc=900, q=1.0, glen=0.004, env=env)
    return s


def sleep():
    out = np.zeros(ns(3.0))
    notes = (('G5', 0.0), ('E5', 0.38), ('C5', 0.76), ('G4', 1.14), ('C5', 1.6))
    for i, (n, at) in enumerate(notes):
        place(out, chime(midi(n), 1.6, 0.5, 180 + i), at)
        place(out, harp(midi(n) - 12, 1.6, 0.4, 190 + i), at)
    place(out, pad(chord_notes('C', 48, 3), 1.6, 0.6, 199, attack=0.5, release=0.8), 0.2)
    return verb(out, 0.35, 1.1, 0.86, 1.2)


def collapse():
    out = np.zeros(ns(1.4))
    place(out, thump(95, 45, 0.5, 0.12, 200, noise=0.8, noise_fc=350), 0.0, 1.0)
    place(out, thump(120, 60, 0.3, 0.06, 201, noise=0.6, noise_fc=500), 0.11, 0.6)
    place(out, thump(150, 80, 0.2, 0.04, 202, noise=0.6, noise_fc=700), 0.19, 0.35)
    place(out, grains(0.3, 600, 203, fc=1200, q=0.8, glen=0.005, env=lambda tm: swell_at(tm, 0.3, 0.2)), 0.02, 0.3)
    t = tt(1.2)
    tone = np.sin(2 * np.pi * 55 * t) * swell(1.2, 0.25, 1.0) * 0.2 + np.sin(2 * np.pi * 82.4 * t) * swell(1.2, 0.3, 1.0) * 0.07
    place(out, tone, 0.1)
    return out


def death():
    out = np.zeros(ns(3.2))
    place(out, pad([midi('D2'), midi('A2'), midi('D3'), midi('F3')], 2.2, 0.9, 210, attack=0.9, release=1.0, bright=0.6), 0)
    place(out, pad([midi('Bb2')], 1.2, 0.4, 211, attack=0.6, release=0.8, bright=0.5), 0.9)
    place(out, harp(midi('D3'), 2.5, 0.6, 212), 0.0)
    place(out, harp(midi('A3'), 2.2, 0.4, 213), 0.12)
    place(out, harp(midi('F4'), 2.0, 0.35, 214), 0.24)
    place(out, bell(midi('D4'), 2.8, 0.3, 215), 0.05)
    return lp(verb(out, 0.35, 1.2, 0.87, 1.0), 4000)


# ---------------------------------------------------------------------------------------------- the shipwreck (opening)

def thunder():
    """A close crack, then the roll: low-passed noise in uneven bursts dying away over several seconds."""
    dur = 5.5
    out = np.zeros(ns(dur))
    t = tt(0.12)
    place(out, hp(white(0.12, 300), 1500) * np.exp(-t / 0.03) * 1.2, 0.0)              # the crack
    rumble = lp(colored(dur, 301, -6.0), 220)
    bursts = smooth_noise(dur, 5.0, 302, 0.25, 1.0) ** 2
    tm = tt(dur)
    env = np.clip(tm / 0.05, 0, 1) * np.exp(-tm / 1.6) * bursts
    place(out, rumble * env * 4.0, 0.03)
    place(out, thump(70, 35, 1.2, 0.4, 303, noise=0.9, noise_fc=180), 0.02, 0.8)
    return verb(out, 0.3, 1.4, 0.88, 1.2)


def wave_crash():
    """A big wave breaking over a deck: a rising roar, the slam, then water pouring off."""
    dur = 3.6
    out = np.zeros(ns(dur))
    place(out, whoosh(1.2, 150, 900, q=0.6, peak_at=0.55, seed=310, color=-4.0) * 2.0, 0.0)
    place(out, lp(colored(1.1, 314, -5.0), 400) * np.clip(tt(1.1) / 0.01, 0, 1) * np.linspace(0.6, 1.0, ns(1.1)) * 1.5, 0.0)   # the roar is there from the start
    place(out, thump(80, 40, 0.9, 0.25, 311, noise=1.0, noise_fc=700), 1.05, 1.2)
    tm = tt(2.5)
    pour = bp(colored(2.5, 312, -2.0), 1800, 0.5) * np.exp(-tm / 0.9) * np.clip(tm / 0.04, 0, 1)
    place(out, pour * 2.2, 1.05)
    place(out, grains(2.2, 900, 313, fc=4000, q=0.7, glen=0.003, env=lambda x: np.exp(-x / 0.8)), 1.1, 0.6)
    return out


def timber_creak():
    """Hull timbers groaning under strain: a slow, rough, gliding resonance."""
    dur = 2.2
    tm = tt(dur)
    f = 95 + 40 * np.sin(2 * np.pi * 0.35 * tm) + 15 * smooth_noise(dur, 6.0, 320, -1, 1)
    saw = np.sin(2 * np.pi * np.cumsum(f) / SR)
    saw = np.sign(saw) * np.abs(saw) ** 0.4
    rough = 0.6 + 0.4 * smooth_noise(dur, 40.0, 321, 0, 1)
    x = resonate(saw * rough, [(310, 8, 1.0), (720, 10, 0.5), (1450, 12, 0.25)])
    return x * swell(dur, 0.45, 1.5)


# ---------------------------------------------------------------------------------------------- footsteps

def step(surface, v):
    r = rng(300 + 17 * v + sum(map(ord, surface)))      # fixed seed per surface and variant
    p = r.uniform(0.9, 1.12)                 # pitch / size variation
    sd = int(r.integers(1 << 20))
    out = np.zeros(ns(0.3))
    if surface == 'grass':
        d = 0.18 * r.uniform(0.9, 1.1)
        place(out, grains(d, 2500, sd, fc=4200 * p, q=0.6, glen=0.003, env=lambda tm: swell_at(tm, d, 0.25)), 0, 0.8)
        place(out, thump(90 * p, 60, 0.12, 0.025, sd + 1, noise=0.5, noise_fc=350), 0.0, 0.6)
        place(out, 0.3 * hp(white(d, sd + 2), 3000) * swell(d, 0.3), 0)
    elif surface == 'dirt':
        place(out, thump(110 * p, 65, 0.14, 0.03, sd, noise=0.9, noise_fc=500 * p), 0, 0.8)
        place(out, grains(0.09, 900, sd + 1, fc=2200 * p, q=0.8, glen=0.003, env=lambda tm: swell_at(tm, 0.09, 0.2)), 0.005, 0.5)
        place(out, thump(140 * p, 80, 0.08, 0.02, sd + 2, noise=0.6, noise_fc=700), 0.05 * r.uniform(0.8, 1.2), 0.35)
    elif surface == 'stone':
        for k, (at, a) in enumerate(((0.0, 1.0), (0.045 * r.uniform(0.8, 1.2), 0.6))):
            tok = modal(1300 * p * (1 + 0.15 * k), 0.08, [(1, 1, 0.012), (1.7, 0.6, 0.008), (2.9, 0.4, 0.005)], seed=sd + k,
                        strike=0.0003, noise=1.2)
            place(out, tok, at, a)
            place(out, thump(160 * p, 100, 0.06, 0.012, sd + 5 + k, noise=0.6, noise_fc=1200), at, 0.4 * a)
        place(out, grains(0.05, 800, sd + 9, fc=4000, q=1.0, glen=0.002), 0.01, 0.1)
    elif surface == 'wood':
        for k, (at, a) in enumerate(((0.0, 1.0), (0.055 * r.uniform(0.8, 1.2), 0.65))):
            place(out, wood(170 * p * (1 + 0.1 * k), 0.2, sd + k, decay=0.07, noise=0.35, ratios=(1, 1.52, 2.4, 3.6)), at, a)
            place(out, thump(110 * p, 75, 0.12, 0.03, sd + 3 + k, noise=0.3), at, 0.4 * a)
        if v == 3:
            creak = resonate(np.where(rng(sd).uniform(0, 1, ns(0.12)) > 0.985, 1.0, 0.0), [(540, 15, 1), (1250, 18, 0.6)])
            place(out, creak * swell(0.12, 0.5), 0.07, 0.15)
    elif surface == 'sand':
        d = 0.2 * r.uniform(0.9, 1.1)
        place(out, grains(d, 4000, sd, fc=2600 * p, q=0.6, glen=0.002, env=lambda tm: swell_at(tm, d, 0.3), amp_spread=0.5), 0, 0.8)
        place(out, lp(white(d, sd + 1), 900) * swell(d, 0.3) * 0.5, 0)
        place(out, thump(80 * p, 55, 0.1, 0.03, sd + 2, noise=0.3, noise_fc=300), 0, 0.25)
    return out


# ---------------------------------------------------------------------------------------------- battle

def battle_start():
    out = np.zeros(ns(1.6))
    # rising rush of air
    place(out, whoosh(0.6, 250, 4000, q=1.4, peak_at=0.9, seed=400), 0, 0.5)
    # fast harp glissando up a diminished scale
    scale = [38, 41, 44, 47, 50, 53, 56, 59, 62, 65, 68, 71, 74]
    for i, m in enumerate(scale):
        place(out, harp(m, 0.8, 0.25 + 0.03 * i, 401 + i), 0.04 + i * 0.038)
    # the hit: drum, low strings, dissonant stab
    hit = 0.56
    place(out, frame_drum('doum', 1.0, 420), hit, 1.0)
    place(out, frame_drum('slap', 0.6, 421), hit + 0.005, 0.8)
    for i, m in enumerate((38, 45, 50, 51)):          # D, A, D, Eb: a bitter minor-second rub
        place(out, bass(m, 1.0, 0.7, 430 + i), hit + 0.004 * i)
    for i, m in enumerate((62, 63, 69)):
        place(out, fiddle(m, 0.45, 0.4, 440 + i), hit)
    return verb(out, 0.22, 0.9, 0.82, 0.8)


def swing():
    return whoosh(0.32, 350, 1900, q=1.6, peak_at=0.55, seed=450, f2=500) + 0.3 * whoosh(0.32, 1200, 5000, 2.5, 0.55, seed=451)


def _impact(seed, weight=1.0):
    out = np.zeros(ns(0.4))
    place(out, thump(95 * (1.1 - 0.15 * weight), 48, 0.3, 0.06 * weight + 0.02, seed, noise=0.9, noise_fc=600), 0)
    place(out, bp(white(0.04, seed + 1), 900, 1.0) * np.exp(-tt(0.04) / 0.008), 0, 1.2)
    place(out, hp(white(0.004, seed + 2), 2000), 0, 0.8)
    return np.tanh(out * 1.5)


def hit():
    return _impact(460, 1.0)


def hit_crit():
    out = np.zeros(ns(1.2))
    place(out, _impact(470, 1.4), 0, 1.0)
    place(out, thump(70, 38, 0.6, 0.15, 471, noise=0.4, noise_fc=250), 0, 0.8)
    place(out, metal_clang(2100, 1.0, 472, decay=0.45, noise=0.4), 0.004, 0.35)
    place(out, chime(midi('E7'), 0.8, 0.2, 473), 0.01)
    return out


def hit_glance():
    out = np.zeros(ns(0.4))
    place(out, whoosh(0.2, 1500, 600, q=1.3, peak_at=0.35, seed=480), 0, 0.5)
    t = tt(0.16)
    scrape = svf(white(0.16, 481), sweep(4500, 2500, 0.16), 4.0, 'bp') * np.exp(-t / 0.05) * 2
    place(out, scrape, 0.05, 0.6)
    place(out, metal_clang(3100, 0.2, 482, decay=0.05, noise=0.3), 0.05, 0.25)
    return out


def parry():
    out = np.zeros(ns(1.3))
    place(out, metal_clang(1250, 1.2, 490, decay=0.55, noise=1.0), 0, 1.0)
    place(out, metal_clang(1710, 1.0, 491, decay=0.4, noise=0.0), 0.002, 0.6)
    t = tt(0.12)
    place(out, svf(white(0.12, 492), sweep(6000, 3000, 0.12), 3.0, 'bp') * np.exp(-t / 0.04), 0.01, 0.6)
    return verb(out, 0.12, 0.6)


def block():
    out = np.zeros(ns(0.45))
    place(out, wood(180, 0.3, 500, decay=0.05, noise=0.5, ratios=(1, 2.25, 3.8, 5.3)), 0, 1.0)
    place(out, thump(110, 70, 0.2, 0.04, 501, noise=0.8, noise_fc=700), 0, 0.8)
    place(out, lp(white(0.05, 502), 2000) * np.exp(-tt(0.05) / 0.01), 0, 0.6)
    return out


def player_hurt():
    out = np.zeros(ns(0.6))
    place(out, _impact(510, 0.8), 0, 0.9)
    d = 0.26
    f0 = sweep(150, 105, d)
    e = adsr(d, 0.012, 0.06, 0.6, 0.12)
    v = voice(d, f0, [(650, 5, 1.0), (1150, 6, 0.6), (2500, 7, 0.25)], seed=511, breath=0.35, rough=0.25, jitter=0.02, env=e)
    place(out, v / (np.max(np.abs(v)) + 1e-9), 0.035, 0.55)
    return out


def wolf_growl():
    d = 1.3
    f0 = 82 * (1 + 0.1 * swell(d, 0.4))
    e = swell(d, 0.3, 1.2) * (1 + 0.25 * np.sin(2 * np.pi * 7 * tt(d)))
    v = voice(d, f0, [(380, 4, 1.0), (850, 5, 0.6), (2300, 6, 0.2)], seed=520, breath=0.4, rough=0.9, jitter=0.08, env=e)
    return lp(v, 4000)


def wolf_snarl():
    d = 0.7
    u = tt(d) / d
    f0 = np.interp(u, [0, 0.2, 1], [150, 240, 160])
    F1 = np.interp(u, [0, 0.17, 1], [450, 850, 550])
    F2 = np.interp(u, [0, 0.17, 1], [1000, 1600, 1100])
    e = adsr(d, 0.015, 0.15, 0.6, 0.2)
    v = voice(d, f0, [(F1, 4, 1.0), (F2, 5, 0.7), (2800, 6, 0.3)], seed=530, breath=0.7, rough=1.1, jitter=0.1, env=e)
    out = np.zeros(ns(0.9))
    place(out, v, 0)
    place(out, wood(900, 0.06, 531, decay=0.01, noise=0.6), 0.32, 0.3)   # jaws snapping
    return out


def wolf_howl():
    d = 2.4
    t = tt(d)
    x = t / d
    f = np.interp(x, [0, 0.18, 0.7, 0.88, 1.0], [360, 600, 640, 520, 430])
    f *= 1 + 0.012 * np.sin(2 * np.pi * 5.2 * t) * np.clip((t - 0.4) / 0.4, 0, 1)
    F1 = np.interp(x, [0, 0.25, 0.7, 1.0], [420, 720, 700, 450])
    F2 = np.interp(x, [0, 0.25, 0.7, 1.0], [900, 1250, 1200, 850])
    e = np.clip(t / 0.25, 0, 1) ** 2 * np.clip((d - t) / 0.7, 0, 1) ** 1.5
    v = voice(d, f, [(F1, 3, 1.0), (F2, 4, 0.45), (2800, 5, 0.08)], seed=540, breath=0.12, rough=0.08, jitter=0.01,
              roll=2.2, env=e)
    v += 0.4 * np.sin(2 * np.pi * np.cumsum(f) / SR) * e
    return reverb(v, size=1.4, decay=0.88, wet=0.35, tail=1.2)


def boar_squeal():
    d = 0.7
    t = tt(d)
    x = t / d
    f = np.interp(x, [0, 0.15, 0.5, 1.0], [700, 1250, 1150, 780])
    e = np.clip(t / 0.03, 0, 1) * np.clip((d - t) / 0.25, 0, 1)
    v = voice(d, f, [(1100, 4, 1.0), (2400, 5, 0.7), (3600, 6, 0.35)], seed=550, breath=0.5, rough=0.8, jitter=0.06, env=e)
    out = np.zeros(ns(0.9))
    place(out, lp(v, 6000), 0.05)
    g = voice(0.12, 110, [(500, 3, 1.0), (1000, 4, 0.5)], seed=551, breath=0.8, rough=1.0, jitter=0.1,
              env=adsr(0.12, 0.01, 0.04, 0.6, 0.05))
    place(out, g, 0, 0.5)
    return out


def _snort(seed):
    d = 0.22
    t = tt(d)
    n = colored(d, seed, -1.0)
    s = resonate(n, [(450, 3, 1.0), (900, 4, 0.6), (1800, 4, 0.3)])
    e = np.clip(t / 0.01, 0, 1) * np.exp(-t / 0.07)
    s = s * e * (1 + 0.8 * np.sign(np.sin(2 * np.pi * 35 * t)) * np.exp(-t / 0.05))
    return s


def boar_charge():
    out = np.zeros(ns(1.7))
    r = rng(560)
    at = 0.0
    k = 0
    while at < 1.15:
        v = 0.35 + 0.65 * at / 1.15
        hoof = thump(105 * r.uniform(0.9, 1.1), 60, 0.12, 0.025, 561 + k, noise=1.0, noise_fc=700)
        hoof += 0.4 * pad_to(grains(0.06, 900, 600 + k, fc=2000, q=0.8, glen=0.003), len(hoof))
        place(out, hoof, at, v)
        at += 0.075 if k % 2 == 0 else 0.17
        k += 1
    place(out, _snort(570), 1.15, 1.4)
    place(out, _snort(571), 1.36, 1.0)
    return out


def beast_hurt():
    d = 0.32
    t = tt(d)
    f = np.interp(t / d, [0, 0.2, 1.0], [650, 1100, 520])
    e = np.clip(t / 0.012, 0, 1) * np.clip((d - t) / 0.15, 0, 1)
    v = voice(d, f, [(800, 4, 1.0), (1500, 5, 0.6), (3000, 6, 0.25)], seed=580, breath=0.4, rough=0.5, jitter=0.05, env=e)
    out = np.zeros(ns(0.45))
    place(out, _impact(581, 0.6), 0, 0.5)
    place(out, v / (np.max(np.abs(v)) + 1e-9), 0.02, 0.9)
    return out


def enemy_die():
    out = np.zeros(ns(1.8))
    place(out, _impact(590, 1.1), 0, 1.0)
    place(out, thump(85, 42, 0.5, 0.12, 591, noise=0.9, noise_fc=300), 0.42, 0.9)
    place(out, thump(110, 60, 0.25, 0.05, 592, noise=0.6, noise_fc=500), 0.53, 0.45)
    place(out, grains(0.25, 500, 593, fc=1500, q=0.8, glen=0.004, env=lambda tm: swell_at(tm, 0.25, 0.2)), 0.43, 0.3)
    t = tt(1.3)
    tone = np.sin(2 * np.pi * 49 * t) * swell(1.3, 0.2, 1.2) * 0.35
    place(out, tone, 0.1)
    return out


def flee():
    out = np.zeros(ns(1.3))
    at, gap = 0.0, 0.17
    for i in range(7):
        s = step('dirt', 1 + i % 3)
        dist = 1.0 - i / 8
        s = lp(s, 6000 * dist + 800)
        place(out, s, at, dist ** 1.5)
        at += gap
        gap *= 0.94
    place(out, whoosh(0.3, 600, 1600, q=1.0, peak_at=0.3, seed=599), 0, 0.25)
    return out


def victory():
    out = np.zeros(ns(3.2))
    fl = (('D5', 0.0, 0.17), ('F#5', 0.18, 0.17), ('A5', 0.36, 0.17), ('D6', 0.54, 1.2))
    for i, (n, at, d) in enumerate(fl):
        place(out, flute(midi(n), d, 0.7, 600 + i, vib=0.8), at, 0.8)
    for i, (n, at) in enumerate((('D4', 0.0), ('F#4', 0.06), ('A4', 0.12), ('D5', 0.18))):
        place(out, harp(midi(n), 1.2, 0.45, 610 + i), at)
    for dt, a in strum(chord_notes('G', 55, 4), 0.36, 0.45, 620, gap=0.012):
        place(out, a, dt)
    for dt, a in strum(chord_notes('D', 50, 6), 0.54, 0.6, 630, gap=0.016):
        place(out, a, dt)
    place(out, bass(midi('D3'), 2.0, 0.6, 640), 0.54)
    place(out, frame_drum('doum', 0.6, 641), 0.54)
    place(out, bell(midi('A6'), 2.0, 0.15, 642), 0.56)
    out = lute_body(out) * 0.5 + out * 0.5
    return verb(out, 0.28, 1.0, 0.85, 1.0)


def defeat():
    out = np.zeros(ns(3.2))
    fl = (('A4', 0.0, 0.38), ('G4', 0.4, 0.38), ('F4', 0.8, 0.38), ('E4', 1.2, 0.3), ('D4', 1.5, 0.9))
    for i, (n, at, d) in enumerate(fl):
        place(out, flute(midi(n), d, 0.65, 650 + i, vib=0.6), at)
    place(out, pad([midi('D3'), midi('F3'), midi('A3')], 0.8, 0.5, 660, attack=0.3, release=0.4), 0)
    place(out, pad([midi('Bb2'), midi('D3'), midi('F3')], 0.7, 0.5, 661, attack=0.3, release=0.4), 0.8)
    place(out, pad([midi('A2'), midi('C#3'), midi('E3')], 0.3, 0.45, 662, attack=0.2, release=0.3), 1.2)
    place(out, pad([midi('D2'), midi('A2'), midi('D3'), midi('F3')], 0.9, 0.55, 663, attack=0.2, release=0.9), 1.5)
    place(out, harp(midi('D2'), 2.0, 0.5, 664), 1.5)
    return verb(out, 0.3, 1.1, 0.86, 1.0)


# ---------------------------------------------------------------------------------------------- magic

def spell_kindle():
    out = np.zeros(ns(1.4))
    r = rng(700)
    for i in range(6):
        at = 0.02 + i * 0.045 + r.uniform(0, 0.02)
        sp = hp(white(0.004, 701 + i), 2500) * np.exp(-tt(0.004) / 0.001)
        place(out, sp, at, r.uniform(0.5, 1.0))
    place(out, whoosh(0.8, 250, 2200, q=0.7, peak_at=0.35, seed=710, color=-5), 0.22, 0.9)
    place(out, crackle(0.8, 25, 711, 0.6), 0.3)
    return out


def spell_flare():
    d = 1.6
    out = np.zeros(ns(2.0))
    t = tt(d)
    roar = colored(d, 720, -5.0)
    roar = svf(roar, sweep(150, 3000, d, 0.5), 0.9, 'lp') * np.clip(t / 0.12, 0, 1) * np.exp(-np.clip(t - 0.25, 0, None) / 0.5)
    place(out, roar, 0.05, 1.8)
    place(out, whoosh(0.7, 200, 3500, q=0.8, peak_at=0.25, seed=721, color=-4), 0.0, 1.0)
    place(out, thump(65, 35, 0.8, 0.2, 722, noise=0.5, noise_fc=200), 0.08, 0.7)
    place(out, crackle(1.3, 40, 723, 0.8), 0.15)
    return out


def spell_water():
    d = 1.2
    out = np.zeros(ns(1.6))
    place(out, whoosh(d, 300, 2400, q=1.5, peak_at=0.75, seed=730), 0, 0.5)
    place(out, bubbles(d, 120, 300, 700, 731, env=lambda tm: 0.3 + 0.7 * tm / d, f_track=lambda tm: tm / d), 0, 0.6)
    place(out, chime(midi('B6'), 0.9, 0.2, 732), d * 0.8)
    place(out, chime(midi('E7'), 0.8, 0.15, 733), d * 0.85)
    return verb(out, 0.25, 0.9)


def heal():
    out = np.zeros(ns(2.2))
    for i, n in enumerate(('A5', 'C#6', 'E6', 'A6', 'C#7')):
        c = chime(midi(n), 1.4, 0.4, 740 + i) * (1 + 0.35 * np.sin(2 * np.pi * (6 + i) * tt(1.4)))
        place(out, c, i * 0.09)
    place(out, whoosh(1.2, 2000, 7000, q=1.5, peak_at=0.4, seed=750), 0.05, 0.25)
    place(out, pad([midi('A4'), midi('E5')], 1.0, 0.25, 751, attack=0.3, release=0.6, bright=0.4), 0)
    return verb(out, 0.35, 1.0, 0.86, 1.0)


# ---------------------------------------------------------------------------------------------- forge

def forge_bellows():
    d = 1.1
    t = tt(d)
    air = colored(d, 800, -4.0)
    air = svf(air, 300 + 1300 * swell(d, 0.35), 0.8, 'lp') * swell(d, 0.35, 1.2) * 2
    air += 0.25 * hp(white(d, 801), 2500) * swell(d, 0.35, 1.5)
    out = np.zeros(ns(1.2))
    place(out, air, 0.03)
    place(out, wood(300, 0.12, 802, decay=0.02, noise=0.3), 0, 0.25)    # the handle
    return out


def forge_hammer(v):
    r = rng(810 + v)
    f = (1180, 1290, 1080)[v - 1]
    out = np.zeros(ns(1.8))
    place(out, anvil(f, 0.8, 811 + v, dur=1.7, damp=r.uniform(0.6, 0.85)), 0)
    place(out, thump(150 + 20 * v, 80, 0.2, 0.03, 815 + v, noise=1.0, noise_fc=900), 0, 0.7)   # the soft hot iron
    place(out, bp(white(0.03, 818 + v), 2500, 1.0) * np.exp(-tt(0.03) / 0.006), 0, 0.5)
    return out


def forge_hammer_bad():
    out = np.zeros(ns(0.8))
    place(out, anvil(560, 0.6, 830, dur=0.7, damp=0.12), 0, 0.6)
    place(out, thump(130, 70, 0.3, 0.05, 831, noise=1.0, noise_fc=500), 0, 1.0)
    place(out, wood(420, 0.2, 832, decay=0.03, noise=0.5), 0.004, 0.6)
    place(out, metal_clang(2300, 0.2, 833, decay=0.04, noise=0.2), 0.09, 0.12)   # a rattle as it skids
    return lp(out, 3500)


def quench():
    d = 1.7
    t = tt(d)
    hiss = hp(colored(d, 840, -1.0), 2500) * (np.clip(t / 0.02, 0, 1) * (0.25 + 0.75 * np.exp(-t / 0.35)))
    hiss *= 1 + 0.3 * (smooth_noise(d, 30, 841) - 0.5)
    hiss *= np.clip((d - t) / 0.4, 0, 1)
    out = np.zeros(ns(d + 0.1))
    place(out, hiss, 0, 1.4)
    place(out, bubbles(d - 0.2, 160, 250, 900, 842, env=lambda tm: np.exp(-tm / 0.6)), 0.02, 0.5)
    place(out, crackle(0.6, 60, 843, 0.4), 0.0)
    place(out, lp(white(0.15, 844), 600) * np.exp(-tt(0.15) / 0.04), 0, 0.6)       # the plunge
    return out


def forge_done():
    out = np.zeros(ns(3.0))
    place(out, anvil(1180, 0.8, 850, dur=2.6, damp=1.3), 0)
    place(out, anvil(1180, 0.5, 851, dur=2.4, damp=1.3), 0.22)
    for i, (n, at) in enumerate((('D5', 0.45), ('F#5', 0.57), ('A5', 0.69))):
        place(out, lute(midi(n), 1.2, 0.6, 852 + i, bright=0.75), at)
    for dt, a in strum(chord_notes('D', 50, 6), 0.85, 0.6, 860, gap=0.015):
        place(out, a, dt)
    place(out, bass(midi('D3'), 1.8, 0.6, 870), 0.85)
    place(out, bell(midi('D7'), 1.6, 0.15, 871), 0.86)
    return verb(out, 0.22, 0.9)


# ---------------------------------------------------------------------------------------------- table

UI, FX, STEP = -9.0, -3.0, -12.0
SFX = {
    'ui_click': (ui_click, UI), 'ui_open': (ui_open, UI), 'ui_close': (ui_close, UI), 'ui_tick': (ui_tick, UI), 'blip': (blip, UI),
    'ui_confirm': (ui_confirm, UI), 'ui_deny': (ui_deny, UI), 'toast': (toast, UI), 'page': (page, UI),
    'quest_new': (quest_new, FX), 'quest_done': (quest_done, FX), 'levelup': (levelup, FX), 'skill_up': (skill_up, FX),
    'coin': (coin, -5.0), 'pickup': (pickup, -6.0), 'eat': (eat, -5.0), 'drink': (drink, -5.0), 'door': (door, FX),
    'chop': (chop, FX), 'water_fill': (water_fill, -5.0), 'campfire': (campfire, FX), 'bandage': (bandage, -5.0),
    'sleep': (sleep, FX), 'collapse': (collapse, FX), 'death': (death, FX),
    'battle_start': (battle_start, FX), 'swing': (swing, FX), 'hit': (hit, FX), 'hit_crit': (hit_crit, FX),
    'hit_glance': (hit_glance, FX), 'parry': (parry, FX), 'block': (block, FX), 'player_hurt': (player_hurt, FX),
    'wolf_growl': (wolf_growl, FX), 'wolf_snarl': (wolf_snarl, FX), 'wolf_howl': (wolf_howl, FX),
    'boar_squeal': (boar_squeal, FX), 'boar_charge': (boar_charge, FX), 'beast_hurt': (beast_hurt, FX),
    'enemy_die': (enemy_die, FX), 'flee': (flee, FX), 'victory': (victory, FX), 'defeat': (defeat, FX),
    'spell_kindle': (spell_kindle, FX), 'spell_flare': (spell_flare, FX), 'spell_water': (spell_water, FX),
    'heal': (heal, FX),
    'forge_bellows': (forge_bellows, FX), 'forge_hammer_bad': (forge_hammer_bad, FX), 'quench': (quench, FX),
    'forge_done': (forge_done, FX),
    'thunder': (thunder, FX), 'wave_crash': (wave_crash, FX), 'timber_creak': (timber_creak, -6.0),
}
for _v in (1, 2, 3):
    SFX[f'forge_hammer_{_v}'] = ((lambda v=_v: forge_hammer(v)), FX)
    for _s in ('grass', 'dirt', 'stone', 'wood', 'sand'):
        SFX[f'step_{_s}_{_v}'] = ((lambda s=_s, v=_v: step(s, v)), STEP)
