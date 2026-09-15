Shader "CityLife/CoastalTerrain"
{
    Properties
    {
        _BaseColor("Overall tint",Color)=(1,1,1,1)
        _Sand("Warm sand",Color)=(0.43,0.31,0.20,1)
        _Ochre("Ochre stone",Color)=(0.41,0.24,0.135,1)
        _Pale("Pale sandstone strata",Color)=(0.57,0.40,0.27,1)
        _Rust("Terracotta weathering",Color)=(0.29,0.17,0.12,1)
        _SeaLevel("Water elevation",Float)=-2
        _BaseMap("Shadow caster base",2D)="white"{}
        _Cutoff("Cutoff",Range(0,1))=.5
        _Cull("Cull",Float)=2
    }
    SubShader
    {
        Tags {"RenderType"="Opaque" "RenderPipeline"="UniversalPipeline"}
        Pass
        {
            Name "ForwardLit"
            Tags {"LightMode"="UniversalForward"}
            Cull Back
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
                float4 _BaseColor,_Sand,_Ochre,_Pale,_Rust,_BaseMap_ST;
                float _SeaLevel,_Cutoff,_Cull;
            CBUFFER_END
            struct Attributes {float4 positionOS:POSITION;float3 normalOS:NORMAL;};
            struct Varyings {float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;float3 normalWS:TEXCOORD1;float fog:TEXCOORD2;};
            float Hash(float3 p){p=frac(p*.1031);p+=dot(p,p.yzx+33.33);return frac((p.x+p.y)*p.z);}
            float Noise(float3 p)
            {
                float3 i=floor(p),f=frac(p);f=f*f*(3-2*f);
                return lerp(lerp(lerp(Hash(i),Hash(i+float3(1,0,0)),f.x),lerp(Hash(i+float3(0,1,0)),Hash(i+float3(1,1,0)),f.x),f.y),
                    lerp(lerp(Hash(i+float3(0,0,1)),Hash(i+float3(1,0,1)),f.x),lerp(Hash(i+float3(0,1,1)),Hash(i+1),f.x),f.y),f.z);
            }
            float CausticNetwork(float2 p,float t)
            {
                // Metre-scale warped cell boundaries: small enough to read as focused
                // refracted light, rather than the multi-metre contour loops seen in R148.
                p*=2.35;
                float2 warp=float2(sin(p.y*.73+t*.67),cos(p.x*.61-t*.53))*.31;
                float a=sin((p.x+warp.x)*1.73+t*1.13);
                float b=sin((p.y+warp.y)*2.07-t*.91);
                float c=sin((p.x+p.y+warp.x-warp.y)*1.19+t*.57);
                float field=min(abs(a+b+c*.82),abs(a*.73-b+c));
                float pixelAA=max(.004,fwidth(field)*.42);
                float networkLine=1-smoothstep(.010-pixelAA,.010+pixelAA,field);
                float patch=smoothstep(.46,.73,Noise(float3(p.x*.16,t*.045,p.y*.16)+83));
                return networkLine*lerp(.12,1,patch);
            }
            Varyings Vert(Attributes input)
            {
                Varyings o;o.positionWS=TransformObjectToWorld(input.positionOS.xyz);o.positionCS=TransformWorldToHClip(o.positionWS);
                o.normalWS=TransformObjectToWorldNormal(input.normalOS);o.fog=ComputeFogFactor(o.positionCS.z);return o;
            }
            half4 Frag(Varyings i):SV_Target
            {
                float3 p=i.positionWS,n=normalize(i.normalWS);
                float broad=Noise(p*.045),weather=Noise(p*.095+17),grain=Noise(p*.72);
                // Metre-scale, laterally interrupted layers replace the evenly repeated
                // bright rings. Broad mineral variation does most of the colour work.
                float strata=p.y*.18+(broad-.5)*.85;
                float layer=Noise(float3(p.x*.07,strata,p.z*.065)+31);
                float seamDistance=abs(frac(strata*.69)-.46);
                float seam=1-smoothstep(.025,.08,seamDistance);
                seam*=smoothstep(.53,.76,weather);
                float cliff=smoothstep(.10,.60,1-saturate(n.y));
                float high=smoothstep(3,16,p.y);
                float3 rock=lerp(_Ochre.rgb,_Pale.rgb,saturate(.20+layer*.48+seam*.06));
                rock=lerp(rock,_Rust.rgb,smoothstep(.47,.81,broad)*.23);
                // Dark recessed seams and warm ledge caps make metre-scale strata
                // readable at player distance without displacing collision geometry.
                float ledgeBand=1-smoothstep(.035,.12,abs(frac(p.y*.115+weather*.08)-.5));
                float ledgeTop=saturate(n.y)*ledgeBand;
                rock*=1-seam*cliff*.24;
                rock=lerp(rock,_Pale.rgb,ledgeTop*.20);
                float crackField=Noise(p*float3(.16,.045,.16)+59);
                float crackWidth=max(.018,fwidth(crackField)*1.1);
                float cracks=(1-smoothstep(crackWidth,crackWidth+.045,abs(crackField-.50)))*smoothstep(.30,.65,weather)*cliff;
                rock*=1-cracks*.24;
                float3 sand=_Sand.rgb*lerp(.91,1.07,broad);
                float3 albedo=lerp(sand,rock,saturate(cliff*.88+high*.40));
                // Grain is filtered toward its mean at distance; no sparkling screen-space noise.
                float fineVisibility=1-saturate(length(fwidth(p))*2);
                albedo*=1+(grain-.5)*.035*fineVisibility;
                float damp=1-smoothstep(_SeaLevel-.2,_SeaLevel+1.0,p.y);
                albedo*=lerp(1,.73,damp);
                // Moving refracted light belongs only to the submerged bed. The
                // cutoff stays below the authored wave trough, so dry sand cannot glow.
                float submerged=1-smoothstep(_SeaLevel-.16,_SeaLevel-.03,p.y);
                // The riverbed needs readable material variation beneath clear water.
                // Broad mineral patches and smaller gravel variation are world-space and
                // remain attached to the actual collision terrain as the camera moves.
                float bedPatch=Noise(float3(p.x*.17,19,p.z*.17));
                float bedGravel=Noise(float3(p.x*.83,47,p.z*.83));
                float3 submergedBed=lerp(float3(.16,.205,.17),float3(.36,.275,.17),bedPatch);
                submergedBed*=lerp(.78,1.12,bedGravel);
                albedo=lerp(albedo,submergedBed,submerged*.62);
                float causticLines=CausticNetwork(p.xz,_Time.y);
                float opticalDepth=max(0,_SeaLevel-p.y);
                // Centimetre-scale weathering relief affects light, not the collider.
                float relief=((weather-.5)*.028+(grain-.5)*.004-cracks*.017)*cliff;
                float3 dpdx=ddx(p),dpdy=ddy(p),r1=cross(dpdy,n),r2=cross(n,dpdx);
                float determinant=dot(dpdx,r1);
                if(abs(determinant)>1e-9)
                    n=normalize(abs(determinant)*n-sign(determinant)*(ddx(relief)*r1+ddy(relief)*r2));
                InputData input=(InputData)0;input.positionWS=p;input.normalWS=n;
                input.viewDirectionWS=GetWorldSpaceNormalizeViewDir(p);input.shadowCoord=TransformWorldToShadowCoord(p);
                input.bakedGI=SampleSH(n);input.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(i.positionCS);input.shadowMask=1;
                SurfaceData surface=(SurfaceData)0;surface.albedo=albedo*_BaseColor.rgb;surface.alpha=1;
                surface.smoothness=lerp(.12,.22,damp);surface.metallic=0;surface.occlusion=1;surface.normalTS=float3(0,0,1);
                half4 color=UniversalFragmentPBR(input,surface);
                // Refracted sun is concentrated light, not another brown/green bed
                // pigment. Add it after PBR shading, only to truly submerged upward
                // facing terrain, and attenuate with measured physical water depth.
                Light sun=GetMainLight();
                float sunEnergy=min(1.4,max(sun.color.r,max(sun.color.g,sun.color.b)));
                float sunFacing=saturate(dot(n,sun.direction))*.55+.45;
                float causticLight=causticLines*submerged*exp(-opticalDepth*.36)*sunEnergy*sunFacing;
                color.rgb+=half3(.20,.33,.31)*causticLight;
                color.rgb=MixFog(color.rgb,i.fog);return color;
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
}
