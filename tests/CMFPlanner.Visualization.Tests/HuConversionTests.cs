using CMFPlanner.Visualization;
using Xunit;

namespace CMFPlanner.Visualization.Tests;

/// <summary>
/// Tests for VtkVolumeBuilder.ConvertToHu — signed/unsigned representation,
/// slope/intercept defaults, and clamping behaviour.
/// </summary>
public sealed class HuConversionTests
{
    // ── Typical CT rescale ─────────────────────────────────────────────────

    [Theory]
    [InlineData(0,    1.0, -1024.0, -1024)] // air
    [InlineData(1024, 1.0, -1024.0,     0)] // water
    [InlineData(1424, 1.0, -1024.0,   400)] // bone threshold
    [InlineData(3071, 1.0, -1024.0,  2047)] // dense bone
    public void ConvertToHu_TypicalCt_CorrectResult(int raw, double slope, double intercept, short expected)
    {
        Assert.Equal(expected, VtkVolumeBuilder.ConvertToHu(raw, slope, intercept));
    }

    // ── Identity transform (slope=1, intercept=0) ──────────────────────────

    [Fact]
    public void ConvertToHu_IdentityTransform_ReturnsRaw()
    {
        Assert.Equal(500, VtkVolumeBuilder.ConvertToHu(500, 1.0, 0.0));
        Assert.Equal(-100, VtkVolumeBuilder.ConvertToHu(-100, 1.0, 0.0));
    }

    // ── Signed 16-bit raw values ───────────────────────────────────────────

    [Fact]
    public void ConvertToHu_SignedNegativeRaw_CorrectResult()
    {
        // Signed raw = -1024, slope=1, intercept=0 → -1024 HU
        Assert.Equal(-1024, VtkVolumeBuilder.ConvertToHu(-1024, 1.0, 0.0));
    }

    // ── Unsigned 16-bit raw values ─────────────────────────────────────────

    [Fact]
    public void ConvertToHu_UnsignedMaxRaw_CorrectResult()
    {
        // Unsigned 16-bit max = 65535
        // With slope=1, intercept=0 → clamped to short.MaxValue (32767)
        Assert.Equal(short.MaxValue, VtkVolumeBuilder.ConvertToHu(65535, 1.0, 0.0));
    }

    // ── Clamping ───────────────────────────────────────────────────────────

    [Fact]
    public void ConvertToHu_AboveShortMax_ClampsToShortMax()
    {
        // raw=40000, slope=1, intercept=0 → 40000 > 32767 → clamped
        Assert.Equal(short.MaxValue, VtkVolumeBuilder.ConvertToHu(40000, 1.0, 0.0));
    }

    [Fact]
    public void ConvertToHu_BelowShortMin_ClampsToShortMin()
    {
        // large negative intercept forcing result below -32768
        Assert.Equal(short.MinValue, VtkVolumeBuilder.ConvertToHu(0, 1.0, -40000.0));
    }

    // ── Slope = 0 (degenerate, should not corrupt) ─────────────────────────

    [Fact]
    public void ConvertToHu_ZeroSlope_ReturnsIntercept()
    {
        Assert.Equal(0, VtkVolumeBuilder.ConvertToHu(9999, 0.0, 0.0));
        Assert.Equal(100, VtkVolumeBuilder.ConvertToHu(9999, 0.0, 100.0));
    }

    // ── Non-integer slope/intercept ───────────────────────────────────────

    [Fact]
    public void ConvertToHu_FractionalSlope_RoundsCorrectly()
    {
        // raw=2000, slope=0.5, intercept=-500 → 2000*0.5 - 500 = 500
        Assert.Equal(500, VtkVolumeBuilder.ConvertToHu(2000, 0.5, -500.0));
    }
}
