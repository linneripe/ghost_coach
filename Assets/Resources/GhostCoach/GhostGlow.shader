// See-through coach body with a light rim.  The stencil lets each pixel be
// colored once, so limbs that overlap do not turn darker.
Shader "GhostCoach/GhostGlow" {
    Properties {
        _Color ("Color", Color) = (0.45, 0.85, 1, 0.35)
        _Rim ("Rim color", Color) = (0.85, 0.97, 1, 1)
        _RimPower ("Rim power", Range(0.5, 6)) = 2.2
        _RimStrength ("Rim strength", Range(0, 1)) = 0.75
    }
    SubShader {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            Stencil { Ref 3 Comp NotEqual Pass Replace }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            fixed4 _Color, _Rim;
            half _RimPower, _RimStrength;

            struct appdata {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct v2f {
                float4 pos : SV_POSITION;
                float3 normal : TEXCOORD0;
                float3 world : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v) {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target {
                float3 n = normalize(i.normal);
                float3 view = normalize(UnityWorldSpaceViewDir(i.world));
                float rim = pow(1 - saturate(dot(n, view)), _RimPower);
                fixed4 c = _Color;
                c.rgb = lerp(c.rgb, _Rim.rgb, saturate(rim * _RimStrength * 1.4));
                c.a = saturate(c.a + rim * _RimStrength * 0.5);
                return c;
            }
            ENDCG
        }
    }
    Fallback "Transparent/Diffuse"
}
