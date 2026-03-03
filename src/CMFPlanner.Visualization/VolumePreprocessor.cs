using System.Diagnostics;
using CMFPlanner.Core.Models;

namespace CMFPlanner.Visualization;

/// <summary>
/// Pre-processes a CT HU volume before marching-cubes extraction to suppress metal
/// artifacts and noise.  Implements the same pipeline as dicom2stl (NIH) in pure C#
/// without any VTK or ITK dependency.
///
/// Pipeline (all steps run on the calling thread; wrap in Task.Run for async use):
///   1. HU clamping      — removes metal spike outliers (&gt;2000 HU) and low noise
///   2. Anisotropic diffusion (Perona-Malik, 5 passes) — smooths noise while
///      preserving bone edges
///   3. Double threshold  — creates binary mask: 1 = bone (200–1800 HU), 0 = other
///   4. 3×3×3 majority-vote median — removes isolated binary noise voxels
///
/// The returned volume contains only values 0 and 1.
/// Pass it to <see cref="MarchingCubesExtractor"/> with threshold = 1 to extract
/// a clean bone isosurface.
/// </summary>
public static class VolumePreprocessor
{
    // ── Tunable parameters ───────────────────────────────────────────────────

    /// <summary>HU above which a voxel is considered metal (dental implants, plates).</summary>
    private const short MetalThresholdHu = 2500;
    /// <summary>Replacement value for metal voxels (cortical bone density).</summary>
    private const short MetalReplacementHu = 1200;
    /// <summary>Upper HU clamp applied after inpainting (safety net).</summary>
    private const short MaxHuClamp = 2000;
    /// <summary>
    /// Lower HU clamp threshold: values below this are considered noise/air outliers.
    /// </summary>
    private const short MinHuClampThreshold = -1500;
    /// <summary>Replacement value for voxels below <see cref="MinHuClampThreshold"/>.</summary>
    private const short AirHu = -1000;

    /// <summary>Number of Perona-Malik diffusion iterations.</summary>
    private const int DiffusionIterations = 5;
    /// <summary>
    /// Conductance in HU.  Controls which gradients are treated as "edges" to
    /// preserve.  Bone/soft-tissue boundary is typically 100–300 HU; 50 HU is
    /// conservative enough to keep sharp bone edges intact.
    /// </summary>
    private const float DiffusionConductance = 50f;
    /// <summary>
    /// Time step for diffusion.  0.0625 = 1/16 satisfies the stability criterion
    /// for 3-D diffusion (dt &lt; 1/6 in continuous formulation).
    /// </summary>
    private const float DiffusionTimeStep = 0.0625f;

    /// <summary>Lower bone HU threshold (Mimics Medical 21.0 default: 490).</summary>
    private const short BoneLowerHu = 490;
    /// <summary>Upper bone HU threshold (Mimics Medical 21.0 default: 1772).</summary>
    private const short BoneUpperHu = 1772;

    // ── Public entry point ───────────────────────────────────────────────────

    /// <summary>
    /// Runs the full metal-artifact-reduction preprocessing pipeline and returns a
    /// binary <see cref="VolumeData"/> (0 = not bone, 1 = bone).
    ///
    /// <para>This method is CPU-bound and synchronous.  Call inside Task.Run for
    /// non-blocking use.</para>
    /// </summary>
    /// <param name="source">Original CT volume (not mutated).</param>
    /// <param name="progress">
    ///   Optional progress sink; receives step labels such as "MAR 1/4: …".
    /// </param>
    /// <param name="ct">Cancellation token checked between each pipeline stage.</param>
    public static VolumeData PreprocessForBone(
        VolumeData source,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        Debug.WriteLine("MAR Pipeline - START");
        LogVolumeStats("Input", source);

        progress?.Report("MAR 1/5: Metal inpainting (streak suppression)…");
        Debug.WriteLine("MAR Step 1: InpaintMetal - START");
        var inpainted = InpaintMetal(source);
        Debug.WriteLine("MAR Step 1: InpaintMetal - DONE");
        LogVolumeStats("After inpainting", inpainted);
        ct.ThrowIfCancellationRequested();

        progress?.Report("MAR 2/5: Clamping HU range…");
        Debug.WriteLine("MAR Step 2: ClampHu - START");
        var clamped = ClampHu(inpainted);
        Debug.WriteLine("MAR Step 2: ClampHu - DONE");
        LogVolumeStats("After clamp", clamped);
        ct.ThrowIfCancellationRequested();

        progress?.Report("MAR 3/5: Anisotropic diffusion (5 passes)…");
        Debug.WriteLine("MAR Step 3: AnisotropicDiffusion - START");
        var diffused = ApplyAnisotropicDiffusion(clamped, ct);
        Debug.WriteLine("MAR Step 3: AnisotropicDiffusion - DONE");
        LogVolumeStats("After diffusion", diffused);
        ct.ThrowIfCancellationRequested();

        progress?.Report("MAR 4/5: Thresholding to bone mask…");
        Debug.WriteLine("MAR Step 4: DoubleThreshold - START");
        var binary = ApplyDoubleThreshold(diffused);
        Debug.WriteLine("MAR Step 4: DoubleThreshold - DONE");
        LogBinaryStats("After threshold", binary);
        ct.ThrowIfCancellationRequested();

        progress?.Report("MAR 5/5: Median filter (noise removal)…");
        Debug.WriteLine("MAR Step 5: BinaryMajorityFilter - START");
        var result = ApplyBinaryMajorityFilter(binary, ct);
        Debug.WriteLine("MAR Step 5: BinaryMajorityFilter - DONE");
        LogBinaryStats("After majority filter", result);

        Debug.WriteLine("MAR Pipeline - COMPLETE");
        return result;
    }

