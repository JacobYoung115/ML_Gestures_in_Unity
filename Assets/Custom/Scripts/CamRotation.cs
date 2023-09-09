using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CamRotation : MonoBehaviour
{
    // Start is called before the first frame update
    public Transform X;
    public Transform Y;
    public Transform Z;

    public Transform face;

    [Range(0.1f, 4)]
    public float dist = 0.5f;


    [Range(-90, 90)]
    public float faceRotation = 0;

    float angleX;

    void Start()
    {
        X.SetParent(transform, true);
        Y.SetParent(transform, true);
        Z.SetParent(transform, true);  

        angleX = face.transform.rotation.eulerAngles.x;
        Debug.Log("Initial rotation x value: " + angleX);
    }

    // Update is called once per frame
    void Update()
    {
        //Solution
        //parent the planes to the center of mass
        //then give the center of mass the rotation value of the face.

        face.transform.rotation = Quaternion.Euler(angleX,faceRotation,0);

        X.transform.localPosition = new Vector3(-dist*2,0,0);
        Y.transform.localPosition = new Vector3(0,dist*2,0);
        Z.transform.localPosition = new Vector3(0,0,dist*2);

        X.transform.localScale = new Vector3(dist * 0.5f, dist * 0.5f, dist * 0.5f);
        Y.transform.localScale = new Vector3(dist * 0.5f, dist * 0.5f, dist * 0.5f);
        Z.transform.localScale = new Vector3(dist * 0.5f, dist * 0.5f, dist * 0.5f);

        transform.rotation = face.transform.rotation;


        //X.transform.localRotation = face.transform.rotation;
        //Y.transform.localRotation = face.transform.rotation;
        //Z.transform.localRotation = face.transform.rotation;



    }
}
