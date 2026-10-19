"""The six music loops. Each piece is written out as a form (sections of bars with one chord per bar, or two
chords split evenly when a bar says 'F C'), melodies in note:beats text, and accompaniment patterns that read
the chord of each bar. Everything is rendered past the loop end, folded back onto the start (so ring-outs of
the last bar sound under the first), then reverberated as if the loop repeated forever, so the seam is exact."""
import numpy as np
from dsp import (SR, ns, rng, place, loop_wrap, circular, reverb, lp, hp, biquad, normalize_rms, limit, rms, peak, db)
from instruments import (tune, name_of, chord_notes, chord, harp, lute, lute_body, strum, bass, flute, fiddle, pad,
                         frame_drum, shaker, anvil, bell, chime)

TAIL = 8.0          # seconds rendered past the loop end before folding back


class Song:
    def __init__(self, name, bpm, bpb, form, seed=1):
        self.name, self.bpm, self.bpb, self.spb = name, bpm, bpb, 60.0 / bpm
        self.chords = []            # (symbol, start beat, beats)
        self.sections = {}          # label -> (start beat, bars)
        self.order = []
        beat = 0.0
        for label, bars in form:
            self.sections[label] = (beat, len(bars))
            self.order.append(label)
            for b in bars:
                syms = b.split()
                for i, sy in enumerate(syms):
                    self.chords.append((sy, beat + i * bpb / len(syms), bpb / len(syms)))
                beat += bpb
        self.total_beats = beat
        self.L = int(round(beat * self.spb * SR))
        self.buses = {}
        self.r = rng(seed)
        self.seed = seed
        self.log = []

    # -- time
    def sec(self, beat):
        return beat * self.spb

    def nseed(self):
        self.seed += 1
        return self.seed

    def put(self, bus, audio, beat, gain=1.0, human=0.005):
        buf = self.buses.get(bus)
        if buf is None:
            buf = self.buses[bus] = np.zeros(self.L + ns(TAIL))
        jit = self.r.uniform(-human, human) if human else 0.0
        place(buf, audio, max(0.0, self.sec(beat) + jit), gain)

    def chords_in(self, label):
        start, bars = self.sections[label]
        end = start + bars * self.bpb
        return [c for c in self.chords if start <= c[1] < end]

    def bars_in(self, label):
        start, bars = self.sections[label]
        return [start + i * self.bpb for i in range(bars)]

    def chord_at(self, beat):
        for sy, b, n in self.chords:
            if b <= beat < b + n:
                return sy
        return self.chords[-1][0]

    # -- melody
    def melody(self, bus, label, text, play, vel=0.8, transpose=0, gain=1.0, log_as=None, offset=0.0):
        """Play a tune from the start of section `label`. play(midi, seconds, vel, seed) -> audio."""
        notes = tune(text)
        start, bars = self.sections[label]
        total = sum(b for _, b in notes)
        assert abs(total - bars * self.bpb) < 1e-6, f'{self.name} {label}: melody has {total} beats, section {bars * self.bpb}'
        beat = start + offset
        names = []
        for i, (m, beats) in enumerate(notes):
            if m is not None:
                m += transpose
                accent = 1.0 if abs((beat % self.bpb)) < 1e-6 else 0.92
                v = vel * accent * self.r.uniform(0.92, 1.05)
                self.put(bus, play(m, beats * self.spb, v, self.nseed()), beat, gain)
                names.append(f'{name_of(m)}' + ('' if beats == 1 else f'({beats:g})'))
            else:
                names.append(f'rest({beats:g})')
            beat += beats
        if log_as is not False:
            self.log.append(f'  {label:<3} {log_as or bus:<7} ' + ' '.join(names))

    # -- mix
    def mixdown(self, settings, rev=None, rms_db=-16.0, ceiling_db=-3.6):
        """settings: {bus: (gain, fx or None, reverb send)}. Returns the finished loop."""
        rev = rev or {}
        dry = np.zeros(self.L)
        send = np.zeros(self.L)
        for bus, buf in self.buses.items():
            g, fx, s = settings.get(bus, (1.0, None, 0.2))
            x = loop_wrap(buf, self.L)
            if fx:
                x = circular(fx, x)
            dry += g * x
            send += g * s * x
        wet = circular(lambda v: reverb(v, wet=1.0, dry=0.0, tail=0.0, **rev), send)
        out = dry + wet
        out = circular(lambda v: hp(v, 35), out)
        out -= np.mean(out)
        for _ in range(3):
            out = normalize_rms(out, rms_db)
            out = circular(lambda v: limit(v, ceiling_db), out)
        return out


