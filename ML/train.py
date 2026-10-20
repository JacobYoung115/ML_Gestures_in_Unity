"""Train, evaluate and export a 3-view shape classifier.

    python train.py --model A --split block
    python train.py --model B --split block --export ../Assets/Custom/ML/ShapeClassifierB.onnx

Outputs a checkpoint, a metrics JSON and (optionally) a Barracuda-compatible
ONNX file in --out-dir.
"""
import argparse
import json
import os
import time

import numpy as np
import torch
import torch.nn as nn
import torch.nn.functional as F
from torch.utils.data import DataLoader

import data
from models import MODELS, split_views

HERE = os.path.dirname(os.path.abspath(__file__))
RESOURCES = os.path.join(HERE, '..', 'Assets', 'Custom', 'Resources')


def get_real(cache):
    if os.path.exists(cache):
        z = np.load(cache)
        return z['views'], z['labels'], z['ids']
    v, l, i = data.load_real(RESOURCES)
    np.savez_compressed(cache, views=v, labels=l, ids=i)
    return v, l, i


@torch.no_grad()
def predict(model, views, device, bs=512):
    model.eval()
    out = []
    for k in range(0, len(views), bs):
        x = torch.from_numpy(views[k:k + bs].astype(np.float32) / 255.0).to(device)
        out.append(F.softmax(model(*split_views(x)), 1).cpu())
    return torch.cat(out).numpy()


def summarize(probs, labels, families=None, threshold=0.0):
    """Accuracy with an optional confidence threshold (low-confidence -> Other)."""
    pred = probs.argmax(1)
    pred = np.where(probs.max(1) < threshold, data.OTHER, pred)
    res = {'acc': float((pred == labels).mean()), 'n': int(len(labels))}
    for c, name in enumerate(data.CLASSES):
        m = labels == c
        if m.any():
            res[f'acc_{name}'] = float((pred[m] == c).mean())
    pos = labels != data.OTHER
    if pos.any():
        res['false_other_rate'] = float((pred[pos] == data.OTHER).mean())
    if families is not None:
        res['per_family'] = {f: float((pred[families == f] == labels[families == f]).mean()) for f in np.unique(families)}
    cm = np.zeros((len(data.CLASSES),) * 2, int)
    np.add.at(cm, (labels, pred), 1)
    res['confusion'] = cm.tolist()
    return res


