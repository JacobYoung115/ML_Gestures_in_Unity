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

    GameObject trailObj;
    GameObject center;
    public static int count;


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

    private void ReadFromMaterial() {
        //Render out the Z
        Graphics.Blit(In_RZ, Out_RZ, blitMat);
        Texture2D tex = new Texture2D(Out_RZ.width, Out_RZ.height, TextureFormat.R8, false);
        tex.ReadPixels(new Rect(0, 0, Out_RZ.width, Out_RZ.height), 0, 0);
        tex.Apply();
        byte[] bytes = ImageConversion.EncodeToPNG(tex);
        Object.Destroy(tex);
        System.IO.File.WriteAllBytes(Application.dataPath + "/Custom/Resources/Square/" + count + "_Z.png", bytes);


        //Render the Y
        Graphics.Blit(In_RY, Out_RY, blitMat);
        tex = new Texture2D(Out_RY.width, Out_RY.height, TextureFormat.R8, false);
        tex.ReadPixels(new Rect(0, 0, Out_RY.width, Out_RY.height), 0, 0);
        tex.Apply();
        bytes = ImageConversion.EncodeToPNG(tex);
        Object.Destroy(tex);
        System.IO.File.WriteAllBytes(Application.dataPath + "/Custom/Resources/Square/" + count + "_Y.png", bytes);


        //Render the X
        Graphics.Blit(In_RX, Out_RX, blitMat);
        tex = new Texture2D(Out_RX.width, Out_RX.height, TextureFormat.R8, false);
        tex.ReadPixels(new Rect(0, 0, Out_RX.width, Out_RX.height), 0, 0);
        tex.Apply();
        bytes = ImageConversion.EncodeToPNG(tex);
        Object.Destroy(tex);
        System.IO.File.WriteAllBytes(Application.dataPath + "/Custom/Resources/Square/" + count + "_X.png", bytes);
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

        //This count is not being subtracted when delete calls it.
        count +=1;
        Save.SaveFile(count);
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
