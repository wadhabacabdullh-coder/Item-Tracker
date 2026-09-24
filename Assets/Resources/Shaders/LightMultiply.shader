// Darkness/lighting overlay: final = 2 * src * dst, so a texel of 0.5 leaves the scene unchanged,
// lower values darken and higher values brighten (lights can glow slightly above albedo).
Shader "ShadowContract/LightMultiply"
{
    Properties { _MainTex ("Light", 2D) = "gray" {} }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off ZWrite Off ZTest Always
        Blend DstColor SrcColor
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            v2f vert (appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; o.color = v.color; return o; }
            fixed4 frag (v2f i) : SV_Target { fixed4 c = tex2D(_MainTex, i.uv); c.rgb = lerp(fixed3(0.5,0.5,0.5), c.rgb, i.color.a); c.a = 1; return c; }
            ENDCG
        }
    }
}
