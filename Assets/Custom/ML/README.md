# Testing the shape classifier in Unity

After each drawing, the classifier decides whether it was a **Circle**, **Square**,
**Triangle** or **Other**. The game then:

* spawns a small physics sphere from the drawing hand, coloured by the result:
  Triangle = red, Circle = blue, Square = green, Other = black;
* saves the drawing's three views into `Assets/Custom/Resources/<Label>/<id>_{X,Y,Z}.png`
  (`Other` drawings go to `Resources/Other/`).

Training and model details are in [`ML/README.md`](../../../ML/README.md).

## Scripts

| Script | Where | What it does |
|---|---|---|
| `Custom/ML/ShapeClassifier.cs` | any GameObject | Runs the ONNX model on the three views |
| `Custom/ML/ShapeClassifierSelfTest.cs` | same GameObject as the classifier | Checks Unity's output matches Python |
| `Custom/Scripts/TestVR.cs` | the drawing hand (existing) | Captures the drawing, classifies it, saves it into the right folder |
| `Custom/Scripts/ShapeSpawner.cs` | the drawing hand | Spawns the coloured sphere |
| `Custom/Scripts/RemoveAsset.cs` | other hand (existing) | Deletes the last saved drawing from whichever folder it went to |

## 1. Open the project

1. Pull the `claude/shape-classifier` branch and open the project in Unity 2021.3.
2. Unity installs **Barracuda 3.0.0** (added to `Packages/manifest.json`). Wait for the
   import to finish and check the Console has no compile errors.
3. Select `Assets/Custom/ML/ShapeClassifierB.onnx`. The Inspector should show an imported
   NN model with inputs `view_x`, `view_y`, `view_z` and output `probs`. If it shows import
   errors, try `ShapeClassifierA.onnx`, then report the error message.

## 2. Self-test (no headset needed)

This confirms the model gives the same numbers in Unity as in Python.

1. Open `Assets/Custom/ML_gestures.unity`.
2. Create an empty GameObject named `ShapeClassifier`.
3. Add **ShapeClassifier**:
   * *Model Asset*: `ShapeClassifierB.onnx`
   * *View X/Y/Z*: `Out_RX`, `Out_RY`, `Out_RZ` (in `Assets/Custom`)
4. Add **ShapeClassifierSelfTest**:
   * *Reference*: `ShapeClassifierReference.json`
5. Press Play (SteamVR errors without a headset are fine here). The Console should show:

   ```
   [ShapeClassifierSelfTest] ShapeClassifierB: 29/30 correct, max |prob diff| vs Python = 0.0000xx -> MATCHES Python. ...
   ```

   * **DIFFERS from Python**: the input orientation or format is wrong. Copy the log line
     into the issue/PR.
   * **Lower count than expected** but MATCHES Python: expected now and then; the model itself
     got those drawings wrong in Python too (B: 29/30, A: 30/30 at time of export).

You can rerun the test from the component's ⋮ menu → **Run self-test**. Uncheck *Run On
Start* (or remove the component) once it passes.

## 3. Wire up the drawing hand

1. Select the hand GameObject that has **TestVR**.
2. On **TestVR**, set *Classifier* to the `ShapeClassifier` GameObject from step 2.
   * *Save Confidence* (default 0.8): a recognised shape is only saved into its folder if
     the model is at least this confident. Less confident shapes still spawn a sphere but
     aren't saved, to keep the dataset clean.
   * *Save Drawings*: untick to play without writing any files.
3. Add **ShapeSpawner** to the same hand. *Drawer* and *Hand* fill themselves in from TestVR.
   Optional settings: *Diameter* (0.08 m), *Launch Speed*, *Lifetime* (10 s), *Sphere Material*.

## 4. Test in SteamVR

1. Start SteamVR, press Play, and draw with the pinch/trigger on the drawing hand.
2. When you release:
   * a sphere drops from your hand in the result's colour and carries your hand's motion;
   * the Console logs e.g. `Drawing 1890: Circle (97%)`;
   * the three PNGs appear under `Assets/Custom/Resources/Circle/` (or the matching folder).
     Unity may need focus or *Assets → Refresh* to show them in the Project window.
3. Try each case:

| Draw | Expected sphere | Saved to |
|---|---|---|
| Circle | blue | `Resources/Circle` |
| Square | green | `Resources/Square` |
| Triangle | red | `Resources/Triangle` |
| Spiral, zigzag, letter, scribble, half a shape, two shapes | black | `Resources/Other` |
| Sloppy shape (confidence between the thresholds) | shape's colour | not saved (Console says why) |

4. To undo a bad save, use the **RemoveAsset** hand. It deletes the most recent drawing from
   whichever folder it was saved in.

### Tuning

* **Too many shapes come out black** → lower *Confidence Threshold* on ShapeClassifier
  (default 0.5).
* **Scribbles come out as shapes** → raise it (0.6–0.7).
* **Wrong drawings being saved** → raise *Save Confidence* on TestVR.

## Before retraining with the new captures

The new drawings are labelled by the model itself, so its mistakes end up in the dataset.
Look through the `Resources/<Label>` folders (especially `Other`) and move or delete
anything in the wrong place before running `ML/train.py` again. Real `Other` drawings are
the most valuable addition: the model has only seen synthetic ones so far.

## Notes

* Files are written with `Application.dataPath`, so saving only works in the Editor (as before).
  `RemoveAsset` uses `UnityEditor` and will break a standalone/Quest build. Disable it or wrap it
  in `#if UNITY_EDITOR` before building.
* The classifier runs on the CPU (`CSharpBurst`), which works on Quest as well.
