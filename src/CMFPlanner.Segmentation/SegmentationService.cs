using System.Diagnostics;
using CMFPlanner.Core.Models;
using CMFPlanner.Visualization;

namespace CMFPlanner.Segmentation;

/// <summary>
/// Implements <see cref="ISegmentationService"/> by delegating to <see cref="IMeshExtractor"/>
/// and <see cref="MeshProcessor"/> for post-processing.
/// </summary>
public sealed class SegmentationService : ISegmentationService
{
    private const int   BoneMaxTriangles = 300_000;
    private const int   SmoothIterations = 2;     // Mimics uses 2 iterations — preserves anatomical detail
    private const float SmoothLambda     = 0.3f;  // Mimics smooth factor 0.3000
    private const int   KeepShells       = 2;     // Mimics "Largest shells: 2"
    private const short BinaryIsoValue   = 1;     // MC threshold for preprocessed binary volumes

    private readonly IMeshExtractor _meshExtractor;

    public SegmentationService(IMeshExtractor meshExtractor)
        => _meshExtractor = meshExtractor;

    // ── Live preview extraction ──────────────────────────────────────────────

    public Task<MeshData> ExtractBoneSurfaceAsync(
        VolumeData volume,
        short thresholdHu,
        int stride,
        IProgress<(int Done, int Total)>? progress = null,
        CancellationToken ct = default)
        => _meshExtractor.ExtractAsync(volume, thresholdHu, stride, progress, ct);

    // ── Full-resolution final segmentation ──────────────────────────────────

    /// <summary>
    /// Applies the full bone-extraction pipeline.
    ///
    /// <para>When <paramref name="metalArtifactReduction"/> is <c>true</c> the volume is
    /// first run through <see cref="VolumePreprocessor"/> (HU clamp → anisotropic
    /// diffusion → double threshold → median filter) which yields a binary mask.
    /// Marching cubes then runs at iso-value 1 on that mask, completely excluding
    /// metal implants (which are above the 1800 HU upper threshold).  This mirrors
    /// the proven dicom2stl (NIH) pipeline.</para>
    ///
    /// <para>When <paramref name="metalArtifactReduction"/> is <c>false</c> marching
    /// cubes runs directly on the raw HU volume at <paramref name="boneThresholdHu"/>
    /// — the original behaviour.</para>
    /// </summary>
    public async Task<MeshData> ApplyFinalSegmentationAsync(
        VolumeData volume,
        short boneThresholdHu,
        bool metalArtifactReduction = true,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        VolumeData extractVolume;
        short      mcThreshold;

        Debug.WriteLine($"ApplyFinalSegmentationAsync: metalArtifactReduction={metalArtifactReduction}, boneThresholdHu={boneThresholdHu}");

        if (metalArtifactReduction)
        {
            // ── Steps 1-5: volumetric preprocessing (runs on thread-pool) ────────
            extractVolume = await Task.Run(
                () => VolumePreprocessor.PreprocessForBone(volume, progress, ct), ct);
            ct.ThrowIfCancellationRequested();

            mcThreshold = BinaryIsoValue;  // binary volume: surface at 0→1 boundary
            Debug.WriteLine($"Using binary volume with MC threshold={mcThreshold}");
        }
        else
        {
            extractVolume = volume;
            mcThreshold   = boneThresholdHu;
            Debug.WriteLine($"Using raw HU volume with MC threshold={mcThreshold}");
        }

        // ── Marching cubes at full resolution ────────────────────────────────
        progress?.Report("Extracting bone surface (full resolution)…");
        var boneMesh = await _meshExtractor.ExtractAsync(
            extractVolume, mcThreshold, stride: 1, ct: ct);
        ct.ThrowIfCancellationRequested();
        Debug.WriteLine($"After MC: {boneMesh.VertexCount} vertices, {boneMesh.TriangleCount} triangles");

        // ── Shell reduction: keep only the N largest components (Mimics-style) ─
        if (boneMesh.TriangleCount > 0)
        {
            progress?.Report($"Keeping {KeepShells} largest shells…");
            boneMesh = await Task.Run(
                () => MeshProcessor.KeepLargestNComponents(boneMesh, KeepShells, ct), ct);
            ct.ThrowIfCancellationRequested();
            Debug.WriteLine($"After shell reduction (keep {KeepShells}): {boneMesh.VertexCount} vertices, {boneMesh.TriangleCount} triangles");
        }

        // ── Taubin smoothing (Mimics: 2 iterations, factor 0.3) ─────────────
        progress?.Report($"Smoothing bone mesh ({SmoothIterations} iterations, λ={SmoothLambda})…");
        boneMesh = await Task.Run(
            () => MeshProcessor.Smooth(boneMesh, SmoothIterations, lambda: SmoothLambda, ct: ct), ct);
        ct.ThrowIfCancellationRequested();

        // ── Decimate to polygon budget ────────────────────────────────────────
        progress?.Report($"Decimating mesh (≤ {BoneMaxTriangles:N0} triangles)…");
        boneMesh = await Task.Run(
            () => MeshProcessor.Decimate(boneMesh, BoneMaxTriangles, ct), ct);

        return boneMesh;
    }
}
