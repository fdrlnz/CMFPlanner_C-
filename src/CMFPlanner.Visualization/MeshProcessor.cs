namespace CMFPlanner.Visualization;

/// <summary>
/// Pure mesh post-processing: Taubin smoothing and vertex-clustering decimation.
/// All public methods return new <see cref="MeshData"/> instances and never mutate inputs.
/// </summary>
public static class MeshProcessor
{
    // ── Taubin smoothing ─────────────────────────────────────────────────────

    /// <summary>
    /// Applies Taubin (λ-μ) smoothing to prevent the shrinkage caused by plain
    /// Laplacian smoothing.  Each full iteration is one λ-pass followed by one μ-pass.
    /// </summary>
    /// <param name="input">Source mesh (not mutated).</param>
    /// <param name="iterations">Number of Taubin iterations (each = 2 Laplacian passes).</param>
    /// <param name="lambda">Positive damping factor (default 0.5).</param>
    /// <param name="mu">Negative inflation factor (default −0.53).</param>
    /// <param name="progress">
    ///   Reports (passesCompleted, totalPasses) where totalPasses = iterations × 2.
    /// </param>
    public static MeshData Smooth(
        MeshData input,
        int iterations,
        float lambda = 0.5f,
        float mu     = -0.53f,
        IProgress<(int Done, int Total)>? progress = null,
        CancellationToken ct = default)
    {
        if (input.VertexCount == 0 || iterations <= 0) return input;

        float[] pos        = (float[])input.Positions.Clone();
        int     vCount     = input.VertexCount;
        int[]   indices    = input.Indices;
        int     totalPasses = iterations * 2;
        int     step        = 0;

        for (int iter = 0; iter < iterations; iter++)
        {
            ct.ThrowIfCancellationRequested();
            pos = LaplacianPass(pos, vCount, indices, lambda);
            progress?.Report((++step, totalPasses));

            ct.ThrowIfCancellationRequested();
            pos = LaplacianPass(pos, vCount, indices, mu);
            progress?.Report((++step, totalPasses));
        }

        return new MeshData(pos, ComputeNormals(pos, indices, vCount), indices);
    }

    /// <summary>
    /// Computes one Laplacian displacement step with damping factor <paramref name="lambda"/>.
    /// Uses per-triangle accumulation so no adjacency list needs to be built.
    /// </summary>
    private static float[] LaplacianPass(
        float[] pos, int vCount, int[] indices, float lambda)
    {
        var sumPos = new float[vCount * 3];
        var count  = new int[vCount];

        // Accumulate neighbour contributions from every triangle.
        for (int t = 0; t < indices.Length; t += 3)
        {
            int a = indices[t], b = indices[t + 1], c = indices[t + 2];
            int oa = a * 3, ob = b * 3, oc = c * 3;

            // Vertex a gets contributions from b and c.
            sumPos[oa]     += pos[ob]     + pos[oc];
            sumPos[oa + 1] += pos[ob + 1] + pos[oc + 1];
            sumPos[oa + 2] += pos[ob + 2] + pos[oc + 2];
            count[a] += 2;

            // Vertex b gets contributions from a and c.
            sumPos[ob]     += pos[oa]     + pos[oc];
            sumPos[ob + 1] += pos[oa + 1] + pos[oc + 1];
            sumPos[ob + 2] += pos[oa + 2] + pos[oc + 2];
            count[b] += 2;

            // Vertex c gets contributions from a and b.
            sumPos[oc]     += pos[oa]     + pos[ob];
            sumPos[oc + 1] += pos[oa + 1] + pos[ob + 1];
            sumPos[oc + 2] += pos[oa + 2] + pos[ob + 2];
            count[c] += 2;
        }

        var newPos = new float[vCount * 3];
        for (int v = 0; v < vCount; v++)
        {
            int o = v * 3;
            if (count[v] == 0)
            {
                newPos[o] = pos[o]; newPos[o + 1] = pos[o + 1]; newPos[o + 2] = pos[o + 2];
                continue;
            }
            float n = count[v];
            newPos[o]     = pos[o]     + lambda * (sumPos[o]     / n - pos[o]);
            newPos[o + 1] = pos[o + 1] + lambda * (sumPos[o + 1] / n - pos[o + 1]);
            newPos[o + 2] = pos[o + 2] + lambda * (sumPos[o + 2] / n - pos[o + 2]);
        }
        return newPos;
    }

