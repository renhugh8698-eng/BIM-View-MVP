using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Web.Script.Serialization;

namespace BIMExporter.Export
{
    public class MeshData
    {
        public List<float> Positions { get; private set; }
        public List<int> Indices { get; private set; }
        public List<float> Normals { get; private set; }
        public List<float> UVs { get; private set; }
        private readonly Dictionary<VertexKey, int> _vertices = new Dictionary<VertexKey, int>();
        public MaterialColor Material { get; set; }

        public MeshData()
        {
            Positions = new List<float>();
            Indices = new List<int>();
            Normals = new List<float>();
            UVs = new List<float>();
            Material = new MaterialColor(180, 180, 180, 1.0f);
        }

        public void AddVertex(Vector3 position, Vector3 normal, Vector2 uv)
        {
            var key = new VertexKey { Position = position, Normal = normal, UV = uv };
            int index;
            if (!_vertices.TryGetValue(key, out index))
            {
                index = Positions.Count / 3;
                _vertices.Add(key, index);
                Positions.AddRange(new[] { position.X, position.Y, position.Z });
                Normals.AddRange(new[] { normal.X, normal.Y, normal.Z });
                UVs.AddRange(new[] { uv.X, uv.Y });
            }
            Indices.Add(index);
        }

        private struct VertexKey : IEquatable<VertexKey>
        {
            public Vector3 Position;
            public Vector3 Normal;
            public Vector2 UV;
            public bool Equals(VertexKey other) => Position.Equals(other.Position) && Normal.Equals(other.Normal) && UV.Equals(other.UV);
            public override bool Equals(object obj) => obj is VertexKey && Equals((VertexKey)obj);
            public override int GetHashCode() => Position.GetHashCode() ^ (Normal.GetHashCode() * 397) ^ (UV.GetHashCode() * 17);
        }
    }

    public class MaterialColor : IEquatable<MaterialColor>
    {
        public byte R { get; private set; }
        public byte G { get; private set; }
        public byte B { get; private set; }
        public float A { get; private set; }
        public string Name { get; private set; }
        public string SourceId { get; private set; }
        public float Roughness { get; private set; }

        public MaterialColor(byte r, byte g, byte b, float a, string name = "", string sourceId = "", float roughness = .65f)
        {
            R = r;
            G = g;
            B = b;
            A = Math.Max(0, Math.Min(1, a));
            Name = name;
            SourceId = sourceId;
            Roughness = Math.Max(.04f, Math.Min(1, roughness));
        }

        public bool Equals(MaterialColor other)
        {
            return other != null && R == other.R && G == other.G && B == other.B && A.Equals(other.A)
                && Name == other.Name && SourceId == other.SourceId && Roughness.Equals(other.Roughness);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as MaterialColor);
        }

