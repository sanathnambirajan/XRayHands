using UnityEngine;

namespace XRayHands.Core
{
    /// <summary>
    /// A loaded specimen: metadata, defect catalog, CPU voxel arrays (for the probe) and the GPU
    /// textures the volume shader samples. Created by <see cref="VolumeLoader"/>.
    /// </summary>
    public sealed class VolumeDataset : System.IDisposable
    {
        /// <summary>Parsed meta.json.</summary>
        public SpecimenMeta Meta { get; }

        /// <summary>Parsed features.csv.</summary>
        public FeatureCatalog Catalog { get; }

        /// <summary>8-bit density, x fastest.</summary>
        public byte[] Density { get; }

        /// <summary>Label ids, x fastest (0 = no defect).</summary>
        public ushort[] Labels { get; }

        /// <summary>RGBA32: RGB = packed gradient, A = density. Trilinear.</summary>
        public Texture3D DensityGradientTexture { get; private set; }

        /// <summary>RG8 label ids (id = R + 256·G). Point sampled.</summary>
        public Texture3D LabelTexture { get; private set; }

        /// <summary>RG8 min/max density per 8³ brick. Point sampled.</summary>
        public Texture3D BrickTexture { get; private set; }

        /// <summary>Voxel counts.</summary>
        public Vector3Int Dims { get; }

        /// <summary>Physical size of the volume in mm.</summary>
        public Vector3 ExtentMm { get; }

        /// <summary>Creates a dataset from already-loaded arrays. Call <see cref="CreateTextures"/> on the main thread.</summary>
        public VolumeDataset(SpecimenMeta meta, FeatureCatalog catalog, byte[] density, ushort[] labels)
        {
            Meta = meta;
            Catalog = catalog;
            Density = density;
            Labels = labels;
            Dims = meta.Dims;
            ExtentMm = PhysicalUnits.ExtentMm(Dims, meta.SpacingMm);
            int n = Dims.x * Dims.y * Dims.z;
            if (density.Length != n || labels.Length != n)
            {
                throw new System.ArgumentException($"voxel arrays ({density.Length}, {labels.Length}) do not match dims {Dims} = {n}");
            }
        }

        /// <summary>
        /// Bakes gradient and brick grid and uploads the three 3D textures (main thread only).
        /// Textures are made non-readable after upload to free the CPU copy Unity keeps.
        /// </summary>
        public void CreateTextures()
        {
            Color32[] packed = GradientBaker.Bake(Density, Dims, Meta.SpacingMm);
            DensityGradientTexture = new Texture3D(Dims.x, Dims.y, Dims.z, TextureFormat.RGBA32, false)
            {
                name = $"{Meta.id}_density_gradient",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear, // trilinear for 3D textures
            };
            DensityGradientTexture.SetPixels32(packed);
            DensityGradientTexture.Apply(false, true);

            var labelBytes = new byte[Labels.Length * 2];
            for (int i = 0; i < Labels.Length; i++)
            {
                labelBytes[2 * i] = (byte)(Labels[i] & 0xFF);
                labelBytes[2 * i + 1] = (byte)(Labels[i] >> 8);
            }
            LabelTexture = new Texture3D(Dims.x, Dims.y, Dims.z, TextureFormat.RG16, false)
            {
                name = $"{Meta.id}_labels",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point, // ids must never be interpolated
            };
            LabelTexture.SetPixelData(labelBytes, 0);
            LabelTexture.Apply(false, true);

            Vector3Int g = BrickGridBaker.GridDims(Dims);
            BrickTexture = new Texture3D(g.x, g.y, g.z, TextureFormat.RG16, false)
            {
                name = $"{Meta.id}_bricks",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point,
            };
            BrickTexture.SetPixelData(BrickGridBaker.Bake(Density, Dims), 0);
            BrickTexture.Apply(false, true);
        }

        /// <summary>Flat array index of a voxel.</summary>
        public int Index(int x, int y, int z) => (z * Dims.y + y) * Dims.x + x;

        /// <summary>Voxel containing texture coordinate <paramref name="uvw"/> (clamped to the volume).</summary>
        public Vector3Int VoxelAt(Vector3 uvw) => new Vector3Int(
            Mathf.Clamp(Mathf.FloorToInt(uvw.x * Dims.x), 0, Dims.x - 1),
            Mathf.Clamp(Mathf.FloorToInt(uvw.y * Dims.y), 0, Dims.y - 1),
            Mathf.Clamp(Mathf.FloorToInt(uvw.z * Dims.z), 0, Dims.z - 1));

        /// <summary>Nearest-voxel density at <paramref name="uvw"/> (what the probe reports).</summary>
        public byte DensityAt(Vector3 uvw)
        {
            Vector3Int v = VoxelAt(uvw);
            return Density[Index(v.x, v.y, v.z)];
        }

        /// <summary>Label id at <paramref name="uvw"/> (0 = none).</summary>
        public int LabelAt(Vector3 uvw)
        {
            Vector3Int v = VoxelAt(uvw);
            return Labels[Index(v.x, v.y, v.z)];
        }

        /// <summary>Destroys the GPU textures.</summary>
        public void Dispose()
        {
            if (DensityGradientTexture != null) Object.Destroy(DensityGradientTexture);
            if (LabelTexture != null) Object.Destroy(LabelTexture);
            if (BrickTexture != null) Object.Destroy(BrickTexture);
        }
    }
}
