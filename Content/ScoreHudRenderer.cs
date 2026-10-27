using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Windows.ApplicationModel;
using HololensGo.Common;

namespace HololensGo.Content
{
    internal sealed class ScoreHudRenderer : Disposer
    {
        private const int AtlasWidth = 128;
        private const int AtlasHeight = 8;
        private const string GlyphCharacters = " 0123456789:CS";
        private const float GlyphWidth = 0.012f;
        private const float GlyphHeight = 0.018f;
        private const float GlyphAdvance = 0.014f;

        private static readonly byte[][] GlyphRows =
        {
            new byte[7],
            new byte[] { 0x0e, 0x11, 0x13, 0x15, 0x19, 0x11, 0x0e },
            new byte[] { 0x04, 0x0c, 0x04, 0x04, 0x04, 0x04, 0x0e },
            new byte[] { 0x0e, 0x11, 0x01, 0x02, 0x04, 0x08, 0x1f },
            new byte[] { 0x1e, 0x01, 0x01, 0x0e, 0x01, 0x01, 0x1e },
            new byte[] { 0x02, 0x06, 0x0a, 0x12, 0x1f, 0x02, 0x02 },
            new byte[] { 0x1f, 0x10, 0x10, 0x1e, 0x01, 0x01, 0x1e },
            new byte[] { 0x0e, 0x10, 0x10, 0x1e, 0x11, 0x11, 0x0e },
            new byte[] { 0x1f, 0x01, 0x02, 0x04, 0x08, 0x08, 0x08 },
            new byte[] { 0x0e, 0x11, 0x11, 0x0e, 0x11, 0x11, 0x0e },
            new byte[] { 0x0e, 0x11, 0x11, 0x0f, 0x01, 0x01, 0x0e },
            new byte[] { 0x00, 0x04, 0x04, 0x00, 0x04, 0x04, 0x00 },
            new byte[] { 0x0e, 0x11, 0x10, 0x10, 0x10, 0x11, 0x0e },
            new byte[] { 0x0f, 0x10, 0x10, 0x0e, 0x01, 0x01, 0x1e },
        };

        private readonly DeviceResources deviceResources;
        private SharpDX.Direct3D11.InputLayout inputLayout;
        private SharpDX.Direct3D11.Buffer vertexBuffer;
        private SharpDX.Direct3D11.Buffer indexBuffer;
        private SharpDX.Direct3D11.VertexShader vertexShader;
        private SharpDX.Direct3D11.GeometryShader geometryShader;
        private SharpDX.Direct3D11.PixelShader pixelShader;
        private SharpDX.Direct3D11.Buffer modelConstantBuffer;
        private SharpDX.Direct3D11.Texture2D glyphTexture;
        private SharpDX.Direct3D11.ShaderResourceView glyphTextureView;
        private SharpDX.Direct3D11.SamplerState glyphSampler;
        private ModelConstantBuffer modelCB;
        private string displayedText;
        private int indexCount;
        private bool loaded;
        private bool usingVprtShaders;

        public ScoreHudRenderer(DeviceResources deviceResources)
        {
            this.deviceResources = deviceResources;
            CreateDeviceDependentResourcesAsync();
        }