    private static void LogVolumeStats(string label, VolumeData vol)
    {
        short min = short.MaxValue, max = short.MinValue;
        long above1800 = 0, above2500 = 0;
        foreach (short v in vol.HuBuffer)
        {
            if (v < min) min = v;
            if (v > max) max = v;
            if (v > 1800) above1800++;
            if (v > 2500) above2500++;
        }
        Debug.WriteLine($"  [{label}] min={min}, max={max}, voxels>1800={above1800}, voxels>2500={above2500}, total={vol.HuBuffer.Length}");
    }

    private static void LogBinaryStats(string label, VolumeData vol)
    {
        long ones = 0;
        foreach (short v in vol.HuBuffer)
            if (v > 0) ones++;
        Debug.WriteLine($"  [{label}] bone voxels={ones}, air voxels={vol.HuBuffer.Length - ones}, total={vol.HuBuffer.Length}");
    }

    // ── Step 0: Metal inpainting ──────────────────────────────────────────────

    /// <summary>
    /// Clamps metal wire-core voxels (&gt; <see cref="MetalThresholdHu"/>) down to cortical
    /// bone density (<see cref="MetalReplacementHu"/>).  With the raised lower threshold
    /// (490 HU, matching Mimics), orthodontic streak artifacts (300–450 HU) are excluded
    /// automatically by the double-threshold step, so neighborhood averaging is no longer
    /// needed.
    /// </summary>
    private static VolumeData InpaintMetal(VolumeData source)
    {
        var buf = (short[])source.HuBuffer.Clone();
        for (int i = 0; i < buf.Length; i++)
            if (buf[i] > MetalThresholdHu)
                buf[i] = MetalReplacementHu;
        return CopyWithBuffer(source, buf);
    }

    // ── Step 1: HU clamping ──────────────────────────────────────────────────

    private static VolumeData ClampHu(VolumeData source)
    {
        var buf = (short[])source.HuBuffer.Clone();
        for (int i = 0; i < buf.Length; i++)
        {
            if (buf[i] > MaxHuClamp)           buf[i] = MaxHuClamp;
            if (buf[i] < MinHuClampThreshold)  buf[i] = AirHu;
        }
        return CopyWithBuffer(source, buf);
    }

    // ── Step 2: Perona-Malik anisotropic diffusion ───────────────────────────

