// VPRT vertex shader for the textured model (no geometry shader needed).
struct VertexShaderOutput
{
    min16float4 pos   : SV_POSITION;
    min16float2 uv    : TEXCOORD0;
    uint        viewId: SV_RenderTargetArrayIndex;
};

#include "ModelVertexShaderShared.hlsl"