def export_onnx(model, path):
    model = model.cpu().eval()
    model.softmax = True
    dummy = tuple(torch.zeros(1, 1, 128, 128) for _ in range(3))
    torch.onnx.export(model, dummy, path, input_names=['view_x', 'view_y', 'view_z'], output_names=['probs'],
                      opset_version=11, do_constant_folding=True, dynamo=False)
    model.softmax = False
    # verify with onnxruntime and list ops for Barracuda compatibility
    import onnx
    import onnxruntime as ort
    m = onnx.load(path)
    ops = sorted({n.op_type for n in m.graph.node})
    sess = ort.InferenceSession(path, providers=['CPUExecutionProvider'])
    x = [(torch.rand(1, 1, 128, 128) > 0.95).float() for _ in range(3)]
    with torch.no_grad():
        ref = F.softmax(model(*x), 1).numpy()
    got = sess.run(None, {'view_x': x[0].numpy(), 'view_y': x[1].numpy(), 'view_z': x[2].numpy()})[0]
    return {'ops': ops, 'max_abs_diff_vs_torch': float(np.abs(ref - got).max()), 'bytes': os.path.getsize(path)}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--model', choices=list(MODELS), required=True)
    ap.add_argument('--split', choices=['block', 'random'], default='block')
    ap.add_argument('--epochs', type=int, default=30)
    ap.add_argument('--samples-per-epoch', type=int, default=12000)
    ap.add_argument('--width', type=int, default=16)
    ap.add_argument('--lr', type=float, default=3e-3)
    ap.add_argument('--bs', type=int, default=128)
    ap.add_argument('--workers', type=int, default=6)
    ap.add_argument('--seed', type=int, default=0)
    ap.add_argument('--data-dir', default='/lab/data/shapes')
    ap.add_argument('--out-dir', default='/lab/data/shapes/runs')
    ap.add_argument('--export', default=None, help='write the trained model as ONNX to this path')
    args = ap.parse_args()

    torch.manual_seed(args.seed)
    os.makedirs(args.data_dir, exist_ok=True)
    os.makedirs(args.out_dir, exist_ok=True)
    tag = f'{args.model}_{args.split}_w{args.width}_s{args.seed}'
    device = 'cuda' if torch.cuda.is_available() else 'cpu'

    views, labels, ids = get_real(os.path.join(args.data_dir, 'real.npz'))
    if args.split == 'block':
        tr, va, te = data.block_split(labels, ids)
    else:
        tr, va, te = data.random_split(len(labels), np.random.default_rng(args.seed))
    print(f'real: train {len(tr)} val {len(va)} test {len(te)}')

    synth_val = data.synth_eval_set(40, seed=1000)
    synth_test = data.synth_eval_set(100, seed=2000)
    unseen = data.synth_eval_set(150, seed=3000, heldout=True)

    ds = data.MixedDataset(views[tr], labels[tr], args.samples_per_epoch, seed=args.seed)
    dl = DataLoader(ds, batch_size=args.bs, shuffle=True, num_workers=args.workers, drop_last=True,
                    persistent_workers=True, worker_init_fn=lambda w: None)

    model = MODELS[args.model](num_classes=len(data.CLASSES), width=args.width).to(device)
    n_params = sum(p.numel() for p in model.parameters())
    print(f'model {args.model}: {n_params} params')
    opt = torch.optim.AdamW(model.parameters(), lr=args.lr, weight_decay=1e-4)
    sched = torch.optim.lr_scheduler.OneCycleLR(opt, max_lr=args.lr, total_steps=args.epochs * len(dl))
    crit = nn.CrossEntropyLoss(label_smoothing=0.05)

    best, best_state, history = -1, None, []
    for ep in range(args.epochs):
        model.train()
        t0, tot, n = time.time(), 0.0, 0
        for x, y in dl:
            x, y = x.to(device, non_blocking=True), y.to(device, non_blocking=True)
            loss = crit(model(*split_views(x)), y)
            opt.zero_grad(set_to_none=True)
            loss.backward()
            opt.step()
            sched.step()
            tot += loss.item() * len(y)
            n += len(y)
        rv = summarize(predict(model, views[va], device), labels[va])
        sv = summarize(predict(model, synth_val[0], device), synth_val[1])
        score = (rv['acc'] + sv['acc']) / 2
        history.append({'epoch': ep, 'loss': tot / n, 'real_val': rv['acc'], 'synth_val': sv['acc']})
        print(f'ep {ep:2d} loss {tot / n:.3f} real_val {rv["acc"]:.3f} synth_val {sv["acc"]:.3f} ({time.time() - t0:.0f}s)', flush=True)
        if score >= best:
            best, best_state = score, {k: v.detach().clone() for k, v in model.state_dict().items()}

    model.load_state_dict(best_state)
    torch.save(best_state, os.path.join(args.out_dir, f'{tag}.pt'))

    p_real = predict(model, views[te], device)
    p_syn = predict(model, synth_test[0], device)
    p_un = predict(model, unseen[0], device)
    metrics = {'args': vars(args), 'params': n_params, 'history': history, 'thresholds': {}}
    for th in [0.0, 0.5, 0.6, 0.7, 0.8]:
        metrics['thresholds'][str(th)] = {
            'real_test': summarize(p_real, labels[te], threshold=th),
            'synth_test': summarize(p_syn, synth_test[1], synth_test[2], threshold=th),
            'unseen_other': summarize(p_un, unseen[1], unseen[2], threshold=th),
        }
    m0 = metrics['thresholds']['0.0']
    print(f'TEST real {m0["real_test"]["acc"]:.3f} (false-other {m0["real_test"]["false_other_rate"]:.3f}) | '
          f'synth {m0["synth_test"]["acc"]:.3f} | unseen-other recall {m0["unseen_other"]["acc"]:.3f}')

    if args.export:
        metrics['onnx'] = export_onnx(model, args.export)
        print('exported', args.export, metrics['onnx'])
    with open(os.path.join(args.out_dir, f'{tag}.json'), 'w') as f:
        json.dump(metrics, f, indent=1)


if __name__ == '__main__':
    main()