        public override int GetHashCode()
        {
            return (R << 24 ^ G << 16 ^ B << 8 ^ A.GetHashCode())
                ^ (SourceId.GetHashCode() * 397) ^ Name.GetHashCode() ^ Roughness.GetHashCode();
        }
    }

    public class GlbBuilder
    {
        public object Package { get; set; }
        private readonly List<object> _nodes = new List<object>();
        private readonly List<object> _meshes = new List<object>();
        private readonly List<object> _materials = new List<object>();
        private readonly List<object> _buffersViews = new List<object>();
        private readonly List<object> _accessors = new List<object>();
        private readonly List<int> _sceneNodes = new List<int>();
        private readonly Dictionary<MaterialColor, int> _materialMap = new Dictionary<MaterialColor, int>();
        private readonly MemoryStream _bin = new MemoryStream();

        public bool AddElement(string bimId, string displayName, IReadOnlyList<MeshData> meshes)
        {
            var primitives = new List<object>();

            foreach (var mesh in meshes)
            {
                if (mesh.Positions.Count == 0 || mesh.Indices.Count == 0)
                    continue;

                var positionAccessor = AddFloatAccessor(mesh.Positions, "VEC3");
                var indexAccessor = AddIndexAccessor(mesh.Indices);
                var attributes = new Dictionary<string, object> { ["POSITION"] = positionAccessor };
                if (mesh.Normals.Count == mesh.Positions.Count) attributes["NORMAL"] = AddFloatAccessor(mesh.Normals, "VEC3");
                if (mesh.UVs.Count == mesh.Positions.Count / 3 * 2) attributes["TEXCOORD_0"] = AddFloatAccessor(mesh.UVs, "VEC2");
                primitives.Add(new Dictionary<string, object>
                {
                    ["attributes"] = attributes,
                    ["indices"] = indexAccessor,
                    ["material"] = GetMaterial(mesh.Material),
                    ["mode"] = 4
                });
            }

            if (primitives.Count == 0)
                return false;

            var meshIndex = _meshes.Count;
            _meshes.Add(new Dictionary<string, object>
            {
                ["name"] = displayName,
                ["primitives"] = primitives
            });

            var nodeIndex = _nodes.Count;
            _nodes.Add(new Dictionary<string, object>
            {
                ["name"] = bimId,
                ["mesh"] = meshIndex,
                ["extras"] = new Dictionary<string, object>
                {
                    ["bimId"] = bimId,
                    ["label"] = displayName
                }
            });
            _sceneNodes.Add(nodeIndex);

            return true;
        }

        public void Write(string path)
        {
            var json = BuildJson();
            var jsonBytes = Pad(Encoding.UTF8.GetBytes(json), 0x20);
            var binBytes = Pad(_bin.ToArray(), 0x00);

            using (var stream = File.Create(path))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(0x46546C67);
                writer.Write(2);
                writer.Write(12 + 8 + jsonBytes.Length + 8 + binBytes.Length);

                writer.Write(jsonBytes.Length);
                writer.Write(0x4E4F534A);
                writer.Write(jsonBytes);

                writer.Write(binBytes.Length);
                writer.Write(0x004E4942);
                writer.Write(binBytes);
            }
        }

        private string BuildJson()
        {
            var root = new Dictionary<string, object>
            {
                ["asset"] = new Dictionary<string, object>
                {
                    ["version"] = "2.0",
                    ["generator"] = "BIMExporter 0.3.0 CustomExporter"
                },
                ["scene"] = 0,
                ["scenes"] = new[]
                {
                    new Dictionary<string, object> { ["nodes"] = new[] { _nodes.Count } }
                },
                ["nodes"] = _nodes.Concat(new object[] { new Dictionary<string, object>
                {
                    ["name"] = "Revit feet Z-up to meters Y-up",
                    ["children"] = _sceneNodes,
                    ["matrix"] = new double[] { .3048,0,0,0, 0,0,-.3048,0, 0,.3048,0,0, 0,0,0,1 }
                }}).ToList(),
                ["extras"] = new Dictionary<string, object> { ["bimPackage"] = Package },
                ["meshes"] = _meshes,
                ["materials"] = _materials,
                ["buffers"] = new[]
                {
                    new Dictionary<string, object> { ["byteLength"] = _bin.Length }
                },
                ["bufferViews"] = _buffersViews,
                ["accessors"] = _accessors
            };

            return new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 256 }.Serialize(root);
        }

        private int AddFloatAccessor(IReadOnlyList<float> values, string type)
        {
            var offset = AlignBin();
            using (var writer = new BinaryWriter(_bin, Encoding.UTF8, true))
            {
                foreach (var value in values)
                    writer.Write(value);
            }

            var byteLength = values.Count * sizeof(float);
            var viewIndex = _buffersViews.Count;
            _buffersViews.Add(new Dictionary<string, object>
            {
                ["buffer"] = 0,
                ["byteOffset"] = offset,
                ["byteLength"] = byteLength,
                ["target"] = 34962
            });

            var components = type == "VEC2" ? 2 : 3;
            var mins = Enumerable.Repeat(float.MaxValue, components).ToArray();
            var maxs = Enumerable.Repeat(float.MinValue, components).ToArray();
            for (var i = 0; i < values.Count; i += components)
            {
                for (var axis = 0; axis < components; axis++)
                {
                    mins[axis] = Math.Min(mins[axis], values[i + axis]);
                    maxs[axis] = Math.Max(maxs[axis], values[i + axis]);
                }
            }

            var accessorIndex = _accessors.Count;
            _accessors.Add(new Dictionary<string, object>
            {
                ["bufferView"] = viewIndex,
                ["byteOffset"] = 0,
                ["componentType"] = 5126,
                ["count"] = values.Count / components,
                ["type"] = type,
                ["min"] = mins.Select(ToJsonNumber).ToArray(),
                ["max"] = maxs.Select(ToJsonNumber).ToArray()
            });
            return accessorIndex;
        }

        private int AddIndexAccessor(IReadOnlyList<int> indices)
        {
            var offset = AlignBin();
            using (var writer = new BinaryWriter(_bin, Encoding.UTF8, true))
            {
                foreach (var index in indices)
                    writer.Write((uint)index);
            }

            var byteLength = indices.Count * sizeof(uint);
            var viewIndex = _buffersViews.Count;
            _buffersViews.Add(new Dictionary<string, object>
            {
                ["buffer"] = 0,
                ["byteOffset"] = offset,
                ["byteLength"] = byteLength,
                ["target"] = 34963
            });

            var accessorIndex = _accessors.Count;
            _accessors.Add(new Dictionary<string, object>
            {
                ["bufferView"] = viewIndex,
                ["byteOffset"] = 0,
                ["componentType"] = 5125,
                ["count"] = indices.Count,
                ["type"] = "SCALAR",
                ["min"] = new[] { indices.Min() },
                ["max"] = new[] { indices.Max() }
            });
            return accessorIndex;
        }

        private int GetMaterial(MaterialColor color)
        {
            int index;
            if (_materialMap.TryGetValue(color, out index))
                return index;

            index = _materials.Count;
            _materialMap[color] = index;
            _materials.Add(new Dictionary<string, object>
            {
                ["name"] = string.IsNullOrEmpty(color.Name) ? string.Format(CultureInfo.InvariantCulture, "rgb_{0}_{1}_{2}", color.R, color.G, color.B) : color.Name,
                ["extras"] = new { revitMaterialId = color.SourceId, representation = "Revit shading", roughnessMapping = "Approximate shininess conversion" },
                ["pbrMetallicRoughness"] = new Dictionary<string, object>
                {
                    ["baseColorFactor"] = new object[]
                    {
                        LinearColor(color.R),
                        LinearColor(color.G),
                        LinearColor(color.B),
                        ToJsonNumber(color.A)
                    },
                    ["metallicFactor"] = 0,
                    ["roughnessFactor"] = ToJsonNumber(color.Roughness)
                },
                ["doubleSided"] = true,
                ["alphaMode"] = color.A < 1 ? "BLEND" : "OPAQUE"
            });
            return index;
        }

        private static double LinearColor(byte channel)
        {
            var value = channel / 255.0;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
        }

        private int AlignBin()
        {
            while (_bin.Length % 4 != 0)
                _bin.WriteByte(0);
            return (int)_bin.Length;
        }

        private static byte[] Pad(byte[] bytes, byte pad)
        {
            var paddedLength = bytes.Length + ((4 - bytes.Length % 4) % 4);
            if (paddedLength == bytes.Length)
                return bytes;

            var output = new byte[paddedLength];
            Buffer.BlockCopy(bytes, 0, output, 0, bytes.Length);
            for (var i = bytes.Length; i < output.Length; i++)
                output[i] = pad;
            return output;
        }

        private static double ToJsonNumber(float value)
        {
            return value;
        }
    }
}
