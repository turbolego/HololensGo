// 3D model: "Mickey Mouse - Steamboat Willie Model"
// Source:   https://sketchfab.com/3d-models/mickey-mouse-steamboat-willie-model-3f9bfd873a5e4f3ca2377adf63b25256
// Author:   luqui230610 (https://sketchfab.com/luqui230610)
// License:  CC-BY-4.0 (http://creativecommons.org/licenses/by/4.0/)
// Credit:   This work is based on "Mickey Mouse - Steamboat Willie Model" by luqui230610
//           licensed under CC-BY-4.0 (http://creativecommons.org/licenses/by/4.0/)
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace HololensGo.Content
{
    internal struct GlbMeshData
    {
        public GlbPrimitiveData[] Primitives;
    }

    internal struct GlbPrimitiveData
    {
        public VertexPositionUV[] Vertices;
        public ushort[]           Indices;
        public byte[]             TextureBgra;
        public int                TextureWidth;
        public int                TextureHeight;
    }

    // Wrapper avoids C# 8 tuple syntax not available in UWP C# 7.3 target
    internal struct DecodedBitmap
    {
        public byte[] Pixels;
        public int    Width;
        public int    Height;
        public bool   Valid;
    }

    internal static class GlbLoader
    {
        public static async Task<GlbMeshData> LoadAsync(string packageRelativePath)
        {
            System.Diagnostics.Debug.WriteLine($"[GlbLoader] Loading from path: {packageRelativePath}");
            var folder = Windows.ApplicationModel.Package.Current.InstalledLocation;
            StorageFile file = await folder.GetFileAsync(packageRelativePath.Replace('/', '\\'));
            System.Diagnostics.Debug.WriteLine($"[GlbLoader] File found, size: {(await file.GetBasicPropertiesAsync()).Size} bytes");
            IBuffer buf = await FileIO.ReadBufferAsync(file);
            byte[] data = new byte[buf.Length];
            using (DataReader dr = DataReader.FromBuffer(buf))
                dr.ReadBytes(data);
            System.Diagnostics.Debug.WriteLine($"[GlbLoader] Buffer read: {data.Length} bytes");
            return await ParseAsync(data);
        }

        private static async Task<GlbMeshData> ParseAsync(byte[] data)
        {
            System.Diagnostics.Debug.WriteLine($"[GlbLoader.Parse] Starting, data length: {data.Length}");
            int jsonLen    = BitConverter.ToInt32(data, 12);
            string json    = Encoding.UTF8.GetString(data, 20, jsonLen);
            int binDataOff = 20 + jsonLen + 8; // skip chunk1 header (len+type)
            System.Diagnostics.Debug.WriteLine($"[GlbLoader.Parse] JSON length: {jsonLen}, binDataOff: {binDataOff}");

            var root        = JsonObject.Parse(json);
            var accessors   = root.GetNamedArray("accessors");
            var bufferViews = root.GetNamedArray("bufferViews");
            var primitives = new List<GlbPrimitiveData>();
            System.Diagnostics.Debug.WriteLine($"[GlbLoader.Parse] Accessors: {accessors.Count}, BufferViews: {bufferViews.Count}");

            if (root.ContainsKey("meshes"))
            {
                System.Diagnostics.Debug.WriteLine($"[GlbLoader.Parse] Found meshes");
                foreach (IJsonValue meshVal in root.GetNamedArray("meshes"))
                {
                    foreach (IJsonValue primVal in meshVal.GetObject().GetNamedArray("primitives"))
                    {
                        System.Diagnostics.Debug.WriteLine($"[GlbLoader.Parse] Processing primitive {primitives.Count}");
                        var prim       = primVal.GetObject();
                        var attrs      = prim.GetNamedObject("attributes");
                        int primitiveMode = prim.ContainsKey("mode") ? (int)prim.GetNamedNumber("mode") : 4;
                        if (primitiveMode != 4)
                            throw new InvalidDataException("Only triangle-list GLB primitives are supported.");

                        int posAccIdx  = (int)attrs.GetNamedNumber("POSITION");
                        var pos        = ReadVec3(accessors.GetObjectAt((uint)posAccIdx), bufferViews, data, binDataOff);
                        System.Diagnostics.Debug.WriteLine($"[GlbLoader.Parse] Position array loaded: {pos.Length} verts");
                        if (pos.Length == 0 || pos.Length > ushort.MaxValue + 1)
                            throw new InvalidDataException("GLB primitive vertex count exceeds the 16-bit index limit.");

                        Vector2[] uvs = null;
                        if (attrs.ContainsKey("TEXCOORD_0"))
                        {
                            int uvAccIdx = (int)attrs.GetNamedNumber("TEXCOORD_0");
                            uvs = ReadVec2(accessors.GetObjectAt((uint)uvAccIdx), bufferViews, data, binDataOff);
                            System.Diagnostics.Debug.WriteLine($"[GlbLoader.Parse] UV array loaded: {uvs.Length} uvs");
                        }

                        var vertices = new VertexPositionUV[pos.Length];
                        for (int i = 0; i < pos.Length; i++)
                            vertices[i] = new VertexPositionUV(
                                new Vector3(pos[i].X, pos[i].Y, -pos[i].Z), // flip Z: GLTF→DirectX
                                uvs != null ? uvs[i] : Vector2.Zero);

                        var indices = new List<ushort>();
                        if (prim.ContainsKey("indices"))
                        {
                            int idxAccIdx = (int)prim.GetNamedNumber("indices");
                            var acc       = accessors.GetObjectAt((uint)idxAccIdx);
                            int bvIdx     = (int)acc.GetNamedNumber("bufferView");
                            var bv        = bufferViews.GetObjectAt((uint)bvIdx);
                            int bvOff     = bv.ContainsKey("byteOffset")  ? (int)bv.GetNamedNumber("byteOffset")  : 0;
                            int accOff    = acc.ContainsKey("byteOffset") ? (int)acc.GetNamedNumber("byteOffset") : 0;
                            int count     = (int)acc.GetNamedNumber("count");
                            int ctype     = (int)acc.GetNamedNumber("componentType");
                            int dOff      = binDataOff + bvOff + accOff;

                            if (count == 0 || count % 3 != 0)
                                throw new InvalidDataException("GLB triangle primitive has an invalid index count.");

                            for (int t = 0; t < count; t += 3)
                            {
                                ushort i0 = ReadIdx(data, dOff, t, ctype);
                                ushort i1 = ReadIdx(data, dOff, t + 1, ctype);
                                ushort i2 = ReadIdx(data, dOff, t + 2, ctype);
                                if (i0 >= vertices.Length || i1 >= vertices.Length || i2 >= vertices.Length)
                                    throw new InvalidDataException("GLB primitive index is outside its vertex range.");
                                indices.Add(i0);
                                indices.Add(i2); // reversed for z-flip
                                indices.Add(i1);
                            }
                        }

                        if (indices.Count == 0)
                            throw new InvalidDataException("GLB primitive has no indices.");

                        int materialIndex = prim.ContainsKey("material")
                            ? (int)prim.GetNamedNumber("material")
                            : -1;
                        DecodedBitmap texture = await TryLoadTextureAsync(
                            root, bufferViews, data, binDataOff, materialIndex);
                        primitives.Add(new GlbPrimitiveData
                        {
                            Vertices = vertices,
                            Indices = indices.ToArray(),
                            TextureBgra = texture.Valid ? texture.Pixels : new byte[] { 255, 255, 255, 255 },
                            TextureWidth = texture.Valid ? texture.Width : 1,
                            TextureHeight = texture.Valid ? texture.Height : 1,
                        });
                    }
                }
            }

            System.Diagnostics.Debug.WriteLine($"[GlbLoader.Parse] Returning {primitives.Count} primitives");
            return new GlbMeshData
            {
                Primitives = primitives.ToArray(),
            };
        }

        private static async Task<DecodedBitmap> TryLoadTextureAsync(
            JsonObject root, JsonArray bufferViews, byte[] data, int binDataOff, int materialIndex)
        {
            try
            {
                JsonArray mats = root.ContainsKey("materials") ? root.GetNamedArray("materials") : null;
                JsonArray texs = root.ContainsKey("textures")  ? root.GetNamedArray("textures")  : null;
                JsonArray imgs = root.ContainsKey("images")    ? root.GetNamedArray("images")    : null;
                if (mats == null || texs == null || imgs == null ||
                    materialIndex < 0 || materialIndex >= mats.Count)
                    return default(DecodedBitmap);

                var mat    = mats.GetObjectAt((uint)materialIndex);
                int texIdx = -1;

                if (mat.ContainsKey("pbrMetallicRoughness"))
                {
                    var pbr = mat.GetNamedObject("pbrMetallicRoughness");
                    if (pbr.ContainsKey("baseColorTexture"))
                        texIdx = (int)pbr.GetNamedObject("baseColorTexture").GetNamedNumber("index");
                }
                if (texIdx < 0 && mat.ContainsKey("extensions"))
                {
                    var exts = mat.GetNamedObject("extensions");
                    if (exts.ContainsKey("KHR_materials_pbrSpecularGlossiness"))
                    {
                        var sg = exts.GetNamedObject("KHR_materials_pbrSpecularGlossiness");
                        if (sg.ContainsKey("diffuseTexture"))
                            texIdx = (int)sg.GetNamedObject("diffuseTexture").GetNamedNumber("index");
                    }
                }
                if (texIdx < 0) return default(DecodedBitmap);

                var tex    = texs.GetObjectAt((uint)texIdx);
                int imgIdx = tex.ContainsKey("source") ? (int)tex.GetNamedNumber("source") : 0;
                var img    = imgs.GetObjectAt((uint)imgIdx);

                if (!img.ContainsKey("bufferView")) return default(DecodedBitmap);

                int bvI   = (int)img.GetNamedNumber("bufferView");
                var bv    = bufferViews.GetObjectAt((uint)bvI);
                int bvOff = bv.ContainsKey("byteOffset") ? (int)bv.GetNamedNumber("byteOffset") : 0;
                int bvLen = (int)bv.GetNamedNumber("byteLength");

                byte[] pngBytes = new byte[bvLen];
                Array.Copy(data, binDataOff + bvOff, pngBytes, 0, bvLen);
                return await DecodePngAsync(pngBytes);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("GlbLoader texture: " + ex.Message);
                return default(DecodedBitmap);
            }
        }

        private static async Task<DecodedBitmap> DecodePngAsync(byte[] pngBytes)
        {
            // Use InMemoryRandomAccessStream — MemoryStream.AsRandomAccessStream()
            // is a C#8/WinRT extension not available on this UWP target
            var ras = new InMemoryRandomAccessStream();
            try
            {
                using (var dw = new DataWriter(ras))
                {
                    dw.WriteBytes(pngBytes);
                    await dw.StoreAsync();
                    dw.DetachStream();
                }
                ras.Seek(0);

                var decoder = await BitmapDecoder.CreateAsync(ras);
                const uint maxTextureDimension = 1024;
                SoftwareBitmap bitmap;
                if (decoder.PixelWidth > maxTextureDimension || decoder.PixelHeight > maxTextureDimension)
                {
                    double scale = Math.Min(
                        (double)maxTextureDimension / decoder.PixelWidth,
                        (double)maxTextureDimension / decoder.PixelHeight);
                    var transform = new BitmapTransform
                    {
                        ScaledWidth = (uint)Math.Max(1, Math.Round(decoder.PixelWidth * scale)),
                        ScaledHeight = (uint)Math.Max(1, Math.Round(decoder.PixelHeight * scale)),
                        InterpolationMode = BitmapInterpolationMode.Fant,
                    };
                    bitmap = await decoder.GetSoftwareBitmapAsync(
                        BitmapPixelFormat.Bgra8,
                        BitmapAlphaMode.Premultiplied,
                        transform,
                        ExifOrientationMode.IgnoreExifOrientation,
                        ColorManagementMode.DoNotColorManage);
                }
                else
                {
                    bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
                }

                using (bitmap)
                {
                    int w = bitmap.PixelWidth;
                    int h = bitmap.PixelHeight;
                    byte[] pixels = new byte[w * h * 4];
                    var outBuf = new Windows.Storage.Streams.Buffer((uint)pixels.Length);
                    bitmap.CopyToBuffer(outBuf);
                    using (DataReader dr = DataReader.FromBuffer(outBuf))
                        dr.ReadBytes(pixels);

                    return new DecodedBitmap { Pixels = pixels, Width = w, Height = h, Valid = true };
                }
            }
            finally
            {
                ras.Dispose();
            }
        }

        private static Vector3[] ReadVec3(JsonObject acc, JsonArray bvs, byte[] data, int binOff)
        {
            int bvI   = (int)acc.GetNamedNumber("bufferView");
            var bv    = bvs.GetObjectAt((uint)bvI);
            int bvOff = bv.ContainsKey("byteOffset")  ? (int)bv.GetNamedNumber("byteOffset")  : 0;
            int aOff  = acc.ContainsKey("byteOffset") ? (int)acc.GetNamedNumber("byteOffset") : 0;
            int count = (int)acc.GetNamedNumber("count");
            int dOff  = binOff + bvOff + aOff;
            var result = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                int o = dOff + i * 12;
                result[i] = new Vector3(
                    BitConverter.ToSingle(data, o),
                    BitConverter.ToSingle(data, o + 4),
                    BitConverter.ToSingle(data, o + 8));
            }
            return result;
        }

        private static Vector2[] ReadVec2(JsonObject acc, JsonArray bvs, byte[] data, int binOff)
        {
            int bvI   = (int)acc.GetNamedNumber("bufferView");
            var bv    = bvs.GetObjectAt((uint)bvI);
            int bvOff = bv.ContainsKey("byteOffset")  ? (int)bv.GetNamedNumber("byteOffset")  : 0;
            int aOff  = acc.ContainsKey("byteOffset") ? (int)acc.GetNamedNumber("byteOffset") : 0;
            int count = (int)acc.GetNamedNumber("count");
            int dOff  = binOff + bvOff + aOff;
            var result = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                int o = dOff + i * 8;
                result[i] = new Vector2(
                    BitConverter.ToSingle(data, o),
                    BitConverter.ToSingle(data, o + 4));
            }
            return result;
        }

        private static ushort ReadIdx(byte[] data, int baseOff, int element, int componentType)
        {
            if (componentType == 5125) // UNSIGNED_INT
            {
                uint index = BitConverter.ToUInt32(data, baseOff + element * 4);
                if (index > ushort.MaxValue)
                    throw new InvalidDataException("GLB index exceeds the 16-bit index limit.");
                return (ushort)index;
            }
            if (componentType == 5121) // UNSIGNED_BYTE
                return data[baseOff + element];
            if (componentType == 5123) // UNSIGNED_SHORT
                return BitConverter.ToUInt16(data, baseOff + element * 2);
            throw new InvalidDataException("Unsupported GLB index component type.");
        }
    }
}
