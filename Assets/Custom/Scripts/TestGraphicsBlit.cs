using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;

//references:
//https://discussions.unity.com/t/can-a-texture2d-be-created-at-runtime-from-a-snapshot-of-a-rendertexture/38227
//https://discussions.unity.com/t/is-it-possible-to-save-rendertextures-into-png-files-for-later-use/11221
//https://docs.unity3d.com/ScriptReference/ImageConversion.EncodeToPNG.html
//https://stackoverflow.com/questions/44264468/convert-rendertexture-to-texture2d
//https://docs.unity3d.com/ScriptReference/Material.SetTexture.html
//https://discussions.unity.com/t/getpixels-of-rendertexture/7714/2
//https://forum.unity.com/threads/save-texture-generated-by-a-shader.326561/
//https://forum.unity.com/threads/export-result-of-shader.452767/
//https://docs.unity3d.com/ScriptReference/Graphics.Blit.html

public class TestGraphicsBlit : MonoBehaviour
{
    public Texture aTexture;
    public RenderTexture rTex;
    public Material mat;
    // = new Material(Shader.Find("Custom/MyRendTextShader"));

    // Start is called before the first frame update
    void Start()
    {
        if (!aTexture || !rTex) {
            Debug.LogError("A texture or a render texture are missing, assign them.");
        }

    }

    // Update is called once per frame
    void Update()
    {
        //passes aTexture to the shader within mat and sets that value to rTex.
        //Don't set source (aTexture) & dest (rTex) to the same render texture. 
        if (Input.GetKeyDown(KeyCode.Space)) {
            Debug.Log("Setting texture of cube to rendered texture.");
            Graphics.Blit(aTexture, rTex, mat);
            GetComponent<Renderer>().material.SetTexture("_MainTex", rTex);


            //Note, a render texture CANNOT be converted to a texture2d, therefore it is necessary to create a new one
            //then, use the Texture2D to read pixels from the active RenderTexture (since we used blit, rTex is the active RT).
            //Write out the image.
            Texture2D tex = new Texture2D(rTex.width, rTex.height, TextureFormat.RGB24, false);

            //Read the screen contents into the texture
            tex.ReadPixels(new Rect(0, 0, rTex.width, rTex.height), 0, 0);
            tex.Apply();

            //Encode texture into PNG
            byte[] bytes = ImageConversion.EncodeToPNG(tex);
            Object.Destroy(tex);

            //Write to a file in project folder
            File.WriteAllBytes(Application.dataPath + "/Custom/Resources/SavedRenderTex.png", bytes);
        }
        
    }
}
