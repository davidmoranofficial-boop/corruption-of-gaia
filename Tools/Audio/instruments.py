"""Instruments shared by the music and the musical sound effects (jingles, chimes, the anvil).
Each returns a dry mono note as a numpy array; the caller places it in time and adds reverb on the bus."""
import math
import re
import numpy as np
from dsp import (SR, ns, tt, rng, mtof, karplus, additive, saw_partials, vibrato, adsr, perc, end_ramp,
                 white, colored, lp, hp, bp, onepole_lp, modal, resonate, sweep)

# ---------------------------------------------------------------------------------------------- note names

NAMES = {'C': 0, 'D': 2, 'E': 4, 'F': 5, 'G': 7, 'A': 9, 'B': 11}
PRINT_NAMES = ['C', 'C#', 'D', 'Eb', 'E', 'F', 'F#', 'G', 'G#', 'A', 'Bb', 'B']


def midi(name):
    """'C4' -> 60, 'F#5' -> 78, 'Bb3' -> 58."""
    m = re.fullmatch(r'([A-G])([#b]?)(-?\d)', name)
    if not m:
        raise ValueError(name)
    n = NAMES[m.group(1)] + {'#': 1, 'b': -1, '': 0}[m.group(2)]
    return 12 * (int(m.group(3)) + 1) + n


