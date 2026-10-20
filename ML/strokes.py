"""Synthetic 3D stroke generator and orthographic 3-view renderer.

Mimics the capture rig in Assets/Custom/Scripts/TestVR.cs: a fixed-width
TrailRenderer stroke, framed by three orthographic cameras (X, Y, Z) centred
on the stroke's bounding box and scaled to its largest extent. Rendering
statistics (shape size ~70 px, stroke ~5.5 px, thinner for bigger shapes)
were measured from the real captures in Assets/Custom/Resources.

Every generator returns a list of polylines (each an (N, 3) float array) in a
roughly unit-sized local frame. `render_views` turns them into a
(3, 128, 128) uint8 array ordered X, Y, Z with values 0 / 255.
"""
import math

import cv2
import numpy as np

RES = 128
SS = 4  # supersampling factor for fractional stroke widths

# ---------------------------------------------------------------- helpers


def _resample(pts, step=0.01):
    """Resample a polyline at roughly uniform arc length (TrailRenderer minVertexDistance)."""
    seg = np.linalg.norm(np.diff(pts, axis=0), axis=1)
    s = np.concatenate([[0], np.cumsum(seg)])
    if s[-1] < 1e-6:
        return pts[:1]
    n = max(2, int(s[-1] / step))
    t = np.linspace(0, s[-1], n)
    return np.stack([np.interp(t, s, pts[:, i]) for i in range(pts.shape[1])], 1)


def _smooth_noise(n, rng, scale, freq):
    """Low-frequency 1D noise, used for hand wobble."""
    k = max(2, int(freq))
    ctrl = rng.normal(0, scale, k + 2)
    return np.interp(np.linspace(0, k + 1, n), np.arange(k + 2), ctrl)


def _to3d(xy, rng, depth_wobble=0.04):
    """Lift a planar curve into 3D with a little out-of-plane hand drift."""
    n = len(xy)
    z = _smooth_noise(n, rng, depth_wobble, rng.integers(2, 6))
    return np.column_stack([xy, z])


