Shader "CityLife/IslandTerrain"
{
    Properties
    {
        _BaseColor ("Tint", Color) = (1,1,1,1)
        _Emission ("Emission", Range(0,2)) = 0
        _FadeStart ("Distance fade start", Float) = 50000
        _FadeEnd ("Distance fade end", Float) = 60000
        _SeaLevel ("Sea level", Float) = -1000
        _GroundAlbedo ("Ground detail albedo", 2D) = "gray" {}
        _GroundTextureStrength ("Ground detail strength", Range(0,1)) = 0
        _GroundTiling ("Ground repeats per metre", Float) = .333333
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "CityLifeForward"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_GroundAlbedo); SAMPLER(sampler_GroundAlbedo);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _GroundAlbedo_ST;
                float _Emission, _FadeStart, _FadeEnd, _SeaLevel;
                float _GroundTextureStrength, _GroundTiling;
            CBUFFER_END
            float4 _CityLifeAmbient;
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half3 normalWS : TEXCOORD1; half4 color : COLOR; half fog : TEXCOORD2; UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v); UNITY_TRANSFER_INSTANCE_ID(v,o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS; o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.color = v.color * _BaseColor; o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float d = distance(i.positionWS, _WorldSpaceCameraPos);
                float fade = saturate((_FadeEnd-d)/max(1,_FadeEnd-_FadeStart));
                float noise = frac(52.9829189 * frac(dot(i.positionCS.xy, float2(.06711056,.00583715))));
                clip(fade-noise);
                Light sun = GetMainLight();
                half3 n = normalize(i.normalWS);
                half diffuse = saturate(dot(n,sun.direction));
                half sky = saturate(n.y*.4+.6);
                half3 ambient = _CityLifeAmbient.rgb * (.55 + sky*.27);
                half3 albedo = i.color.rgb;
                if (_GroundTextureStrength > .001)
                {
                    // World coordinates keep detail continuous across terrain chunks and LODs.
                    // The actual CC0 dry-ground photograph supplies texture, while luminance
                    // modulation preserves the source biome colours and uses texture mipmaps.
                    half3 weights = pow(abs(n), 4);
                    weights /= max(.001, weights.x + weights.y + weights.z);
                    float3 p = i.positionWS * _GroundTiling;
                    half3 detail = SAMPLE_TEXTURE2D(_GroundAlbedo, sampler_GroundAlbedo, p.zy).rgb * weights.x
                        + SAMPLE_TEXTURE2D(_GroundAlbedo, sampler_GroundAlbedo, p.xz).rgb * weights.y
                        + SAMPLE_TEXTURE2D(_GroundAlbedo, sampler_GroundAlbedo, p.xy).rgb * weights.z;
                    half grain = dot(detail, half3(.2126,.7152,.0722));
                    // Mean linear luminance of the retained dry_mud_field_001 diffuse is .107.
                    half modulation = clamp(grain / .107, .50, 1.48);
                    albedo *= lerp(1, modulation, _GroundTextureStrength);
                }
                half3 color = albedo * (ambient + sun.color * diffuse * .77 + _Emission);
                // A restrained wet shoreline grounds the beaches in the same water level.
                half wet = saturate(1-abs(i.positionWS.y-_SeaLevel)*.28);
                color *= 1-wet*.16;
                return half4(MixFog(color,i.fog),1);
            }
            ENDHLSL
        }
    }
}