    // ── Vertex-clustering decimation ─────────────────────────────────────────

    /// <summary>
    /// Reduces the triangle count to at most <paramref name="maxTriangles"/> using vertex
    /// clustering.  A binary search finds the finest grid resolution that stays within the
    /// budget.  Returns the input unchanged if it is already within the limit.
    /// </summary>
    public static MeshData Decimate(
        MeshData input,
        int maxTriangles,
        CancellationToken ct = default)
    {
        if (input.TriangleCount <= maxTriangles) return input;

        // Compute bounding box.
        float[] pos    = input.Positions;
        int     vCount = input.VertexCount;
        float minX = pos[0], minY = pos[1], minZ = pos[2];
        float maxX = pos[0], maxY = pos[1], maxZ = pos[2];

        for (int v = 1; v < vCount; v++)
        {
            int o = v * 3;
            if (pos[o]     < minX) minX = pos[o];     if (pos[o]     > maxX) maxX = pos[o];
            if (pos[o + 1] < minY) minY = pos[o + 1]; if (pos[o + 1] > maxY) maxY = pos[o + 1];
            if (pos[o + 2] < minZ) minZ = pos[o + 2]; if (pos[o + 2] > maxZ) maxZ = pos[o + 2];
        }

        float maxDim = MathF.Max(maxX - minX, MathF.Max(maxY - minY, maxZ - minZ));

        // lo = guaranteed-valid coarse cell size (1 giant cell → 1 triangle)
        // hi = very fine cell size (may exceed maxTriangles)
        // Binary search narrows hi down to the finest cell that stays within budget.
        float lo = maxDim;           // coarse, trivially valid (very few triangles)
        float hi = maxDim / 2000f;   // fine, likely exceeds limit

        MeshData? result = null;

        for (int iter = 0; iter < 16; iter++)
        {
            ct.ThrowIfCancellationRequested();
            float mid = (lo + hi) * 0.5f;
            var candidate = ClusterVertices(input, mid, minX, minY, minZ);

            if (candidate.TriangleCount <= maxTriangles)
            {
                result = candidate; // valid — try a finer grid (smaller cell)
                hi = mid;
            }
            else
            {
                lo = mid; // too many triangles — coarsen
            }
        }

        // Guard: if hi never produced a valid result, fall back to extreme coarsening.
        return result ?? ClusterVertices(input, lo, minX, minY, minZ);
    }

