"""
Builds "Tidal Pixel", the bitmap-style font of the Tidal Pixel theme (wwwroot/fonts/tidal-pixel.ttf).

Every glyph is drawn below as a grid of pixels in the style of the Game Boy / Game Boy Advance Pokémon games. The
script turns the pixels into square TrueType outlines and writes the font file directly (standard library only).

Grid: rows 0..9 from the top. Row 0 is above capitals (accents), rows 1..7 hold capitals (row 7 sits on the baseline),
rows 8..9 are descenders. One pixel = 100 font units and the em is 700 units, so the font is pixel-perfect at 14px
(2x), 21px (3x) and 28px (4x). Characters that aren't here fall back to the next font in the CSS font-family list.

Run: python make_tidal_pixel.py   (writes ../../Web/wwwroot/fonts/tidal-pixel.ttf)
"""
import os
import struct
import time

PX = 100            # font units per pixel
EM = 700            # units per em: 14px text = 2 screen pixels per font pixel
ASCENT, DESCENT = 800, 200
SPACING = 1         # pixels between glyphs

# ---------------------------------------------------------------- glyphs
# G(top, rows): rows of '#'/'.' starting at grid row `top` (1 = cap top).
GLYPHS = {}


def G(chars, top, *rows):
    for ch in chars:
        GLYPHS[ch] = (top, rows)


# Capitals (rows 1..7)
G('A', 1, '.###.', '#...#', '#...#', '#####', '#...#', '#...#', '#...#')
G('B', 1, '####.', '#...#', '#...#', '####.', '#...#', '#...#', '####.')
G('C', 1, '.###.', '#...#', '#....', '#....', '#....', '#...#', '.###.')
G('D', 1, '####.', '#...#', '#...#', '#...#', '#...#', '#...#', '####.')
G('E', 1, '#####', '#....', '#....', '####.', '#....', '#....', '#####')
G('F', 1, '#####', '#....', '#....', '####.', '#....', '#....', '#....')
G('G', 1, '.###.', '#...#', '#....', '#.###', '#...#', '#...#', '.####')
G('H', 1, '#...#', '#...#', '#...#', '#####', '#...#', '#...#', '#...#')
G('I', 1, '###', '.#.', '.#.', '.#.', '.#.', '.#.', '###')
G('J', 1, '..###', '...#.', '...#.', '...#.', '#..#.', '#..#.', '.##..')
G('K', 1, '#...#', '#..#.', '#.#..', '##...', '#.#..', '#..#.', '#...#')
G('L', 1, '#....', '#....', '#....', '#....', '#....', '#....', '#####')
G('M', 1, '#...#', '##.##', '#.#.#', '#.#.#', '#...#', '#...#', '#...#')
G('N', 1, '#...#', '#...#', '##..#', '#.#.#', '#..##', '#...#', '#...#')
G('O', 1, '.###.', '#...#', '#...#', '#...#', '#...#', '#...#', '.###.')
G('P', 1, '####.', '#...#', '#...#', '####.', '#....', '#....', '#....')
G('Q', 1, '.###.', '#...#', '#...#', '#...#', '#.#.#', '#..#.', '.##.#')
G('R', 1, '####.', '#...#', '#...#', '####.', '#.#..', '#..#.', '#...#')
G('S', 1, '.####', '#....', '#....', '.###.', '....#', '....#', '####.')
G('T', 1, '#####', '..#..', '..#..', '..#..', '..#..', '..#..', '..#..')
G('U', 1, '#...#', '#...#', '#...#', '#...#', '#...#', '#...#', '.###.')
G('V', 1, '#...#', '#...#', '#...#', '#...#', '#...#', '.#.#.', '..#..')
G('W', 1, '#...#', '#...#', '#...#', '#.#.#', '#.#.#', '#.#.#', '.#.#.')
G('X', 1, '#...#', '#...#', '.#.#.', '..#..', '.#.#.', '#...#', '#...#')
G('Y', 1, '#...#', '#...#', '.#.#.', '..#..', '..#..', '..#..', '..#..')
G('Z', 1, '#####', '....#', '...#.', '..#..', '.#...', '#....', '#####')

