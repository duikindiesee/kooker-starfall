Shader "CityLife/IslandOcean"
{
    Properties
    {
        _HeightMap ("Immutable terrain heights", 2D) = "black" {}
        _WorldWidth ("Island width", Float) = 4096
        _SeaLevel ("Sea level", Float) = 0
        _Night ("Night", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry+10" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_HeightMap); SAMPLER(sampler_HeightMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _HeightMap_ST, _HeightMap_TexelSize;
                float _WorldWidth, _SeaLevel, _Night;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; half fog:TEXCOORD1; };
            // Source: src/colony/render/oceanWaves.ts. Frequencies, amplitudes and time scale
            // are retained exactly; Unity X/Z corresponds to the browser ring's local X/Y.
            float3 Phases(float2 p) { float t=_Time.y*.5; return float3(p.x*.05+t*.85,p.y*.063-t*.7,(p.x+p.y)*.028+t*1.25); }
            Varyings Vert(Attributes v)
            {
                Varyings o; float3 p=TransformObjectToWorld(v.positionOS.xyz);
                p.y += dot(sin(Phases(p.xz)),float3(.18,.14,.09));
                o.positionWS=p; o.positionCS=TransformWorldToHClip(p); o.fog=ComputeFogFactor(o.positionCS.z); return o;
            }
            half4 Frag(Varyings i):SV_Target
            {
                float2 uv=i.positionWS.xz/_WorldWidth+.5;
                // Height texels represent vertices, including both boundary rows. Place grid
                // vertex coordinates at texel centres instead of shifting by up to half a cell.
                float2 heightUV=uv*(1-_HeightMap_TexelSize.xy)+.5*_HeightMap_TexelSize.xy;
                float height=SAMPLE_TEXTURE2D(_HeightMap,sampler_HeightMap,heightUV).r;
                float inWorld=step(0,uv.x)*step(uv.x,1)*step(0,uv.y)*step(uv.y,1);
                float depth=lerp(80,max(0,_SeaLevel-height),inWorld);
                half3 shallow=half3(.075,.59,.56), deep=half3(.025,.21,.33);
                half3 water=lerp(shallow,deep,saturate(depth/48));
                water=lerp(water,water*half3(.31,.39,.62),_Night);
                float3 phase=Phases(i.positionWS.xz);
                // Fade subpixel wave normals analytically instead of letting distant sine
                // fields fold into the conspicuous moire seen in the overview capture.
                float3 waveFilter=1-smoothstep(.65,2.4,abs(ddx(phase))+abs(ddy(phase)));
                float3 c=cos(phase)*waveFilter;
                float nx=c.x*.18*.05+c.z*.09*.028;
                float nz=c.y*.14*.063+c.z*.09*.028;
                // Fine wind ripples affect lighting on the GPU, independent of mesh resolution.
                float windX=i.positionWS.x*.61+i.positionWS.z*.39+_Time.y;
                float windZ=i.positionWS.z*.75-i.positionWS.x*.23+_Time.y*.8;
                nx+=sin(windX)*.027*(1-smoothstep(.65,2.4,fwidth(windX)));
                nz+=cos(windZ)*.019*(1-smoothstep(.65,2.4,fwidth(windZ)));
                half3 n=normalize(float3(-nx,1,-nz));
                half3 v=normalize(_WorldSpaceCameraPos-i.positionWS);
                Light sun=GetMainLight();
                half fresnel=pow(1-saturate(dot(n,v)),4);
                water=lerp(water,lerp(half3(.46,.71,.77),half3(.09,.14,.26),_Night),fresnel*.57);
                float shine=pow(saturate(dot(n,normalize(v+sun.direction))),160);
                water+=sun.color*shine*.58;
                float pulse=sin(depth*2.1-_Time.y*1.15+sin(i.positionWS.x*.07+i.positionWS.z*.05)*.55);
                float foam=(1-smoothstep(.35,3.1,depth))*smoothstep(.31,.77,pulse);
                water=lerp(water,half3(.79,.93,.88)*(1-_Night*.68),foam*.63);
                return half4(MixFog(water,i.fog),1);
            }
            ENDHLSL
        }
    }
}
