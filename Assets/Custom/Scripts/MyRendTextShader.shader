Shader "Custom/MyRendTextShader"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _Tex("InputTex", 2D) = "white" {}
    }

    SubShader
    {
        Lighting Off
        Blend One Zero

        Pass
        {
            CGPROGRAM
            #include "UnityCustomRenderTexture.cginc"

            #pragma vertex InitCustomRenderTextureVertexShader
            #pragma fragment frag
            #pragma target 3.0

            float4      _Color;
            sampler2D   _Tex;

            float4 frag(v2f_init_customrendertexture IN) : COLOR
            {
                fixed4 txtrColor = tex2D(_Tex, IN.texcoord.xy);
                //return _Color * txtrColor;

                if (txtrColor.r < 1.0) {
                    txtrColor.rgb = 0.0;
                    return _Color * txtrColor;
                }
                else {
                    txtrColor.rgb = 1.0;
                    return _Color * txtrColor;
                }

                
            }
            ENDCG
        }
    }
}