# Lowercase (x-height rows 3..7, ascenders from row 1, descenders rows 8..9)
G('a', 3, '.###.', '....#', '.####', '#...#', '.####')
G('b', 1, '#....', '#....', '#.##.', '##..#', '#...#', '#...#', '####.')
G('c', 3, '.###.', '#....', '#....', '#...#', '.###.')
G('d', 1, '....#', '....#', '.##.#', '#..##', '#...#', '#...#', '.####')
G('e', 3, '.###.', '#...#', '#####', '#....', '.###.')
G('f', 1, '..##', '.#..', '.#..', '###.', '.#..', '.#..', '.#..')
G('g', 3, '.####', '#...#', '#...#', '#...#', '.####', '....#', '.###.')
G('h', 1, '#....', '#....', '#.##.', '##..#', '#...#', '#...#', '#...#')
G('i', 1, '.#.', '...', '##.', '.#.', '.#.', '.#.', '###')
G('j', 1, '...#', '....', '..##', '...#', '...#', '...#', '...#', '#..#', '.##.')
G('k', 1, '#...', '#...', '#..#', '#.#.', '##..', '#.#.', '#..#')
G('l', 1, '##.', '.#.', '.#.', '.#.', '.#.', '.#.', '###')
G('m', 3, '##.#.', '#.#.#', '#.#.#', '#.#.#', '#.#.#')
G('n', 3, '#.##.', '##..#', '#...#', '#...#', '#...#')
G('o', 3, '.###.', '#...#', '#...#', '#...#', '.###.')
G('p', 3, '####.', '#...#', '#...#', '#...#', '####.', '#....', '#....')
G('q', 3, '.####', '#...#', '#...#', '#...#', '.####', '....#', '....#')
G('r', 3, '#.##.', '##..#', '#....', '#....', '#....')
G('s', 3, '.####', '#....', '.###.', '....#', '####.')
G('t', 1, '.#..', '.#..', '###.', '.#..', '.#..', '.#.#', '..#.')
G('u', 3, '#...#', '#...#', '#...#', '#..##', '.##.#')
G('v', 3, '#...#', '#...#', '#...#', '.#.#.', '..#..')
G('w', 3, '#...#', '#...#', '#.#.#', '#.#.#', '.#.#.')
G('x', 3, '#...#', '.#.#.', '..#..', '.#.#.', '#...#')
G('y', 3, '#...#', '#...#', '#...#', '#...#', '.####', '....#', '.###.')
G('z', 3, '#####', '...#.', '..#..', '.#...', '#####')

# Digits
G('0', 1, '.###.', '#...#', '#..##', '#.#.#', '##..#', '#...#', '.###.')
G('1', 1, '.#.', '##.', '.#.', '.#.', '.#.', '.#.', '###')
G('2', 1, '.###.', '#...#', '....#', '...#.', '..#..', '.#...', '#####')
G('3', 1, '####.', '....#', '....#', '.###.', '....#', '....#', '####.')
G('4', 1, '...#.', '..##.', '.#.#.', '#..#.', '#####', '...#.', '...#.')
G('5', 1, '#####', '#....', '####.', '....#', '....#', '#...#', '.###.')
G('6', 1, '..##.', '.#...', '#....', '####.', '#...#', '#...#', '.###.')
G('7', 1, '#####', '....#', '...#.', '..#..', '.#...', '.#...', '.#...')
G('8', 1, '.###.', '#...#', '#...#', '.###.', '#...#', '#...#', '.###.')
G('9', 1, '.###.', '#...#', '#...#', '.####', '....#', '...#.', '.##..')