def pluck_play(inst, ring=1.6, minimum=0.6):
    return lambda m, sec, v, sd: inst(m, dur=max(minimum, sec * ring), vel=v, seed=sd)


def flute_play(legato=0.95, **kw):
    return lambda m, sec, v, sd: flute(m, max(0.08, sec * legato), vel=v, seed=sd, **kw)


def fiddle_play(legato=0.9, **kw):
    return lambda m, sec, v, sd: fiddle(m, max(0.07, sec * legato), vel=v, seed=sd, **kw)


def pads(song, bus, labels, low=57, count=3, vel=0.35, attack=0.8):
    for label in labels:
        for sy, b, n in song.chords_in(label):
            song.put(bus, pad(chord_notes(sy, low, count), song.sec(n), vel=vel, seed=song.nseed(), attack=attack), b, human=0)


# ================================================================================================ title

def mus_title():
    """D dorian, 3/4, 70 BPM. Harp arpeggios and a breathy flute: intro, A, B, A'."""
    s = Song('mus_title', 70, 3, [
        ('I', ['Dm', 'C', 'G', 'Dm']),
        ('A', ['Dm', 'Am', 'C', 'G', 'Dm', 'F', 'G', 'Am']),
        ('B', ['F', 'C', 'Dm', 'Am', 'F', 'G', 'Em', 'Am']),
        ('A2', ['Dm', 'Am', 'C', 'G', 'Dm', 'F', 'G', 'Dm']),
    ], seed=100)
    A = 'D5:1.5 E5:.5 F5:1 | E5:2 C5:1 | G4:1 C5:1 E5:1 | D5:3 | F5:1.5 E5:.5 D5:1 | C5:1 A4:1 C5:1 | B4:1.5 A4:.5 G4:1 | A4:3'
    B = 'A5:2 G5:1 | E5:1.5 F5:.5 G5:1 | F5:1 E5:1 D5:1 | E5:3 | C5:1 D5:1 F5:1 | G5:2 F5:.5 E5:.5 | E5:1 D5:1 B4:1 | A4:2 r:1'
    A2 = A.rsplit('|', 1)[0] + '| D5:3'
    fl = flute_play(0.97)
    s.melody('flute', 'A', A, fl, vel=0.8)
    s.melody('flute', 'B', B, fl, vel=0.85)
    s.melody('flute', 'A2', A2, fl, vel=0.8)
    s.melody('harp', 'A2', A2, pluck_play(harp, 2.0, 1.2), vel=0.32, transpose=12, log_as='harp 8va')
    # harp: rolling eighths over each bar, bass note on the downbeat
    for sy, b, n in s.chords:
        c = chord_notes(sy, 50, 4)            # root, third, fifth, octave from around D3
        pat = [c[0], c[2], c[3], c[1] + 12, c[3], c[2]]
        for i, m in enumerate(pat):
            s.put('harp', harp(m, vel=0.42 if i else 0.5, seed=s.nseed()), b + i * 0.5)
        s.put('harp', harp(c[0] - 12, vel=0.55, seed=s.nseed()), b)
    pads(s, 'pad', s.order, low=57, count=3, vel=0.22, attack=1.2)
    out = s.mixdown({'flute': (0.8, None, 0.35), 'harp': (1.4, None, 0.3),
                     'pad': (1.4, lambda v: lp(v, 1800), 0.5)},
                    rev=dict(size=1.25, decay=0.88, damp=0.4))
    return out, s


# ================================================================================================ village

