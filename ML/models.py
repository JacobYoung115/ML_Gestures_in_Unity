"""Two lightweight 3-view shape classifiers.

Both take three inputs (view_x, view_y, view_z), each [N, 1, 128, 128] with
stroke = 1, background = 0, and return class probabilities [N, num_classes].
Only Barracuda-friendly ops are used: Conv, BatchNorm (folded on export),
Relu, MaxPool, GlobalAveragePool, Concat, Max, Add, Mul, Gemm, Softmax.

A (EarlyFusion): the three views are stacked as channels of one image.
B (SharedViewEncoder): one encoder applied to each view separately, features
pooled across views (max + mean), so the result does not depend on which
camera saw which side of the drawing.
"""
import torch
import torch.nn as nn


def conv_block(cin, cout):
    return nn.Sequential(nn.Conv2d(cin, cout, 3, padding=1, bias=False), nn.BatchNorm2d(cout), nn.ReLU(inplace=True))


def encoder(cin, width):
    w = width
    return nn.Sequential(
        nn.MaxPool2d(2),                       # 128 -> 64: keeps thin strokes (max, not average)
        conv_block(cin, w), nn.MaxPool2d(2),   # 32
        conv_block(w, 2 * w), nn.MaxPool2d(2),  # 16
        conv_block(2 * w, 4 * w), nn.MaxPool2d(2),  # 8
        conv_block(4 * w, 4 * w),
        conv_block(4 * w, 4 * w),
        nn.AdaptiveAvgPool2d(1), nn.Flatten(),
    )


class EarlyFusion(nn.Module):
    def __init__(self, num_classes=4, width=16, softmax=False):
        super().__init__()
        self.features = encoder(3, width)
        self.head = nn.Sequential(nn.Dropout(0.2), nn.Linear(4 * width, num_classes))
        self.softmax = softmax

    def forward(self, vx, vy, vz):
        out = self.head(self.features(torch.cat([vx, vy, vz], 1)))
        return torch.softmax(out, 1) if self.softmax else out


class SharedViewEncoder(nn.Module):
    def __init__(self, num_classes=4, width=16, softmax=False):
        super().__init__()
        self.features = encoder(1, width)
        f = 4 * width
        self.head = nn.Sequential(nn.Linear(2 * f, f), nn.ReLU(inplace=True), nn.Dropout(0.2), nn.Linear(f, num_classes))
        self.softmax = softmax

    def forward(self, vx, vy, vz):
        fx, fy, fz = self.features(vx), self.features(vy), self.features(vz)
        fmax = torch.max(torch.max(fx, fy), fz)
        fmean = (fx + fy + fz) * (1.0 / 3.0)
        out = self.head(torch.cat([fmax, fmean], 1))
        return torch.softmax(out, 1) if self.softmax else out


MODELS = {'A': EarlyFusion, 'B': SharedViewEncoder}


def split_views(x):
    """[N, 3, H, W] -> three [N, 1, H, W] tensors (X, Y, Z)."""
    return x[:, 0:1], x[:, 1:2], x[:, 2:3]