# Punctuation and symbols
G('!', 1, '#', '#', '#', '#', '#', '.', '#')
G('"', 1, '#.#', '#.#')
G('#', 1, '.#.#.', '.#.#.', '#####', '.#.#.', '#####', '.#.#.', '.#.#.')
G('$', 1, '..#..', '.####', '#.#..', '.###.', '..#.#', '####.', '..#..')
G('%', 1, '##...', '##..#', '...#.', '..#..', '.#...', '#..##', '...##')
G('&', 1, '.##..', '#..#.', '#.#..', '.#...', '#.#.#', '#..#.', '.##.#')
G("'’‘", 1, '#', '#')
G('(', 1, '.#', '#.', '#.', '#.', '#.', '#.', '.#')
G(')', 1, '#.', '.#', '.#', '.#', '.#', '.#', '#.')
G('*', 2, '..#..', '#.#.#', '.###.', '#.#.#', '..#..')
G('+', 2, '..#..', '..#..', '#####', '..#..', '..#..')
G(',', 6, '.#', '.#', '#.')
G('-', 4, '####')
G('–—', 4, '#####')
G('.', 7, '#')
G('/', 1, '....#', '....#', '...#.', '..#..', '.#...', '#....', '#....')
G(':', 3, '#', '.', '.', '.', '#')
G(';', 3, '.#', '..', '..', '..', '.#', '#.')
G('<', 1, '...#', '..#.', '.#..', '#...', '.#..', '..#.', '...#')
G('=', 3, '####', '....', '####')
G('>', 1, '#...', '.#..', '..#.', '...#', '..#.', '.#..', '#...')
G('?', 1, '.###.', '#...#', '....#', '...#.', '..#..', '.....', '..#..')
G('@', 1, '.###.', '#...#', '#.###', '#.#.#', '#.###', '#....', '.###.')
G('[', 1, '##', '#.', '#.', '#.', '#.', '#.', '##')
G('\\', 1, '#....', '#....', '.#...', '..#..', '...#.', '....#', '....#')
G(']', 1, '##', '.#', '.#', '.#', '.#', '.#', '##')
G('^', 1, '..#..', '.#.#.', '#...#')
G('_', 8, '#####')
G('`', 1, '#.', '.#')
G('{', 1, '..#', '.#.', '.#.', '#..', '.#.', '.#.', '..#')
G('|', 1, '#', '#', '#', '#', '#', '#', '#')
G('}', 1, '#..', '.#.', '.#.', '..#', '.#.', '.#.', '#..')
G('~', 3, '.#..#', '#.##.')
G('·•', 4, '#')                       # middle dot, bullet
G('›', 3, '#.', '.#', '#.')                # ›
G('‹', 3, '.#', '#.', '.#')                # ‹
G('…', 7, '#.#.#')                         # …
G('×', 3, '#...#', '.#.#.', '..#..', '.#.#.', '#...#')  # ×
G('é', 1, '...#.', '..#..', '.###.', '#...#', '#####', '#....', '.###.')   # é
G('É', 0, '...#.', '#####', '#....', '#....', '####.', '#....', '#....', '#####')  # É
G('è', 1, '.#...', '..#..', '.###.', '#...#', '#####', '#....', '.###.')   # è
G('á', 1, '...#.', '..#..', '.###.', '....#', '.####', '#...#', '.####')   # á
G('í', 1, '..#', '.#.', '##.', '.#.', '.#.', '.#.', '###')                 # í
G('ó', 1, '...#.', '..#..', '.###.', '#...#', '#...#', '#...#', '.###.')   # ó
G('ú', 1, '...#.', '..#..', '#...#', '#...#', '#...#', '#..##', '.##.#')   # ú
G('ñ', 1, '.##.#', '#.##.', '#.##.', '##..#', '#...#', '#...#', '#...#')   # ñ
G('ä', 1, '.#.#.', '.....', '.###.', '....#', '.####', '#...#', '.####')   # ä
G('ö', 1, '.#.#.', '.....', '.###.', '#...#', '#...#', '#...#', '.###.')   # ö
G('ü', 1, '.#.#.', '.....', '#...#', '#...#', '#...#', '#..##', '.##.#')   # ü
G('ß', 1, '.##..', '#..#.', '#.#..', '#..#.', '#...#', '#...#', '#.##.')   # ß
G('ç', 3, '.###.', '#....', '#....', '#...#', '.###.', '..#..', '.#...')   # ç
G('▶►', 1, '#...', '##..', '###.', '####', '###.', '##..', '#...')  # ▶ (the GBA menu cursor)

SPACE_WIDTH = 3


