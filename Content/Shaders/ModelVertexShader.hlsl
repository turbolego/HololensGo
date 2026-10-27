// Non-VPRT vertex shader for the textured model (pairs with ModelGeometryShader).
struct VertexShaderOutput
{
    min16float4 pos   : SV_POSITION;
    min16float2 uv    : TEXCOORD0;
    uint        viewId: TEXCOORD1;
};

#include "ModelVertexShaderShared.hlsl"
