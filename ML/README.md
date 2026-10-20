# 3D gesture shape classifier

Classifies a drawing captured by `TestVR.cs` (three 128×128 orthographic views, X/Y/Z)
as **Circle**, **Square**, **Triangle** or **Other**. "Other" covers anything else
(spirals, letters, scribbles, two shapes at once, 3D paths…), so the game can treat
it as a failed cast or a special effect.

## What's here

| Path | Purpose |
|---|---|
| `ML/strokes.py` | Synthetic 3D stroke generator and renderer that copies the Unity capture rig (framing, fixed trail width, stray dots) |
| `ML/data.py` | Loads real captures, augments them, mixes in synthetic positives and "Other" drawings |
| `ML/models.py` | Model **A** (views stacked as channels) and model **B** (one encoder shared by all views, pooled across views) |
| `ML/train.py` | Train, evaluate, export to ONNX |
| `ML/make_reference.py` | Writes expected outputs used by the Unity self-test |
| `Assets/Custom/ML/ShapeClassifier{A,B}.onnx` | Trained models (~100k params, ~0.4 MB each) |
| `Assets/Custom/ML/ShapeClassifier.cs` | Runtime component: reads `Out_RX/RY/RZ`, returns label + confidence |
| `Assets/Custom/ML/ShapeClassifierSelfTest.cs` | Confirms Barracuda gives the same numbers as Python |

## Data

* Real: 1,885 drawings (each = 3 views) in `Assets/Custom/Resources/{Circle,Square,Triangle}`.
* Synthetic "Other": 16 training families (lines, arcs, open polylines, zigzags, waves,
  2D spirals, helices, pentagons/hexagons, figure-8s, crosses, 2D/3D scribbles, letters,
  two shapes together, cube paths, partial squares) rendered at random 3D orientations.
* Real-derived "Other": two real drawings overlaid, or a real drawing mostly erased.
* Synthetic Circle/Square/Triangle are mixed in too, so "looks synthetic" is not a shortcut to "Other".
* Augmentation of real drawings: view permutation and mirroring (90° re-orientations),
  shared scale, small rotation/shift per view, stroke thickness, stroke gaps, stray dots.

Four families are **never used for training** — heart, smiley, cone spiral, star — and
are used to measure whether "Other" catches unfamiliar drawings.

## Results (held-out test sets)

The captures were recorded class by class, so a random split puts near-identical
neighbouring drawings in both train and test. The **block** split holds out a
contiguous stretch of each class's recording instead, and is the honest number.

| Model | Split | Real test acc | Real shapes wrongly → Other | Synthetic test acc | Unseen families → Other |
|---|---|---|---|---|---|
| A | block | 95.2% | 1.6% | 96.8% | 97.7% |
| **B** | block | **95.2%** | 1.6% | **99.1%** | 97.8% |
| A | random | 100% | 0% | 97.1% | 91.2% |
| B | random | 99.5% | 0% | 99.2% | 92.5% |

(No confidence threshold. Real test: 187 drawings. The gap between block and random
splits shows the model is sensitive to how drawings change across recording sessions —
more varied captures will help more than a bigger model.)

Effect of the confidence threshold (model B, block split):

| Threshold | Real acc | Real → Other | Unseen → Other |
|---|---|---|---|
| 0.0 | 95.2% | 1.6% | 97.8% |
| **0.5 (default)** | 94.7% | 3.2% | 99.2% |
| 0.6 | 92.5% | 5.3% | 99.8% |

Most remaining real errors are ambiguous drawings (rounded triangles vs circles,
three-cornered squares). `Square/1885` looks like a triangle and is probably mislabelled.

**Recommendation: use model B.** It is far better on the synthetic shapes, which suggests
it generalises better, and it does not care which camera sees the shape.

## Caveat: "Other" is measured on synthetic drawings only

There are no real "Other" captures yet. Record some (scribbles, letters, spirals, half
shapes…) into `Assets/Custom/Resources/Other/<id>_{X,Y,Z}.png` — `data.py` picks the folder
up automatically — and retrain. That is the single most useful next step.

## Using it in Unity

Step-by-step test instructions: [`Assets/Custom/ML/README.md`](../Assets/Custom/ML/README.md).

1. Barracuda `3.0.0` is added to `Packages/manifest.json`; Unity installs it on open.
2. Add `ShapeClassifier` to a GameObject. Assign `ShapeClassifierB.onnx` and the
   `Out_RX`, `Out_RY`, `Out_RZ` render textures.
3. After TestVR blits the views (in `ReadFromMaterial`), call:
   ```csharp
   var r = shapeClassifier.Classify();
   if (r.IsOther) { /* fizzle */ } else Debug.Log($"{r.label} ({r.confidence:P0})");
   ```
4. To check the import, add `ShapeClassifierSelfTest` to the same GameObject, assign
   `ShapeClassifierReference.json`, and press Play. The console should report
   `MATCHES Python`.

The worker defaults to `CSharpBurst` (CPU), which works on both PC and Quest.

## Adding classes later (e.g. cone spiral, smiley)

1. Record captures into `Assets/Custom/Resources/<NewClass>/`.
2. Add the name to `CLASSES` in `data.py` (before `Other`) and to `Labels` in `ShapeClassifier.cs`.
3. If the shape is also a synthetic "Other" family in `strokes.py`, remove it from
   `OTHER_TRAIN` / `OTHER_HELDOUT` (optionally add it to `TARGET_GENERATORS`).
4. Retrain and export:
   ```bash
   pip install -r ML/requirements.txt
   cd ML
   python train.py --model B --split block --export ../Assets/Custom/ML/ShapeClassifierB.onnx
   python make_reference.py   # needs both A and B exported
   ```
   About 10 minutes on the RTX 4050.
