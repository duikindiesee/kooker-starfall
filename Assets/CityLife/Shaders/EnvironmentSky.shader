Shader "Starfall/EnvironmentSky"
{
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A {float4 vertex:POSITION;};struct V {float4 position:SV_POSITION;float3 direction:TEXCOORD0;};
            float _StarfallWeather;
            V Vert(A a){V o;o.position=TransformObjectToHClip(a.vertex.xyz);o.direction=a.vertex.xyz;return o;}
            float Hash(float3 p){return frac(sin(dot(p,float3(12.9898,78.233,37.719)))*43758.5453);}
            half4 Frag(V i):SV_Target
            {
                float3 d=normalize(i.direction);float haze=pow(1-saturate(d.y),5);
                float3 color=lerp(float3(.007,.019,.048),float3(.055,.12,.16),haze);
                float galaxy=exp(-pow((d.x*.6+d.y*.8-.29)*12,2));color+=galaxy*float3(.02,.035,.06)*(.5+.5*sin(d.z*91+d.x*24));
                float star=step(.9985,Hash(floor(d*700)));color+=star*.45*(1-_StarfallWeather*.92);
                float3 center=normalize(float3(-.25,.17,.95));float cosine=dot(d,center);float radius=.13;
                float2 p=float2(dot(d,normalize(cross(float3(0,1,0),center))),dot(d,normalize(cross(center,normalize(cross(float3(0,1,0),center))))))/radius;
                if(cosine>0 && dot(p,p)<1)
                {
                    float z=sqrt(1-dot(p,p));float band=.5+.5*sin(p.y*32+sin(p.x*8+p.y*3)*.6);
                    float light=.15+.85*saturate(dot(normalize(float3(p,z)),normalize(float3(-.6,.5,.8))));
                    color=lerp(float3(.025,.10,.24),float3(.12,.39,.61),band)*light;
                    color+=pow(1-z,4)*float3(.06,.17,.24);
                }
                color=lerp(color,float3(.035,.048,.061),_StarfallWeather*.65);return half4(color,1);
            }
            ENDHLSL
        }
    }
}