    /// <summary>
    /// 3-D Perona-Malik anisotropic diffusion.
    /// Update rule (face-connected, 6-neighbour):
    ///   I(t+1) = I(t) + dt * Σ_d [ c(∇I_d) * ∇I_d ]
    /// where c(x) = exp(−x²/K²) and K = conductance.
    /// Smooths within uniform regions while leaving steep gradients (bone edges) intact.
    /// </summary>
    private static VolumeData ApplyAnisotropicDiffusion(VolumeData vol, CancellationToken ct)
    {
        int nx = vol.Columns;
        int ny = vol.Rows;
        int nz = vol.SliceCount;
        int sliceStride = nx * ny;
        int total       = sliceStride * nz;

        // Promote to float for numerical precision during diffusion.
        var curr = new float[total];
        for (int i = 0; i < total; i++) curr[i] = vol.HuBuffer[i];
        var next = new float[total];

        float kSq = DiffusionConductance * DiffusionConductance;

        for (int iter = 0; iter < DiffusionIterations; iter++)
        {
            ct.ThrowIfCancellationRequested();

            // Copy all values so border voxels (excluded from the inner loop) are
            // preserved unchanged.
            Array.Copy(curr, next, total);

            Parallel.For(1, nz - 1, z =>
            {
                int zOff = z * sliceStride;

                for (int y = 1; y < ny - 1; y++)
                {
                    int yOff = zOff + y * nx;

                    for (int x = 1; x < nx - 1; x++)
                    {
                        int idx = yOff + x;
                        float v = curr[idx];

                        // Finite differences to 6 face-connected neighbours.
                        float dN = curr[idx - nx]         - v;   // y−1
                        float dS = curr[idx + nx]         - v;   // y+1
                        float dE = curr[idx + 1]           - v;   // x+1
                        float dW = curr[idx - 1]           - v;   // x−1
                        float dU = curr[idx + sliceStride] - v;   // z+1
                        float dD = curr[idx - sliceStride] - v;   // z−1

                        // Perona-Malik conductance: 1 inside flat regions, ≈0 at edges.
                        float cN = MathF.Exp(-dN * dN / kSq);
                        float cS = MathF.Exp(-dS * dS / kSq);
                        float cE = MathF.Exp(-dE * dE / kSq);
                        float cW = MathF.Exp(-dW * dW / kSq);
                        float cU = MathF.Exp(-dU * dU / kSq);
                        float cD = MathF.Exp(-dD * dD / kSq);

                        next[idx] = v + DiffusionTimeStep *
                            (cN * dN + cS * dS + cE * dE + cW * dW + cU * dU + cD * dD);
                    }
                }
            });

            // Swap buffers in-place.
            (curr, next) = (next, curr);
        }

        // Convert back to short, clamped to valid range.
        var outBuf = new short[total];
        for (int i = 0; i < total; i++)
            outBuf[i] = (short)Math.Clamp((int)MathF.Round(curr[i]), short.MinValue, short.MaxValue);

        return CopyWithBuffer(vol, outBuf);
    }

    // ── Step 3: Double threshold → binary mask ───────────────────────────────

    /// <summary>
    /// Creates a binary volume: 1 where HU is in [BoneLowerHu, BoneUpperHu], else 0.
    /// Metal (≥2000 HU after clamping → above BoneUpperHu) is excluded automatically.
    /// </summary>
    private static VolumeData ApplyDoubleThreshold(VolumeData vol)
    {
        var   src = vol.HuBuffer;
        var   buf = new short[src.Length];
        for (int i = 0; i < src.Length; i++)
            buf[i] = (src[i] >= BoneLowerHu && src[i] <= BoneUpperHu) ? (short)1 : (short)0;
        return CopyWithBuffer(vol, buf);
    }

    // ── Step 4: 3×3×3 majority-vote median on binary volume ──────────────────

    /// <summary>
    /// For each interior voxel, counts the number of 1s in the 3×3×3 neighbourhood
    /// (27 voxels).  Sets the output to 1 if count ≥ 14 (majority), else 0.
    /// This is equivalent to a 3-D median filter for binary images and removes
    /// isolated single-voxel noise without blurring the bone boundary.
    /// </summary>
    private static VolumeData ApplyBinaryMajorityFilter(VolumeData vol, CancellationToken ct)
    {
        int nx = vol.Columns;
        int ny = vol.Rows;
        int nz = vol.SliceCount;
        int sliceStride = nx * ny;

        short[] src    = vol.HuBuffer;
        // Clone so border voxels (1-wide perimeter) keep their original values.
        var     result = (short[])src.Clone();

        Parallel.For(1, nz - 1, z =>
        {
            if (ct.IsCancellationRequested) return;

            int zOff = z * sliceStride;

            for (int y = 1; y < ny - 1; y++)
            {
                int yOff = zOff + y * nx;

                for (int x = 1; x < nx - 1; x++)
                {
                    // Count bone (= 1) voxels in the 3×3×3 kernel.
                    int count = 0;
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        int zzBase = (z + dz) * sliceStride;
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            // Read three consecutive x neighbours in one pass.
                            int nBase = zzBase + (y + dy) * nx + (x - 1);
                            if (src[nBase]     > 0) count++;
                            if (src[nBase + 1] > 0) count++;
                            if (src[nBase + 2] > 0) count++;
                        }
                    }
                    // Majority: ≥14 out of 27 → bone.
                    result[yOff + x] = count >= 14 ? (short)1 : (short)0;
                }
            }
        });

        ct.ThrowIfCancellationRequested();
        return CopyWithBuffer(vol, result);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static VolumeData CopyWithBuffer(VolumeData source, short[] newBuffer)
        => new VolumeData
        {
            HuBuffer   = newBuffer,
            Columns    = source.Columns,
            Rows       = source.Rows,
            SliceCount = source.SliceCount,
            SpacingX   = source.SpacingX,
            SpacingY   = source.SpacingY,
            SpacingZ   = source.SpacingZ,
            OriginX    = source.OriginX,
            OriginY    = source.OriginY,
            OriginZ    = source.OriginZ,
        };
}
