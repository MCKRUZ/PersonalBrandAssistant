namespace PBA.Application.Common;

/// <summary>
/// Pure vector math used by the brand-fit pre-filter, embedding dedup, and read-time ranking.
/// Lives in the Application layer so it is unit-testable without a database (EF InMemory cannot
/// execute pgvector SQL, so all vector logic runs in memory through this helper).
/// </summary>
public static class VectorMath
{
    /// <summary>
    /// Cosine similarity = dot(a,b) / (||a|| * ||b||). Computes the FULL norm — does NOT assume
    /// unit-normalized inputs (Matryoshka-shrunk embeddings are not unit-norm, R-M6).
    /// Returns 0 when either vector is the zero vector, or when an input contains NaN/Infinity
    /// (cosine is undefined; the contract is "never NaN", R-H2).
    /// Throws <see cref="ArgumentException"/> on length mismatch.
    /// </summary>
    public static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length)
        {
            throw new ArgumentException(
                $"Vector length mismatch: {a.Length} vs {b.Length}.", nameof(b));
        }

        double dot = 0, normA = 0, normB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            double ai = a[i];
            double bi = b[i];
            dot += ai * bi;
            normA += ai * ai;
            normB += bi * bi;
        }

        // Cosine vs a zero vector is undefined (0/0 = NaN); contract is 0, never NaN (R-H2).
        if (normA == 0 || normB == 0)
        {
            return 0.0;
        }

        var result = dot / (Math.Sqrt(normA) * Math.Sqrt(normB));

        // A NaN/Infinity in an input vector would propagate here; never leak it downstream (R-H2).
        return double.IsFinite(result) ? result : 0.0;
    }
}
