// Geometry shader: sets render-target array index for stereo, passes UV through.
struct GeometryShaderInput
{
    min16float4 pos   : SV_POSITION;
    min16float2 uv    : TEXCOORD0;
    uint        instId: TEXCOORD1;
};

struct GeometryShaderOutput
{
    min16float4 pos   : SV_POSITION;
    min16float2 uv    : TEXCOORD0;
    uint        rtvId : SV_RenderTargetArrayIndex;
};

[maxvertexcount(3)]
void main(triangle GeometryShaderInput input[3], inout TriangleStream<GeometryShaderOutput> outStream)
{
    GeometryShaderOutput output;
    [unroll(3)]
    for (int i = 0; i < 3; ++i)
    {
        output.pos   = input[i].pos;
        output.uv    = input[i].uv;
        output.rtvId = input[i].instId;
        outStream.Append(output);
    }
}