def mus_village():
    """G mixolydian, 4/4, 95 BPM. Lute boom-chick, flute tune, frame drum and shaker. A B A B'."""
    s = Song('mus_village', 95, 4, [
        ('A', ['G', 'C', 'F', 'G', 'G', 'C', 'F', 'G']),
        ('B', ['Em', 'C', 'F', 'G', 'Em', 'C', 'Dm', 'G']),
        ('A2', ['G', 'C', 'F', 'G', 'G', 'C', 'F', 'G']),
        ('B2', ['Em', 'C', 'F', 'G', 'Em', 'C', 'Dm', 'G']),
    ], seed=200)
    A = ('D5:1 B4:.5 D5:.5 G5:1 D5:1 | E5:1 C5:.5 E5:.5 G5:1.5 E5:.5 | F5:1 E5:.5 D5:.5 C5:1 A4:1 | B4:1 G4:.5 A4:.5 B4:1 D5:1 | '
         'D5:1 B4:.5 D5:.5 G5:1 A5:1 | G5:.5 E5:.5 C5:1 E5:.5 G5:.5 E5:1 | F5:1 A5:1 G5:.5 F5:.5 E5:.5 D5:.5 | G5:3 r:1')
    B = ('B5:1.5 A5:.5 G5:1 E5:1 | G5:1.5 F5:.5 E5:1 C5:1 | F5:1 A5:1 G5:1 F5:1 | D5:2 B4:1 D5:1 | '
         'E5:1 G5:1 B5:1 A5:.5 G5:.5 | E5:1 G5:1 C6:1 B5:.5 A5:.5 | A5:1 F5:1 D5:1 F5:.5 E5:.5 | D5:1 B4:1 G4:2')
    B2 = B.rsplit('|', 1)[0] + '| D5:1 B4:.5 A4:.5 G4:2'
    lp_ = pluck_play(lambda m, dur, vel, seed: lute(m, dur, vel, seed, bright=0.7), 1.3, 0.5)
    fl = flute_play(0.9)
    s.melody('lute', 'A', A, lp_, vel=0.85, log_as='lute')
    s.melody('flute', 'B', B, fl, vel=0.8)
    s.melody('flute', 'A2', A, fl, vel=0.8)
    s.melody('lute', 'A2', A, lp_, vel=0.4, transpose=-12, log_as='lute 8vb')
    s.melody('flute', 'B2', B2, fl, vel=0.85)
    s.melody('lute', 'B2', B2, lp_, vel=0.4, transpose=-12, log_as='lute 8vb')
    for sy, b, n in s.chords:
        root, iv = chord(sy)
        low = 43 + ((root - 43) % 12)                 # root between G2 and F#3
        s.put('bass', bass(low, dur=1.2, vel=0.75, seed=s.nseed()), b)
        s.put('bass', bass(low + iv[2] - 12 if low + iv[2] - 12 >= 40 else low + iv[2], dur=1.0, vel=0.6, seed=s.nseed()), b + 2)
        voicing = chord_notes(sy, 55, 4)
        for at in (1, 3):
            for dt, a in strum(voicing, 0, vel=0.32, seed=s.nseed(), gap=0.012):
                s.put('strum', a, b + at + dt / s.spb)
        for dt, a in strum(voicing[1:], 0, vel=0.18, seed=s.nseed(), gap=0.01, up=True):
            s.put('strum', a, b + 3.5 + dt / s.spb)
        # percussion: doum on 1, slap on 3, tek on 2 and 4, shaker eighths
        s.put('drum', frame_drum('doum', 0.8, s.nseed()), b)
        s.put('drum', frame_drum('slap', 0.35, s.nseed()), b + 2)
        s.put('drum', frame_drum('tek', 0.25, s.nseed()), b + 1)
        s.put('drum', frame_drum('tek', 0.22, s.nseed()), b + 3)
        s.put('drum', frame_drum('doum', 0.4, s.nseed()), b + 2.5)
        for e in range(8):
            s.put('shaker', shaker(0.5 if e % 2 else 0.3, s.nseed()), b + e * 0.5)
    pads(s, 'pad', s.order, low=55, count=3, vel=0.18)
    out = s.mixdown({'lute': (2.5, lute_body, 0.25), 'strum': (1.6, lute_body, 0.2), 'bass': (0.9, lambda v: lp(v, 900), 0.1),
                     'flute': (0.5, None, 0.3), 'drum': (0.6, None, 0.15), 'shaker': (1.8, None, 0.15),
                     'pad': (1.0, lambda v: lp(v, 1500), 0.4)},
                    rev=dict(size=0.9, decay=0.82, damp=0.4))
    return out, s


