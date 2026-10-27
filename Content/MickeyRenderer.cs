// 3D model: "Mickey Mouse - Steamboat Willie Model"
// Source:   https://sketchfab.com/3d-models/mickey-mouse-steamboat-willie-model-3f9bfd873a5e4f3ca2377adf63b25256
// Author:   luqui230610 (https://sketchfab.com/luqui230610)
// License:  CC-BY-4.0 (http://creativecommons.org/licenses/by/4.0/)
// Credit:   This work is based on "Mickey Mouse - Steamboat Willie Model" by luqui230610
//           licensed under CC-BY-4.0 (http://creativecommons.org/licenses/by/4.0/)
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using HololensGo.Common;

namespace HololensGo.Content
{
    internal class MickeyRenderer : Disposer
    {
        private readonly DeviceResources deviceResources;

        private SharpDX.Direct3D11.InputLayout        inputLayout;
        private SharpDX.Direct3D11.VertexShader       vertexShader;
        private SharpDX.Direct3D11.GeometryShader     geometryShader;
        private SharpDX.Direct3D11.PixelShader        pixelShader;
        private SharpDX.Direct3D11.Buffer             modelConstantBuffer;
        private SharpDX.Direct3D11.SamplerState       samplerState;

        private readonly List<MeshResources> meshResources = new List<MeshResources>();
        private ModelConstantBuffer modelCB;
        private bool loaded;
        private bool usingVprtShaders;

        private sealed class MeshResources
        {
            public SharpDX.Direct3D11.Buffer VertexBuffer;
            public SharpDX.Direct3D11.Buffer IndexBuffer;
            public SharpDX.Direct3D11.Texture2D DiffuseTexture;
            public SharpDX.Direct3D11.ShaderResourceView DiffuseSRV;
            public int IndexCount;
        }

        public Vector3 Position  { get; set; } = Vector3.Zero;
        public float   Scale     { get; set; } = 0.15f;
        public bool    IsHit     { get; set; } = false;
        public bool    IsVisible { get; set; } = true;

        private float hitAnimTimer  = 0f;
        private float idleAnimTime  = 0f;

        public MickeyRenderer(DeviceResources deviceResources)
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
                System.Diagnostics.Debug.WriteLine("[MickeyRenderer] Starting CreateDeviceDependentResourcesAsync");

                var folder = Package.Current.InstalledLocation;
                string vsFile = usingVprtShaders
                    ? "Content\\Shaders\\ModelVPRTVertexShader.cso"
                    : "Content\\Shaders\\ModelVertexShader.cso";
                var vsBytes = await DirectXHelper.ReadDataAsync(await folder.GetFileAsync(vsFile));
                System.Diagnostics.Debug.WriteLine("[MickeyRenderer] Vertex shader loaded: " + vsBytes.Length + " bytes");
                vertexShader = ToDispose(new SharpDX.Direct3D11.VertexShader(deviceResources.D3DDevice, vsBytes));
                inputLayout = ToDispose(new SharpDX.Direct3D11.InputLayout(
                    deviceResources.D3DDevice, vsBytes, new[]
                    {
                        new SharpDX.Direct3D11.InputElement("POSITION", 0, SharpDX.DXGI.Format.R32G32B32_Float, 0,  0),
                        new SharpDX.Direct3D11.InputElement("TEXCOORD", 0, SharpDX.DXGI.Format.R32G32_Float,    12, 0),
                    }));
                System.Diagnostics.Debug.WriteLine("[MickeyRenderer] Input layout created");

                if (!usingVprtShaders)
                {
                    var gsBytes = await DirectXHelper.ReadDataAsync(
                        await folder.GetFileAsync("Content\\Shaders\\ModelGeometryShader.cso"));
                    System.Diagnostics.Debug.WriteLine("[MickeyRenderer] Geometry shader loaded: " + gsBytes.Length + " bytes");
                    geometryShader = ToDispose(
                        new SharpDX.Direct3D11.GeometryShader(deviceResources.D3DDevice, gsBytes));
                }