    /// <summary>
    /// Clusters vertices into axis-aligned cells of size <paramref name="cellSize"/>
    /// and rebuilds the triangle list, discarding degenerate and duplicate triangles.
    /// </summary>
    private static MeshData ClusterVertices(
        MeshData input,
        float cellSize,
        float minX, float minY, float minZ)
    {
        float[] pos    = input.Positions;
        int[]   indices = input.Indices;
        int     vCount  = input.VertexCount;

        float invCell = cellSize > 0f ? 1f / cellSize : 1f;

        // Pass 1: compute per-cell position sums.
        var cellSum = new Dictionary<long, (float sx, float sy, float sz, int cnt)>();

        for (int v = 0; v < vCount; v++)
        {
            int o  = v * 3;
            int cx = (int)((pos[o]     - minX) * invCell);
            int cy = (int)((pos[o + 1] - minY) * invCell);
            int cz = (int)((pos[o + 2] - minZ) * invCell);
            long key = ((long)cx << 40) | ((long)(cy & 0xFFFFF) << 20) | (long)(cz & 0xFFFFF);

            cellSum.TryGetValue(key, out var acc);
            cellSum[key] = (acc.sx + pos[o], acc.sy + pos[o + 1], acc.sz + pos[o + 2], acc.cnt + 1);
        }

        // Build representative vertex array (centroid of each cell).
        var cellToRep  = new Dictionary<long, int>(cellSum.Count);
        var repPositions = new float[cellSum.Count * 3];
        int repIdx = 0;
        foreach (var (key, acc) in cellSum)
        {
            int r = repIdx++;
            cellToRep[key] = r;
            repPositions[r * 3]     = acc.sx / acc.cnt;
            repPositions[r * 3 + 1] = acc.sy / acc.cnt;
            repPositions[r * 3 + 2] = acc.sz / acc.cnt;
        }

        // Pass 2: remap original vertex indices to representative indices.
        var vertToRep = new int[vCount];
        for (int v = 0; v < vCount; v++)
        {
            int o  = v * 3;
            int cx = (int)((pos[o]     - minX) * invCell);
            int cy = (int)((pos[o + 1] - minY) * invCell);
            int cz = (int)((pos[o + 2] - minZ) * invCell);
            long key = ((long)cx << 40) | ((long)(cy & 0xFFFFF) << 20) | (long)(cz & 0xFFFFF);
            vertToRep[v] = cellToRep[key];
        }

        // Pass 3: rebuild triangles — skip degenerate and duplicate.
        int newVCount  = repIdx;
        var newIndices = new List<int>(indices.Length / 3);
        var seenTris   = new HashSet<long>();

        for (int t = 0; t < indices.Length; t += 3)
        {
            int a = vertToRep[indices[t]];
            int b = vertToRep[indices[t + 1]];
            int c = vertToRep[indices[t + 2]];

            if (a == b || b == c || a == c) continue; // degenerate

            // Canonical ordering for deduplication.
            int ia = Math.Min(a, Math.Min(b, c));
            int ic = Math.Max(a, Math.Max(b, c));
            int ib = a + b + c - ia - ic;
            long triKey = ((long)ia << 42) | ((long)(ib & 0x1FFFFF) << 21) | (long)(ic & 0x1FFFFF);

            if (!seenTris.Add(triKey)) continue;

            newIndices.Add(a);
            newIndices.Add(b);
            newIndices.Add(c);
        }

        int[] idxArray = newIndices.ToArray();
        return new MeshData(repPositions, ComputeNormals(repPositions, idxArray, newVCount), idxArray);
    }

    // ── Normal computation ───────────────────────────────────────────────────

    /// <summary>Computes smooth per-vertex normals by averaging adjacent face normals.</summary>
    private static float[] ComputeNormals(float[] pos, int[] indices, int vCount)
    {
        if (vCount == 0) return [];

        var normals = new float[vCount * 3];

        for (int t = 0; t < indices.Length; t += 3)
        {
            int ia = indices[t], ib = indices[t + 1], ic = indices[t + 2];
            int oa = ia * 3, ob = ib * 3, oc = ic * 3;

            float ax = pos[ob]     - pos[oa], ay = pos[ob + 1] - pos[oa + 1], az = pos[ob + 2] - pos[oa + 2];
            float bx = pos[oc]     - pos[oa], by = pos[oc + 1] - pos[oa + 1], bz = pos[oc + 2] - pos[oa + 2];

            float nx = ay * bz - az * by;
            float ny = az * bx - ax * bz;
            float nz = ax * by - ay * bx;

            normals[oa]     += nx; normals[oa + 1] += ny; normals[oa + 2] += nz;
            normals[ob]     += nx; normals[ob + 1] += ny; normals[ob + 2] += nz;
            normals[oc]     += nx; normals[oc + 1] += ny; normals[oc + 2] += nz;
        }

        for (int v = 0; v < vCount; v++)
        {
            int o   = v * 3;
            float len = MathF.Sqrt(
                normals[o] * normals[o] +
                normals[o + 1] * normals[o + 1] +
                normals[o + 2] * normals[o + 2]);
            if (len > 1e-6f)
            {
                normals[o]     /= len;
                normals[o + 1] /= len;
                normals[o + 2] /= len;
            }
        }

        return normals;
    }
}
