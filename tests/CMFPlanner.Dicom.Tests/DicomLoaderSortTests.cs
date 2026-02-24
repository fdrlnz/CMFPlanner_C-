using CMFPlanner.Dicom;
using Xunit;

namespace CMFPlanner.Dicom.Tests;

/// <summary>
/// Tests for DicomLoader sort comparator and spacing computation.
/// Uses internal visibility granted via [assembly: InternalsVisibleTo] in DicomLoader.cs.
/// </summary>
public sealed class DicomLoaderSortTests
{
    // ── CompareSlices ──────────────────────────────────────────────────────

    [Fact]
    public void CompareSlices_BothHaveGeo_SortsByGeoKey()
    {
        var a = Slice(geo: 10.0, sl: double.MinValue, inst: 5, path: "a");
        var b = Slice(geo: 20.0, sl: double.MinValue, inst: 1, path: "b");

        Assert.True(DicomLoader.CompareSlices(a, b) < 0);
        Assert.True(DicomLoader.CompareSlices(b, a) > 0);
        Assert.Equal(0, DicomLoader.CompareSlices(a, a));
    }

    [Fact]
    public void CompareSlices_BothLackGeo_FallsBackToSliceLocation()
    {
        var a = Slice(geo: double.MinValue, sl: 10.0, inst: 5, path: "a");
        var b = Slice(geo: double.MinValue, sl: 20.0, inst: 1, path: "b");

        Assert.True(DicomLoader.CompareSlices(a, b) < 0);
    }

    [Fact]
    public void CompareSlices_BothLackGeoAndSL_FallsBackToInstanceNumber()
    {
        var a = Slice(geo: double.MinValue, sl: double.MinValue, inst: 1, path: "b");
        var b = Slice(geo: double.MinValue, sl: double.MinValue, inst: 5, path: "a");

        Assert.True(DicomLoader.CompareSlices(a, b) < 0);
    }

    [Fact]
    public void CompareSlices_AllTied_FallsBackToFilePath()
    {
        var a = Slice(geo: double.MinValue, sl: double.MinValue, inst: 1, path: "aaa");
        var b = Slice(geo: double.MinValue, sl: double.MinValue, inst: 1, path: "zzz");

        Assert.True(DicomLoader.CompareSlices(a, b) < 0);
        Assert.True(DicomLoader.CompareSlices(b, a) > 0);
        Assert.Equal(0, DicomLoader.CompareSlices(a, a));
    }

    [Fact]
    public void CompareSlices_MixedGeo_DoesNotCrash_AndIsTransitive()
    {
        // One slice has geometry, one does not — should not throw
        var withGeo    = Slice(geo: 5.0,          sl: 5.0, inst: 1, path: "a");
        var withoutGeo = Slice(geo: double.MinValue, sl: 5.0, inst: 2, path: "b");

        // Should not throw; result must be consistent (antisymmetry)
        int ab = DicomLoader.CompareSlices(withGeo, withoutGeo);
        int ba = DicomLoader.CompareSlices(withoutGeo, withGeo);
        Assert.True(Math.Sign(ab) == -Math.Sign(ba) || (ab == 0 && ba == 0));
    }

    [Fact]
    public void CompareSlices_SortList_IsStable_ForAllGeoPresent()
    {
        var slices = new List<DicomLoader.SliceInfo>
        {
            Slice(geo: 30.0, sl: double.MinValue, inst: 3, path: "c"),
            Slice(geo: 10.0, sl: double.MinValue, inst: 1, path: "a"),
            Slice(geo: 20.0, sl: double.MinValue, inst: 2, path: "b"),
        };

        slices.Sort(DicomLoader.CompareSlices);

        Assert.Equal([10.0, 20.0, 30.0], slices.Select(s => s.GeometrySortKey));
    }

    // ── ComputeSpacingZ ────────────────────────────────────────────────────

