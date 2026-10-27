// Shared vertex shader body for the GLB-sourced textured model.
// Included by ModelVertexShader.hlsl and ModelVPRTVertexShader.hlsl.

cbuffer ModelConstantBuffer : register(b0)
{
    float4x4 model;
};

cbuffer ViewProjectionConstantBuffer : register(b1)
{
    float4x4 viewProjection[2];
};

struct VertexShaderInput
{
    min16float3 pos   : POSITION;
    min16float2 uv    : TEXCOORD0;
    uint        instId: SV_InstanceID;
};

VertexShaderOutput main(VertexShaderInput input)
{
    VertexShaderOutput output;
    float4 pos = float4(input.pos, 1.0f);
    int idx = input.instId % 2;
    pos = mul(pos, model);
    pos = mul(pos, viewProjection[idx]);
    output.pos   = (min16float4)pos;
    output.uv    = input.uv;
    output.viewId = idx;
    return output;
}