def name_of(m):
    return PRINT_NAMES[m % 12] + str(m // 12 - 1)


def tune(text):
    """Parse 'D5:1.5 E5:.5 r:1' into [(midi or None, beats)]. Bar lines '|' are ignored (they are for reading)."""
    out = []
    for tok in text.replace('|', ' ').split():
        p, b = tok.split(':')
        out.append((None if p == 'r' else midi(p), float(b)))
    return out


CHORDS = {'': (0, 4, 7), 'm': (0, 3, 7), '7': (0, 4, 7, 10), 'm7': (0, 3, 7, 10), 'maj7': (0, 4, 7, 11),
          'sus4': (0, 5, 7), 'sus2': (0, 2, 7), 'dim': (0, 3, 6), '5': (0, 7)}


def chord(sym):
    """'Dm' -> (root pitch class, intervals). Roots may be sharp/flat: 'F#m', 'Bb'."""
    m = re.fullmatch(r'([A-G][#b]?)(.*)', sym)
    root = NAMES[m.group(1)[0]] + ({'#': 1, 'b': -1}.get(m.group(1)[1:], 0))
    return root % 12, CHORDS[m.group(2)]


def chord_notes(sym, low=48, count=4, spread=True):
    """Voice a chord upward from just above `low` (midi)."""
    root, iv = chord(sym)
    base = low + ((root - low) % 12)
    notes = [base + i for i in iv]
    k = 0
    while len(notes) < count:
        notes.append(notes[k] + 12)
        k += 1
    return notes[:count]


# ---------------------------------------------------------------------------------------------- plucked strings

def harp(m, dur=None, vel=0.8, seed=0):
    """Concert-harp-ish: a soft, round pluck with a long ring and a sine reinforcing the fundamental."""
    f = mtof(m)
    decay = float(np.clip(7.0 - (m - 48) * 0.09, 2.2, 7.0))
    dur = dur or min(decay * 0.8, 5.0)
    s = karplus(f, dur, seed=seed, bright=0.35 + 0.25 * vel, decay=decay, pick=0.28)
    t = tt(dur)
    s += 0.35 * np.sin(2 * np.pi * f * t) * np.exp(-t / (decay * 0.3)) * np.clip(t / 0.004, 0, 1)
    return vel * s * end_ramp(len(s), 0.05)


def lute(m, dur=None, vel=0.8, seed=0, bright=0.62):
    """Lute / folk guitar: brighter pluck nearer the bridge, quicker decay. Give the bus lute_body()."""
    f = mtof(m)
    decay = float(np.clip(3.4 - (m - 40) * 0.05, 1.0, 3.4))
    dur = dur or min(decay * 0.75, 2.6)
    s = karplus(f, dur, seed=seed, bright=bright * (0.75 + 0.3 * vel), decay=decay, pick=0.13)
    return vel * s * end_ramp(len(s), 0.04)


def lute_body(x):
    """Wooden body: resonances at a few hundred Hz and a presence lift, low end trimmed."""
    y = hp(x, 70)
    return 0.7 * y + resonate(y, [(180, 2.5, 0.35), (420, 3.0, 0.25), (1100, 2.0, 0.12), (2600, 1.5, 0.08)])


def strum(m_list, at, vel=0.7, seed=0, inst=None, gap=0.014, up=False):
    """Several plucks a few ms apart, low to high (or high to low for an upstroke).
    Returns [(time offset, note audio)]."""
    inst = inst or lute
    order = list(reversed(m_list)) if up else list(m_list)
    out = []
    for i, m in enumerate(order):
        v = vel * (0.85 if up else 1.0) * (1 - 0.05 * i)
        out.append((at + i * gap, inst(m, vel=v, seed=seed + i)))
    return out


def bass(m, dur=None, vel=0.8, seed=0):
    """Low plucked string (bass lute / cello pizzicato): dark and short, with a sine body."""
    f = mtof(m)
    dur = dur or 1.6
    s = karplus(f, dur, seed=seed, bright=0.3, decay=1.8, pick=0.2)
    t = tt(dur)
    s += 0.6 * np.sin(2 * np.pi * f * t) * np.exp(-t / 0.5) * np.clip(t / 0.006, 0, 1)
    return vel * s * end_ramp(len(s), 0.05)


# ---------------------------------------------------------------------------------------------- winds and bowed

def flute(m, dur, vel=0.8, seed=0, vib=1.0, breath=1.0, attack=0.05):
    """Breathy wooden flute: nearly-sine tone with weak upper harmonics, delayed vibrato,
    a little 'chiff' of noise at the start and breath noise that follows the loudness."""
    f = mtof(m)
    r = rng(seed)
    total = dur + 0.12
    fr = vibrato(f, total, rate=4.8 + r.uniform(-0.3, 0.3), depth_cents=14 * vib, delay=min(0.25, dur * 0.4),
                 rise=0.35, seed=seed)
    # tiny scoop up into the note
    n = len(fr)
    scoop = 1 - 0.006 * np.exp(-np.arange(n) / (0.03 * SR))
    fr = fr * scoop
    tone = additive(fr, total, [(1, 1.0), (2, 0.18), (3, 0.09), (4, 0.03), (5, 0.015)])
    env = adsr(total, a=attack, d=0.15, s=0.85, r=0.12, curve=2.0)
    # gentle swell on long notes
    t = tt(total)
    env *= 1 + 0.08 * np.sin(np.pi * np.clip(t / max(dur, 0.1), 0, 1))
    noise = white(total, seed + 99)
    br = bp(noise, f * 2.0, 1.2) * 0.9 + hp(noise, 3500) * 0.25
    chiff = bp(noise, f * 3.0, 2.0) * np.exp(-t / 0.025) * 1.6
    out = tone * env + breath * 0.22 * br * env + breath * chiff * np.clip(t / 0.003, 0, 1)
    return vel * out * end_ramp(n, 0.03)


def fiddle(m, dur, vel=0.8, seed=0, vib=1.0):
    """Bowed string lead: soft sawtooth with vibrato, a scratchy bow attack and body formants."""
    f = mtof(m)
    r = rng(seed)
    total = dur + 0.08
    fr = vibrato(f, total, rate=5.6 + r.uniform(-0.3, 0.3), depth_cents=18 * vib, delay=min(0.15, dur * 0.3),
                 rise=0.25, seed=seed)
    tone = additive(fr, total, saw_partials(30, 1.15))
    env = adsr(total, a=0.035, d=0.1, s=0.8, r=0.07)
    t = tt(total)
    bow = hp(white(total, seed + 7), 2500) * np.exp(-t / 0.04) * 0.6 + hp(white(total, seed + 8), 4000) * 0.05
    body = resonate(tone, [(290, 2.0, 0.6), (520, 2.5, 0.5), (1200, 2.0, 0.35), (2900, 1.6, 0.25)])
    out = (0.25 * tone + body) * env + bow * env
    return vel * lp(out, 6000) * end_ramp(len(out), 0.03)


def pad(m_list, dur, vel=0.5, seed=0, attack=0.8, release=1.2, bright=1.0):
    """Soft string/organ-like pad: three slightly detuned voices per note, slow swell, gentle shimmer."""
    total = dur + release
    out = np.zeros(ns(total))
    r = rng(seed)
    parts = [(1, 1.0), (2, 0.35 * bright), (3, 0.14 * bright), (4, 0.06 * bright), (5, 0.03 * bright)]
    for m in m_list:
        for cents in (-7, 0, 6):
            f = mtof(m) * 2 ** ((cents + r.uniform(-2, 2)) / 1200)
            out += additive(f, total, parts, phase0=list(r.uniform(0, 6.28, 5)))
    t = tt(total)
    env = adsr(total, a=attack, d=0.5, s=0.9, r=release, curve=1.5)
    env *= 1 + 0.06 * np.sin(2 * np.pi * 0.23 * t + r.uniform(0, 6))
    return vel * out * env / (3 * len(m_list)) * 0.8


# ---------------------------------------------------------------------------------------------- percussion

def frame_drum(kind='doum', vel=0.8, seed=0):
    """Hand frame drum. 'doum' = low centre stroke, 'tek' = bright rim, 'slap' = mid, 'roll' = soft finger."""
    r = rng(seed)
    if kind == 'doum':
        dur = 0.6
        t = tt(dur)
        f = 78 * (1 + 0.45 * np.exp(-t / 0.02)) * (1 + r.uniform(-0.02, 0.02))
        body = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t / 0.18)
        modes = modal(78, dur, [(1.59, 0.35, 0.09), (2.14, 0.25, 0.06), (2.65, 0.15, 0.05), (3.16, 0.1, 0.04)], seed=seed)
        slap = lp(white(dur, seed), 1200) * np.exp(-t / 0.012) * 1.2
        s = body + modes + slap
    elif kind == 'slap':
        dur = 0.35
        t = tt(dur)
        f = 180 * (1 + 0.25 * np.exp(-t / 0.01))
        body = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t / 0.07)
        s = 0.7 * body + bp(white(dur, seed), 1500, 0.9) * np.exp(-t / 0.02) * 2.2
    elif kind == 'tek':
        dur = 0.2
        t = tt(dur)
        s = bp(white(dur, seed), 3200 * r.uniform(0.95, 1.05), 1.5) * np.exp(-t / 0.012) * 2.5
        s += modal(420, dur, [(1, 0.5, 0.03), (2.3, 0.3, 0.02), (3.9, 0.2, 0.015)], seed=seed)
    else:  # roll / ghost
        dur = 0.15
        t = tt(dur)
        s = bp(white(dur, seed), 2400, 1.0) * np.exp(-t / 0.01) * 1.5
    return vel * s * end_ramp(len(s), 0.01)


