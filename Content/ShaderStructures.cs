using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace HololensGo.Content
{
    /// <summary>
    /// Constant buffer used to send hologram position transform to the shader pipeline.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct ModelConstantBuffer
    {
        public Matrix4x4 model;
    }

    /// <summary>
    /// Vertex with position and color (for cubes, spheres).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct VertexPositionColor
    {
        public Vector3 pos;
        public Vector3 color;

        public VertexPositionColor(Vector3 pos, Vector3 color)
        {
            this.pos = pos;
            this.color = color;
        }
    }

    /// <summary>
    /// Vertex with position and normal (for lit geometry).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct VertexPositionNormal
    {
        public Vector3 pos;
        public Vector3 normal;

        public VertexPositionNormal(Vector3 pos, Vector3 normal)
        {
            this.pos = pos;
            this.normal = normal;
        }
    }

    /// <summary>
    /// Simple UV quad for the glyph atlas (retained for reusable text rendering).
    /// </summary>
    internal struct UvRect
    {
        public float U0, V0, U1, V1;
        public UvRect(float u0, float v0, float u1, float v1)
        {
            U0 = u0; V0 = v0; U1 = u1; V1 = v1;
        }
    }
}