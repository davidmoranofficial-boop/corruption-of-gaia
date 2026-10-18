"""The Gaia palette (v2, muted). Every sprite, tile and icon in the game uses only these colours.
Single letters are what the text-grid sprites in src/ are drawn with. SKINS and HAIRS are the colour
ramps the character's skin and hair letters get swapped to, one ramp per choice at character creation."""

PAL = {
    'o': '#120f14',  # outline (never pure black)
    'e': '#1e1a20',  # eyes, deepest shadow
    'g': '#38343a', 'G': '#575158', 'y': '#7d767a', 'Y': '#a69e9a', 'W': '#cfc6bb',  # greys
    's': '#c49a7a', 'S': '#a3785c', 'q': '#7e563f', 'Q': '#55392a',  # skin, light to dark
    'h': '#4a3426', 'H': '#2c1f18', 'j': '#7a5838', 'J': '#a88a5a',  # hair
    'f': '#7d8f52', 'F': '#566b3e', 'v': '#3b4d30', 'V': '#263322',  # greens
    'i': '#697d48',  # mid green, between f and F
    'z': '#b5a07c',  # pale sand
    'l': '#29424f',  # deep water, between w and X
    'c': '#6e8f8c', 'C': '#46676a', 'n': '#3a4c50', 'N': '#253236',  # teals / slate cloth
    'w': '#34505e', 'X': '#1f3340',  # water
    'r': '#a8523a', 'R': '#7a3328', 'm': '#4a2020',  # reds, blood
    'a': '#c9a44f', 'A': '#8e6e30',  # gold, amber
    'd': '#a08c6c', 'D': '#7d6b52', 'b': '#5a4634', 'B': '#3a2d24',  # linen, leather
    'p': '#7e5a80', 'P': '#553a5a', 'u': '#2e1e34',  # corruption purples
}

# Skin ramps for the letters s, S, q, Q (light to dark). Keys match "skin" in data.json.
SKINS = {
    'fair':  ['#d8b294', '#b88e70', '#8e6650', '#5e4234'],
    'light': ['#c49a7a', '#a3785c', '#7e563f', '#55392a'],
    'olive': ['#a9825e', '#8a6646', '#664a32', '#443022'],
    'brown': ['#8a6248', '#6e4a34', '#523424', '#38241a'],
    'dark':  ['#5e4030', '#4a3024', '#36221a', '#241612'],
}

# Hair ramps for the letters h (main) and H (shadow). Keys match "hair" in data.json, lower-cased.
HAIRS = {
    'black':       ['#2e2a2c', '#18151a'],
    'dark brown':  ['#4a3426', '#2c1f18'],
    'light brown': ['#7a5838', '#56391f'],
    'red':         ['#8a4428', '#5e2a18'],
    'blond':       ['#a88a5a', '#7d643c'],
    'grey':        ['#8a8480', '#625d5a'],
}
