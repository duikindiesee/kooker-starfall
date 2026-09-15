Shader "CityLife/CoastalRocks"
{
    Properties
    {
        _BaseColor("Iron-brown stone tint",Color)=(.18,.135,.11,1)
        _BaseMap("Base",2D)="white"{}
        _Vegetation("Succulent vertex colour",Float)=0
        _Cutoff("Cutoff",Float)=.5
    }
    SubShader
    {
        Tags {"RenderType"="Opaque" "RenderPipeline"="UniversalPipeline"}
        Pass
        {
            Name "ForwardLit"
            Tags {"LightMode"="UniversalForward"}
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor, _BaseMap_ST;
                float _Vegetation, _Cutoff;
            CBUFFER_END
            struct Attributes {float4 positionOS:POSITION;float3 normalOS:NORMAL;float4 color:COLOR;};
            struct Varyings {float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;float3 normalWS:TEXCOORD1;float4 color:COLOR;float fog:TEXCOORD2;};
            float Hash(float3 p){p=frac(p*.1031);p+=dot(p,p.yzx+33.33);return frac((p.x+p.y)*p.z);}
            float Noise(float3 p)
            {
                float3 i=floor(p), f=frac(p);f=f*f*(3-2*f);
                return lerp(lerp(lerp(Hash(i),Hash(i+float3(1,0,0)),f.x),lerp(Hash(i+float3(0,1,0)),Hash(i+float3(1,1,0)),f.x),f.y),
                    lerp(lerp(Hash(i+float3(0,0,1)),Hash(i+float3(1,0,1)),f.x),lerp(Hash(i+float3(0,1,1)),Hash(i+1),f.x),f.y),f.z);
            }
            float CausticNetwork(float2 p,float t)
            {
                p*=2.35;
                float2 warp=float2(sin(p.y*.73+t*.67),cos(p.x*.61-t*.53))*.31;
                float a=sin((p.x+warp.x)*1.73+t*1.13),b=sin((p.y+warp.y)*2.07-t*.91);
                float c=sin((p.x+p.y+warp.x-warp.y)*1.19+t*.57);
                float field=min(abs(a+b+c*.82),abs(a*.73-b+c));
                float pixelAA=max(.004,fwidth(field)*.42);
                float networkLine=1-smoothstep(.010-pixelAA,.010+pixelAA,field);
                float patch=smoothstep(.46,.73,Noise(float3(p.x*.16,t*.045,p.y*.16)+83));
                return networkLine*lerp(.12,1,patch);
            }
            float Surface(float3 p)
            {
                float warp=Noise(p*.61)*.17;
                float strata=p.y*3.7+p.x*.10+p.z*.065+warp;
                float layer=abs(frac(strata)-.5);
                float grooves=1-smoothstep(.008,.044,layer);
                float fine=Noise(p*23)*.14+Noise(p*61)*.045;
                return fine-grooves*.10+Noise(p*3.1)*.18;
            }
            Varyings Vert(Attributes v)
            {
                Varyings o;VertexPositionInputs p=GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS=p.positionCS;o.positionWS=p.positionWS;o.normalWS=TransformObjectToWorldNormal(v.normalOS);
                o.color=v.color;o.fog=ComputeFogFactor(p.positionCS.z);return o;
            }
            half4 Frag(Varyings i):SV_Target
            {
                float3 n=normalize(i.normalWS), p=i.positionWS;
                float3 albedo;float smoothness;
                float causticSubmerged=1-smoothstep(-2.16,-2.03,p.y);
                if(_Vegetation>.5)
                {
                    albedo=i.color.rgb*(.96+.08*Noise(p*36));smoothness=.30;
                }
                else
                {
                    float broad=Noise(p*.54),grain=Noise(p*17);
                    float strata=p.y*3.7+p.x*.10+p.z*.065+Noise(p*.61)*.17;
                    float lamina=Noise(float3(p.x*.08,floor(strata)*1.71,p.z*.08));
                    float seam=1-smoothstep(.01,.055,abs(frac(strata)-.5));
                    float fracture=abs(sin(p.x*1.8+p.z*1.13+Noise(p*.8)*1.4));
                    float crack=(1-smoothstep(.009,.035,fracture))*smoothstep(.25,.70,Noise(p*1.4));
                    albedo=_BaseColor.rgb*i.color.rgb*lerp(.72,1.16,broad)*lerp(.88,1.08,lamina)*(1-seam*.21-crack*.32);
                    albedo*=lerp(.91,1.06,grain);
                    // Dust settles on ledges; the immersed base becomes darker with a cool mineral stain.
                    float dust=saturate(n.y)*smoothstep(.25,.74,broad);
                    albedo=lerp(albedo,float3(.29,.235,.18),dust*.22);
                    float wet=1-smoothstep(-2.10,-1.58,p.y);
                    // A neutral dark wet stone is filtered once by the water
                    // volume; pre-cyan staining here erased rock colour twice.
                    albedo*=lerp(float3(1,1,1),float3(.68,.68,.72),wet*.85);
                    // Coherent animated underwater caustics affect submerged stone only.
                    smoothness=lerp(.12,.31,wet);
                    float h=Surface(p);
                    float3 dx=ddx(p),dy=ddy(p),r1=cross(dy,n),r2=cross(n,dx);
                    float det=dot(dx,r1);float3 grad=sign(det)*(ddx(h)*r1+ddy(h)*r2);
                    n=normalize(max(abs(det),1e-7)*n-grad*.019);
                }
                InputData input=(InputData)0;input.positionWS=p;input.normalWS=n;
                input.viewDirectionWS=GetWorldSpaceNormalizeViewDir(p);input.shadowCoord=TransformWorldToShadowCoord(p);
                input.bakedGI=SampleSH(n);input.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(i.positionCS);input.shadowMask=1;
                SurfaceData surface=(SurfaceData)0;surface.albedo=albedo;surface.alpha=1;surface.smoothness=smoothness;
                surface.occlusion=1;surface.normalTS=float3(0,0,1);
                half4 color=UniversalFragmentPBR(input,surface);
                Light sun=GetMainLight(input.shadowCoord);
                float sunEnergy=min(1.4,max(sun.color.r,max(sun.color.g,sun.color.b)));
                float causticLight=CausticNetwork(p.xz,_Time.y)*causticSubmerged*exp(-max(0,-2-p.y)*.36)*
                    sunEnergy*saturate(dot(n,sun.direction))*sun.shadowAttenuation;
                color.rgb+=half3(.20,.33,.31)*causticLight;
                color.rgb=MixFog(color.rgb,i.fog);return color;
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
}
