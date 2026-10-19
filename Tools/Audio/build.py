"""Builds every sound the game uses, procedurally, into Assets/Game/Resources/Audio as mono 44.1 kHz Ogg Vorbis.

    python3 Tools/Audio/build.py                 (from the repository root; needs numpy and ffmpeg with libvorbis)
    python3 Tools/Audio/build.py hit wolf_*      (only the ids matching these patterns)

Nothing is recorded: every sound is computed from numbers, with fixed random seeds, so a build is byte-for-byte
repeatable. The pieces:

- dsp.py          the toolkit: sine/additive oscillators, glides and vibrato, ADSR and percussive envelopes, white
                  and coloured noise, one-pole and RBJ biquad filters, a moving state-variable filter, frequency-
                  domain shaping for long noise beds, Karplus-Strong plucked strings, modal (struck-object)
                  synthesis, a Freeverb-style comb/allpass reverb, a look-ahead limiter and loop helpers.
- instruments.py  harp, lute, bass pluck (Karplus-Strong), breathy flute (sine partials + band-passed breath noise
                  + delayed vibrato), fiddle (soft sawtooth through body formants), pads, frame drum, shaker,
                  anvil, bell and chime. Also the note/chord notation used by the music ('D5:1.5 E5:.5 r:1').
- sfx.py          the sound effects, layered from wooden/metal modal hits, noise whooshes, grain clouds (rustle,
                  crunch, crackle, tearing), bubbles and a small source-filter voice for grunts and animal calls.
- ambience.py     40 s loops: noise beds cross-faded into their own start, plus seeded events folded onto the start.
- music.py        six loops written as forms of sections with chords, melodies and accompaniment patterns.

How a file is finished:
- sfx: DC removed, leading and trailing near-silence trimmed, 1.5 ms fade-in and a short fade-out, then the peak
  is normalised (-3 dBFS for most, -9 dBFS for UI, -12 dBFS for footsteps; set per id in sfx.SFX).
- ambience: about -20 dBFS RMS with a limiter keeping peaks under -6 dBFS. music: -16 dBFS RMS, peaks under -3 dBFS.
  Both are processed as if the loop repeated forever (dsp.circular), so filters and reverb wrap across the seam.

To add a sound:
- an effect: write a function in sfx.py that returns a numpy array (build it from the helpers there; give every
  random thing its own seed), and add it to the SFX table with its peak level. Variants are ids ending _1, _2, _3.
- an ambience: write a function in ambience.py from bed()/events()/fold() and finish(), add it to LOOPS.
- a music loop: write a Song in music.py (see mus_road for a compact example) and add it to TRACKS.
Then run this script; it prints a table of every file (length, peak, RMS) and the checks below, and fails loudly
on clipping, NaNs, DC, clicks at loop seams or missing fades.
"""
import fnmatch
import hashlib
import os
import subprocess
import sys
from concurrent.futures import ProcessPoolExecutor

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from dsp import SR, db, peak, rms, finish_sfx   # noqa: E402

OUT = os.path.normpath(os.path.join(HERE, '..', '..', 'Assets', 'Game', 'Resources', 'Audio'))
QUALITY = {'Sfx': 5, 'Ambience': 4, 'Music': 4}
TAIL_PAD = 1024


def jobs():
    import sfx, ambience, music
    out = []
    for k, (fn, lvl) in sfx.SFX.items():
        out.append(('Sfx', k, lvl))
    for k in ambience.LOOPS:
        out.append(('Ambience', k, None))
    for k in music.TRACKS:
        out.append(('Music', k, None))
    return out


