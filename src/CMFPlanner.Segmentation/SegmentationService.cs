using CMFPlanner.Core.Models;
using CMFPlanner.Visualization;

namespace CMFPlanner.Segmentation;

/// <summary>
/// Implements <see cref="ISegmentationService"/> by delegating to <see cref="IMeshExtractor"/>
/// and <see cref="MeshProcessor"/> for post-processing.
/// </summary>
public sealed class SegmentationService : ISegmentationService
{
    private const int BoneMaxTriangles = 300_000;
    private const int SmoothIterations = 20;

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

    public async Task<MeshData> ApplyFinalSegmentationAsync(
        VolumeData volume,
        short boneThresholdHu,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        // Step 1/3 — full-resolution bone extraction.
        progress?.Report("Extracting bone surface (full resolution)…");
        var boneMesh = await _meshExtractor.ExtractAsync(
            volume, boneThresholdHu, stride: 1, ct: ct);

        ct.ThrowIfCancellationRequested();

        // Step 2/3 — Taubin smoothing.
        progress?.Report($"Smoothing bone mesh ({SmoothIterations} iterations)…");
        boneMesh = await Task.Run(
            () => MeshProcessor.Smooth(boneMesh, SmoothIterations, ct: ct), ct);

        ct.ThrowIfCancellationRequested();

        // Step 3/3 — decimate to polygon budget.
        progress?.Report($"Decimating bone mesh (≤ {BoneMaxTriangles:N0} triangles)…");
        boneMesh = await Task.Run(
            () => MeshProcessor.Decimate(boneMesh, BoneMaxTriangles, ct), ct);

        return boneMesh;
    }
}
