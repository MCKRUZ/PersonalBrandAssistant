using PBA.Application.Common;
using Xunit;

namespace PBA.Application.Tests.Common;

public class VectorMathTests
{
    [Fact]
    public void CosineSimilarity_IdenticalVectors_ReturnsOne()
    {
        var a = new[] { 1f, 2f, 3f };
        var b = new[] { 1f, 2f, 3f };

        Assert.Equal(1.0, VectorMath.CosineSimilarity(a, b), precision: 6);
    }

    [Fact]
    public void CosineSimilarity_OrthogonalVectors_ReturnsZero()
    {
        var a = new[] { 1f, 0f };
        var b = new[] { 0f, 1f };

        Assert.Equal(0.0, VectorMath.CosineSimilarity(a, b), precision: 6);
    }

    [Fact]
    public void CosineSimilarity_OppositeVectors_ReturnsMinusOne()
    {
        var a = new[] { 1f, 2f, 3f };
        var b = new[] { -1f, -2f, -3f };

        Assert.Equal(-1.0, VectorMath.CosineSimilarity(a, b), precision: 6);
    }

    [Theory]
    // Scale-invariant: b is a positive multiple of a => cosine 1.0 even though norms differ (R-M6).
    [InlineData(new[] { 1f, 2f, 3f }, new[] { 2f, 4f, 6f }, 1.0)]
    // Non-unit-length inputs with a known non-trivial cosine: [1,0] vs [1,1] => 1/sqrt(2).
    [InlineData(new[] { 1f, 0f }, new[] { 1f, 1f }, 0.70710678)]
    public void CosineSimilarity_NonNormalizedInputs_ComputesFullNorm(float[] a, float[] b, double expected)
    {
        // A naive dot-product-only implementation (assuming unit norm) would fail these.
        Assert.Equal(expected, VectorMath.CosineSimilarity(a, b), precision: 6);
    }

    [Fact]
    public void CosineSimilarity_ZeroVector_ReturnsZeroNeverNaN()
    {
        var a = new[] { 0f, 0f, 0f };
        var b = new[] { 1f, 2f, 3f };

        var result = VectorMath.CosineSimilarity(a, b);

        Assert.Equal(0.0, result);
        Assert.False(double.IsNaN(result));
    }

    [Fact]
    public void CosineSimilarity_BothZeroVectors_ReturnsZeroNeverNaN()
    {
        var a = new[] { 0f, 0f };
        var b = new[] { 0f, 0f };

        var result = VectorMath.CosineSimilarity(a, b);

        Assert.Equal(0.0, result);
        Assert.False(double.IsNaN(result));
    }

    [Fact]
    public void CosineSimilarity_NaNInInput_ReturnsZeroNeverNaN()
    {
        var a = new[] { 1f, float.NaN, 3f };
        var b = new[] { 1f, 2f, 3f };

        var result = VectorMath.CosineSimilarity(a, b);

        Assert.Equal(0.0, result);
        Assert.False(double.IsNaN(result));
    }

    [Fact]
    public void CosineSimilarity_InfinityInInput_ReturnsZeroNeverNaN()
    {
        var a = new[] { float.PositiveInfinity, 0f };
        var b = new[] { 1f, 1f };

        var result = VectorMath.CosineSimilarity(a, b);

        Assert.Equal(0.0, result);
        Assert.False(double.IsNaN(result));
    }

    [Fact]
    public void CosineSimilarity_LengthMismatch_Throws()
    {
        var a = new[] { 1f, 2f, 3f };
        var b = new[] { 1f, 2f };

        Assert.Throws<ArgumentException>(() => VectorMath.CosineSimilarity(a, b));
    }
}
