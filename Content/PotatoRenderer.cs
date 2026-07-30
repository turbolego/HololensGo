using System;
using System.Collections.Generic;
using System.Numerics;
using HololensGo.Common;
using HololensGo.Models;

namespace HololensGo.Content
{
    /// <summary>
    /// Renders potato projectiles as small brown ellipsoid meshes.
    /// Uses the same shader pipeline as MickeyRenderer (vertex color shaders).
    /// </summary>
    internal class PotatoRenderer : Disposer
    {
        private readonly DeviceResources deviceResources;

        private SharpDX.Direct3D11.InputLayout inputLayout;
        private SharpDX.Direct3D11.Buffer vertexBuffer;
        private SharpDX.Direct3D11.Buffer indexBuffer;
        private SharpDX.Direct3D11.VertexShader vertexShader;
        private SharpDX.Direct3D11.GeometryShader geometryShader;
        private SharpDX.Direct3D11.PixelShader pixelShader;
        private SharpDX.Direct3D11.Buffer modelConstantBuffer;

        private ModelConstantBuffer modelCB;
        private int indexCount;
        private bool loaded;
        private bool usingVprtShaders;

        /// <summary>Uniform scale factor for each potato.</summary>
        public float PotatoScale { get; set; } = 0.03f;

        public PotatoRenderer(DeviceResources deviceResources)
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
                var folder = Windows.ApplicationModel.Package.Current.InstalledLocation;

                // Load shaders
                string vsFile = usingVprtShaders
                    ? "Content\\Shaders\\VPRTVertexShader.cso"
                    : "Content\\Shaders\\VertexShader.cso";
                var vsBytes = await DirectXHelper.ReadDataAsync(await folder.GetFileAsync(vsFile));
                vertexShader = ToDispose(new SharpDX.Direct3D11.VertexShader(deviceResources.D3DDevice, vsBytes));
                inputLayout = ToDispose(new SharpDX.Direct3D11.InputLayout(
                    deviceResources.D3DDevice, vsBytes, new[]
                    {
                        new SharpDX.Direct3D11.InputElement("POSITION", 0, SharpDX.DXGI.Format.R32G32B32_Float, 0, 0),
                        new SharpDX.Direct3D11.InputElement("COLOR", 0, SharpDX.DXGI.Format.R32G32B32_Float, 12, 0)
                    }));

                if (!usingVprtShaders)
                {
                    var gsBytes = await DirectXHelper.ReadDataAsync(
                        await folder.GetFileAsync("Content\\Shaders\\GeometryShader.cso"));
                    geometryShader = ToDispose(
                        new SharpDX.Direct3D11.GeometryShader(deviceResources.D3DDevice, gsBytes));
                }

                var psBytes = await DirectXHelper.ReadDataAsync(
                    await folder.GetFileAsync("Content\\Shaders\\PixelShader.cso"));
                pixelShader = ToDispose(
                    new SharpDX.Direct3D11.PixelShader(deviceResources.D3DDevice, psBytes));

                // Build an ellipsoid (UV sphere with Y squished to 0.7)
                const int LatSegments = 16;
                const int LonSegments = 16;
                Vector3 brown = new Vector3(0.6f, 0.3f, 0.1f);
                Vector3 brownHighlight = new Vector3(0.7f, 0.4f, 0.15f);

                List<VertexPositionColor> verts = new List<VertexPositionColor>();
                List<ushort> idx = new List<ushort>();

                for (int lat = 0; lat <= LatSegments; lat++)
                {
                    float theta = lat * (float)Math.PI / LatSegments;
                    float sinTheta = (float)Math.Sin(theta);
                    float cosTheta = (float)Math.Cos(theta);

                    for (int lon = 0; lon <= LonSegments; lon++)
                    {
                        float phi = lon * 2.0f * (float)Math.PI / LonSegments;
                        float sinPhi = (float)Math.Sin(phi);
                        float cosPhi = (float)Math.Cos(phi);

                        // Ellipsoid: Y squished to 0.7
                        float x = sinTheta * cosPhi;
                        float y = cosTheta * 0.7f;
                        float z = sinTheta * sinPhi;

                        // Lighter colour near top pole (simple directional bias)
                        Vector3 c = Vector3.Lerp(brown, brownHighlight, Math.Max(0, -cosTheta) * 0.4f);
                        verts.Add(new VertexPositionColor(new Vector3(x, y, z), c));
                    }
                }

                for (int lat = 0; lat < LatSegments; lat++)
                {
                    for (int lon = 0; lon < LonSegments; lon++)
                    {
                        int first = lat * (LonSegments + 1) + lon;
                        int second = first + (LonSegments + 1);

                        idx.Add((ushort)(first));
                        idx.Add((ushort)(second));
                        idx.Add((ushort)(first + 1));

                        idx.Add((ushort)(second));
                        idx.Add((ushort)(second + 1));
                        idx.Add((ushort)(first + 1));
                    }
                }

                vertexBuffer = ToDispose(SharpDX.Direct3D11.Buffer.Create(
                    deviceResources.D3DDevice,
                    SharpDX.Direct3D11.BindFlags.VertexBuffer,
                    verts.ToArray()));

                indexBuffer = ToDispose(SharpDX.Direct3D11.Buffer.Create(
                    deviceResources.D3DDevice,
                    SharpDX.Direct3D11.BindFlags.IndexBuffer,
                    idx.ToArray()));

                indexCount = idx.Count;

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
                System.Diagnostics.Debug.WriteLine($"PotatoRenderer load error: {ex.Message}");
                loaded = false;
            }
        }

        /// <summary>
        /// Draw a single potato projectile at its current position.
        /// </summary>
        public void Draw(in Potato proj)
        {
            if (!loaded || proj.HasCollided)
                return;

            var ctx = deviceResources.D3DDeviceContext;

            // Compute model matrix: scale (potato size), translate to world position
            var m = Matrix4x4.CreateScale(PotatoScale)
                    * Matrix4x4.CreateTranslation(proj.Position);
            modelCB.model = Matrix4x4.Transpose(m);
            ctx.UpdateSubresource(ref modelCB, modelConstantBuffer);

            ctx.InputAssembler.InputLayout = inputLayout;
            ctx.InputAssembler.PrimitiveTopology = SharpDX.Direct3D.PrimitiveTopology.TriangleList;
            ctx.InputAssembler.SetVertexBuffers(0,
                new SharpDX.Direct3D11.VertexBufferBinding(
                    vertexBuffer,
                    System.Runtime.InteropServices.Marshal.SizeOf<VertexPositionColor>(),
                    0));
            ctx.InputAssembler.SetIndexBuffer(indexBuffer, SharpDX.DXGI.Format.R16_UInt, 0);

            ctx.VertexShader.SetShader(vertexShader, null, 0);
            ctx.VertexShader.SetConstantBuffers(0, modelConstantBuffer);
            if (!usingVprtShaders && geometryShader != null)
            {
                ctx.GeometryShader.SetShader(geometryShader, null, 0);
            }
            ctx.PixelShader.SetShader(pixelShader, null, 0);

            ctx.DrawIndexedInstanced(indexCount, 2, 0, 0, 0);
        }

        public void ReleaseDeviceDependentResources()
        {
            // Resources tracked via Disposer base class; diposed in base.Dispose()
        }

        public new void Dispose()
        {
            ReleaseDeviceDependentResources();
            base.Dispose();
        }
    }
}