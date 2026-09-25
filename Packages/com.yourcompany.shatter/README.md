# Shatter — Destruction Toolkit (early scaffold)

Target: **Unity 6.3.5f2+ (Unity 6000.x line), URP.** Package is written against
stable Unity 6 APIs only (no preview packages), so it should track forward
through 6.x updates without breakage — the main thing to re-check on each
Unity upgrade is the `Mesh` API surface (`indexFormat`, `SetTriangles`, etc.)
and URP material/shader compatibility for the interior-face material.

## Install into a Unity 6 project

1. Copy the `Shatter/` folder into `<YourProject>/Packages/com.yourcompany.shatter/`
   (or use `Window > Package Manager > Add package from disk...` pointing at `package.json`).
2. Unity will pull in the declared dependencies (URP, Mathematics, Burst, Collections)
   automatically if they aren't already in the project.

## What's implemented so far

- `Shatter.Core.ShatterMeshData` — triangle-list intermediate mesh format +
  fast import/export to `UnityEngine.Mesh`.
- `Shatter.Fracture.MeshClipper` — single-plane clip with automatic cap
  triangulation (ear-clipping, handles concave loops). This is the foundation
  everything else builds on.
- `Shatter.Core.ShatterObject` — **the component you attach to any sliceable
  object.** It owns its own mesh state: `Slice(worldPoint, worldNormal)` clips
  its current mesh, turns itself into the "positive" piece in place, and
  spawns a sibling `ShatterObject` for the "negative" piece. Because the
  result is itself a normal `ShatterObject`, pieces can be re-sliced
  recursively — this is also the pattern `VoronoiFracture` will build on later
  (a fracture is just many slices applied in sequence).
- `Samples~/SliceDemo/SliceTester.cs` — thin input-only test harness. It does
  not touch meshes; it just raycasts, finds the `ShatterObject` under the
  cursor, and calls `.Slice(...)` on it.

## How to test the clipper right now

1. Create a new scene. Add a cube (`GameObject > 3D Object > Cube`).
2. Add the **`ShatterObject`** component directly to the cube. Assign an
   interior material in its inspector (any unlit material works for now — a
   proper interior shader comes later). Leave "Simulate Physics" on — it will
   add its own `Rigidbody`/`MeshCollider` if missing.
3. Add `SliceTester` to the Main Camera (this only needs a Camera reference —
   it is not itself sliceable).
4. Enter Play mode, click on the cube. It should be replaced in place by two
   convex fragment pieces (a flat capped face where the cut was), which then
   fall under gravity.
5. Click one of the resulting fragments again — it should slice further,
   confirming fragments are valid input for another cut.
4. **Things to specifically check while testing** (these are exactly the risk
   areas called out in the design doc):
   - Does the cut face look sealed (no holes / missing triangles)?
   - Do normals on the cap face point the correct direction on *both* pieces
     (each piece's cap should face outward from that piece, not inward)?
   - Try slicing near a corner/edge of the cube (near-degenerate clip case).
   - Try slicing a non-convex mesh (e.g. an L-shaped or torus-like mesh) — this
     exercises multi-loop capping, which is the least battle-tested code path.
   - Try slicing the *same* fragment again (recursive slicing) — this tests
     that fragment output meshes are themselves valid, well-formed input for
     another clip pass, which fracturing will depend on heavily.

## Next steps (per the roadmap)

- Stress-test `MeshClipper` against a battery of "hostile" meshes (concave,
  thin slivers, near-coplanar cuts) and fix any triangulation failures found.
- `VoronoiFracture`: point sampling + repeated bisector-plane clipping using
  `MeshClipper`, plus adjacency recording for `ConnectivityGraph`.
- Editor-side fracture preview tooling.