        public async void CreateDeviceDependentResourcesAsync()
        {
            try
            {
                ReleaseDeviceDependentResources();
                usingVprtShaders = deviceResources.D3DDeviceSupportsVprt;
                var folder = Package.Current.InstalledLocation;
                string vertexShaderFile = usingVprtShaders
                    ? "Content\\Shaders\\TextVPRTVertexShader.cso"
                    : "Content\\Shaders\\TextVertexShader.cso";
                byte[] vertexShaderData = await DirectXHelper.ReadDataAsync(
                    await folder.GetFileAsync(vertexShaderFile));
                vertexShader = ToDispose(new SharpDX.Direct3D11.VertexShader(
                    deviceResources.D3DDevice, vertexShaderData));
                inputLayout = ToDispose(new SharpDX.Direct3D11.InputLayout(
                    deviceResources.D3DDevice,
                    vertexShaderData,
                    new[]
                    {
                        new SharpDX.Direct3D11.InputElement("POSITION", 0, SharpDX.DXGI.Format.R32G32B32_Float, 0, 0),
                        new SharpDX.Direct3D11.InputElement("TEXCOORD", 0, SharpDX.DXGI.Format.R32G32_Float, 12, 0),
                    }));

                if (!usingVprtShaders)
                {
                    byte[] geometryShaderData = await DirectXHelper.ReadDataAsync(
                        await folder.GetFileAsync("Content\\Shaders\\TextGeometryShader.cso"));
                    geometryShader = ToDispose(new SharpDX.Direct3D11.GeometryShader(
                        deviceResources.D3DDevice, geometryShaderData));
                }

                byte[] pixelShaderData = await DirectXHelper.ReadDataAsync(
                    await folder.GetFileAsync("Content\\Shaders\\TextPixelShader.cso"));
                pixelShader = ToDispose(new SharpDX.Direct3D11.PixelShader(
                    deviceResources.D3DDevice, pixelShaderData));

                CreateGlyphTexture();
                glyphSampler = ToDispose(new SharpDX.Direct3D11.SamplerState(
                    deviceResources.D3DDevice,
                    new SharpDX.Direct3D11.SamplerStateDescription
                    {
                        Filter = SharpDX.Direct3D11.Filter.MinMagMipPoint,
                        AddressU = SharpDX.Direct3D11.TextureAddressMode.Clamp,
                        AddressV = SharpDX.Direct3D11.TextureAddressMode.Clamp,
                        AddressW = SharpDX.Direct3D11.TextureAddressMode.Clamp,
                        MaximumAnisotropy = 1,
                        ComparisonFunction = SharpDX.Direct3D11.Comparison.Never,
                        MinimumLod = 0,
                        MaximumLod = float.MaxValue,
                    }));

                modelConstantBuffer = ToDispose(new SharpDX.Direct3D11.Buffer(
                    deviceResources.D3DDevice,
                    SharpDX.Utilities.SizeOf<ModelConstantBuffer>(),
                    SharpDX.Direct3D11.ResourceUsage.Default,
                    SharpDX.Direct3D11.BindFlags.ConstantBuffer,
                    SharpDX.Direct3D11.CpuAccessFlags.None,
                    SharpDX.Direct3D11.ResourceOptionFlags.None,
                    0));
                modelCB = new ModelConstantBuffer();
                loaded = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("ScoreHudRenderer load error: " + ex);
                loaded = false;
            }
        }

        public void Render(int score, int combo, Vector3 worldPosition, Vector3 cameraPosition, Vector3 cameraForward)
        {
            if (!loaded)
                return;

            string text = String.Format("S:{0:D6} C:{1:D2}", score, combo);
            if (!String.Equals(displayedText, text, StringComparison.Ordinal))
                UpdateTextGeometry(text);

            var context = deviceResources.D3DDeviceContext;
            Matrix4x4 billboard = Matrix4x4.CreateBillboard(
                worldPosition, cameraPosition, Vector3.UnitY, cameraForward);
            modelCB.model = Matrix4x4.Transpose(billboard);
            context.UpdateSubresource(ref modelCB, modelConstantBuffer);

            context.InputAssembler.InputLayout = inputLayout;
            context.InputAssembler.PrimitiveTopology = SharpDX.Direct3D.PrimitiveTopology.TriangleList;
            context.InputAssembler.SetVertexBuffers(0,
                new SharpDX.Direct3D11.VertexBufferBinding(
                    vertexBuffer, SharpDX.Utilities.SizeOf<VertexPositionUV>(), 0));
            context.InputAssembler.SetIndexBuffer(indexBuffer, SharpDX.DXGI.Format.R16_UInt, 0);

            context.VertexShader.SetShader(vertexShader, null, 0);
            context.VertexShader.SetConstantBuffers(0, modelConstantBuffer);
            if (!usingVprtShaders && geometryShader != null)
                context.GeometryShader.SetShader(geometryShader, null, 0);

            context.PixelShader.SetShader(pixelShader, null, 0);
            context.PixelShader.SetShaderResource(0, glyphTextureView);
            context.PixelShader.SetSampler(0, glyphSampler);
            context.DrawIndexedInstanced(indexCount, 2, 0, 0, 0);
        }

