Shader "Hidden/RoofTopStudio/SimpleMeshEditor/VertexColorPreview"
{
    Properties
    {
        _ChannelMask ("Channel Mask", Vector) = (1, 1, 1, 0)
        _AlphaOnly ("Alpha Only", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Cull Off
        ZWrite On
        ZTest LEqual

        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            float4 _ChannelMask;
            float _AlphaOnly;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 normal : TEXCOORD0;
                float4 color : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                if (_AlphaOnly > 0.5)
                {
                    return fixed4(i.color.a, i.color.a, i.color.a, 1);
                }

                float3 rgb = i.color.rgb * _ChannelMask.rgb;
                return fixed4(rgb, 1);
            }
            ENDCG
        }
    }
}
