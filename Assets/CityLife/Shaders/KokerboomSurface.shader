Shader "CityLife/KokerboomSurface"
{
    Properties
    {
        _BaseColor("Tint",Color)=(1,1,1,1)
        _BaseMap("Base",2D)="white"{}
        _IsLeaf("Succulent surface",Float)=0
        _Smoothness("Smoothness",Range(0,1))=.25
        _Cutoff("Cutoff",Float)=.5
        _Cull("Cull",Float)=0
        _UseSourceBark("Mapped source bark",Float)=0
        _BumpMap("Source normal",2D)="bump"{}
        _MetallicGlossMap("Source metallic smoothness",2D)="white"{}
        _TrunkRect("Trunk atlas region",Vector)=(.43,.16,.33,.55)
        _BranchRect("Branch atlas region",Vector)=(.10,.21,.11,.59)
        _DiagnosticAlbedo("Albedo diagnostic",Float)=0
    }
    SubShader
    {
        Tags {"RenderType"="Opaque" "RenderPipeline"="UniversalPipeline"}
        Pass
        {
            Name "ForwardLit"
            Tags {"LightMode"="UniversalForward"}
            Cull Off
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
                float4 _BaseColor,_BaseMap_ST;
                float _IsLeaf,_Smoothness,_Cutoff,_Cull;
                float _UseSourceBark,_DiagnosticAlbedo;
                float4 _TrunkRect,_BranchRect;
            CBUFFER_END
            TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap);SAMPLER(sampler_BumpMap);
            TEXTURE2D(_MetallicGlossMap);SAMPLER(sampler_MetallicGlossMap);
            struct Attributes {float4 positionOS:POSITION;float3 normalOS:NORMAL;float4 color:COLOR;float2 uv:TEXCOORD0;UNITY_VERTEX_INPUT_INSTANCE_ID};
            struct Varyings {float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;float3 normalWS:TEXCOORD1;float3 positionOS:TEXCOORD2;float4 color:COLOR;float2 uv:TEXCOORD3;float fog:TEXCOORD4;UNITY_VERTEX_INPUT_INSTANCE_ID};
            float hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
            float noise(float2 p){float2 i=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(hash(i),hash(i+float2(1,0)),f.x),lerp(hash(i+float2(0,1)),hash(i+1),f.x),f.y);}
            // Four overlapping translated atlas patches. Each remains inside the
            // photographed trunk region; no mirrored scars or atlas-padding repeats.
            void SourcePatch(float2 q,float seed,out float3 color,out float3 bump,out float gloss)
            {
                float2 cell=floor(q),f=frac(q),w=smoothstep(.36,.64,f);
                float2 dx=ddx(q)*.32*_TrunkRect.zw,dy=ddy(q)*.32*_TrunkRect.zw;
                color=0;bump=0;gloss=0;
                [unroll]for(int y=0;y<2;y++)[unroll]for(int x=0;x<2;x++)
                {
                    float2 id=cell+float2(x,y);
                    float2 shift=float2(hash(id+seed),hash(id+seed+31))*.24-.12;
                    float2 local=(q-id)*.32+.5+shift;
                    float2 uv=_TrunkRect.xy+local*_TrunkRect.zw;
                    float weight=(x==0?1-w.x:w.x)*(y==0?1-w.y:w.y);
                    color+=SAMPLE_TEXTURE2D_GRAD(_BaseMap,sampler_BaseMap,uv,dx,dy).rgb*weight;
                    bump+=UnpackNormal(SAMPLE_TEXTURE2D_GRAD(_BumpMap,sampler_BumpMap,uv,dx,dy))*weight;
                    gloss+=SAMPLE_TEXTURE2D_GRAD(_MetallicGlossMap,sampler_MetallicGlossMap,uv,dx,dy).a*weight;
                }
            }
            void SourceBark(float3 p,float3 nOS,out float3 color,out float3 gradient,out float gloss)
            {
                float3 w=pow(abs(nOS),4);w/=max(w.x+w.y+w.z,1e-5);
                float3 cx,cy,cz,bx,by,bz;float gx,gy,gz;
                SourcePatch(p.zy*float2(1.12,.90),13,cx,bx,gx);
                SourcePatch(p.xz*1.12,47,cy,by,gy);
                SourcePatch(p.xy*float2(1.12,.90),83,cz,bz,gz);
                color=cx*w.x+cy*w.y+cz*w.z;gloss=gx*w.x+gy*w.y+gz*w.z;
                gradient=float3(0,bx.y,bx.x)*w.x+float3(by.x,0,by.y)*w.y+float3(bz.x,bz.y,0)*w.z;
                gradient-=nOS*dot(gradient,nOS);
            }
            float barkHeight(float3 p)
            {
                float a=atan2(p.z,p.x);
                float2 uv=float2(a*5.8,p.y*18.5);
                uv+=float2(noise(uv*.67+7),noise(uv*.73+29))*1.25;
                float2 cell=floor(uv),part=frac(uv);float nearest=20,next=20;float2 owner=0,offset=0;
                [unroll]for(int y=-1;y<=1;y++)[unroll]for(int x=-1;x<=1;x++)
                {
                    float2 id=cell+float2(x,y);
                    float2 delta=float2(x,y)+float2(hash(id),hash(id+41))-part;
                    float d=dot(delta,delta);
                    if(d<nearest){next=nearest;nearest=d;owner=id;offset=delta;}
                    else next=min(next,d);
                }
                float edge=sqrt(next)-sqrt(nearest);
                float plate=smoothstep(.004,.055,edge);
                float curl=saturate(.58-offset.y*.7+hash(owner+11)*.25);
                float retained=smoothstep(.13,.40,noise(uv*.87+31));
                float grain=noise(float2(a*76+p.y*.8,p.y*6))* .11;
                return plate*(.15+curl*.43+hash(owner+73)*.16)*retained+grain+noise(p.xy*87+p.z*13)*.035;
            }
            Varyings Vert(Attributes v)
            {
                Varyings o;UNITY_SETUP_INSTANCE_ID(v);UNITY_TRANSFER_INSTANCE_ID(v,o);
                VertexPositionInputs p=GetVertexPositionInputs(v.positionOS.xyz);o.positionCS=p.positionCS;o.positionWS=p.positionWS;
                o.positionOS=v.positionOS.xyz;o.normalWS=TransformObjectToWorldNormal(v.normalOS);o.color=v.color;o.uv=v.uv;o.fog=ComputeFogFactor(p.positionCS.z);return o;
            }
            half4 Frag(Varyings i,FRONT_FACE_TYPE face:FRONT_FACE_SEMANTIC):SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 n=normalize(i.normalWS)*IS_FRONT_VFACE(face,1,-1);float3 albedo;
                float roughness=_Smoothness;
                if(_IsLeaf>.5)
                {
                    float striation=noise(float2(i.uv.x*8,i.uv.y*150));
                    albedo=i.color.rgb*lerp(.94,1.05,striation);
                }
                else
                {
                    float trunk=saturate(i.color.r);float h=barkHeight(i.positionOS);
                    if(_UseSourceBark>.5)
                    {
                        float3 nOS=normalize(TransformWorldToObjectDir(n));
                        float3 sourceColor,gradient;float sourceGloss;
                        SourceBark(i.positionOS+i.color.b*float3(11,17,7),nOS,sourceColor,gradient,sourceGloss);
                        // Upper shoots have pale powdery skin. Projecting an entire
                        // curved branch through a trunk polar angle stretched its map
                        // into horizontal bands; this local surface has no polar seam.
                        float3 pale=KokerboomBranchSkin(i.positionOS);
                        sourceColor*=float3(1.13,1.055,.86);
                        albedo=lerp(pale,sourceColor,trunk);
                        n=normalize(n+TransformObjectToWorldDir(gradient,false)*.48*trunk);
                        roughness=lerp(.22,sourceGloss,trunk);
                    }
                    else
                    {
                    float3 substrate=lerp(float3(.32,.28,.19),float3(.55,.48,.31),noise(i.positionOS.xy*9+i.positionOS.z*2));
                    float3 mature=lerp(substrate,float3(.73,.49,.22),saturate(h*1.65));
                    float3 young=float3(.66,.59,.39)*(0.93+.13*noise(i.positionOS.xy*13+i.positionOS.z));
                    young=lerp(float3(.43,.49,.29),young,i.color.g);
                    albedo=lerp(young,mature,trunk);
                    // Screen-space surface gradients add small peeling edges without disconnected geometry.
                    float3 dpdx=ddx(i.positionWS),dpdy=ddy(i.positionWS);float3 r1=cross(dpdy,n),r2=cross(n,dpdx);
                    float det=dot(dpdx,r1);float3 grad=sign(det)*(ddx(h)*r1+ddy(h)*r2);
                    n=normalize(abs(det)*n-grad*.0024*trunk);
                    }
                }
                if(_DiagnosticAlbedo>.5)return half4(albedo*_BaseColor.rgb,1);
                InputData input=(InputData)0;input.positionWS=i.positionWS;input.normalWS=n;input.viewDirectionWS=GetWorldSpaceNormalizeViewDir(i.positionWS);
                input.shadowCoord=TransformWorldToShadowCoord(i.positionWS);input.bakedGI=SampleSH(n);input.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(i.positionCS);input.shadowMask=1;
                SurfaceData surface=(SurfaceData)0;surface.albedo=albedo*_BaseColor.rgb;surface.alpha=1;surface.metallic=0;surface.smoothness=roughness;surface.occlusion=1;surface.normalTS=float3(0,0,1);
                half4 color=UniversalFragmentPBR(input,surface);color.rgb=MixFog(color.rgb,i.fog);return color;
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
}
