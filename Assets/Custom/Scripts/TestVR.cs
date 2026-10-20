using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Valve.VR;
using Valve.VR.InteractionSystem;
using UnityEngine.Events;


public class TestVR : MonoBehaviour
{
    // Start is called before the first frame update
    //public UnityEvent m_External;
    public SteamVR_Action_Boolean grabPinchAction = SteamVR_Input.GetAction<SteamVR_Action_Boolean>("GrabPinch");

    //It would be a good idea to be able to delete a frame that you know you goofed on.


    public Hand hand;

    public Material drawMat;

    public Material blitMat;              //graphics blit --> the material containing the shader.

     //public Texture aTexture;            //used for graphics blit --> source texture passed to shader
    public Texture In_RZ;            //used for graphics blit --> source texture passed to shader
    public Texture In_RY;            //used for graphics blit --> source texture passed to shader
    public Texture In_RX;            //used for graphics blit --> source texture passed to shader

    public RenderTexture Out_RZ;          //graphics blit --> dest texture from shader
    public RenderTexture Out_RY;          //graphics blit --> dest texture from shader
    public RenderTexture Out_RX;          //graphics blit --> dest texture from shader

    public Camera drawCam_Z;
    public Camera drawCam_Y;
    public Camera drawCam_X;

    public Transform VRCam;

    //public RenderTexture rt;

    [Header("Classification")]
    public ShapeClassifier classifier;

    [Range(0f, 1f)]
    [Tooltip("A recognised shape is only saved into its Resources folder if the classifier is at least this confident")]
    public float saveConfidence = 0.8f;

    [Tooltip("Write each drawing's three views into Assets/Custom/Resources/<label>/")]
    public bool saveDrawings = true;

    [Tooltip("Folder used when no classifier is assigned")]
    public string unclassifiedFolder = "Unclassified";

    //Raised after every drawing is classified (whether or not it was saved).
    public event System.Action<ShapeClassifier.Result> ShapeClassified;

    GameObject trailObj;
    GameObject center;
    public static int count;
    public static string lastSavedFolder;   //Resources sub-folder of the most recently saved drawing (used by RemoveAsset)
    int pendingId;


    private void OnEnable() {
        if (hand == null)
            hand = this.GetComponent<Hand>();

        //grabPinchAction.AddOnChangeListener(OnPinchActionChange, hand.handType);            //this is actually where the SteamVR_Action_Boolean's function is called for an addchange listener.
        grabPinchAction.AddOnStateDownListener(OnActionDown, hand.handType);
        grabPinchAction.AddOnStateUpListener(OnActionUp, hand.handType);

        //Debug.Log("action's state? " + grabPinchAction.GetState(hand.handtype))
        trailObj = new GameObject("Trail Code");
        trailObj.transform.SetParent(this.transform, false);
        trailObj.layer = 3;     //this is my custom drawing layer.
        //trailObj.transform.position = new Vector3(0f, 0f, 0f);
        count = Save.LoadFile().count;
        Debug.Log("Loaded in count value: " + count);
    
        // 
        center = new GameObject("Center");
        drawCam_Z.transform.SetParent(center.transform, true);
        drawCam_Y.transform.SetParent(center.transform, true);
        drawCam_X.transform.SetParent(center.transform, true);
    }


    private void OnDisable() {
        //grabPinchAction.RemoveOnChangeListener(OnPinchActionChange, hand.handType);
        grabPinchAction.RemoveOnStateDownListener(OnActionDown, hand.handType);
        grabPinchAction.RemoveOnStateDownListener(OnActionUp, hand.handType);
    }

    //The Change here is only for organization purposes, it depends on the call in OnEnable.
    /*
    private void OnPinchActionChange(SteamVR_Action_Boolean actionIn, SteamVR_Input_Sources inputSource, bool newValue) {
        
        // At the moment, this is only printing 'True'. How can I create an action which tells me when it is pressed and unpressed?
        if (newValue) {
            Debug.Log(newValue);
            MyAction();
        }
    }

    public void MyAction() {
        Debug.Log("I'm clicking the button!");
    }
    */

    private void ReadFromRenderTexture() {
        RenderTexture rt = drawCam_Z.targetTexture;
        //RenderTexture rt = RenderTexture.GetTemporary();
        drawCam_Z.Render();
        RenderTexture.active = rt;
        Texture2D screenshot = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        screenshot.ReadPixels(new Rect(0,0,rt.width, rt.height),0,0);

        byte[] bytes = screenshot.EncodeToPNG();
        System.IO.File.WriteAllBytes(Application.dataPath + "/Custom/Resources/screenshot01.png", bytes);

        RenderTexture.active = null;
    }

