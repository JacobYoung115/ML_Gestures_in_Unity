using System.IO;
using UnityEngine;

// Checks that the model gives the same answers inside Unity (Barracuda) as in
// Python. Add next to a ShapeClassifier, enter Play mode, and use the
// component's context menu "Run self-test". It loads the captures listed in
// ShapeClassifierReference.json, classifies them, and compares the
// probabilities with the ones computed by ML/train.py at export time.
[RequireComponent(typeof(ShapeClassifier))]
public class ShapeClassifierSelfTest : MonoBehaviour
{
    public TextAsset reference;   // ShapeClassifierReference.json
    public bool runOnStart = true;

    [System.Serializable] class Sample { public string path; public int label; public float[] probsA; public float[] probsB; }
    [System.Serializable] class Reference { public Sample[] samples; }

    void Start()
    {
        if (runOnStart) Run();
    }

    [ContextMenu("Run self-test")]
    public void Run()
    {
        var clf = GetComponent<ShapeClassifier>();
        var refData = JsonUtility.FromJson<Reference>(reference.text);
        bool isB = clf.modelAsset.name.EndsWith("B");
        string root = Path.Combine(Application.dataPath, "Custom", "Resources");

        int correct = 0;
        float maxDiff = 0;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        foreach (var s in refData.samples)
        {
            var t = new Texture2D[3];
            string[] axes = { "X", "Y", "Z" };
            for (int i = 0; i < 3; i++)
            {
                t[i] = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                t[i].LoadImage(File.ReadAllBytes(Path.Combine(root, $"{s.path}_{axes[i]}.png")));
            }
            var r = clf.Classify(t[0], t[1], t[2]);
            foreach (var tex in t) Destroy(tex);

            float[] expected = isB ? s.probsB : s.probsA;
            for (int i = 0; i < expected.Length; i++)
                maxDiff = Mathf.Max(maxDiff, Mathf.Abs(expected[i] - r.probs[i]));
            int argmax = 0;
            for (int i = 1; i < r.probs.Length; i++) if (r.probs[i] > r.probs[argmax]) argmax = i;
            if (argmax == s.label) correct++;
        }
        watch.Stop();
        int n = refData.samples.Length;
        string verdict = maxDiff < 1e-3f ? "MATCHES Python" : "DIFFERS from Python (check input orientation/format)";
        Debug.Log($"[ShapeClassifierSelfTest] {clf.modelAsset.name}: {correct}/{n} correct, " +
                  $"max |prob diff| vs Python = {maxDiff:0.000000} -> {verdict}. " +
                  $"{watch.Elapsed.TotalMilliseconds / n:0.00} ms per drawing (incl. PNG decode).");
    }
}
