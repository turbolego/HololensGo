using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Storage;
using HololensGo.Common;

namespace HololensGo.Content
{
    /// <summary>
    /// Procedurally generates Steamboat Willie Mickey Mouse geometry from primitives
    /// (spheres, cylinders, torus, boxes) and renders him into the world at a fixed
    /// position.
    ///
    /// No external OBJ / FBX assets required — all geometry is built in code.
    /// </summary>
    internal class MickeyRenderer : Disposer
    {
        private readonly DeviceResources deviceResources;

        // D3D resources
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

        /// <summary>World-space position of the character's root.</summary>
        public Vector3 Position { get; set; } = Vector3.Zero;

        /// <summary>Uniform scale factor (0.15 = ~80 cm tall).</summary>
        public float Scale { get; set; } = 0.15f;

        /// <summary>Set true when hit by a potato — triggers a short reaction animation.</summary>
        public bool IsHit { get; set; } = false;

        /// <summary>Controls whether the target is drawn while waiting to respawn.</summary>
        public bool IsVisible { get; set; } = true;

        private float hitAnimTimer = 0f;
        private float idleAnimationTime = 0f;

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

                // Load shaders
                var folder = Package.Current.InstalledLocation;
                string vsFile = usingVprtShaders
                    ? "Content\\Shaders\\VPRTVertexShader.cso"
                    : "Content\\Shaders\\VertexShader.cso";
                var vsBytes = await DirectXHelper.ReadDataAsync(await folder.GetFileAsync(vsFile));
                vertexShader = ToDispose(new SharpDX.Direct3D11.VertexShader(deviceResources.D3DDevice, vsBytes));
                inputLayout = ToDispose(new SharpDX.Direct3D11.InputLayout(
                    deviceResources.D3DDevice, vsBytes, new[]
                    {
                        new SharpDX.Direct3D11.InputElement("POSITION", 0, SharpDX.DXGI.Format.R32G32B32_Float, 0, 0),
                        new SharpDX.Direct3D11.InputElement("COLOR",    0, SharpDX.DXGI.Format.R32G32B32_Float, 12, 0)
                    }));

                if (!usingVprtShaders)
                {
                    var gsFile = "Content\\Shaders\\GeometryShader.cso";
                    var gsBytes = await DirectXHelper.ReadDataAsync(await folder.GetFileAsync(gsFile));
                    geometryShader = ToDispose(
                        new SharpDX.Direct3D11.GeometryShader(deviceResources.D3DDevice, gsBytes));
                }

                var psFile = "Content\\Shaders\\PixelShader.cso";
                var psBytes = await DirectXHelper.ReadDataAsync(await folder.GetFileAsync(psFile));
                pixelShader = ToDispose(
                    new SharpDX.Direct3D11.PixelShader(deviceResources.D3DDevice, psBytes));

                // ── Build Steamboat Willie geometry ──
                List<VertexPositionColor> vertices = new List<VertexPositionColor>();
                List<ushort> indices = new List<ushort>();

                // Color palette (required)
                Vector3 black = new Vector3(0.1f, 0.1f, 0.1f);
                Vector3 skin  = new Vector3(1.0f, 0.85f, 0.7f);
                Vector3 red   = new Vector3(0.9f, 0.2f, 0.15f);
                Vector3 white = new Vector3(1.0f, 1.0f, 1.0f);
                Vector3 brown = new Vector3(0.5f, 0.3f, 0.15f);
                Vector3 grey  = new Vector3(0.4f, 0.4f, 0.4f);

                // HEAD — sphere @ center (0, 0.7, 0)
                AddSphere(vertices, indices, new Vector3(0, 0.7f, 0), 0.25f, black, 14);

                // EARS — two small spheres
                AddSphere(vertices, indices, new Vector3(-0.22f, 0.95f, 0), 0.14f, black, 10);
                AddSphere(vertices, indices, new Vector3( 0.22f, 0.95f, 0), 0.14f, black, 10);

                // FACE — flat disc for face region (slightly raised)
                AddCylinder(vertices, indices, new Vector3(0, 0.68f, 0.20f), 0.18f, 0.03f, skin, 12);