        private void CreateGlyphTexture()
        {
            byte[] pixels = new byte[AtlasWidth * AtlasHeight * 4];
            for (int glyph = 0; glyph < GlyphRows.Length; glyph++)
            {
                int glyphX = (glyph % 16) * 8;
                int glyphY = (glyph / 16) * 8;
                for (int row = 0; row < 7; row++)
                {
                    for (int column = 0; column < 5; column++)
                    {
                        if ((GlyphRows[glyph][row] & (1 << (4 - column))) == 0)
                            continue;

                        int pixelOffset = ((glyphY + row) * AtlasWidth + glyphX + column) * 4;
                        pixels[pixelOffset] = 255;
                        pixels[pixelOffset + 1] = 255;
                        pixels[pixelOffset + 2] = 255;
                        pixels[pixelOffset + 3] = 255;
                    }
                }
            }

            var description = new SharpDX.Direct3D11.Texture2DDescription
            {
                Width = AtlasWidth,
                Height = AtlasHeight,
                MipLevels = 1,
                ArraySize = 1,
                Format = SharpDX.DXGI.Format.B8G8R8A8_UNorm,
                SampleDescription = new SharpDX.DXGI.SampleDescription(1, 0),
                Usage = SharpDX.Direct3D11.ResourceUsage.Default,
                BindFlags = SharpDX.Direct3D11.BindFlags.ShaderResource,
            };
            glyphTexture = ToDispose(new SharpDX.Direct3D11.Texture2D(
                deviceResources.D3DDevice, description));
            GCHandle handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try
            {
                deviceResources.D3DDeviceContext.UpdateSubresource(
                    glyphTexture, 0, null, handle.AddrOfPinnedObject(), AtlasWidth * 4, 0);
            }
            finally
            {
                handle.Free();
            }
            glyphTextureView = ToDispose(new SharpDX.Direct3D11.ShaderResourceView(
                deviceResources.D3DDevice, glyphTexture));
        }

        private void UpdateTextGeometry(string text)
        {
            var vertices = new List<VertexPositionUV>(text.Length * 4);
            var indices = new List<ushort>(text.Length * 6);
            float startX = -(text.Length * GlyphAdvance) * 0.5f;

            for (int characterIndex = 0; characterIndex < text.Length; characterIndex++)
            {
                int glyph = GlyphCharacters.IndexOf(text[characterIndex]);
                if (glyph < 0)
                    throw new InvalidOperationException("Score HUD contains an unsupported glyph.");

                float left = startX + (characterIndex * GlyphAdvance);
                float right = left + GlyphWidth;
                float top = GlyphHeight * 0.5f;
                float bottom = -top;
                float u0 = ((glyph % 16) * 8f) / AtlasWidth;
                float u1 = (glyph % 16 * 8f + 8f) / AtlasWidth;
                float v0 = ((glyph / 16) * 8f) / AtlasHeight;
                float v1 = (glyph / 16 * 8f + 8f) / AtlasHeight;
                ushort vertex = (ushort)vertices.Count;

                vertices.Add(new VertexPositionUV(new Vector3(left, top, 0f), new Vector2(u0, v0)));
                vertices.Add(new VertexPositionUV(new Vector3(left, bottom, 0f), new Vector2(u0, v1)));
                vertices.Add(new VertexPositionUV(new Vector3(right, top, 0f), new Vector2(u1, v0)));
                vertices.Add(new VertexPositionUV(new Vector3(right, bottom, 0f), new Vector2(u1, v1)));
                indices.Add((ushort)(vertex + 0));
                indices.Add((ushort)(vertex + 1));
                indices.Add((ushort)(vertex + 2));
                indices.Add((ushort)(vertex + 2));
                indices.Add((ushort)(vertex + 1));
                indices.Add((ushort)(vertex + 3));
            }

            RemoveAndDispose(ref vertexBuffer);
            RemoveAndDispose(ref indexBuffer);
            vertexBuffer = ToDispose(SharpDX.Direct3D11.Buffer.Create(
                deviceResources.D3DDevice,
                SharpDX.Direct3D11.BindFlags.VertexBuffer,
                vertices.ToArray()));
            indexBuffer = ToDispose(SharpDX.Direct3D11.Buffer.Create(
                deviceResources.D3DDevice,
                SharpDX.Direct3D11.BindFlags.IndexBuffer,
                indices.ToArray()));
            indexCount = indices.Count;
            displayedText = text;
        }

        public void ReleaseDeviceDependentResources()
        {
            loaded = false;
            displayedText = null;
            indexCount = 0;
            RemoveAndDispose(ref inputLayout);
            RemoveAndDispose(ref vertexBuffer);
            RemoveAndDispose(ref indexBuffer);
            RemoveAndDispose(ref vertexShader);
            RemoveAndDispose(ref geometryShader);
            RemoveAndDispose(ref pixelShader);
            RemoveAndDispose(ref modelConstantBuffer);
            RemoveAndDispose(ref glyphTextureView);
            RemoveAndDispose(ref glyphTexture);
            RemoveAndDispose(ref glyphSampler);
        }

        protected override void Dispose(bool disposeManagedResources)
        {
            if (!IsDisposed)
            {
                if (disposeManagedResources)
                    ReleaseDeviceDependentResources();
                base.Dispose(disposeManagedResources);
            }
        }
    }
}