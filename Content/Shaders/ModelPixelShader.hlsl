// Samples the model's diffuse texture.
Texture2D    diffuse  : register(t0);
SamplerState sampler0 : register(s0);

struct PixelShaderInput
{
    min16float4 pos : SV_POSITION;
    min16float2 uv  : TEXCOORD0;
};

min16float4 main(PixelShaderInput input) : SV_TARGET
{
    return (min16float4)diffuse.Sample(sampler0, input.uv);
}