                // EYES — tiny white spheres
                AddSphere(vertices, indices, new Vector3(-0.10f, 0.75f, 0.21f), 0.04f, white, 8);
                AddSphere(vertices, indices, new Vector3( 0.10f, 0.75f, 0.21f), 0.04f, white, 8);

                // PUPILS — tiny black spheres
                AddSphere(vertices, indices, new Vector3(-0.10f, 0.75f, 0.24f), 0.025f, black, 6);
                AddSphere(vertices, indices, new Vector3( 0.10f, 0.75f, 0.24f), 0.025f, black, 6);

                // NOSE — small black sphere
                AddSphere(vertices, indices, new Vector3(0, 0.66f, 0.23f), 0.03f, black, 6);

                // MOUTH — small line of red dots (approximated as thin cylinder)
                AddCylinder(vertices, indices, new Vector3(0, 0.61f, 0.18f), 0.02f, 0.07f, red, 8);

                // BODY — rounded torso (box)
                AddBox(vertices, indices, new Vector3(0, 0.35f, 0), 0.22f, 0.35f, 0.15f, red);

                // BUTTONS — two white spheres
                AddSphere(vertices, indices, new Vector3(0, 0.42f, 0.14f), 0.03f, white, 8);
                AddSphere(vertices, indices, new Vector3(0, 0.32f, 0.14f), 0.03f, white, 8);

                // ARMS — thin cylinders
                AddCylinder(vertices, indices, new Vector3(-0.24f, 0.40f, 0), 0.04f, 0.25f, black, 8);
                AddCylinder(vertices, indices, new Vector3( 0.24f, 0.40f, 0), 0.04f, 0.25f, black, 8);

                // HANDS — small spheres
                AddSphere(vertices, indices, new Vector3(-0.30f, 0.28f, 0), 0.05f, skin, 8);
                AddSphere(vertices, indices, new Vector3( 0.30f, 0.28f, 0), 0.05f, skin, 8);

                // LEGS — two cylinders
                AddCylinder(vertices, indices, new Vector3(-0.08f, 0.05f, 0), 0.04f, 0.22f, red, 8);
                AddCylinder(vertices, indices, new Vector3( 0.08f, 0.05f, 0), 0.04f, 0.22f, red, 8);

                // SHOES — small boxes
                AddBox(vertices, indices, new Vector3(-0.08f, -0.10f, 0.04f), 0.10f, 0.06f, 0.18f, brown);
                AddBox(vertices, indices, new Vector3( 0.08f, -0.10f, 0.04f), 0.10f, 0.06f, 0.18f, brown);

                // TAIL — short cylinder from back
                AddCylinder(vertices, indices, new Vector3(0, 0.35f, -0.17f), 0.03f, 0.12f, black, 8);

                // STEAMBOAT WHEEL — torus-like ring around the waist
                AddRing(vertices, indices, new Vector3(0, 0.15f, 0), 0.17f, 0.03f, grey, 20);

                // CAPTAIN'S HAT — small cylinder + disc (black crown, white brim)
                AddCylinder(vertices, indices, new Vector3(0, 0.98f, 0), 0.08f, 0.08f, black, 10);
                AddCylinder(vertices, indices, new Vector3(0, 0.96f, 0), 0.15f, 0.03f, white, 12);

                // Build vertex/index buffers
                var vertArray = vertices.ToArray();
                var idxArray = indices.ToArray();

                vertexBuffer = ToDispose(SharpDX.Direct3D11.Buffer.Create(
                    deviceResources.D3DDevice,
                    SharpDX.Direct3D11.BindFlags.VertexBuffer,
                    vertArray));

                indexBuffer = ToDispose(SharpDX.Direct3D11.Buffer.Create(
                    deviceResources.D3DDevice,
                    SharpDX.Direct3D11.BindFlags.IndexBuffer,
                    idxArray));

                indexCount = idxArray.Length;

