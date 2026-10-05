Shader "Starfall/EnvironmentWater"
{
    Properties { _BaseColor("Water",Color)=(.01,.55,.65,1) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            float3 _StarfallWind; float _StarfallEnvironmentTime;
            CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            CBUFFER_END
            struct A { float4 positionOS:POSITION; };
            struct V { float4 positionCS:SV_POSITION; float3 world:TEXCOORD0; };
            float Phase(float2 p) {float speed=length(_StarfallWind.xz);float2 d=_StarfallWind.xz/max(speed,.001);return dot(p,d)*.7-_StarfallEnvironmentTime*sqrt(max(speed,0))*.7;}
            V Vert(A a){V o;float3 p=TransformObjectToWorld(a.positionOS.xyz);p.y+=sin(Phase(p.xz))*min(.15,length(_StarfallWind)*.008);o.world=p;o.positionCS=TransformWorldToHClip(p);return o;}
            half4 Frag(V i):SV_Target
            {
                float speed=length(_StarfallWind.xz);float2 d=_StarfallWind.xz/max(speed,.001);
                float slope=cos(Phase(i.world.xz))*.7*min(.15,speed*.008);
                float3 normal=normalize(float3(-d.x*slope,1,-d.y*slope));Light sun=GetMainLight();
                float sheen=pow(saturate(dot(normal,normalize(GetWorldSpaceNormalizeViewDir(i.world)+sun.direction))),80);
                float phase=Phase(i.world.xz);float filter=1-smoothstep(.5,2,fwidth(phase));
                return half4(_BaseColor.rgb*(.8+.08*sin(phase)*filter)+sheen*.2*filter,1);
            }
            ENDHLSL
        }
    }
}
