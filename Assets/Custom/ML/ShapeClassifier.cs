using System;
using UnityEngine;
using Unity.Barracuda;

// Classifies a 3D drawing from the three orthographic views captured by TestVR
// (Out_RX / Out_RY / Out_RZ, 128x128, white stroke on black).
//
// The ONNX models are trained by ML/train.py. Inputs: view_x, view_y, view_z
// (each 1x1x128x128, 1 = stroke). Output: probs (1x4) in the order of Labels.
// Any prediction below confidenceThreshold is reported as Other, so a
// drawing that doesn't match a known shape can trigger a failure/effect.
public class ShapeClassifier : MonoBehaviour
{
    public static readonly string[] Labels = { "Circle", "Square", "Triangle", "Other" };
    public const int OtherIndex = 3;
    const int Res = 128;

    public struct Result
    {
        public int index;          // index into Labels after thresholding
        public string label;
        public float confidence;   // probability of the model's top class
        public float[] probs;      // raw probabilities, same order as Labels
        public bool IsOther => index == OtherIndex;
    }

    [Tooltip("ShapeClassifierA.onnx or ShapeClassifierB.onnx")]
    public NNModel modelAsset;

    [Tooltip("Binarised view textures written by TestVR (Out_RX, Out_RY, Out_RZ)")]
    public RenderTexture viewX, viewY, viewZ;

    [Range(0f, 1f)]
    [Tooltip("Predictions less confident than this are reported as Other")]
    public float confidenceThreshold = 0.5f;

    [Tooltip("CSharpBurst runs on the CPU on both PC and Quest; the model is small enough that this is fast")]
    public WorkerFactory.Type workerType = WorkerFactory.Type.CSharpBurst;

    IWorker worker;
    Texture2D readback;
    readonly float[][] buffers = { new float[Res * Res], new float[Res * Res], new float[Res * Res] };

    void Awake()
    {
        worker = WorkerFactory.CreateWorker(workerType, ModelLoader.Load(modelAsset));
        readback = new Texture2D(Res, Res, TextureFormat.R8, false);
    }

    void OnDestroy()
    {
        worker?.Dispose();
        if (readback) Destroy(readback);
    }

    // Classify whatever is currently in viewX/Y/Z. Call after TestVR has blitted the views.
    public Result Classify()
    {
        ReadView(viewX, buffers[0]);
        ReadView(viewY, buffers[1]);
        ReadView(viewZ, buffers[2]);
        return Run(buffers[0], buffers[1], buffers[2]);
    }

    // Classify from three readable textures (e.g. saved PNGs loaded with Texture2D.LoadImage).
    public Result Classify(Texture2D x, Texture2D y, Texture2D z)
    {
        FromTexture(x, buffers[0]);
        FromTexture(y, buffers[1]);
        FromTexture(z, buffers[2]);
        return Run(buffers[0], buffers[1], buffers[2]);
    }

    Result Run(float[] x, float[] y, float[] z)
    {
        // Barracuda tensors are NHWC; with one channel the memory layout matches the ONNX NCHW input.
        using (var tx = new Tensor(1, Res, Res, 1, x))
        using (var ty = new Tensor(1, Res, Res, 1, y))
        using (var tz = new Tensor(1, Res, Res, 1, z))
        {
            worker.SetInput("view_x", tx);
            worker.SetInput("view_y", ty);
            worker.SetInput("view_z", tz);
            worker.Execute();
            float[] probs = worker.PeekOutput("probs").ToReadOnlyArray();

            int best = 0;
            for (int i = 1; i < probs.Length; i++)
                if (probs[i] > probs[best]) best = i;
            int index = probs[best] < confidenceThreshold ? OtherIndex : best;
            return new Result { index = index, label = Labels[index], confidence = probs[best], probs = (float[])probs.Clone() };
        }
    }

    void ReadView(RenderTexture rt, float[] dst)
    {
        if (rt.width != Res || rt.height != Res)
            throw new ArgumentException($"{rt.name} must be {Res}x{Res}");
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        readback.ReadPixels(new Rect(0, 0, Res, Res), 0, 0);
        readback.Apply();
        RenderTexture.active = prev;
        FromTexture(readback, dst);
    }

    // Texture rows start at the bottom; the training PNGs (and the model) start at the top.
    static void FromTexture(Texture2D tex, float[] dst)
    {
        if (tex.width != Res || tex.height != Res)
            throw new ArgumentException($"{tex.name} must be {Res}x{Res}");
        Color32[] px = tex.GetPixels32();
        for (int row = 0; row < Res; row++)
        {
            int src = (Res - 1 - row) * Res;
            int o = row * Res;
            for (int col = 0; col < Res; col++)
            {
                Color32 c = px[src + col];
                // min(r, a) covers RGBA32/RGB24 (r = value), R8 (a = 255) and Alpha8 (r = 255) textures
                dst[o + col] = Math.Min(c.r, c.a) >= 128 ? 1f : 0f;
            }
        }
    }
}