    //Blit a camera view through the shader and read it back as a black/white texture.
    private Texture2D RenderView(Texture source, RenderTexture dest) {
        Graphics.Blit(source, dest, blitMat);
        RenderTexture.active = dest;
        Texture2D tex = new Texture2D(dest.width, dest.height, TextureFormat.R8, false);
        tex.ReadPixels(new Rect(0, 0, dest.width, dest.height), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        return tex;
    }

    private void ReadFromMaterial() {
        Texture2D texZ = RenderView(In_RZ, Out_RZ);
        Texture2D texY = RenderView(In_RY, Out_RY);
        Texture2D texX = RenderView(In_RX, Out_RX);

        //Classify the drawing and pick the Resources folder to save it into.
        string folder = null;
        if (classifier != null) {
            ShapeClassifier.Result result = classifier.Classify(texX, texY, texZ);
            Debug.Log($"Drawing {pendingId}: {result.label} ({result.confidence:P0})");

            if (result.IsOther)
                folder = "Other";
            else if (result.confidence >= saveConfidence)
                folder = result.label;
            else
                Debug.Log($"Not saving drawing {pendingId}: {result.label} confidence {result.confidence:P0} is below saveConfidence {saveConfidence:P0}");

            ShapeClassified?.Invoke(result);
        } else {
            Debug.LogWarning("TestVR: no ShapeClassifier assigned, saving drawing to Resources/" + unclassifiedFolder);
            folder = unclassifiedFolder;
        }

        if (folder != null && saveDrawings) {
            string dir = Application.dataPath + "/Custom/Resources/" + folder;
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllBytes(dir + "/" + pendingId + "_Z.png", ImageConversion.EncodeToPNG(texZ));
            System.IO.File.WriteAllBytes(dir + "/" + pendingId + "_Y.png", ImageConversion.EncodeToPNG(texY));
            System.IO.File.WriteAllBytes(dir + "/" + pendingId + "_X.png", ImageConversion.EncodeToPNG(texX));

            //Only advance the counter when something was written, so ids stay contiguous.
            count = pendingId;
            Save.SaveFile(count);
            lastSavedFolder = folder;
        }

        Object.Destroy(texZ);
        Object.Destroy(texY);
        Object.Destroy(texX);
    }

    
    private void OnActionDown(SteamVR_Action_Boolean actionIn, SteamVR_Input_Sources inputSource) {
        Debug.Log("Button pressed down on hand: " + inputSource);
        
        //So, this does read from the render texture, but we actually need to read the modified pixels returned by the shader.
        //ReadFromRenderTexture();
        
        //add line renderer
        //Note: you cannot add existing components to new gameobjects.
        TrailRenderer trend = trailObj.AddComponent<TrailRenderer>();
        trend.material = drawMat;
        trend.minVertexDistance = 0.01f;
        trend.startWidth = 0.05f;
        trend.endWidth = 0.05f;
        trend.startColor = new Color(1.0f, 0.0f, 0.0f);
        trend.endColor = new Color(1.0f, 0.0f, 0.0f);


    }
    

    private void OnActionUp(SteamVR_Action_Boolean actionIn, SteamVR_Input_Sources inputSource) {
        Debug.Log("Button let go!");

        TrailRenderer trend = trailObj.GetComponent<TrailRenderer>();
        int numPositions = trend.positionCount;
        //Debug.Log("Number of points in trail: " + numPositions);

        /*
        Vector3 centerOfMass = new Vector3(0,0,0);
        Vector3[] positions = new Vector3[numPositions];         //initialize empty array
        float X_max = 0;
        float Y_max = 0;
        float Z_max = 0;

        int count = trend.GetPositions(positions);      //this actually puts the values into the array.
        for (int i = 0; i < count; i++) {
            Vector3 p = positions[i];

            X_max = Mathf.Max(p.x, X_max);
            Y_max = Mathf.Max(p.y, Y_max);
            Z_max = Mathf.Max(p.z, Z_max);
                

            //Debug.Log(p);
            centerOfMass += p;
        }

        centerOfMass /= numPositions;
        */

        //Create an Object at the center of mass (working)
        Vector3 centerOfMass = trend.bounds.center;
        /*
        GameObject center = GameObject.CreatePrimitive(PrimitiveType.Cube);
        center.transform.position = centerOfMass;
        center.transform.localScale = new Vector3(0.1f, 0.1f, 0.1f);
        center.GetComponent<Renderer>().material.color = new Color(1,0,0);
        */


        //Now, we need to get the center of mass of all the points AND create a bounding box.
        //Debug.Log("Bounding box extents: " + trend.bounds.extents);
        //Debug.Log("Bounding box size: " + trend.bounds.size);
        //Debug.Log("Camera size (before): " + drawCam_Z.orthographicSize);

        //note, we actually need the maximum axis.
        float max = Mathf.Max(Mathf.Max(trend.bounds.extents.x, trend.bounds.extents.y), trend.bounds.extents.z);
        float margin = 1.4f;
        float resize = margin * (max * 0.5f);

        //Vector3 eulerRotation = new Vector3(transform.eulerAngles.x, otherObject.transform.eulerAngles.y, transform.eulerAngles.z);
        //transform.rotation = Quaternion.Euler(eulerRotation);
        center.transform.position = centerOfMass;
        center.transform.rotation = VRCam.transform.rotation;


        drawCam_Z.orthographicSize = resize;
        //drawCam_Z.transform.position = centerOfMass;
        drawCam_Z.transform.localPosition = new Vector3(0, 0, max);
        //drawCam_Z.transform.rotation = VRCam.transform.rotation;

        drawCam_Y.orthographicSize = resize;
        drawCam_Y.transform.localPosition = new Vector3(0, max, 0);
        //drawCam_Y.transform.rotation = VRCam.transform.rotation;

        drawCam_X.orthographicSize = resize;
        drawCam_X.transform.localPosition = new Vector3(-max, 0, 0);
        //drawCam_X.transform.rotation = VRCam.transform.rotation;


        //finally, we need rotation invariance.
        //parent the cameras afterwards?


        //Debug.Log("Camera size (after): " + drawCam_X.orthographicSize);


        //Although the camera is updating and filling the field of view correctly, the shader & saveout method seems to
        //be saving out before the camera position is correct.
        Invoke("ReadFromMaterial", 0.1f);
        Destroy(trend, 0.2f);

        //The new drawing gets the next id; ReadFromMaterial commits it once the files are written.
        pendingId = count + 1;
    }
    /*
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        //Debug.Log("Hand position: " + gameObject.transform.position);
        //this.gameObject.transform.position
    }
    */
}