# ---------------------------------------------------------------- outlines
def rects(top, rows):
    """Merges the pixels into rectangles (runs per row, extended down while identical). Font units, y up."""
    out, open_runs = [], {}
    for i, row in enumerate(list(rows) + ['']):
        runs, x = set(), 0
        while x < len(row):
            if row[x] == '#':
                start = x
                while x < len(row) and row[x] == '#':
                    x += 1
                runs.add((start, x))
            else:
                x += 1
        r = top + i
        for run in list(open_runs):
            if run not in runs:
                out.append((run, open_runs.pop(run), r))
        for run in runs:
            open_runs.setdefault(run, r)
    result = []
    for (x0, x1), r0, r1 in out:
        # grid row r spans y = (7 - r) .. (8 - r) pixels above the baseline
        result.append((x0 * PX, (8 - r1) * PX, x1 * PX, (8 - r0) * PX))
    return result


def glyph_data(boxes):
    """A simple glyph: one clockwise 4-point contour per rectangle."""
    if not boxes:
        return b'', (0, 0, 0, 0), 0, 0
    xs = [b[0] for b in boxes] + [b[2] for b in boxes]
    ys = [b[1] for b in boxes] + [b[3] for b in boxes]
    bbox = (min(xs), min(ys), max(xs), max(ys))
    points, ends = [], []
    for x0, y0, x1, y1 in boxes:
        points += [(x0, y0), (x0, y1), (x1, y1), (x1, y0)]
        ends.append(len(points) - 1)
    data = struct.pack('>hhhhh', len(boxes), *bbox)
    data += struct.pack(f'>{len(ends)}H', *ends)
    data += struct.pack('>H', 0)                       # no instructions
    data += bytes([0x01] * len(points))                # on-curve, 16-bit x and y
    px = py = 0
    xd, yd = b'', b''
    for x, y in points:
        xd += struct.pack('>h', x - px)
        yd += struct.pack('>h', y - py)
        px, py = x, y
    data += xd + yd
    if len(data) % 4:
        data += b'\0' * (4 - len(data) % 4)
    return data, bbox, len(points), len(boxes)


# ---------------------------------------------------------------- tables
def checksum(data):
    data += b'\0' * (-len(data) % 4)
    return sum(struct.unpack(f'>{len(data) // 4}I', data)) & 0xFFFFFFFF


