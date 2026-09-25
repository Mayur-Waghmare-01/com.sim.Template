using System.Collections.Generic;
using UnityEngine;
using Shatter.Core;

namespace Shatter.Fracture
{
    /// <summary>
    /// Voronoi mesh fracturing: for each seed point, the resulting fragment is
    /// that seed's Voronoi cell intersected with the source mesh. Built
    /// entirely on top of MeshClipper - for a given seed, repeatedly clip the
    /// working mesh against the perpendicular-bisector plane to every OTHER
    /// seed, keeping only the half closer to this seed. What's left after
    /// processing every other seed is exactly that seed's cell.
    ///
    /// Also records adjacency between fragments as a byproduct: whenever a
    /// bisector plane between seed i and seed j actually cuts through the
    /// working mesh, i and j are adjacency candidates. This is a heuristic -
    /// a candidate cut face can occasionally get fully clipped away by a
    /// LATER plane, leaving no real shared face in the final result - but is
    /// accurate enough for clustering/connectivity purposes, where an
    /// occasional extra edge just means a slightly more conservative "stays
    /// connected" call, not a visual or physical bug.
    ///
    /// Cost: O(seeds^2) mesh clips. Fine for the seed counts a fracture UI
    /// typically exposes (single digits to low hundreds).
    /// </summary>
    public static class VoronoiFracture
    {
        public class FractureResult
        {
            public List<ShatterMeshData> fragments = new List<ShatterMeshData>();
            public ConnectivityGraph graph = new ConnectivityGraph();
        }

        /// <summary>
        /// Generates `count` seed points inside the mesh's actual volume
        /// (rejection sampling: random point in bounds, kept only if
        /// MeshGeometryUtility says it's inside). Deterministic for a given
        /// randomSeed, so the same settings always reproduce the same fracture.
        /// </summary>
        public static List<Vector3> GenerateSeeds(ShatterMeshData source, int count, int randomSeed, int maxAttemptsPerPoint = 60)
        {
            var rng = new System.Random(randomSeed);
            Bounds bounds = source.ComputeBounds();
            var points = new List<Vector3>(count);

            for (int i = 0; i < count; i++)
            {
                Vector3 candidate = bounds.center;
                bool found = false;

                for (int attempt = 0; attempt < maxAttemptsPerPoint; attempt++)
                {
                    candidate = new Vector3(
                        Mathf.Lerp(bounds.min.x, bounds.max.x, (float)rng.NextDouble()),
                        Mathf.Lerp(bounds.min.y, bounds.max.y, (float)rng.NextDouble()),
                        Mathf.Lerp(bounds.min.z, bounds.max.z, (float)rng.NextDouble()));

                    if (MeshGeometryUtility.IsPointInside(source, candidate))
                    {
                        found = true;
                        break;
                    }
                }

                points.Add(found ? candidate : bounds.center);
            }

            return points;
        }

        /// <summary>
        /// Produces one ShatterMeshData fragment per seed (in the same local
        /// space as `source`), plus a ConnectivityGraph over the resulting
        /// fragment indices (NOT seed indices - some seeds may yield no
        /// fragment and are dropped, so fragment indices are the compacted list).
        /// </summary>
        public static FractureResult Fracture(ShatterMeshData source, List<Vector3> seeds, Material interiorMaterial)
        {
            var result = new FractureResult();
            var seedToFragment = new int[seeds.Count];
            for (int i = 0; i < seedToFragment.Length; i++) seedToFragment[i] = -1;

            // Candidate adjacency pairs, in SEED index space - resolved to
            // fragment indices after we know which seeds actually survived.
            var rawAdjacency = new List<(int a, int b)>();

            for (int i = 0; i < seeds.Count; i++)
            {
                ShatterMeshData current = source.Clone();
                Vector3 seedI = seeds[i];

                for (int j = 0; j < seeds.Count && current != null; j++)
                {
                    if (i == j) continue;
                    Vector3 seedJ = seeds[j];

                    Vector3 normal = seedJ - seedI;
                    float len = normal.magnitude;
                    if (len < 1e-6f) continue; // coincident seeds - skip

                    normal /= len;
                    Vector3 midpoint = (seedI + seedJ) * 0.5f;
                    var plane = new Plane(normal, midpoint);

                    var clip = MeshClipper.Clip(current, plane, interiorMaterial);

                    if (!clip.wasClipped)
                    {
                        // By construction, seedI is always on the negative side
                        // of this plane. If the ENTIRE working mesh is on the
                        // positive side instead, seedI's cell doesn't reach here.
                        if (clip.positive != null && clip.negative == null)
                            current = null;
                        continue;
                    }

                    // A real cut happened between i and j - adjacency candidate.
                    rawAdjacency.Add((i, j));
                    current = clip.negative;
                }

                if (current != null && current.subMeshes.Count > 0 && current.VertexCount > 0)
                {
                    seedToFragment[i] = result.fragments.Count;
                    result.fragments.Add(current);
                }
            }

            for (int i = 0; i < seedToFragment.Length; i++)
                if (seedToFragment[i] >= 0)
                    result.graph.AddNode(seedToFragment[i]);

            foreach (var (a, b) in rawAdjacency)
            {
                int fa = seedToFragment[a];
                int fb = seedToFragment[b];
                if (fa >= 0 && fb >= 0)
                    result.graph.AddEdge(fa, fb);
            }

            return result;
        }
    }
}