    [Fact]
    public void ComputeSpacingZ_GeometryPresent_ReturnsMedianGap()
    {
        // Three slices with gaps of 2.5 mm
        var sorted = SliceList(geoKeys: [0.0, 2.5, 5.0], sbs: 0, st: 0);
        Assert.Equal(2.5, DicomLoader.ComputeSpacingZ(sorted), precision: 10);
    }

    [Fact]
    public void ComputeSpacingZ_GeometryPresent_EvenCount_ReturnsAverageOfTwoMiddleGaps()
    {
        // Four slices: gaps 1, 2, 3 → median of [1,2,3] = 2
        var sorted = SliceList(geoKeys: [0.0, 1.0, 3.0, 6.0], sbs: 0, st: 0);
        Assert.Equal(2.0, DicomLoader.ComputeSpacingZ(sorted), precision: 10);
    }

    [Fact]
    public void ComputeSpacingZ_NoGeo_FallsBackToSpacingBetweenSlices()
    {
        var sorted = SliceList(geoKeys: [], sbs: 3.0, st: 1.0);
        Assert.Equal(3.0, DicomLoader.ComputeSpacingZ(sorted), precision: 10);
    }

    [Fact]
    public void ComputeSpacingZ_NoGeoNoSbs_FallsBackToSliceThickness()
    {
        var sorted = SliceList(geoKeys: [], sbs: 0, st: 2.0);
        Assert.Equal(2.0, DicomLoader.ComputeSpacingZ(sorted), precision: 10);
    }

    [Fact]
    public void ComputeSpacingZ_NoMetadata_Returns1()
    {
        var sorted = SliceList(geoKeys: [], sbs: 0, st: 0);
        Assert.Equal(1.0, DicomLoader.ComputeSpacingZ(sorted), precision: 10);
    }

    [Fact]
    public void ComputeSpacingZ_SingleSlice_ReturnsFromFallbackNotGeo()
    {
        // Only one geo key — no gaps possible, must fall through to SBS
        var sorted = SliceList(geoKeys: [5.0], sbs: 1.5, st: 3.0);
        Assert.Equal(1.5, DicomLoader.ComputeSpacingZ(sorted), precision: 10);
    }

    [Fact]
    public void ComputeSpacingZ_NegativeGapsIgnored_PositiveGapsUsed()
    {
        // Unsorted geo keys (should not happen after sort, but defence-in-depth)
        // Negative gap from 10→5 is filtered; positive gap 5→15 = 10 is used
        var sorted = new List<DicomLoader.SliceInfo>
        {
            Slice(geo: 10.0, sl: double.MinValue, inst: 0, path: "a"),
            Slice(geo: 5.0,  sl: double.MinValue, inst: 1, path: "b"),  // gap = -5, ignored
            Slice(geo: 15.0, sl: double.MinValue, inst: 2, path: "c"),  // gap = +10
        };
        // Only one positive gap: 10.0
        Assert.Equal(10.0, DicomLoader.ComputeSpacingZ(sorted), precision: 10);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static DicomLoader.SliceInfo Slice(
        double geo, double sl, int inst, string path) =>
        new()
        {
            GeometrySortKey = geo,
            SliceLocation   = sl,
            InstanceNumber  = inst,
            FilePath        = path,
        };

    private static IReadOnlyList<DicomLoader.SliceInfo> SliceList(
        double[] geoKeys, double sbs, double st)
    {
        if (geoKeys.Length == 0)
        {
            return
            [
                new DicomLoader.SliceInfo
                {
                    GeometrySortKey      = double.MinValue,
                    SpacingBetweenSlices = sbs,
                    SliceThickness       = st,
                }
            ];
        }

        return geoKeys.Select((k, i) => new DicomLoader.SliceInfo
        {
            GeometrySortKey      = k,
            SpacingBetweenSlices = sbs,
            SliceThickness       = st,
            FilePath             = i.ToString(),
        }).ToList();
    }
}
