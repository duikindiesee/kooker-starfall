#ifndef CITYLIFE_KOKERBOOM_BRANCH_SKIN_INCLUDED
#define CITYLIFE_KOKERBOOM_BRANCH_SKIN_INCLUDED

float KokerboomSkinHash(float2 p)
{
    return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);
}

float KokerboomSkinNoise(float2 p)
{
    float2 i=floor(p),f=frac(p); f=f*f*(3-2*f);
    return lerp(lerp(KokerboomSkinHash(i),KokerboomSkinHash(i+float2(1,0)),f.x),
                lerp(KokerboomSkinHash(i+float2(0,1)),KokerboomSkinHash(i+1),f.x),f.y);
}

// Both wood and fitted support are baked in the tree's object coordinates.
// Share the linear albedo and spatial variation, avoiding a Color-property
// conversion and unrelated world-space pattern at their lower attachment.
float3 KokerboomBranchSkin(float3 p)
{
    float mottling=KokerboomSkinNoise(p.xy*8+p.z*3);
    float powder=KokerboomSkinNoise(p.yz*47+p.x*7);
    return lerp(float3(.48,.40,.235),float3(.66,.57,.38),mottling)*lerp(.96,1.04,powder);
}
#endif