# ================================================================================================ road

def mus_road():
    """D major, 4/4, 85 BPM. Walking lute arpeggios, a lonely flute that finds its footing in B. I A B A'."""
    s = Song('mus_road', 85, 4, [
        ('I', ['D', 'G', 'D', 'A']),
        ('A', ['D', 'A', 'Bm', 'G', 'D', 'G', 'Em', 'A']),
        ('B', ['Bm', 'G', 'D', 'A', 'Bm', 'G', 'Em', 'A']),
        ('A2', ['D', 'A', 'Bm', 'G', 'D', 'G', 'Em A', 'D']),
    ], seed=300)
    A = ('F#5:1.5 E5:.5 D5:1 A4:1 | C#5:1 E5:1 A5:2 | F#5:1.5 E5:.5 D5:1 F#5:1 | B4:3 r:1 | '
         'A4:1 D5:1 F#5:1 A5:1 | G5:1.5 F#5:.5 E5:1 D5:1 | E5:1 G5:1 B4:1 D5:.5 C#5:.5 | E5:3 r:1')
    B = ('D5:1 F#5:1 B5:2 | A5:1 G5:1 D5:2 | F#5:1.5 G5:.5 A5:1 F#5:1 | E5:4 | '
         'D5:1 C#5:1 B4:1 D5:1 | G5:2 F#5:1 E5:1 | E5:1 F#5:1 G5:1 B5:1 | A5:3 r:1')
    A2 = A.rsplit('|', 1)[0] + '| D5:3 r:1'
    fl = flute_play(0.94)
    s.melody('flute', 'A', A, fl, vel=0.8)
    s.melody('flute', 'B', B, fl, vel=0.85)
    s.melody('harp', 'B', B, pluck_play(harp, 1.5, 1.0), vel=0.3, transpose=-12, log_as='harp 8vb')
    s.melody('flute', 'A2', A2, fl, vel=0.8)
    for sy, b, n in s.chords:
        c = chord_notes(sy, 50, 4)
        pat = [c[0], c[2], c[3], c[2], c[1] + 12, c[2], c[3], c[2]]
        steps = int(n * 2)
        for i in range(steps):
            s.put('lute', lute(pat[i % 8], vel=0.5 if i % 2 == 0 else 0.38, seed=s.nseed()), b + i * 0.5)
        root = chord(sy)[0]
        low = 38 + ((root - 38) % 12)
        s.put('bass', bass(low, dur=1.4, vel=0.7, seed=s.nseed()), b)
        if n >= 4:
            s.put('bass', bass(low + 7 if low + 7 < 52 else low - 5, dur=1.2, vel=0.55, seed=s.nseed()), b + 2)
    for label in ('B', 'A2'):
        for b in s.bars_in(label):
            s.put('drum', frame_drum('doum', 0.55, s.nseed()), b)
            s.put('drum', frame_drum('roll', 0.25, s.nseed()), b + 2.5)
            for e in (1, 3, 5, 7):
                s.put('shaker', shaker(0.4, s.nseed()), b + e * 0.5)
    pads(s, 'pad', s.order, low=57, count=3, vel=0.2, attack=1.0)
    out = s.mixdown({'lute': (1.6, lute_body, 0.25), 'bass': (0.8, lambda v: lp(v, 800), 0.1), 'flute': (0.6, None, 0.35),
                     'harp': (1.8, None, 0.35), 'drum': (0.8, None, 0.2), 'shaker': (1.5, None, 0.2),
                     'pad': (1.0, lambda v: lp(v, 1600), 0.45)},
                    rev=dict(size=1.15, decay=0.86, damp=0.4))
    return out, s


# ================================================================================================ night

