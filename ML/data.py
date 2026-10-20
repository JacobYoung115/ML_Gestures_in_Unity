"""Datasets: real captures (with augmentation) mixed with synthetic strokes.

Samples are (3, 128, 128) float tensors in [0, 1], view order X, Y, Z, and an
integer label: 0 Circle, 1 Square, 2 Triangle, 3 Other.

Real captures live in Assets/Custom/Resources/<Class>/<id>_<X|Y|Z>.png. Any
class folder named in CLASSES is picked up, so real "Other" captures can be
added later as Assets/Custom/Resources/Other/<id>_X.png etc.
"""
import glob
import os
import re

import cv2
import numpy as np
import torch
from torch.utils.data import Dataset

import strokes

CLASSES = ['Circle', 'Square', 'Triangle', 'Other']
OTHER = CLASSES.index('Other')
RES = strokes.RES


def load_real(root):
    """Return (views uint8 [N,3,128,128], labels [N], ids [N]) from the Resources folder."""
    views, labels, ids = [], [], []
    for ci, cls in enumerate(CLASSES):
        files = glob.glob(os.path.join(root, cls, '*_Z.png'))
        for f in sorted(files, key=lambda p: int(re.match(r'(\d+)_', os.path.basename(p)).group(1))):
            base = f[:-6]
            v = [cv2.imread(f'{base}_{ax}.png', cv2.IMREAD_GRAYSCALE) for ax in 'XYZ']
            if any(x is None for x in v):
                continue
            views.append(np.stack([np.where(x >= 128, 255, 0).astype(np.uint8) for x in v]))
            labels.append(ci)
            ids.append(int(re.match(r'(\d+)_', os.path.basename(f)).group(1)))
    return np.stack(views), np.array(labels), np.array(ids)


def block_split(labels, ids, n_blocks=10, val_block=4, test_block=9):
    """Split each class into contiguous id blocks (captures were recorded in class-ordered sessions),
    so validation/test drawings come from different stretches of recording than training ones."""
    tr, va, te = [], [], []
    for c in np.unique(labels):
        idx = np.where(labels == c)[0]
        idx = idx[np.argsort(ids[idx])]
        for b, chunk in enumerate(np.array_split(idx, n_blocks)):
            (te if b == test_block else va if b == val_block else tr).extend(chunk)
    return np.array(tr), np.array(va), np.array(te)


def random_split(n, rng, val=0.1, test=0.1):
    p = rng.permutation(n)
    a, b = int(n * test), int(n * (test + val))
    return p[b:], p[a:b], p[:a]


# ---------------------------------------------------------------- augmentation of real 3-view samples


def _affine_view(img, rng, scale, max_rot, max_shift):
    ang = rng.uniform(-max_rot, max_rot)
    M = cv2.getRotationMatrix2D((RES / 2, RES / 2), ang, scale)
    M[:, 2] += rng.uniform(-max_shift, max_shift, 2)
    out = cv2.warpAffine(img, M, (RES, RES), flags=cv2.INTER_LINEAR, borderValue=0)
    return np.where(out >= 128, 255, 0).astype(np.uint8)


def augment_real(views, rng):
    """Geometric and stroke augmentations that keep the class of a real 3-view capture."""
    v = views.copy()
    # 90-degree re-orientations of the drawing permute and mirror the three views
    if rng.random() < 0.5:
        v = v[rng.permutation(3)]
    if rng.random() < 0.5:
        v = v[:, :, ::-1]
    if rng.random() < 0.5:
        v = v[:, ::-1, :]
    if rng.random() < 0.3:
        v = v.transpose(0, 2, 1)
    v = np.ascontiguousarray(v)
    scale = rng.uniform(0.75, 1.25)  # one camera size for all three views
    v = np.stack([_affine_view(x, rng, scale, 20, 6) for x in v])
    # stroke thickness
    r = rng.random()
    if r < 0.25:
        v = np.stack([cv2.dilate(x, np.ones((2, 2), np.uint8)) for x in v])
    elif r < 0.4:
        v = np.stack([cv2.erode(x, np.ones((2, 2), np.uint8)) for x in v])
    # small gaps in the stroke (tracking dropouts)
    if rng.random() < 0.3:
        for x in v:
            for _ in range(rng.integers(1, 3)):
                cx, cy = rng.integers(20, RES - 20, 2)
                cv2.circle(x, (int(cx), int(cy)), int(rng.integers(2, 5)), 0, -1)
    # stray dots like the ones in the real captures
    if rng.random() < 0.15:
        x = v[rng.integers(3)]
        cx, cy = rng.integers(2, RES - 2, 2)
        cv2.circle(x, (int(cx), int(cy)), int(rng.integers(1, 3)), 255, -1)
    return v


