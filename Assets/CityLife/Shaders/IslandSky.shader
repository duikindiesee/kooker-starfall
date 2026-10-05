Shader "CityLife/IslandSky"
{
    Properties { _Night ("Night", Range(0,1))=0 _SunDirection ("Sun direction",Vector)=(.3,.5,.7,0) }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial) float _Night; float4 _SunDirection; CBUFFER_END
            struct Attributes { float4 vertex:POSITION; };
            struct Varyings { float4 position:SV_POSITION; float3 direction:TEXCOORD0; };
            Varyings Vert(Attributes v) { Varyings o; o.position=TransformObjectToHClip(v.vertex.xyz); o.direction=v.vertex.xyz; return o; }
            float Hash(float3 p) { p=frac(p*.1031); p+=dot(p,p.yzx+33.33); return frac((p.x+p.y)*p.z); }
            half4 Frag(Varyings i):SV_Target
            {
                float3 d=normalize(i.direction);
                float altitude=pow(saturate(d.y),.52);
                half3 day=lerp(half3(.65,.77,.79),half3(.15,.39,.57),altitude);
                float glow=pow(saturate(dot(d,normalize(_SunDirection.xyz))),32);
                day+=half3(.24,.14,.045)*glow;
                float disc=smoothstep(.99972,.99985,dot(d,normalize(_SunDirection.xyz)));
                day+=half3(1,.82,.54)*disc*2;
                half3 night=lerp(half3(.07,.105,.19),half3(.009,.014,.036),altitude);
                float3 cell=floor(d*930);
                float star=step(.9985,Hash(cell))*pow(saturate(d.y+.2),.7);
                night+=star*half3(.61,.74,1)*(.5+Hash(cell+12)*.5);
                half3 sky=lerp(day,night,_Night);
                // The blue gas giant is part of CityLife's existing celestial identity. It is a
                // directional sky feature, never a second playable planet or a generated world.
                float3 centre=normalize(float3(-.52,.31,-.84));
                float3 tangent=normalize(cross(float3(0,1,0),centre));
                float3 up=cross(centre,tangent);
                float2 p=float2(dot(d,tangent),dot(d,up))/.14;
                float radius=length(p);
                float forward=step(.95,dot(d,centre));
                float mask=(1-smoothstep(.985,1,radius))*forward;
                float stripe=sin(p.y*30+sin(p.x*7)*.27)*.5+.5;
                half3 planet=lerp(half3(.15,.23,.40),half3(.34,.47,.64),stripe*.48+.17);
                float light=saturate((-.55*p.x+.4*sqrt(saturate(1-radius*radius))+.19)*1.4);
                planet*=.3+light*.9;
                sky=lerp(sky,planet,mask*lerp(.42,.94,_Night));
                float halo=exp(-abs(radius-1)*48)*forward;
                sky+=half3(.11,.21,.37)*halo*lerp(.07,.32,_Night);
                return half4(sky,1);
            }
            ENDHLSL
        }
    }
}