def render(job):
    kind, key, lvl = job
    log = []
    if kind == 'Sfx':
        import sfx
        x = finish_sfx(sfx.SFX[key][0](), lvl)
    elif kind == 'Ambience':
        import ambience
        x = ambience.LOOPS[key]()
    else:
        import music
        x, song = music.TRACKS[key]()
        log = [f'{key}: {song.__class__.__name__} {song.bpm} BPM, {song.bpb}/4, {len(song.chords)} chords, '
               f'form {" ".join(song.order)}'] + song.log
    if kind == 'Sfx':
        x = np.concatenate([x, np.zeros(TAIL_PAD)])   # Vorbis drops up to ~128 samples off the end of short files
    x = np.asarray(x, dtype=np.float32)
    path = os.path.join(OUT, kind, key + '.ogg')
    # Lossy coding can push peaks up by a dB or so: decode, and if over the limit, scale down and encode again.
    ceiling = {'Sfx': (lvl or 0) + 0.5, 'Ambience': -6.1, 'Music': -3.1}[kind]
    for _ in range(4):
        encode(x, path, QUALITY[kind])
        over = db(peak(decode(path))) - ceiling
        if over <= 0:
            break
        x = (x * 10 ** (-(over + 0.15) / 20)).astype(np.float32)
    return kind, key, x, log


def encode(x, path, q):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    cmd = ['ffmpeg', '-y', '-hide_banner', '-loglevel', 'error', '-f', 'f32le', '-ar', str(SR), '-ac', '1', '-i', 'pipe:0',
           '-c:a', 'libvorbis', '-q:a', str(q), '-map_metadata', '-1', '-fflags', '+bitexact', '-flags:a', '+bitexact',
           path]
    subprocess.run(cmd, input=x.astype('<f4').tobytes(), check=True)


def decode(path):
    cmd = ['ffmpeg', '-hide_banner', '-loglevel', 'error', '-i', path, '-f', 'f32le', '-ac', '1', '-ar', str(SR), 'pipe:1']
    return np.frombuffer(subprocess.run(cmd, capture_output=True, check=True).stdout, dtype='<f4').astype(np.float64)


# ---------------------------------------------------------------------------------------------- checks

def seam(x, ms=50):
    """How well the end of a loop flows into its start, judged against the audio right around the seam.
    jump: |first - last| over the largest sample-to-sample step within `ms` either side (a click would be >> 1).
    slope: change of slope across the seam over the largest such change nearby.
    level: RMS of the last vs the first 0.5 s (the bed should not step up or down)."""
    k = int(SR * ms / 1000)
    around = np.concatenate([x[-k:], x[:k]])
    d1 = np.diff(around)
    d2 = np.diff(d1)
    seam_i = k - 1                                   # d1[seam_i] is the step from x[-1] to x[0]
    others = np.delete(np.abs(d1), seam_i)
    jump = abs(d1[seam_i]) / (others.max() + 1e-12)
    curv = np.delete(np.abs(d2), [seam_i - 1, seam_i])
    slope = max(abs(d2[seam_i - 1]), abs(d2[seam_i])) / (curv.max() + 1e-12)
    h = SR // 2
    lvl = db(rms(x[-h:])) - db(rms(x[:h]))
    return jump, slope, lvl


def longest_quiet(x, rel_db=-50.0, frame=0.01):
    """Longest run (s) of 10 ms frames quieter than rel_db below the peak, between the first and last loud frame."""
    k = int(SR * frame)
    n = len(x) // k
    if n < 2:
        return 0.0
    fr = np.sqrt(np.mean(x[:n * k].reshape(n, k) ** 2, axis=1))
    loud = fr > peak(x) * 10 ** (rel_db / 20)
    idx = np.nonzero(loud)[0]
    if len(idx) < 2:
        return 0.0
    run = best = 0
    for v in loud[idx[0]:idx[-1] + 1]:
        run = 0 if v else run + 1
        best = max(best, run)
    return best * frame