def _jitter(pts, rng, amount=0.008):
    n = len(pts)
    out = pts.copy()
    for i in range(pts.shape[1]):
        out[:, i] += _smooth_noise(n, rng, amount, n // 6) + rng.normal(0, amount * 0.3, n)
    return out


def _polygon(verts, rng, closure=(-0.06, 0.08), round_r=(0.0, 0.12), bow=0.06):
    """Hand-drawn closed polygon: optional rounded corners, bowed sides, gap/overshoot at the join."""
    verts = np.asarray(verts, float)
    k = len(verts)
    start = rng.integers(k)
    verts = np.roll(verts, -start, axis=0)
    pts = []
    for i in range(k):
        a, b = verts[i], verts[(i + 1) % k]
        t = np.linspace(0, 1, 40)[:, None]
        seg = a + (b - a) * t
        nrm = np.array([-(b - a)[1], (b - a)[0]])
        seg += nrm * rng.uniform(-bow, bow) * np.sin(np.pi * t)
        pts.append(seg)
    pts = np.concatenate(pts)
    r = rng.uniform(*round_r)
    if r > 0:
        w = max(1, int(r * 40))
        ker = np.ones(w) / w
        padded = np.concatenate([pts[-w:], pts, pts[:w]])
        pts = np.column_stack([np.convolve(padded[:, i], ker, 'same')[w:-w] for i in range(2)])
    # closure: negative = gap, positive = overshoot along the start
    c = rng.uniform(*closure)
    n = len(pts)
    extra = int(abs(c) * n)
    if c < 0 and extra:
        pts = pts[:-extra]
    elif extra:
        pts = np.concatenate([pts, pts[:extra]])
    return pts


def _ellipse(rng, sweep=None, aspect=None, wobble=0.05, spiral=0.0):
    sweep = rng.uniform(math.radians(325), math.radians(395)) if sweep is None else sweep
    aspect = rng.uniform(0.7, 1.0) if aspect is None else aspect
    a0 = rng.uniform(0, 2 * math.pi)
    t = np.linspace(a0, a0 + sweep * rng.choice([-1, 1]), 200)
    r = 1 + _smooth_noise(len(t), rng, wobble, 5) + spiral * (t - a0) / (2 * math.pi)
    return np.column_stack([r * np.cos(t), aspect * r * np.sin(t)])


# ---------------------------------------------------------------- target classes


def gen_circle(rng):
    return [_to3d(_ellipse(rng, wobble=rng.uniform(0.01, 0.07), spiral=rng.uniform(-0.06, 0.06)), rng)]


def gen_square(rng):
    asp = rng.uniform(0.7, 1.0)
    v = np.array([[-1, -asp], [1, -asp], [1, asp], [-1, asp]], float)
    v += rng.normal(0, 0.07, v.shape)
    return [_to3d(_polygon(v, rng), rng)]


def gen_triangle(rng):
    while True:
        ang = np.sort(rng.uniform(0, 2 * math.pi, 3))
        v = np.column_stack([np.cos(ang), np.sin(ang)])
        e = [v[(i + 1) % 3] - v[i] for i in range(3)]
        angles = [math.degrees(math.acos(np.clip(np.dot(-e[i - 1], e[i]) / (np.linalg.norm(e[i - 1]) * np.linalg.norm(e[i])), -1, 1))) for i in range(3)]
        if min(angles) > 28:
            break
    return [_to3d(_polygon(v, rng, round_r=(0.0, 0.1)), rng)]


# ---------------------------------------------------------------- "other" families


def gen_line(rng):
    t = np.linspace(-1, 1, 80)
    bend = rng.uniform(-0.3, 0.3)
    return [_to3d(np.column_stack([t, bend * (1 - t ** 2)]), rng)]


def gen_arc(rng):
    return [_to3d(_ellipse(rng, sweep=math.radians(rng.uniform(60, 220))), rng)]


def gen_open_polyline(rng):
    """Two or three sides of a polygon: L, V, U, N shapes, clearly not closed."""
    k = rng.integers(3, 5)
    v = rng.uniform(-1, 1, (k, 2))
    return [_to3d(np.concatenate([np.linspace(v[i], v[i + 1], 30) for i in range(k - 1)]), rng)]


def gen_zigzag(rng):
    k = rng.integers(3, 8)
    x = np.linspace(-1, 1, k + 1)
    y = np.where(np.arange(k + 1) % 2, 1, -1) * rng.uniform(0.3, 0.8)
    pts = np.concatenate([np.linspace([x[i], y[i]], [x[i + 1], y[i + 1]], 20) for i in range(k)])
    return [_to3d(pts, rng)]


def gen_wave(rng):
    t = np.linspace(-1, 1, 200)
    return [_to3d(np.column_stack([t, rng.uniform(0.2, 0.6) * np.sin(t * math.pi * rng.uniform(1.5, 4))]), rng)]


def gen_spiral2d(rng):
    turns = rng.uniform(1.6, 4)
    t = np.linspace(0, turns * 2 * math.pi, 400)
    r = 0.1 + t / t[-1]
    return [_to3d(np.column_stack([r * np.cos(t), r * np.sin(t)]), rng)]


def gen_helix(rng):
    turns = rng.uniform(1.5, 4)
    t = np.linspace(0, turns * 2 * math.pi, 400)
    h = rng.uniform(0.8, 2.0)
    return [np.column_stack([np.cos(t), np.sin(t), np.linspace(-h / 2, h / 2, len(t))])]


def gen_cone_spiral(rng):
    turns = rng.uniform(2, 5)
    t = np.linspace(0, turns * 2 * math.pi, 500)
    r = np.linspace(rng.uniform(0, 0.2), 1, len(t))
    h = rng.uniform(0.8, 2.0)
    return [np.column_stack([r * np.cos(t), r * np.sin(t), np.linspace(h / 2, -h / 2, len(t))])]


def gen_pentagon(rng):
    k = rng.choice([5, 6])
    ang = np.linspace(0, 2 * math.pi, k, endpoint=False) + rng.uniform(0, 2 * math.pi)
    v = np.column_stack([np.cos(ang), np.sin(ang)]) + rng.normal(0, 0.04, (k, 2))
    return [_to3d(_polygon(v, rng, round_r=(0, 0.03), bow=0.02), rng)]


def gen_star(rng):
    ang = np.linspace(0, 4 * math.pi, 6) + rng.uniform(0, 2 * math.pi)  # pentagram, one stroke
    v = np.column_stack([np.cos(ang), np.sin(ang)]) + rng.normal(0, 0.05, (6, 2))
    return [_to3d(np.concatenate([np.linspace(v[i], v[i + 1], 40) for i in range(5)]), rng)]


def gen_figure8(rng):
    t = np.linspace(0, 2 * math.pi, 300)
    pts = np.column_stack([np.sin(t), np.sin(t) * np.cos(t) * rng.uniform(0.8, 1.6)])
    return [_to3d(pts, rng)]


def gen_heart(rng):
    t = np.linspace(0, 2 * math.pi, 300)
    x = 16 * np.sin(t) ** 3
    y = 13 * np.cos(t) - 5 * np.cos(2 * t) - 2 * np.cos(3 * t) - np.cos(4 * t)
    return [_to3d(np.column_stack([x, y]) / 16, rng)]


def gen_smiley(rng):
    face = _ellipse(rng, wobble=0.03)
    eye_l = np.array([[-0.35, 0.3]]) + 0.06 * np.column_stack([np.cos(np.linspace(0, 6.3, 20)), np.sin(np.linspace(0, 6.3, 20))])
    eye_r = eye_l + [0.7, 0]
    t = np.linspace(math.radians(200), math.radians(340), 40)
    mouth = np.column_stack([0.5 * np.cos(t), 0.5 * np.sin(t) - 0.05])
    parts = [face, eye_l, eye_r, mouth]
    if rng.random() < 0.5:  # drawn in one continuous trail: connect the parts
        return [_to3d(np.concatenate(parts), rng)]
    return [_to3d(p, rng) for p in parts]


def gen_cross(rng):
    a = rng.uniform(0, math.pi)
    d1 = np.array([math.cos(a), math.sin(a)])
    d2 = np.array([math.cos(a + math.pi / 2 + rng.normal(0, 0.2)), math.sin(a + math.pi / 2)])
    s1 = np.linspace(-d1, d1, 40)
    s2 = np.linspace(-d2, d2, 40)
    if rng.random() < 0.5:
        return [_to3d(np.concatenate([s1, np.linspace(d1, -d2, 30), s2]), rng)]
    return [_to3d(s1, rng), _to3d(s2, rng)]


def gen_scribble(rng):
    n = rng.integers(6, 16)
    ctrl = rng.uniform(-1, 1, (n, 2))
    t = np.linspace(0, n - 1, 300)
    pts = np.column_stack([np.interp(t, np.arange(n), ctrl[:, i]) for i in range(2)])
    pts = cv2.GaussianBlur(pts.reshape(-1, 1, 2), (1, 31), 0).reshape(-1, 2)
    return [_to3d(pts, rng, 0.1)]


def gen_scribble3d(rng):
    n = rng.integers(5, 12)
    ctrl = rng.uniform(-1, 1, (n, 3))
    t = np.linspace(0, n - 1, 300)
    return [np.column_stack([np.interp(t, np.arange(n), ctrl[:, i]) for i in range(3)])]


def gen_letter(rng):
    which = rng.choice(['S', 'Z', 'M', 'W', 'N', 'check', 'arrow', 'e'])
    if which == 'S':  # two opposing half-circles stacked vertically
        t = np.linspace(0, 1, 200)
        a = np.where(t < 0.5, math.pi * (0.5 + 2 * t * 1.25), math.pi * (0.5 - 2 * (t - 0.5) * 1.25))
        cy = np.where(t < 0.5, 0.5, -0.5)
        pts = np.column_stack([0.5 * np.cos(a), cy + 0.5 * np.sin(a)])
        return [_to3d(pts, rng)]
    elif which == 'Z':
        pts = np.array([[-1, 1], [1, 1], [-1, -1], [1, -1]])
    elif which == 'M':
        pts = np.array([[-1, -1], [-1, 1], [0, 0], [1, 1], [1, -1]])
    elif which == 'W':
        pts = np.array([[-1, 1], [-0.5, -1], [0, 0.3], [0.5, -1], [1, 1]])
    elif which == 'N':
        pts = np.array([[-1, -1], [-1, 1], [1, -1], [1, 1]])
    elif which == 'check':
        pts = np.array([[-1, 0], [-0.4, -0.7], [1, 1]])
    elif which == 'arrow':
        pts = np.array([[-1, 0], [1, 0], [0.5, 0.4], [1, 0], [0.5, -0.4]])
    else:  # cursive loop 'e'
        t = np.linspace(0.3, 2 * math.pi + 1.2, 200)
        pts = np.column_stack([t / 3 + 0.5 * np.cos(t), 0.6 * np.sin(t)])
        return [_to3d(pts, rng)]
    pts = pts.astype(float) + rng.normal(0, 0.06, pts.shape)
    return [_to3d(np.concatenate([np.linspace(pts[i], pts[i + 1], 30) for i in range(len(pts) - 1)]), rng)]


def gen_double_shape(rng):
    """Two target shapes in one drawing (e.g. circle next to triangle) -> not a single shape."""
    g = [gen_circle, gen_square, gen_triangle]
    a = g[rng.integers(3)](rng)
    b = g[rng.integers(3)](rng)
    s = rng.uniform(0.4, 1.0)
    off = rng.uniform(1.4, 2.4) * np.array([math.cos(th := rng.uniform(0, 2 * math.pi)), math.sin(th), 0])
    if rng.random() < 0.3:  # concentric
        off = np.zeros(3)
        s = rng.uniform(0.35, 0.65)
    b = [p * s + off for p in b]
    if rng.random() < 0.5:
        return [np.concatenate(a + [np.linspace(a[0][-1], b[0][0], 20)] + b)]
    return a + b


def gen_cube_path(rng):
    """A 3D wireframe-ish path through cube corners."""
    corners = np.array([[x, y, z] for x in (-1, 1) for y in (-1, 1) for z in (-1, 1)], float)
    order = [0, 1, 3, 2, 0, 4, 5, 7, 6, 4]
    k = rng.integers(6, len(order) + 1)
    v = corners[order[:k]] + rng.normal(0, 0.06, (k, 3))
    return [np.concatenate([np.linspace(v[i], v[i + 1], 30) for i in range(k - 1)])]


def gen_partial_square(rng):
    pts = gen_square(rng)[0]
    keep = rng.uniform(0.3, 0.55)
    s = rng.integers(len(pts))
    return [np.roll(pts, -s, axis=0)[: int(len(pts) * keep)]]


TARGET_GENERATORS = {0: gen_circle, 1: gen_square, 2: gen_triangle}

# "Other" families used for training, and families only ever seen at test time
# (to measure whether the "other" class generalises to unseen kinds of drawing).
OTHER_TRAIN = {
    'line': gen_line, 'arc': gen_arc, 'open_polyline': gen_open_polyline, 'zigzag': gen_zigzag,
    'wave': gen_wave, 'spiral2d': gen_spiral2d, 'helix': gen_helix, 'pentagon': gen_pentagon,
    'figure8': gen_figure8, 'cross': gen_cross, 'scribble': gen_scribble, 'scribble3d': gen_scribble3d,
    'letter': gen_letter, 'double_shape': gen_double_shape, 'cube_path': gen_cube_path,
    'partial_square': gen_partial_square,
}
OTHER_HELDOUT = {'heart': gen_heart, 'smiley': gen_smiley, 'cone_spiral': gen_cone_spiral, 'star': gen_star}

# ---------------------------------------------------------------- rendering


def random_rotation(rng, facing_prob=0.6):
    """Mostly 'drawn facing the viewer' (plane near the Z camera), sometimes any orientation."""
    if rng.random() < facing_prob:
        rz = rng.uniform(0, 2 * math.pi)
        rx, ry = rng.normal(0, math.radians(20), 2)
    else:
        q = rng.normal(size=4)
        q /= np.linalg.norm(q)
        w, x, y, z = q
        return np.array([
            [1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
            [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
            [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])
    cz, sz = math.cos(rz), math.sin(rz)
    cx, sx = math.cos(rx), math.sin(rx)
    cy, sy = math.cos(ry), math.sin(ry)
    Rz = np.array([[cz, -sz, 0], [sz, cz, 0], [0, 0, 1]])
    Rx = np.array([[1, 0, 0], [0, cx, -sx], [0, sx, cx]])
    Ry = np.array([[cy, 0, sy], [0, 1, 0], [-sy, 0, cy]])
    return Ry @ Rx @ Rz


def render_views(strokes, rng, rotate=True, stray_dot_prob=0.15):
    """Render polylines into X, Y, Z orthographic views like the Unity rig."""
    strokes = [_jitter(_resample(np.asarray(s, float), 0.02), rng) for s in strokes]
    if rotate:
        R = random_rotation(rng)
        strokes = [s @ R.T for s in strokes]
    allp = np.concatenate(strokes)
    lo, hi = allp.min(0), allp.max(0)
    centre = (lo + hi) / 2
    extent = max((hi - lo).max(), 1e-3)
    # size/thickness distributions measured from the real captures
    size_px = float(np.clip(rng.normal(72, 15), 45, 115))
    thick_px = float(np.clip(5.5 - 0.03 * (size_px - 72) + rng.normal(0, 0.5), 3.5, 7.5))
    scale = size_px / extent
    off = rng.normal(0, 2, 2)
    # (horizontal, vertical) world axes seen by each camera, ordered X, Y, Z
    axes = [(2, 1), (0, 2), (0, 1)]
    views = np.zeros((3, RES, RES), np.uint8)
    big = RES * SS
    for vi, (h, v) in enumerate(axes):
        canvas = np.zeros((big, big), np.uint8)
        for s in strokes:
            px = (s[:, h] - centre[h]) * scale + RES / 2 + off[0]
            py = RES / 2 - (s[:, v] - centre[v]) * scale + off[1]
            p = np.round(np.column_stack([px, py]) * SS * 8).astype(np.int32)  # 3 fractional bits
            cv2.polylines(canvas, [p.reshape(-1, 1, 2)], False, 255, max(1, int(round(thick_px * SS))), cv2.LINE_8, shift=3)
        small = cv2.resize(canvas, (RES, RES), interpolation=cv2.INTER_AREA)
        views[vi] = np.where(small >= 128, 255, 0)
        if rng.random() < stray_dot_prob:
            cx, cy = rng.integers(2, RES - 2, 2)
            cv2.circle(views[vi], (int(cx), int(cy)), int(rng.integers(1, 3)), 255, -1)
    return views