def mus_night():
    """A minor, 4/4, 60 BPM. Harp melody over slow harp arpeggios and a dark pad; very sparse. I A B."""
    s = Song('mus_night', 60, 4, [
        ('I', ['Am', 'F', 'Am', 'E']),
        ('A', ['Am', 'F', 'C', 'G', 'Am', 'F', 'Dm', 'E']),
        ('B', ['F', 'G', 'Em', 'Am', 'Dm', 'G', 'C', 'E']),
    ], seed=400)
    A = 'E5:2 A5:1 G5:1 | F5:3 E5:1 | G5:2 E5:2 | D5:4 | C5:1 E5:1 A5:2 | G5:1 F5:1 C5:2 | D5:1 F5:1 A5:1 G5:1 | E5:4'
    B = 'C6:2 A5:2 | B5:2 G5:2 | G5:1 E5:1 B4:2 | C5:2 E5:2 | F5:2 A5:1 F5:1 | D5:2 B4:2 | E5:1 G5:1 C6:2 | B5:4'
    hp_ = pluck_play(harp, 2.2, 2.0)
    s.melody('lead', 'A', A, hp_, vel=0.75, log_as='harp')
    s.melody('lead', 'B', B, hp_, vel=0.75, log_as='harp')
    for sy, b, n in s.chords:
        c = chord_notes(sy, 45, 4)
        for i, m in enumerate([c[0], c[2], c[3], c[1] + 12]):
            s.put('harp', harp(m, vel=0.38 if i else 0.45, seed=s.nseed()), b + i)
        s.put('harp', harp(c[0] - 12, vel=0.4, seed=s.nseed()), b)
    for i, b in enumerate(s.bars_in('B')):
        if i % 2 == 0:
            s.put('bell', chime(81 + (i % 4) * 2 - (i % 4 == 2) * 1, 2.5, 0.2, s.nseed()), b + 3.5)
    pads(s, 'pad', s.order, low=52, count=4, vel=0.3, attack=1.8)
    out = s.mixdown({'lead': (1.0, None, 0.45), 'harp': (0.7, lambda v: lp(v, 3500), 0.45),
                     'pad': (0.8, lambda v: lp(v, 1100), 0.55), 'bell': (1.0, None, 0.8)},
                    rev=dict(size=1.4, decay=0.9, damp=0.45), rms_db=-17.0)
    return out, s


# ================================================================================================ battle