                var psBytes = await DirectXHelper.ReadDataAsync(
                    await folder.GetFileAsync("Content\\Shaders\\ModelPixelShader.cso"));
                System.Diagnostics.Debug.WriteLine("[MickeyRenderer] Pixel shader loaded: " + psBytes.Length + " bytes");
                pixelShader = ToDispose(new SharpDX.Direct3D11.PixelShader(deviceResources.D3DDevice, psBytes));

                // Load GLB model (CC-BY-4.0 by luqui230610 / sketchfab.com)
                System.Diagnostics.Debug.WriteLine("[MickeyRenderer] Loading GLB model...");
                var mesh = await GlbLoader.LoadAsync(
                    @"Assets\3dmodels\mickey-mouse-steamboat-willie-model\source\Steamboat Willie Modelo.glb");
                if (mesh.Primitives == null || mesh.Primitives.Length == 0)
                    throw new InvalidOperationException("GLB model contains no renderable primitives.");

                foreach (GlbPrimitiveData primitive in mesh.Primitives)
                {
                    if (primitive.Vertices == null || primitive.Vertices.Length == 0 ||
                        primitive.Indices == null || primitive.Indices.Length == 0)
                        throw new InvalidOperationException("GLB primitive has no renderable geometry.");

                    var resources = new MeshResources { IndexCount = primitive.Indices.Length };
                    meshResources.Add(resources);
                    resources.VertexBuffer = ToDispose(SharpDX.Direct3D11.Buffer.Create(
                        deviceResources.D3DDevice,
                        SharpDX.Direct3D11.BindFlags.VertexBuffer,
                        primitive.Vertices));
                    resources.IndexBuffer = ToDispose(SharpDX.Direct3D11.Buffer.Create(
                        deviceResources.D3DDevice,
                        SharpDX.Direct3D11.BindFlags.IndexBuffer,
                        primitive.Indices));

                    var texDesc = new SharpDX.Direct3D11.Texture2DDescription
                    {
                        Width = primitive.TextureWidth,
                        Height = primitive.TextureHeight,
                        MipLevels = 1,
                        ArraySize = 1,
                        Format = SharpDX.DXGI.Format.B8G8R8A8_UNorm,
                        SampleDescription = new SharpDX.DXGI.SampleDescription(1, 0),
                        Usage = SharpDX.Direct3D11.ResourceUsage.Default,
                        BindFlags = SharpDX.Direct3D11.BindFlags.ShaderResource,
                    };
                    resources.DiffuseTexture = ToDispose(new SharpDX.Direct3D11.Texture2D(
                        deviceResources.D3DDevice, texDesc));

                    GCHandle handle = GCHandle.Alloc(primitive.TextureBgra, GCHandleType.Pinned);
                    try
                    {
                        deviceResources.D3DDeviceContext.UpdateSubresource(
                            resources.DiffuseTexture, 0, null,
                            handle.AddrOfPinnedObject(),
                            primitive.TextureWidth * 4, 0);
                    }
                    finally
                    {
                        handle.Free();
                    }

                    resources.DiffuseSRV = ToDispose(new SharpDX.Direct3D11.ShaderResourceView(
                        deviceResources.D3DDevice, resources.DiffuseTexture));
                }

                samplerState = ToDispose(new SharpDX.Direct3D11.SamplerState(
                    deviceResources.D3DDevice, new SharpDX.Direct3D11.SamplerStateDescription
                    {
                        Filter = SharpDX.Direct3D11.Filter.MinMagMipLinear,
                        AddressU = SharpDX.Direct3D11.TextureAddressMode.Wrap,
                        AddressV = SharpDX.Direct3D11.TextureAddressMode.Wrap,
                        AddressW = SharpDX.Direct3D11.TextureAddressMode.Wrap,
                        MaximumAnisotropy = 1,
                        ComparisonFunction = SharpDX.Direct3D11.Comparison.Never,
                        MinimumLod = 0, MaximumLod = float.MaxValue,
                    }));

