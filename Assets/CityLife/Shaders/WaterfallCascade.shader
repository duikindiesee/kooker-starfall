Shader "CityLife/WaterfallCascade"
{
    Properties
    {
        _DeepColor ("Deep Torrent", Color) = (0.03, 0.38, 0.52, 0.85)
        _ShallowColor ("Aerated Cyan", Color) = (0.08, 0.68, 0.76, 0.78)
        _FoamColor ("Whitewater Froth", Color) = (0.92, 0.97, 1.0, 0.95)
        _FlowSpeedMain ("Main Flow Speed", Float) = 3.6
        _FlowSpeedTurbulent ("Turbulent Flow Speed", Float) = 6.2
        _FoamAeration ("Foam Aeration", Range(0, 1)) = 0.32
        _EdgeSoftness ("Edge Softness Buffer", Float) = 0.8
        _IsPlungePool ("Is Plunge Pool Disc", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+50" }
        Pass
        {
            Name "WaterfallForward"
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

            CBUFFER_START(UnityPerMaterial)
                half4 _DeepColor, _ShallowColor, _FoamColor;
                float _FlowSpeedMain, _FlowSpeedTurbulent, _FoamAeration, _EdgeSoftness, _IsPlungePool;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float4 screenPos : TEXCOORD3;
                half fog : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 posWS = TransformObjectToWorld(input.positionOS.xyz);
                
                // Subtle turbulent surface flutter along normal
                float flutter = sin(posWS.y * 4.0 - _Time.y * 6.0 + posWS.x * 3.0) * 0.04;
                posWS += input.normalOS * flutter;

                output.positionWS = posWS;
                output.positionCS = TransformWorldToHClip(posWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                output.screenPos = ComputeScreenPos(output.positionCS);
                output.fog = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float t = _Time.y;
                float2 uv = input.uv;
                float3 normWS = normalize(input.normalWS);
                float3 viewDir = normalize(GetCameraPositionWS() - input.positionWS);

                half4 col;

                if (_IsPlungePool > 0.5)
                {
                    // Plunge pool churn disc: organic expanding turbulent froth and boiling ripples
                    float2 centeredUV = uv - float2(0.5, 0.5);
                    float dist = length(centeredUV) * 2.0; // 0 at center, 1 at edge
                    float2 p = centeredUV * 9.0;

                    // Multi-frequency chaotic boiling noise
                    float boil1 = sin(p.x * 2.7 + p.y * 2.1 - t * 4.2);
                    float boil2 = cos(p.x * -1.8 + p.y * 3.3 - t * 3.7);
                    float boil3 = sin((p.x + p.y) * 3.1 + t * 5.5);
                    float boil4 = cos((p.x - p.y) * 4.6 - t * 2.8);
                    float turbulentNoise = (boil1 + boil2 + boil3 + boil4) * 0.25;

                    // Asymmetric expanding shockwaves from the impact zone
                    float expandingWaves = sin(dist * 15.0 - t * 4.6 + turbulentNoise * 1.6);
                    float impactBoil = saturate(1.0 - dist * 1.15) * (turbulentNoise * 0.45 + 0.55);
                    float foam = saturate(impactBoil * 0.88 + expandingWaves * 0.22 * (1.0 - dist * 0.65));
                    foam = smoothstep(0.26, 0.72, foam);

                    half4 baseWater = lerp(_DeepColor, _ShallowColor, saturate(dist * 0.75));
                    col = lerp(baseWater, _FoamColor, foam * 0.78);

                    // Soft radial edge fade so plunge pool blends seamlessly into river
                    float radialAlpha = 1.0 - smoothstep(0.60, 0.96, dist);
                    col.a = saturate(lerp(0.50, 0.88, foam) * radialAlpha);
                }
                else
                {
                    // Cascading vertical waterfall curtain
                    // Fast downward scrolling multi-scale UVs
                    float2 flow1 = float2(uv.x * 3.0, uv.y * 2.5 - t * _FlowSpeedMain);
                    float2 flow2 = float2(uv.x * 5.5 + 0.2, uv.y * 4.0 - t * _FlowSpeedTurbulent);
                    float2 flow3 = float2(uv.x * 1.5 - 0.1, uv.y * 1.2 - t * (_FlowSpeedMain * 0.6));

                    // Procedural striated whitewater froth channels
                    float w1 = sin(flow1.x * 10.0 + sin(flow1.y * 5.0) * 1.5) * cos(flow1.y * 8.0);
                    float w2 = cos(flow2.x * 16.0 - sin(flow2.y * 8.0)) * sin(flow2.y * 12.0);
                    float w3 = sin(flow3.x * 6.0 + flow3.y * 4.0);
                    float turbulence = (w1 * 0.45 + w2 * 0.35 + w3 * 0.20);

                    // Aeration increases progressively toward the bottom plunge
                    float verticalAeration = saturate(uv.y * 0.55 + 0.15);
                    float foamMask = saturate(turbulence * 0.7 + _FoamAeration + verticalAeration * 0.35);
                    foamMask = smoothstep(0.42, 0.78, foamMask);

                    // Translucent rich turquoise water body with dynamic whitewater streaks
                    half4 waterBody = lerp(_DeepColor, _ShallowColor, saturate(uv.y * 0.4 + 0.2));
                    col = lerp(waterBody, _FoamColor, foamMask * 0.82);

                    // Soft lateral edge fade and crest lip fade to prevent hard geometric slice edges
                    float lateralFade = saturate(min(uv.x, 1.0 - uv.x) * 12.0);
                    float topFade = smoothstep(0.0, 0.04, uv.y);
                    col.a = saturate(lerp(0.68, 0.92, foamMask) * lateralFade * topFade);
                }

                // Main light directional illumination & sun glints
                Light mainLight = GetMainLight();
                float NdotL = saturate(dot(normWS, mainLight.direction) * 0.5 + 0.5);
                col.rgb *= (mainLight.color * NdotL + half3(0.25, 0.30, 0.38));

                // Specular sunlight highlights on water rapids
                float3 halfDir = normalize(viewDir + mainLight.direction);
                float spec = pow(saturate(dot(normWS, halfDir)), 24.0) * 0.45;
                col.rgb += mainLight.color * spec;

                // Soft scene depth intersection edge blending
                float2 screenUV = input.screenPos.xy / max(0.0001, input.screenPos.w);
                if (_CameraDepthTexture_TexelSize.z > 2 && _CameraDepthTexture_TexelSize.w > 2)
                {
                    float sceneDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                    float surfaceDepth = input.screenPos.w;
                    float depthDiff = sceneDepth - surfaceDepth;
                    if (depthDiff > 0.0)
                    {
                        float edgeFade = saturate(depthDiff / max(0.05, _EdgeSoftness));
                        col.a *= edgeFade;
                    }
                }

                col.rgb = MixFog(col.rgb, input.fog);
                return col;
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Unlit"
}