def shaker(vel=0.5, seed=0, length=0.09):
    t = tt(length + 0.03)
    env = np.clip(t / 0.012, 0, 1) * np.exp(-np.clip(t - 0.012, 0, None) / (length * 0.35))
    s = bp(white(length + 0.03, seed), 7000, 0.8) + 0.4 * hp(white(length + 0.03, seed + 1), 9000)
    return vel * s * env * end_ramp(len(t), 0.01)


ANVIL_MODES = [(1.0, 0.7, 1.0), (2.76, 0.8, 0.8), (4.12, 0.5, 0.6), (5.40, 0.6, 0.5), (6.93, 0.35, 0.35),
               (8.93, 0.3, 0.25), (11.2, 0.15, 0.18)]


def anvil(f=880, vel=0.8, seed=0, dur=2.0, damp=1.0):
    """Hammer on an anvil: bright inharmonic ring plus the click of contact. damp < 1 shortens the ring."""
    modes = [(r_, a, d * damp) for r_, a, d in ANVIL_MODES]
    s = modal(f, dur, modes, seed=seed, strike=0.0005, noise=0.6)
    t = tt(dur)
    s += 0.5 * lp(white(dur, seed + 3), 3000) * np.exp(-t / 0.006)
    return vel * s


def bell(m, dur=3.0, vel=0.7, seed=0):
    """Small hand bell: hum, prime, minor third, fifth, octave partials, each with its own decay."""
    f = mtof(m)
    modes = [(0.5, 0.25, dur * 0.6), (1.0, 1.0, dur * 0.4), (1.19, 0.35, dur * 0.25), (1.5, 0.3, dur * 0.2),
             (2.0, 0.45, dur * 0.18), (2.52, 0.15, dur * 0.1), (3.01, 0.12, dur * 0.08), (4.1, 0.06, dur * 0.05)]
    return vel * modal(f, dur, modes, seed=seed, strike=0.001)


def chime(m, dur=1.5, vel=0.7, seed=0):
    """Glassy chime (softer than a bell): near-harmonic partials, quick upper decay."""
    f = mtof(m)
    modes = [(1.0, 1.0, dur * 0.45), (2.0, 0.25, dur * 0.2), (3.0, 0.08, dur * 0.12), (4.17, 0.06, dur * 0.06)]
    return vel * modal(f, dur, modes, seed=seed, strike=0.002)