                modelConstantBuffer = ToDispose(new SharpDX.Direct3D11.Buffer(
                    deviceResources.D3DDevice,
                    SharpDX.Utilities.SizeOf<ModelConstantBuffer>(),
                    SharpDX.Direct3D11.ResourceUsage.Default,
                    SharpDX.Direct3D11.BindFlags.ConstantBuffer,
                    SharpDX.Direct3D11.CpuAccessFlags.None,
                    SharpDX.Direct3D11.ResourceOptionFlags.None, 0));

                modelCB = new ModelConstantBuffer();
                loaded  = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("MickeyRenderer load error: " + ex);
                loaded = false;
            }
        }

        public void Update(float elapsedSeconds)
        {
            if (elapsedSeconds <= 0f) return;
            float dt = Math.Min(elapsedSeconds, 0.1f);
            idleAnimTime += dt;
            if (IsHit) { hitAnimTimer = 0.3f; IsHit = false; }
            hitAnimTimer = Math.Max(0f, hitAnimTimer - dt);
        }

        public void TriggerHit() { IsHit = true; }

        public void Render()
        {
            if (!loaded)
            {
                return;
            }
            if (!IsVisible && hitAnimTimer <= 0f)
            {
                return;
            }

            var ctx = deviceResources.D3DDeviceContext;

            float bob     = (float)Math.Sin(idleAnimTime * 2.4) * 0.0125f;
            float vibrate = hitAnimTimer > 0f
                ? (float)Math.Sin((hitAnimTimer / 0.3f) * 30.0) * 0.03f * (hitAnimTimer / 0.3f)
                : 0f;
            float pulse   = hitAnimTimer > 0f ? 1f + 0.08f * (hitAnimTimer / 0.3f) : 1f;

            var animPos = Position + new Vector3(vibrate, bob, 0f);
            modelCB.model = Matrix4x4.Transpose(
                Matrix4x4.CreateScale(Scale * pulse) * Matrix4x4.CreateTranslation(animPos));
            ctx.UpdateSubresource(ref modelCB, modelConstantBuffer);

            ctx.InputAssembler.InputLayout = inputLayout;
            ctx.InputAssembler.PrimitiveTopology = SharpDX.Direct3D.PrimitiveTopology.TriangleList;

            ctx.VertexShader.SetShader(vertexShader, null, 0);
            ctx.VertexShader.SetConstantBuffers(0, modelConstantBuffer);
            if (!usingVprtShaders && geometryShader != null)
                ctx.GeometryShader.SetShader(geometryShader, null, 0);

            ctx.PixelShader.SetShader(pixelShader, null, 0);
            ctx.PixelShader.SetSampler(0, samplerState);

            foreach (MeshResources mesh in meshResources)
            {
                ctx.InputAssembler.SetVertexBuffers(0,
                    new SharpDX.Direct3D11.VertexBufferBinding(
                        mesh.VertexBuffer, SharpDX.Utilities.SizeOf<VertexPositionUV>(), 0));
                ctx.InputAssembler.SetIndexBuffer(mesh.IndexBuffer, SharpDX.DXGI.Format.R16_UInt, 0);
                ctx.PixelShader.SetShaderResource(0, mesh.DiffuseSRV);
                ctx.DrawIndexedInstanced(mesh.IndexCount, 2, 0, 0, 0);
            }
        }

        public void ReleaseDeviceDependentResources()
        {
            loaded = false;
            RemoveAndDispose(ref inputLayout);
            RemoveAndDispose(ref vertexShader);
            RemoveAndDispose(ref geometryShader);
            RemoveAndDispose(ref pixelShader);
            RemoveAndDispose(ref modelConstantBuffer);
            for (int i = 0; i < meshResources.Count; i++)
            {
                MeshResources mesh = meshResources[i];
                RemoveAndDispose(ref mesh.DiffuseSRV);
                RemoveAndDispose(ref mesh.DiffuseTexture);
                RemoveAndDispose(ref mesh.IndexBuffer);
                RemoveAndDispose(ref mesh.VertexBuffer);
            }
            meshResources.Clear();
            RemoveAndDispose(ref samplerState);
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