def mus_battle():
    """D minor (harmonic: C# over A), 4/4, 140 BPM. Frame drums, low plucked ostinato, fiddle lead.
    I A B A' C(breather, flute) B'."""
    s = Song('mus_battle', 140, 4, [
        ('I', ['Dm', 'Dm', 'Bb', 'A']),
        ('A', ['Dm', 'Dm', 'Bb', 'C', 'Dm', 'Dm', 'Bb', 'A']),
        ('B', ['Gm', 'Dm', 'Gm', 'A', 'Bb', 'F', 'Gm', 'A']),
        ('A2', ['Dm', 'Dm', 'Bb', 'C', 'Dm', 'Dm', 'Bb', 'A']),
        ('C', ['Dm', 'C', 'Bb', 'A', 'Dm', 'C', 'Bb', 'A']),
        ('B2', ['Gm', 'Dm', 'Gm', 'A', 'Bb', 'F', 'Gm', 'A']),
    ], seed=500)
    A = ('D5:.5 D5:.5 A5:1 G5:.5 F5:.5 E5:1 | F5:1.5 E5:.5 D5:2 | D5:.5 F5:.5 Bb5:1 A5:.5 G5:.5 F5:1 | G5:1.5 F5:.5 E5:1 C5:1 | '
         'D5:.5 D5:.5 A5:1 Bb5:.5 A5:.5 G5:1 | A5:1 F5:1 D5:1 F5:1 | G5:1 F5:.5 E5:.5 D5:1 Bb4:1 | C#5:1 E5:1 A5:2')
    B = ('Bb5:2 A5:1 G5:1 | A5:2 F5:1 D5:1 | G5:1 Bb5:1 D6:1 C6:.5 Bb5:.5 | A5:3 E5:1 | '
         'D6:1.5 C6:.5 Bb5:1 F5:1 | C6:1.5 Bb5:.5 A5:1 F5:1 | G5:1 A5:1 Bb5:1 G5:1 | A5:2 G5:.5 F5:.5 E5:1')
    C = 'F5:4 | E5:4 | D5:4 | C#5:4 | A5:4 | G5:4 | F5:2 G5:2 | E5:4'
    fd = fiddle_play(0.88)
    s.melody('fiddle', 'A', A, fd, vel=0.8)
    s.melody('fiddle', 'B', B, fd, vel=0.85)
    s.melody('fiddle', 'A2', A, fd, vel=0.8)
    s.melody('flute', 'A2', A, flute_play(0.85, vib=0.6), vel=0.35, transpose=12, log_as='flute 8va')
    s.melody('flute', 'C', C, flute_play(0.98), vel=0.8)
    s.melody('fiddle', 'B2', B, fd, vel=0.85)
    s.melody('flute', 'B2', B, flute_play(0.85, vib=0.6), vel=0.3, transpose=-12, log_as='flute 8vb')
    sparse = set(s.bars_in('C'))
    for sy, b, n in s.chords:
        root = chord(sy)[0]
        R = 38 + ((root - 38) % 12)           # D2..C#3
        bar_start = b - (b % 4)
        light = bar_start in sparse
        if light:
            pat = [R, None, R + 12, None, R + 7, None, R + 12, None]
        else:
            pat = [R, R, R + 12, R, R + 7, R, R + 12, R + 10 if chord(sy)[1][1] == 3 else R + 7]
        for i, m in enumerate(pat):
            if m is None:
                continue
            v = 0.85 if i in (0, 3, 6) else 0.6
            s.put('ost', bass(m, dur=0.45, vel=v, seed=s.nseed()), b + i * 0.5)
        # drums
        if light:
            s.put('drum', frame_drum('doum', 0.7, s.nseed()), b)
            s.put('drum', frame_drum('doum', 0.45, s.nseed()), b + 2)
            s.put('drum', frame_drum('tek', 0.25, s.nseed()), b + 3)
        else:
            for pos, kind, v in ((0, 'doum', 0.95), (1.5, 'doum', 0.6), (2, 'doum', 0.8), (1, 'tek', 0.45), (3, 'tek', 0.5),
                                 (0.5, 'roll', 0.2), (2.5, 'roll', 0.25), (3.5, 'roll', 0.2)):
                s.put('drum', frame_drum(kind, v, s.nseed()), b + pos)
            for e in range(8):
                s.put('shaker', shaker(0.5 if e % 2 else 0.35, s.nseed(), 0.06), b + e * 0.5)
    # fills at the end of each section: sixteenth teks on beat 4, doum pickup
    for label in s.order:
        start, bars = s.sections[label]
        last = start + (bars - 1) * 4
        for k in range(4):
            s.put('drum', frame_drum('tek' if k % 2 == 0 else 'slap', 0.35 + 0.1 * k, s.nseed()), last + 3 + k * 0.25)
    pads(s, 'pad', ['B', 'C', 'B2'], low=50, count=3, vel=0.28, attack=0.6)
    out = s.mixdown({'fiddle': (1.0, None, 0.22), 'flute': (0.75, None, 0.3), 'ost': (0.8, lambda v: lp(v, 1400), 0.1),
                     'drum': (0.8, None, 0.12), 'shaker': (1.5, None, 0.1), 'pad': (1.0, lambda v: lp(v, 1300), 0.35)},
                    rev=dict(size=0.85, decay=0.8, damp=0.45))
    return out, s


# ================================================================================================ forge