                // Constant buffer
                modelConstantBuffer = ToDispose(new SharpDX.Direct3D11.Buffer(
                    deviceResources.D3DDevice,
                    SharpDX.Utilities.SizeOf<ModelConstantBuffer>(),
                    SharpDX.Direct3D11.ResourceUsage.Default,
                    SharpDX.Direct3D11.BindFlags.ConstantBuffer,
                    SharpDX.Direct3D11.CpuAccessFlags.None,
                    SharpDX.Direct3D11.ResourceOptionFlags.None, 0));

                modelCB = new ModelConstantBuffer();
                loaded = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MickeyRenderer load error: {ex}");
                loaded = false;
            }
        }

        /// <summary>
        /// Advances renderer-owned animation clocks. Keeping this separate from Render makes
        /// movement consistent when the device's render cadence changes.
        /// </summary>
        public void Update(float elapsedSeconds)
        {
            if (elapsedSeconds <= 0f)
            {
                return;
            }

            float clampedElapsedSeconds = Math.Min(elapsedSeconds, 0.1f);
            idleAnimationTime += clampedElapsedSeconds;

            if (IsHit)
            {
                hitAnimTimer = 0.3f;
                IsHit = false;
            }

            hitAnimTimer = Math.Max(0f, hitAnimTimer - clampedElapsedSeconds);
        }

        /// <summary>
        /// Queues a hit reaction for the next animation update.
        /// </summary>
        public void TriggerHit()
        {
            IsHit = true;
        }

        /// <summary>
        /// Renders Mickey into the scene using indexed instanced drawing (stereo).
        /// </summary>
        public void Render()
        {
            // Keep the model visible for the short hit reaction even after gameplay
            // has marked the target unavailable for scoring.
            if (!loaded || (!IsVisible && hitAnimTimer <= 0f))
                return;

            var ctx = deviceResources.D3DDeviceContext;

            float vibrate = 0f;
            if (hitAnimTimer > 0f)
            {
                float t = hitAnimTimer / 0.3f;
                vibrate = Sin(t * 30f) * 0.03f * t;
            }

            float bob = Sin(idleAnimationTime * 2.4f) * 0.0125f;
            float hitPulse = hitAnimTimer > 0f ? 1f + (0.08f * (hitAnimTimer / 0.3f)) : 1f;
            var animPos = Position + new Vector3(vibrate, bob, 0f);

            var m = Matrix4x4.CreateScale(Scale * hitPulse)
                    * Matrix4x4.CreateTranslation(animPos);
            modelCB.model = Matrix4x4.Transpose(m);
            ctx.UpdateSubresource(ref modelCB, modelConstantBuffer);

            ctx.InputAssembler.InputLayout = inputLayout;
            ctx.InputAssembler.PrimitiveTopology = SharpDX.Direct3D.PrimitiveTopology.TriangleList;
            ctx.InputAssembler.SetVertexBuffers(0,
                new SharpDX.Direct3D11.VertexBufferBinding(
                    vertexBuffer,
                    SharpDX.Utilities.SizeOf<VertexPositionColor>(),
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

        /// <summary>
        /// Releases device-based resources.
        /// </summary>
        public void ReleaseDeviceDependentResources()
        {
            loaded = false;
            RemoveAndDispose(ref vertexShader);
            RemoveAndDispose(ref inputLayout);
            RemoveAndDispose(ref pixelShader);
            RemoveAndDispose(ref geometryShader);
            RemoveAndDispose(ref modelConstantBuffer);
            RemoveAndDispose(ref vertexBuffer);
            RemoveAndDispose(ref indexBuffer);
        }

        protected override void Dispose(bool disposeManagedResources)
        {
            if (!IsDisposed)
            {
                if (disposeManagedResources)
                {
                    ReleaseDeviceDependentResources();
                }
                base.Dispose(disposeManagedResources);
            }
        }

        // ────────────────────────────────────────────────────────
        //  Geometry primitives (all use back-faced CCW winding)
        // ────────────────────────────────────────────────────────

        /// <summary>
        /// Adds an indexed UV sphere to the shared vertex/index lists.
        /// </summary>
        private static void AddSphere(
            List<VertexPositionColor> vertices,
            List<ushort> indices,
            Vector3 center,
            float radius,
            Vector3 color,
            int subdivisions)
        {
            int latBands = subdivisions;
            int lonBands = subdivisions * 2;
            int vertOffset = vertices.Count;

            // Vertices
            for (int lat = 0; lat <= latBands; lat++)
            {
                float theta = lat * Pi / latBands;
                float sinTheta = Sin(theta);
                float cosTheta = Cos(theta);

                for (int lon = 0; lon <= lonBands; lon++)
                {
                    float phi = lon * 2f * Pi / lonBands;
                    float sinPhi = Sin(phi);
                    float cosPhi = Cos(phi);

                    vertices.Add(new VertexPositionColor(
                        center + new Vector3(
                            cosPhi * sinTheta * radius,
                            cosTheta * radius,
                            sinPhi * sinTheta * radius),
                        color));
                }
            }

            // Indices (back-faced, CCW)
            for (int lat = 0; lat < latBands; lat++)
            {
                for (int lon = 0; lon < lonBands; lon++)
                {
                    int first = vertOffset + lat * (lonBands + 1) + lon;
                    int second = first + lonBands + 1;

                    indices.Add((ushort)first);
                    indices.Add((ushort)(second + 1));
                    indices.Add((ushort)second);

                    indices.Add((ushort)first);
                    indices.Add((ushort)(first + 1));
                    indices.Add((ushort)(second + 1));
                }
            }
        }

        /// <summary>
        /// Adds an indexed cylinder (caps + body) to the shared vertex/index lists.
        /// The cylinder axis is along Y.
        /// </summary>
        private static void AddCylinder(
            List<VertexPositionColor> vertices,
            List<ushort> indices,
            Vector3 center,
            float radius,
            float height,
            Vector3 color,
            int segments)
        {
            int vertOffset = vertices.Count;
            float halfH = height * 0.5f;

            // Center vertices for caps
            Vector3 bottomCenter = center + new Vector3(0, -halfH, 0);
            Vector3 topCenter = center + new Vector3(0, halfH, 0);

            vertices.Add(new VertexPositionColor(bottomCenter, color)); // index: bottomCenterIdx
            vertices.Add(new VertexPositionColor(topCenter, color));    // index: topCenterIdx

            int bottomCenterIdx = vertOffset;
            int topCenterIdx = vertOffset + 1;

            // Bottom rim vertices (segments + 1 to close the loop)
            int bottomRimStart = vertices.Count;
            for (int i = 0; i <= segments; i++)
            {
                float angle = i * 2f * Pi / segments;
                float x = Cos(angle) * radius;
                float z = Sin(angle) * radius;
                vertices.Add(new VertexPositionColor(
                    bottomCenter + new Vector3(x, 0, z), color));
            }

            // Top rim vertices
            int topRimStart = vertices.Count;
            for (int i = 0; i <= segments; i++)
            {
                float angle = i * 2f * Pi / segments;
                float x = Cos(angle) * radius;
                float z = Sin(angle) * radius;
                vertices.Add(new VertexPositionColor(
                    topCenter + new Vector3(x, 0, z), color));
            }

            // Bottom cap (fan, CCW when viewed from below)
            for (int i = 0; i < segments; i++)
            {
                indices.Add((ushort)bottomCenterIdx);
                indices.Add((ushort)(bottomRimStart + i + 1));
                indices.Add((ushort)(bottomRimStart + i));
            }

            // Top cap (fan, CCW when viewed from above — reversed order)
            for (int i = 0; i < segments; i++)
            {
                indices.Add((ushort)topCenterIdx);
                indices.Add((ushort)(topRimStart + i));
                indices.Add((ushort)(topRimStart + i + 1));
            }

            // Body (quads → 2 triangles each, CCW from outside)
            for (int i = 0; i < segments; i++)
            {
                int b0 = bottomRimStart + i;
                int b1 = bottomRimStart + i + 1;
                int t0 = topRimStart + i;
                int t1 = topRimStart + i + 1;

                indices.Add((ushort)b0);
                indices.Add((ushort)t1);
                indices.Add((ushort)t0);

                indices.Add((ushort)b0);
                indices.Add((ushort)b1);
                indices.Add((ushort)t1);
            }
        }

        /// <summary>
        /// Adds an indexed box (6 faces, 12 triangles) to the shared vertex/index lists.
        /// </summary>
        private static void AddBox(
            List<VertexPositionColor> vertices,
            List<ushort> indices,
            Vector3 center,
            float width,
            float height,
            float depth,
            Vector3 color)
        {
            float hw = width * 0.5f;
            float hh = height * 0.5f;
            float hd = depth * 0.5f;
            int vertOffset = vertices.Count;

            // 8 corners in same order as SpinningCubeRenderer for index reuse
            Vector3[] corners = new Vector3[]
            {
                center + new Vector3(-hw, -hh, -hd),  // 0
                center + new Vector3(-hw, -hh,  hd),  // 1
                center + new Vector3(-hw,  hh, -hd),  // 2
                center + new Vector3(-hw,  hh,  hd),  // 3
                center + new Vector3( hw, -hh, -hd),  // 4
                center + new Vector3( hw, -hh,  hd),  // 5
                center + new Vector3( hw,  hh, -hd),  // 6
                center + new Vector3( hw,  hh,  hd),  // 7
            };

            foreach (var c in corners)
                vertices.Add(new VertexPositionColor(c, color));

            // 12 triangles — indexed winding matches SpinningCubeRenderer (CCW)
            ushort[] boxIndices = new ushort[]
            {
                2, 1, 0,  2, 3, 1,   // -x
                6, 4, 5,  6, 5, 7,   // +x
                0, 1, 5,  0, 5, 4,   // -y
                2, 6, 7,  2, 7, 3,   // +y
                0, 4, 6,  0, 6, 2,   // -z
                1, 3, 7,  1, 7, 5,   // +z
            };

            foreach (var idx in boxIndices)
                indices.Add((ushort)(vertOffset + idx));
        }

        /// <summary>
        /// Adds a torus-like ring (tube bent into a circle) to the shared vertex/index lists.
        /// The ring lies in the XZ plane.
        /// </summary>
        private static void AddRing(
            List<VertexPositionColor> vertices,
            List<ushort> indices,
            Vector3 center,
            float radius,
            float thickness,
            Vector3 color,
            int segments)
        {
            int tubeSegs = 6; // cross-section resolution for a convincing torus
            int vertOffset = vertices.Count;

            // Generate torus vertices
            for (int i = 0; i <= segments; i++)
            {
                float phi = i * 2f * Pi / segments;
                float cosPhi = Cos(phi);
                float sinPhi = Sin(phi);

                for (int j = 0; j <= tubeSegs; j++)
                {
                    float theta = j * 2f * Pi / tubeSegs;
                    float cosTheta = Cos(theta);
                    float sinTheta = Sin(theta);

                    // Torus: (R + r*cos(theta)) * cos(phi),  r*sin(theta),  (R + r*cos(theta)) * sin(phi)
                    float rCosT = thickness * cosTheta;
                    float x = (radius + rCosT) * cosPhi;
                    float y = thickness * sinTheta;
                    float z = (radius + rCosT) * sinPhi;

                    vertices.Add(new VertexPositionColor(
                        center + new Vector3(x, y, z), color));
                }
            }

            // Indices — quads along the ring, CCW winding
            for (int i = 0; i < segments; i++)
            {
                for (int j = 0; j < tubeSegs; j++)
                {
                    int first = vertOffset + i * (tubeSegs + 1) + j;
                    int second = first + tubeSegs + 1;

                    indices.Add((ushort)first);
                    indices.Add((ushort)(second + 1));
                    indices.Add((ushort)second);

                    indices.Add((ushort)first);
                    indices.Add((ushort)(first + 1));
                    indices.Add((ushort)(second + 1));
                }
            }
        }

        private const float Pi = (float)Math.PI;

        private static float Sin(float value) => (float)Math.Sin(value);

        private static float Cos(float value) => (float)Math.Cos(value);
    }
}
