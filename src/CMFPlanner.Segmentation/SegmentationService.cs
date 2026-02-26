using CMFPlanner.Core.Models;
using CMFPlanner.Visualization;

namespace CMFPlanner.Segmentation;

/// <summary>
/// Implements <see cref="ISegmentationService"/> by delegating to <see cref="IMeshExtractor"/>
/// and <see cref="MeshProcessor"/> for post-processing.
/// </summary>
public sealed class SegmentationService : ISegmentationService
{
    private const int BoneMaxTriangles     = 200_000;
    private const int SoftTissueMaxTriangles = 100_000;
    private const int SmoothIterations    = 20;

    private readonly IMeshExtractor _meshExtractor;

    public SegmentationService(IMeshExtractor meshExtractor)
        => _meshExtractor = meshExtractor;

    // ── Live preview extractions ─────────────────────────────────────────────

    public Task<MeshData> ExtractBoneSurfaceAsync(
        VolumeData volume,
        short thresholdHu,
        int stride,
        IProgress<(int Done, int Total)>? progress = null,
        CancellationToken ct = default)
        => _meshExtractor.ExtractAsync(volume, thresholdHu, stride, progress, ct);

    public Task<MeshData> ExtractSoftTissueSurfaceAsync(
        VolumeData volume,
        short thresholdHu,
        int stride,
        IProgress<(int Done, int Total)>? progress = null,
        CancellationToken ct = default)
        => _meshExtractor.ExtractAsync(volume, thresholdHu, stride, progress, ct);

    // ── Full-resolution final segmentation ──────────────────────────────────

    public async Task<(MeshData BoneMesh, MeshData SoftTissueMesh)> ApplyFinalSegmentationAsync(
        VolumeData volume,
        short boneThresholdHu,
        short softTissueThresholdHu,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        // Step 1/6 — full-resolution bone extraction.
        progress?.Report("Extracting bone surface (full resolution)…");
        var boneMesh = await _meshExtractor.ExtractAsync(
            volume, boneThresholdHu, stride: 1, ct: ct);

        ct.ThrowIfCancellationRequested();

        // Step 2/6 — Taubin smoothing on bone.
        progress?.Report($"Smoothing bone mesh ({SmoothIterations} iterations)…");
        boneMesh = await Task.Run(
            () => MeshProcessor.Smooth(boneMesh, SmoothIterations, ct: ct), ct);

        ct.ThrowIfCancellationRequested();

        // Step 3/6 — decimate bone to polygon budget.
        progress?.Report($"Decimating bone mesh (≤ {BoneMaxTriangles:N0} triangles)…");
        boneMesh = await Task.Run(
            () => MeshProcessor.Decimate(boneMesh, BoneMaxTriangles, ct), ct);

        ct.ThrowIfCancellationRequested();

        // Step 4/6 — full-resolution soft-tissue extraction.
        progress?.Report("Extracting soft tissue surface (full resolution)…");
        var softMesh = await _meshExtractor.ExtractAsync(
            volume, softTissueThresholdHu, stride: 1, ct: ct);

        ct.ThrowIfCancellationRequested();

        // Step 5/6 — Taubin smoothing on soft tissue.
        progress?.Report($"Smoothing soft tissue mesh ({SmoothIterations} iterations)…");
        softMesh = await Task.Run(
            () => MeshProcessor.Smooth(softMesh, SmoothIterations, ct: ct), ct);

        ct.ThrowIfCancellationRequested();

        // Step 6/6 — decimate soft tissue to polygon budget.
        progress?.Report($"Decimating soft tissue mesh (≤ {SoftTissueMaxTriangles:N0} triangles)…");
        softMesh = await Task.Run(
            () => MeshProcessor.Decimate(softMesh, SoftTissueMaxTriangles, ct), ct);

        return (boneMesh, softMesh);
    }
}
