Shader "CityLife/CoastalWater"
{
    Properties
    {
        _ShallowColor ("Luminous turquoise shallows", Color) = (.008,.83,.75,1)
        _RiverColor ("Turquoise channel", Color) = (.004,.64,.72,1)
        _DeepColor ("Deep blue sea", Color) = (.006,.12,.34,1)
        _SkyReflection ("Navy sky reflection", Color) = (.035,.085,.19,1)
        _FoamColor ("Fine shore edge", Color) = (.40,.85,.74,1)
        _WaterLevel ("World water level", Float) = -2
        _UseSceneDepth ("Use available camera depth", Range(0,1)) = 1
        _WaveStrength ("Wave strength", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Name "CoastalWaterForward"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor, _RiverColor, _DeepColor, _SkyReflection, _FoamColor;
                float _WaterLevel, _UseSceneDepth, _WaveStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half fog : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float3 _StarfallWind;
            float _StarfallEnvironmentTime, _StarfallIntegratedWeather;
            float3 WavePhase(float2 p)
            {
                float t = _Time.y;
                if (_StarfallIntegratedWeather > .5)
                {
                    float speed = length(_StarfallWind.xz);
                    float2 direction = speed > .001 ? _StarfallWind.xz / speed : float2(1,0);
                    p = float2(dot(p,direction), dot(p,float2(-direction.y,direction.x)));
                    t = _StarfallEnvironmentTime * max(.3,sqrt(speed)*.5);
                }
                return float3(dot(p,float2(.31,.19)) + t*.62,
                    dot(p,float2(-.16,.37)) - t*.47,
                    dot(p,float2(.09,.12)) + t*.29);
            }
            float SeaBlend(float z) { return smoothstep(50,95,z); }

            Varyings Vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input,o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 p = TransformObjectToWorld(input.positionOS.xyz);
                float strength = lerp(.42,1,SeaBlend(p.z)) * saturate(_WaveStrength);
                p.y += dot(sin(WavePhase(p.xz)),float3(.045,.034,.032)) * strength;
                o.positionWS = p;
                o.positionCS = TransformWorldToHClip(p);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            float WaterDepth(Varyings input, out float measured)
            {
                // The fallback is an explicit art direction for this bounded river/estuary,
                // not a claimed terrain measurement. Camera depth replaces it when available.
                float depth = lerp(2.3,25,SeaBlend(input.positionWS.z));
                measured = 0;
                if (_UseSceneDepth > .5 && _CameraDepthTexture_TexelSize.z > 2 && _CameraDepthTexture_TexelSize.w > 2)
                {
                    float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                    float raw = SampleSceneDepth(screenUV);
                    #if UNITY_REVERSED_Z
                        bool surfaceHit = raw > .000001;
                    #else
                        bool surfaceHit = raw < .999999;
                        raw = lerp(UNITY_NEAR_CLIP_VALUE,1,raw);
                    #endif
                    if (surfaceHit)
                    {
                        float3 sceneWS = ComputeWorldSpacePosition(screenUV,raw,UNITY_MATRIX_I_VP);
                        // Ignore foreground depth and invalid projections. Opaque land in front
                        // already rejects the water through ZTest; a submerged bed gives depth.
                        float difference = input.positionWS.y - sceneWS.y;
                        if (difference >= -.08 && difference < 250)
                        {
                            depth = max(0,difference);
                            measured = 1;
                        }
                    }
                }
                return depth;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float measured;
                float depth = WaterDepth(input,measured);
                float river = 1-exp(-depth*.30);
                float deep = smoothstep(4,24,depth);
                half3 water = lerp(_ShallowColor.rgb,_RiverColor.rgb,river);
                water = lerp(water,_DeepColor.rgb,deep);
                // R01's unfiltered alpha blend transmitted the strongly lit orange bed, turning
                // turquoise gray/green. Preserve shallow detail through colour-filtered scene
                // transmission instead. Increasing depth absorbs the bed; sky never acts as bed.
                if (measured > .5 && _CameraOpaqueTexture_TexelSize.z > 2 && _CameraOpaqueTexture_TexelSize.w > 2)
                {
                    half3 bed = SampleSceneColor(GetNormalizedScreenSpaceUV(input.positionCS));
                    half3 filteredBed = min(bed,half3(1.5,1.5,1.5))*half3(.12,.86,1.0);
                    // Clear turquoise shallows reveal the real bed and submerged props;
                    // depth attenuation naturally closes that window toward the sea.
                    float transmission = .90*exp(-depth*.18);
                    water = lerp(water,filteredBed,transmission);
                }

                float strength = lerp(.42,1,SeaBlend(input.positionWS.z)) * saturate(_WaveStrength);
                float3 phase = WavePhase(input.positionWS.xz);
                // Derivative filtering prevents fine wave fields shimmering into distant stripes.
                float3 attenuation = 1-smoothstep(.6,2.1,abs(ddx(phase))+abs(ddy(phase)));
                float3 c = cos(phase)*attenuation;
                float nx = (c.x*.045*.31 + c.y*.034*-.16 + c.z*.032*.09)*strength;
                float nz = (c.x*.045*.19 + c.y*.034*.37 + c.z*.032*.12)*strength;
                float ripple = dot(input.positionWS.xz,float2(2.2,1.4)) + _Time.y*.85;
                float rippleFilter = 1-smoothstep(.6,2.0,fwidth(ripple));
                nx += sin(ripple)*.033*rippleFilter*_WaveStrength;
                nz += cos(ripple*.83)*.024*rippleFilter*_WaveStrength;
                half3 normalWS = normalize(float3(-nx,1,-nz));
                half3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float fresnel = pow(1-saturate(dot(normalWS,view)),4);
                // Keep turquoise readable in the intended navy lighting. This is a deliberately
                // luminous art surface, not a physical ocean/sky reflection simulation.
                water = lerp(water,_SkyReflection.rgb,fresnel*.28);
                Light sun = GetMainLight();
                float glint = pow(saturate(dot(normalWS,normalize(view+sun.direction))),190);
                // A small neutral/cool glint keeps the warm key from bleaching the whole colour.
                float sunStrength = min(1.5,max(sun.color.r,max(sun.color.g,sun.color.b)));
                water += half3(.55,.85,.95)*sunStrength*glint*.24;
                float glimmer = sin(phase.x+phase.y*.47)*cos(phase.z-phase.y*.24);
                float fineCrest = smoothstep(.72,.98,sin(ripple+sin(phase.y)*.8))*rippleFilter;
                fineCrest *= .5+.5*cos(phase.x-phase.z);
                water += _ShallowColor.rgb*(glimmer*.055+fineCrest*.085)*(1-deep*.65);

                // Thin intermittent contact edge only when depth is measured, never a false
                // white line generated from the fallback colour gradient.
                float shore = (1-smoothstep(.04,.36,depth))*measured;
                float pulse = .45+.55*smoothstep(-.5,.65,sin(phase.x*2.1-phase.y*.4));
                water = lerp(water,_FoamColor.rgb,shore*pulse*.68);
                // The opaque scene was already transmitted above. Do not add the warm bed a
                // second time through ordinary alpha; retain only a narrow actual contact fade.
                float shallowAlpha=lerp(.62,1,smoothstep(.35,9,depth));
                float alpha=lerp(1,smoothstep(0,.045,depth)*shallowAlpha,measured);
                water = MixFog(water,input.fog);
                return half4(water,alpha);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Unlit"
}