def mus_forge():
    """A dorian, 4/4, 100 BPM. Anvil ring on 1 and 3 woven into frame drum and bass; lute then flute tune.
    A B A' B'."""
    s = Song('mus_forge', 100, 4, [
        ('A', ['Am', 'D', 'Am', 'D', 'C', 'G', 'Em', 'D']),
        ('B', ['C', 'G', 'D', 'Am', 'C', 'G', 'D', 'E']),
        ('A2', ['Am', 'D', 'Am', 'D', 'C', 'G', 'Em', 'D']),
        ('B2', ['C', 'G', 'D', 'Am', 'C', 'G', 'D', 'E']),
    ], seed=600)
    A = ('A4:.5 C5:.5 E5:1 D5:.5 C5:.5 A4:1 | D5:1 F#5:1 E5:.5 D5:.5 A4:1 | A4:.5 C5:.5 E5:1 G5:1 E5:1 | F#5:1.5 E5:.5 D5:2 | '
         'E5:1 G5:1 E5:.5 D5:.5 C5:1 | D5:1 B4:1 G4:1 B4:1 | E5:1 G5:1 B5:1 A5:.5 G5:.5 | F#5:2 E5:1 D5:1')
    B = ('G5:2 E5:1 G5:1 | G5:1.5 A5:.5 B5:1 G5:1 | A5:2 F#5:1 D5:1 | E5:3 r:1 | '
         'E5:1 G5:1 C6:1.5 B5:.5 | B5:1 A5:1 G5:1 D5:1 | F#5:1 A5:1 G5:.5 F#5:.5 E5:1 | B4:1 E5:1 G#5:2')
    lp_ = pluck_play(lambda m, dur, vel, seed: lute(m, dur, vel, seed, bright=0.72), 1.2, 0.5)
    fl = flute_play(0.92)
    s.melody('lute', 'A', A, lp_, vel=0.9, log_as='lute')
    s.melody('flute', 'B', B, fl, vel=0.8)
    s.melody('flute', 'A2', A, fl, vel=0.75)
    s.melody('lute', 'A2', A, lp_, vel=0.55, log_as='lute')
    s.melody('flute', 'B2', B, fl, vel=0.8)
    s.melody('lute', 'B2', B, lp_, vel=0.35, transpose=-12, log_as='lute 8vb')
    for sy, b, n in s.chords:
        root = chord(sy)[0]
        R = 40 + ((root - 40) % 12)
        for pos, m, v in ((0, R, 0.85), (0.75, R, 0.5), (1.5, R + 7, 0.6), (2, R, 0.8), (2.75, R + 12, 0.5), (3.5, R + 7, 0.6)):
            s.put('bass', bass(m, dur=0.6, vel=v, seed=s.nseed()), b + pos)
        voicing = chord_notes(sy, 57, 3)
        for at in (1, 3):
            for dt, a in strum(voicing, 0, vel=0.25, seed=s.nseed(), gap=0.008, inst=lambda m, vel, seed: lute(m, 0.5, vel, seed)):
                s.put('strum', a, b + at + 0.5 + dt / s.spb)
        # anvil pulse: struck on 1 and 3, a lighter double tap leading into the next bar
        s.put('anvil', anvil(1760, 0.6, s.nseed(), dur=1.2, damp=0.35), b)
        s.put('anvil', anvil(1760, 0.45, s.nseed(), dur=1.2, damp=0.35), b + 2)
        s.put('anvil', anvil(1760, 0.22, s.nseed(), dur=0.6, damp=0.2), b + 3.5)
        s.put('drum', frame_drum('doum', 0.85, s.nseed()), b)
        s.put('drum', frame_drum('doum', 0.5, s.nseed()), b + 1.5)
        s.put('drum', frame_drum('slap', 0.4, s.nseed()), b + 1)
        s.put('drum', frame_drum('slap', 0.45, s.nseed()), b + 3)
        s.put('drum', frame_drum('doum', 0.6, s.nseed()), b + 2)
    pads(s, 'pad', s.order, low=55, count=3, vel=0.2)
    out = s.mixdown({'lute': (2.6, lute_body, 0.22), 'strum': (1.8, lute_body, 0.2), 'flute': (0.55, None, 0.3),
                     'bass': (0.85, lambda v: lp(v, 900), 0.08), 'anvil': (0.22, lambda v: lp(hp(v, 600), 7000), 0.35),
                     'drum': (0.7, None, 0.15), 'pad': (1.0, lambda v: lp(v, 1500), 0.4)},
                    rev=dict(size=1.0, decay=0.83, damp=0.4))
    return out, s


TRACKS = {'mus_title': mus_title, 'mus_village': mus_village, 'mus_road': mus_road, 'mus_night': mus_night,
          'mus_battle': mus_battle, 'mus_forge': mus_forge}
