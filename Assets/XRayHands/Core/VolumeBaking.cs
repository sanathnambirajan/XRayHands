using UnityEngine;

namespace XRayHands.Core
{
    /// <summary>
    /// Precomputes, once at load time, the data the ray marcher would otherwise compute per
    /// sample. Both bakers work on CPU arrays in Texture3D order (x fastest).
    /// </summary>
    public static class GradientBaker
    {
        /// <summary>
        /// Packs density and its gradient into one RGBA32 voxel: RGB = gradient mapped from
        /// [-1,1] to [0,255], A = density.
        /// </summary>
        /// <remarks>
        /// Why: shading needs a normal and gradient magnitude at every sample. Computing them
        /// in the shader costs 6 extra 3D texture fetches per sample (central differences),
        /// which is the dominant cost on a mobile GPU. Baking makes it one fetch. The gradient
        /// is divided by the largest magnitude in the volume so the packed vector keeps both
        /// direction and relative magnitude (needed for gradient-magnitude weighting).
        /// Central differences use the physical voxel spacing, so anisotropic volumes shade
        /// correctly.
        /// </remarks>
        public static Color32[] Bake(byte[] density, Vector3Int dims, Vector3 spacingMm)
        {
            int nx = dims.x, ny = dims.y, nz = dims.z;
            int sliceStride = nx * ny;
            var gx = new float[density.Length];
            var gy = new float[density.Length];
            var gz = new float[density.Length];
            float maxMagSq = 1e-12f;

            for (int z = 0; z < nz; z++)
            {
                int zm = Mathf.Max(z - 1, 0), zp = Mathf.Min(z + 1, nz - 1);
                for (int y = 0; y < ny; y++)
                {
                    int ym = Mathf.Max(y - 1, 0), yp = Mathf.Min(y + 1, ny - 1);
                    int row = z * sliceStride + y * nx;
                    for (int x = 0; x < nx; x++)
                    {
                        int xm = Mathf.Max(x - 1, 0), xp = Mathf.Min(x + 1, nx - 1);
                        int i = row + x;
                        float dx = (density[row + xp] - density[row + xm]) / ((xp - xm) * spacingMm.x);
                        float dy = (density[z * sliceStride + yp * nx + x] - density[z * sliceStride + ym * nx + x]) / ((yp - ym) * spacingMm.y);
                        float dz = (density[zp * sliceStride + y * nx + x] - density[zm * sliceStride + y * nx + x]) / ((zp - zm) * spacingMm.z);
                        gx[i] = dx; gy[i] = dy; gz[i] = dz;
                        float m = dx * dx + dy * dy + dz * dz;
                        if (m > maxMagSq) maxMagSq = m;
                    }
                }
            }

            float inv = 1f / Mathf.Sqrt(maxMagSq);
            var packed = new Color32[density.Length];
            for (int i = 0; i < density.Length; i++)
            {
                packed[i] = new Color32(Pack(gx[i] * inv), Pack(gy[i] * inv), Pack(gz[i] * inv), density[i]);
            }
            return packed;
        }

        private static byte Pack(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt((v * 0.5f + 0.5f) * 255f), 0, 255);

        /// <summary>Inverse of the packing, for tests: byte → [-1, 1].</summary>
        public static float Unpack(byte b) => b / 255f * 2f - 1f;
    }

    /// <summary>
    /// Builds the empty-space-skipping grid: for every 8×8×8 brick, the min and max density.
    /// </summary>
    /// <remarks>
    /// The ray marcher reads the brick under the current sample; if the brick's max density is
    /// below what the current transfer function makes visible, it jumps to the brick exit
    /// instead of sampling every voxel. Air around and inside a specimen is often &gt;50 % of
    /// the volume, so this roughly halves the samples. Bricks overlap their neighbours by one
    /// voxel so trilinear filtering at brick borders can never read a value the brick
    /// summary missed.
    /// </remarks>
    public static class BrickGridBaker
    {
        /// <summary>Brick edge length in voxels.</summary>
        public const int BrickSize = 8;

        /// <summary>Number of bricks per axis for a volume of <paramref name="dims"/>.</summary>
        public static Vector3Int GridDims(Vector3Int dims) => new Vector3Int(
            (dims.x + BrickSize - 1) / BrickSize, (dims.y + BrickSize - 1) / BrickSize, (dims.z + BrickSize - 1) / BrickSize);

        /// <summary>Returns RG pairs (min, max) per brick in Texture3D order.</summary>
        public static byte[] Bake(byte[] density, Vector3Int dims)
        {
            Vector3Int g = GridDims(dims);
            var minMax = new byte[g.x * g.y * g.z * 2];
            int sliceStride = dims.x * dims.y;
            for (int bz = 0; bz < g.z; bz++)
            for (int by = 0; by < g.y; by++)
            for (int bx = 0; bx < g.x; bx++)
            {
                byte lo = 255, hi = 0;
                int x0 = Mathf.Max(bx * BrickSize - 1, 0), x1 = Mathf.Min((bx + 1) * BrickSize + 1, dims.x);
                int y0 = Mathf.Max(by * BrickSize - 1, 0), y1 = Mathf.Min((by + 1) * BrickSize + 1, dims.y);
                int z0 = Mathf.Max(bz * BrickSize - 1, 0), z1 = Mathf.Min((bz + 1) * BrickSize + 1, dims.z);
                for (int z = z0; z < z1; z++)
                for (int y = y0; y < y1; y++)
                {
                    int row = z * sliceStride + y * dims.x;
                    for (int x = x0; x < x1; x++)
                    {
                        byte v = density[row + x];
                        if (v < lo) lo = v;
                        if (v > hi) hi = v;
                    }
                }
                int o = ((bz * g.y + by) * g.x + bx) * 2;
                minMax[o] = lo;
                minMax[o + 1] = hi;
            }
            return minMax;
        }
    }
}