def check(kind, key, x, y):
    """x: what we rendered, y: decoded from the ogg. Returns (row, problems)."""
    probs = []
    if not np.all(np.isfinite(x)):
        probs.append('NaN/inf')
    if abs(len(y) - len(x)) > (TAIL_PAD if kind == 'Sfx' else 0):
        probs.append(f'decoded length {len(y)} != {len(x)}')
    pk, r, dc = db(peak(y)), db(rms(y)), float(np.mean(y))
    if pk > -0.5:
        probs.append(f'peak {pk:.1f} dBFS (clipping risk)')
    if abs(dc) > 1e-3:
        probs.append(f'DC {dc:.4f}')
    extra = ''
    if kind == 'Sfx':
        p = peak(x)
        head, tail = abs(x[0]) / p, abs(x[-1]) / p
        lead = np.argmax(np.abs(x) > p * 0.0316) / SR       # time to reach -30 dB below the peak
        gap = longest_quiet(x)
        if head > 0.01 or tail > 0.01:
            probs.append(f'edge not faded ({head:.3f}, {tail:.3f})')
        if lead > 0.06:
            probs.append(f'{lead * 1000:.0f} ms before it starts')
        if gap > 0.25:
            probs.append(f'{gap:.2f} s silent gap inside')
        extra = f'lead {lead * 1000:4.1f} ms  gap {gap:.2f} s'
    else:
        lim_pk, lim_rms = (-6.0, (-24.5, -18.5)) if kind == 'Ambience' else (-3.0, (-18, -14.5))
        if pk > lim_pk + 0.3:
            probs.append(f'peak {pk:.1f} above {lim_pk}')
        if not lim_rms[0] <= r <= lim_rms[1]:
            probs.append(f'RMS {r:.1f} outside {lim_rms}')
        j, s, lv = seam(x)
        jd, sd, lvd = seam(y)
        if j > 1.0 or s > 1.0 or (kind == 'Ambience' and abs(lv) > 3.0):
            probs.append(f'loop seam: jump {j:.2f} slope {s:.2f} level {lv:+.1f} dB')
        extra = f'seam jump {j:.2f} slope {s:.2f} lvl {lv:+.1f}dB (ogg: {jd:.2f}/{sd:.2f})'
    row = f'{kind:<8} {key:<18} {len(y) / SR:6.2f} s  peak {pk:6.1f}  rms {r:6.1f}  {extra}'
    return row, probs


def main(patterns):
    todo = [j for j in jobs() if not patterns or any(fnmatch.fnmatch(j[1], p) for p in patterns)]
    # music first: the longest jobs start early
    todo.sort(key=lambda j: {'Music': 0, 'Ambience': 1, 'Sfx': 2}[j[0]])
    print(f'Rendering {len(todo)} sounds into {OUT}')
    results = []
    with ProcessPoolExecutor(max_workers=max(1, os.cpu_count() or 1)) as ex:
        for kind, key, x, log in ex.map(render, todo):
            results.append((kind, key, x, log))
            print(f'  done {kind}/{key}', flush=True)
    order = {'Sfx': 0, 'Ambience': 1, 'Music': 2}
    results.sort(key=lambda r: (order[r[0]], r[1]))
    print('\nMelodies')
    for kind, key, x, log in results:
        for line in log:
            print(line)
    print('\nFiles')
    problems = []
    for kind, key, x, log in results:
        path = os.path.join(OUT, kind, key + '.ogg')
        row, probs = check(kind, key, x.astype(np.float64), decode(path))
        size = os.path.getsize(path)
        md5 = hashlib.md5(open(path, 'rb').read()).hexdigest()[:8]
        print(f'{row}  {size / 1024:7.1f} KB  {md5}' + ('   <-- ' + '; '.join(probs) if probs else ''))
        problems += [f'{key}: {p}' for p in probs]
    total = sum(os.path.getsize(os.path.join(dp, f)) for dp, _, fs in os.walk(OUT) for f in fs)
    print(f'\nTotal size of {OUT}: {total / 1e6:.2f} MB')
    if problems:
        print('\nPROBLEMS:\n  ' + '\n  '.join(problems))
        sys.exit(1)
    print('All checks passed.')


if __name__ == '__main__':
    main(sys.argv[1:])