def build():
    chars = sorted(set(GLYPHS) | {' '})
    glyphs = [('.notdef', rects(1, ('####', '#..#', '#..#', '#..#', '#..#', '#..#', '####')), 5)]
    for ch in chars:
        if ch == ' ':
            glyphs.append((ch, [], SPACE_WIDTH))
            continue
        top, rows = GLYPHS[ch]
        glyphs.append((ch, rects(top, rows), max(len(r) for r in rows) + SPACING))

    glyf, loca, hmtx = b'', [], b''
    max_pts = max_ctrs = 0
    bboxes = []
    for _, boxes, width in glyphs:
        loca.append(len(glyf))
        data, bbox, npts, nctr = glyph_data(boxes)
        glyf += data
        max_pts, max_ctrs = max(max_pts, npts), max(max_ctrs, nctr)
        if boxes:
            bboxes.append(bbox)
        hmtx += struct.pack('>Hh', width * PX, bbox[0] if boxes else 0)
    loca.append(len(glyf))
    n = len(glyphs)
    x_min = min(b[0] for b in bboxes); y_min = min(b[1] for b in bboxes)
    x_max = max(b[2] for b in bboxes); y_max = max(b[3] for b in bboxes)
    adv_max = max(w for _, _, w in glyphs) * PX

    now = int(time.time()) + 2082844800                # seconds since 1904
    head = struct.pack('>IIIIHHqqhhhhHHhhh', 0x00010000, 0x00010000, 0, 0x5F0F3CF5, 0b1011, EM,
                       now, now, x_min, y_min, x_max, y_max, 0, 7, 2, 1, 0)
    hhea = struct.pack('>Ihhh' 'Hhhh' 'hhh' 'hhhh' 'hH', 0x00010000, ASCENT, -DESCENT, 0,
                       adv_max, 0, 0, x_max, 1, 0, 0, 0, 0, 0, 0, 0, n)
    maxp = struct.pack('>IHHHHHHHHHHHHHH', 0x00010000, n, max_pts, max_ctrs, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0)

    cps = [ord(c) for c in chars]
    avg = sum(w for _, _, w in glyphs) * PX // n
    os2 = struct.pack('>HhHHH' 'hhhhhhhhhh' 'h' '10s' 'IIII' '4s' 'HHH' 'hhhHH' 'II' 'hhHHH',
                      4, avg, 400, 5, 0,
                      350, 350, 0, 70, 350, 350, 0, 350, 100, 300,
                      0,
                      bytes([2, 11, 5, 9, 0, 0, 0, 0, 0, 0]),
                      0x00000003, 0, 0, 0,
                      b'TIDL',
                      0x0040, min(cps), min(max(cps), 0xFFFF),
                      ASCENT, -DESCENT, 0, ASCENT, DESCENT,
                      0x00000001, 0,
                      500, 700, 0, 32, 1)

    # cmap: format 4, one segment per character (plus the closing 0xFFFF segment)
    segs = [(cp, cp, (i + 1 - cp) & 0xFFFF) for i, cp in enumerate(cps)] + [(0xFFFF, 0xFFFF, 1)]
    seg_x2 = len(segs) * 2
    sr = 2 ** (len(segs).bit_length() - 1) * 2
    es = len(segs).bit_length() - 1
    body = struct.pack(f'>{len(segs)}H', *[s[1] for s in segs]) + b'\0\0'
    body += struct.pack(f'>{len(segs)}H', *[s[0] for s in segs])
    body += struct.pack(f'>{len(segs)}H', *[s[2] for s in segs])
    body += struct.pack(f'>{len(segs)}H', *([0] * len(segs)))
    sub = struct.pack('>HHHHHHH', 4, 14 + len(body), 0, seg_x2, sr, es, seg_x2 - sr) + body
    cmap = struct.pack('>HHHHI', 0, 1, 3, 1, 12) + sub

    names = {1: 'Tidal Pixel', 2: 'Regular', 3: 'Tidal Pixel Regular 1.000', 4: 'Tidal Pixel',
             5: 'Version 1.000', 6: 'TidalPixel-Regular'}
    records, strings = b'', b''
    for nid, text in names.items():
        enc = text.encode('utf-16-be')
        records += struct.pack('>HHHHHH', 3, 1, 0x409, nid, len(enc), len(strings))
        strings += enc
    name = struct.pack('>HHH', 0, len(names), 6 + len(records)) + records + strings

    post = struct.pack('>IIhhIIIII', 0x00030000, 0, -100, 100, 0, 0, 0, 0, 0)
    loca_t = struct.pack(f'>{len(loca)}I', *loca)

    tables = {b'OS/2': os2, b'cmap': cmap, b'glyf': glyf, b'head': head, b'hhea': hhea,
              b'hmtx': hmtx, b'loca': loca_t, b'maxp': maxp, b'name': name, b'post': post}
    tags = sorted(tables)
    count = len(tags)
    es = count.bit_length() - 1
    sr = (2 ** es) * 16
    font = struct.pack('>IHHHH', 0x00010000, count, sr, es, count * 16 - sr)
    offset = 12 + 16 * count
    directory, blob = b'', b''
    head_offset = 0
    for tag in tags:
        data = tables[tag]
        if tag == b'head':
            head_offset = offset
        directory += struct.pack('>4sIII', tag, checksum(data), offset, len(data))
        padded = data + b'\0' * (-len(data) % 4)
        blob += padded
        offset += len(padded)
    font += directory + blob
    adjust = (0xB1B0AFBA - checksum(font)) & 0xFFFFFFFF
    font = font[:head_offset + 8] + struct.pack('>I', adjust) + font[head_offset + 12:]
    return font, n


if __name__ == '__main__':
    out = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'Web', 'wwwroot', 'fonts', 'tidal-pixel.ttf')
    os.makedirs(os.path.dirname(out), exist_ok=True)
    data, glyph_count = build()
    with open(out, 'wb') as f:
        f.write(data)
    print(f'{os.path.normpath(out)}: {glyph_count} glyphs, {len(data)} bytes')