def real_negative(pool, rng):
    """Build an 'Other' sample from real strokes: two real drawings overlaid, or a mostly-erased one."""
    a = pool[rng.integers(len(pool))]
    if rng.random() < 0.6:
        b = pool[rng.integers(len(pool))]
        s = rng.uniform(0.45, 0.75)
        d = rng.uniform(22, 36) * np.array([np.cos(t := rng.uniform(0, 2 * np.pi)), np.sin(t)])
        out = []
        for x, y in zip(a, b):
            xa = _affine_view(x, rng, s, 10, 0)
            yb = _affine_view(y, rng, s, 10, 0)
            xa = cv2.warpAffine(xa, np.float32([[1, 0, d[0]], [0, 1, d[1]]]), (RES, RES))
            yb = cv2.warpAffine(yb, np.float32([[1, 0, -d[0]], [0, 1, -d[1]]]), (RES, RES))
            out.append(np.maximum(xa, yb))
        return np.where(np.stack(out) >= 128, 255, 0).astype(np.uint8)
    # keep only a narrow wedge (~30-45%) of the stroke around the centre
    out = a.copy()
    ang0 = rng.uniform(0, 2 * np.pi)
    keep = rng.uniform(0.3, 0.45) * 2 * np.pi
    yy, xx = np.mgrid[:RES, :RES]
    ang = (np.arctan2(yy - RES / 2, xx - RES / 2) - ang0) % (2 * np.pi)
    out[:, ang > keep] = 0
    return out


# ---------------------------------------------------------------- datasets


class MixedDataset(Dataset):
    """Infinite-style training set: real positives + synthetic positives + synthetic/real 'Other'."""

    def __init__(self, real_views, real_labels, length, p_real=0.45, p_synth_pos=0.2, p_real_neg=0.07, seed=0):
        self.rv, self.rl = real_views, real_labels
        self.length = length
        self.p = np.array([p_real, p_synth_pos, p_real_neg, 1 - p_real - p_synth_pos - p_real_neg])
        self.seed = seed
        self.others = list(strokes.OTHER_TRAIN.values())

    def __len__(self):
        return self.length

    def __getitem__(self, i):
        rng = np.random.default_rng((self.seed, i, torch.initial_seed() % 2 ** 31))
        kind = rng.choice(4, p=self.p)
        if kind == 0:
            j = rng.integers(len(self.rv))
            v, y = augment_real(self.rv[j], rng), int(self.rl[j])
        elif kind == 1:
            y = int(rng.integers(3))
            v = strokes.render_views(strokes.TARGET_GENERATORS[y](rng), rng)
        elif kind == 2:
            v, y = real_negative(self.rv, rng), OTHER
        else:
            g = self.others[rng.integers(len(self.others))]
            v, y = strokes.render_views(g(rng), rng), OTHER
        return torch.from_numpy(v.astype(np.float32) / 255.0), y


def synth_eval_set(n_per_family, seed, heldout=False):
    """Fixed synthetic evaluation set. Returns views, labels, family names."""
    rng = np.random.default_rng(seed)
    fams = dict(strokes.OTHER_HELDOUT if heldout else strokes.OTHER_TRAIN)
    if not heldout:
        fams.update({CLASSES[k]: g for k, g in strokes.TARGET_GENERATORS.items()})
    V, Y, F = [], [], []
    for name, g in fams.items():
        y = CLASSES.index(name) if name in CLASSES else OTHER
        for _ in range(n_per_family):
            V.append(strokes.render_views(g(rng), rng))
            Y.append(y)
            F.append(name)
    return np.stack(V), np.array(Y), np.array(F)
