Shader "CityLife/PH02FittedSupport"
{
    Properties
    {
        _BaseColor("Source tint",Color)=(1,1,1,1)
        _BaseMap("Source albedo",2D)="white"{}
        _BumpMap("Source OpenGL normal",2D)="bump"{}
        _BumpScale("Source normal scale",Float)=1
        _MetallicGlossMap("Metallic zero / smoothness alpha",2D)="white"{}
        _Smoothness("Source smoothness scale",Range(0,1))=1
        _PaleColor("Lower branch skin",Color)=(.52,.45,.31,1)
        _PaleSmoothness("Lower branch smoothness",Range(0,1))=.22
        _Cull("Cull",Float)=2
        _Cutoff("Unused opaque cutoff",Float)=.5
        _DiagnosticGray("Gray surface diagnostic",Float)=0
        _DiagnosticAlbedo("Albedo diagnostic",Float)=0
        _FoliageTintStrength("Blue-green foliage art tint",Range(0,1))=0
        _MatchTreeSkin("Use shared tree branch skin",Float)=0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "KokerboomBranchSkin.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST,_BaseColor,_PaleColor;
                float _BumpScale,_Smoothness,_PaleSmoothness,_Cull,_Cutoff,_DiagnosticGray,_DiagnosticAlbedo,_FoliageTintStrength,_MatchTreeSkin;
            CBUFFER_END
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
            TEXTURE2D(_MetallicGlossMap); SAMPLER(sampler_MetallicGlossMap);
            struct Attributes
            {
                float4 positionOS:POSITION; float3 normalOS:NORMAL; float4 tangentOS:TANGENT;
                float2 uv:TEXCOORD0; float4 color:COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0;
                float3 normalWS:TEXCOORD1; float4 tangentWS:TEXCOORD2;
                float2 uv:TEXCOORD3; float blend:TEXCOORD4; float fog:TEXCOORD5; float foliage:TEXCOORD6;
                float3 positionOS:TEXCOORD7;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            Varyings Vert(Attributes v)
            {
                Varyings o; UNITY_SETUP_INSTANCE_ID(v); UNITY_TRANSFER_INSTANCE_ID(v,o);
                VertexPositionInputs p=GetVertexPositionInputs(v.positionOS.xyz);
                VertexNormalInputs n=GetVertexNormalInputs(v.normalOS,v.tangentOS);
                o.positionCS=p.positionCS; o.positionWS=p.positionWS; o.positionOS=v.positionOS.xyz; o.normalWS=n.normalWS;
                o.tangentWS=float4(n.tangentWS,v.tangentOS.w*GetOddNegativeScale());
                o.uv=TRANSFORM_TEX(v.uv,_BaseMap); o.blend=saturate(v.color.r); o.foliage=saturate(v.color.g);
                o.fog=ComputeFogFactor(p.positionCS.z); return o;
            }
            half4 Frag(Varyings i):SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                // The compound crown/support uses this shader. Weight 0 at the cut rim preserves
                // the PH02 source atlas/basis; weight 1 reaches proposed pale branch skin.
                // UV extrapolation is experimental and requires textured underside review.
                float blend=saturate(i.blend);
                float3 source=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).rgb*_BaseColor.rgb;
                // Explicit art treatment, limited by caller-supplied geometric foliage
                // weights and green-biased albedo. Brown damage and support skin remain.
                // This heuristic is not a publisher semantic mask or measured biology.
                // A narrow green-channel threshold amplified texture compression
                // blocks into cyan squares. Use the geometric leaf weight for the
                // coating and smoothly retain only strongly brown damaged areas.
                float green=1-smoothstep(.075,.28,source.r-source.g);
                float luminance=dot(source,float3(.2126,.7152,.0722));
                // A diffuse glaucous coating lifts the very dark photographed leaf
                // albedo while preserving its tonal variation. No emission or extra
                // scene light is used; brown damage retains the source treatment.
                float3 blueGreen=float3(.035,.060,.062)+sqrt(saturate(luminance))*float3(.25,.52,.54);
                source=lerp(source,blueGreen,saturate(_FoliageTintStrength*i.foliage*green));
                float gloss=SAMPLE_TEXTURE2D(_MetallicGlossMap,sampler_MetallicGlossMap,i.uv).a*_Smoothness;
                float3 normalTS=UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,i.uv),_BumpScale);
                normalTS=normalize(lerp(normalTS,float3(0,0,1),blend));
                float3 geometricNormal=normalize(i.normalWS);
                float3 tangent=normalize(i.tangentWS.xyz);
                float3 bitangent=i.tangentWS.w*cross(geometricNormal,tangent);
                float3 n=NormalizeNormalPerPixel(TransformTangentToWorld(normalTS,half3x3(tangent,bitangent,geometricNormal)));
                float powder=sin(i.positionWS.x*173+i.positionWS.y*83)*sin(i.positionWS.z*127-i.positionWS.y*61);
                float3 pale=_PaleColor.rgb*(1+.018*powder);
                if(_MatchTreeSkin>.5)pale=KokerboomBranchSkin(i.positionOS);
                float3 albedo=lerp(source,pale,blend);
                gloss=lerp(gloss,_PaleSmoothness,blend);
                if(_DiagnosticGray>.5) { albedo=.32; gloss=.1; n=geometricNormal; }
                if(_DiagnosticAlbedo>.5)return half4(albedo,1);
                InputData input=(InputData)0;
                input.positionWS=i.positionWS; input.normalWS=n; input.viewDirectionWS=GetWorldSpaceNormalizeViewDir(i.positionWS);
                input.shadowCoord=TransformWorldToShadowCoord(i.positionWS); input.bakedGI=SampleSH(n);
                input.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(i.positionCS); input.shadowMask=1;
                SurfaceData surface=(SurfaceData)0;
                surface.albedo=albedo; surface.alpha=1; surface.metallic=0; surface.smoothness=gloss;
                surface.occlusion=1; surface.normalTS=normalTS;
                half4 color=UniversalFragmentPBR(input,surface); color.rgb=MixFog(color.rgb,i.fog); return color;
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
}
