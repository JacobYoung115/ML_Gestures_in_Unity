using UnityEngine;
using Valve.VR.InteractionSystem;

// Spawns a small physics sphere from the player's hand each time TestVR
// finishes classifying a drawing. Colour shows the result:
// Triangle = red, Circle = blue, Square = green, Other = black.
public class ShapeSpawner : MonoBehaviour
{
    public TestVR drawer;          // raises ShapeClassified after each drawing
    public Hand hand;              // spawn point; defaults to the drawer's hand

    [Header("Sphere")]
    public float diameter = 0.08f;
    public float mass = 0.2f;
    [Tooltip("Metres in front of the hand, so the sphere doesn't start inside the hand's colliders")]
    public float spawnOffset = 0.12f;
    [Tooltip("Extra forward speed added on top of the hand's own velocity")]
    public float launchSpeed = 0.5f;
    [Tooltip("Seconds before the sphere is removed (0 = never)")]
    public float lifetime = 10f;
    [Tooltip("Optional material to copy; the built-in default material is used if empty")]
    public Material sphereMaterial;

    [Header("Colours")]
    public Color circleColor = Color.blue;
    public Color squareColor = Color.green;
    public Color triangleColor = Color.red;
    public Color otherColor = Color.black;

    void OnEnable()
    {
        if (drawer == null) drawer = GetComponent<TestVR>();
        if (hand == null && drawer != null) hand = drawer.hand != null ? drawer.hand : drawer.GetComponent<Hand>();
        if (drawer != null) drawer.ShapeClassified += Spawn;
        else Debug.LogError("ShapeSpawner: assign the TestVR component that does the drawing.");
    }

    void OnDisable()
    {
        if (drawer != null) drawer.ShapeClassified -= Spawn;
    }

    public Color ColorFor(ShapeClassifier.Result result)
    {
        if (result.IsOther) return otherColor;
        switch (result.label)
        {
            case "Circle": return circleColor;
            case "Square": return squareColor;
            case "Triangle": return triangleColor;
            default: return otherColor;
        }
    }

    public void Spawn(ShapeClassifier.Result result)
    {
        Transform from = hand != null ? hand.transform : transform;

        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = "Spell_" + result.label;
        sphere.transform.position = from.position + from.forward * spawnOffset;
        sphere.transform.localScale = Vector3.one * diameter;

        Renderer rend = sphere.GetComponent<Renderer>();
        if (sphereMaterial != null) rend.material = new Material(sphereMaterial);
        rend.material.color = ColorFor(result);

        Rigidbody body = sphere.AddComponent<Rigidbody>();
        body.mass = mass;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        Vector3 handVelocity = hand != null ? hand.GetTrackedObjectVelocity() : Vector3.zero;
        body.velocity = handVelocity + from.forward * launchSpeed;

        if (lifetime > 0) Destroy(sphere, lifetime);
    }
}
