using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Valve.VR;
using Valve.VR.InteractionSystem;
using UnityEditor;

public class RemoveAsset : MonoBehaviour
{
    public SteamVR_Action_Boolean grabPinchAction = SteamVR_Input.GetAction<SteamVR_Action_Boolean>("GrabPinch");
    public Hand hand;

    // Start is called before the first frame update
    private void OnEnable() {
        if (hand == null) {
            hand = this.GetComponent<Hand>();
        }

        grabPinchAction.AddOnStateDownListener(DeletePreviousDrawing, hand.handType);

    }

    private void OnDisable() {
        grabPinchAction.RemoveOnStateDownListener(DeletePreviousDrawing, hand.handType);
    }

    private void DeletePreviousDrawing(SteamVR_Action_Boolean actionIn, SteamVR_Input_Sources inputSource) {
        TestVR.count = Save.LoadFile().count;

        Debug.Log("Attempting to delete image(s): " + TestVR.count);

        string[] paths = new string[3];
        paths[0] = "Assets/Custom/Resources/Square/" + TestVR.count + "_X.png";
        paths[1] = "Assets/Custom/Resources/Square/" + TestVR.count + "_Y.png";
        paths[2] = "Assets/Custom/Resources/Square/" + TestVR.count + "_Z.png";

        List<string> outFailedPaths = new List<string>();

        AssetDatabase.DeleteAssets(paths, outFailedPaths);

        if (outFailedPaths.Count > 0) {
            Debug.Log("Unable to delete some of the target items.");
        }

        if (TestVR.count > 0) {
            TestVR.count -=1;  
        }

        Save.SaveFile(TestVR.count);
        
    }
}
