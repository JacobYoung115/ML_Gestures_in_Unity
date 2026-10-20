"""Write Assets/Custom/ML/ShapeClassifierReference.json for ShapeClassifierSelfTest.cs.

Runs both exported ONNX models with onnxruntime on held-out real captures,
reading the PNGs exactly as the C# self-test does, and stores the expected
probabilities so Unity can confirm Barracuda produces the same numbers.
"""
import json
import os

import cv2
import numpy as np
import onnxruntime as ort

import data

HERE = os.path.dirname(os.path.abspath(__file__))
ML_DIR = os.path.join(HERE, '..', 'Assets', 'Custom', 'ML')
RESOURCES = os.path.join(HERE, '..', 'Assets', 'Custom', 'Resources')


def main(per_class=10):
    views, labels, ids = data.load_real(RESOURCES)
    _, _, te = data.block_split(labels, ids)
    sessions = {k: ort.InferenceSession(os.path.join(ML_DIR, f'ShapeClassifier{k}.onnx'), providers=['CPUExecutionProvider'])
                for k in 'AB'}
    rng = np.random.default_rng(0)
    samples = []
    for c in range(3):
        for j in rng.choice(te[labels[te] == c], per_class, replace=False):
            path = f'{data.CLASSES[c]}/{ids[j]}'
            v = [cv2.imread(os.path.join(RESOURCES, f'{path}_{ax}.png'), cv2.IMREAD_GRAYSCALE) for ax in 'XYZ']
            feed = {f'view_{ax.lower()}': (x >= 128).astype(np.float32)[None, None] for ax, x in zip('XYZ', v)}
            s = {'path': path, 'label': int(c)}
            for k, sess in sessions.items():
                s[f'probs{k}'] = [round(float(p), 6) for p in sess.run(None, feed)[0][0]]
            samples.append(s)
    out = os.path.join(ML_DIR, 'ShapeClassifierReference.json')
    with open(out, 'w') as f:
        json.dump({'samples': samples}, f, indent=1)
    print('wrote', out, len(samples), 'samples')


if __name__ == '__main__':
    main()
