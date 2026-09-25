using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Shatter.Core;

namespace Shatter.EditorTools
{
    [CustomEditor(typeof(ShatterFracture))]
    public class ShatterFractureEditor : Editor
    {
        private static readonly Color[] FragmentColors =
        {
            new Color(1f, 0.3f, 0.3f), new Color(0.3f, 1f, 0.3f), new Color(0.3f, 0.6f, 1f),
            new Color(1f, 1f, 0.3f), new Color(1f, 0.3f, 1f), new Color(0.3f, 1f, 1f),
            new Color(1f, 0.6f, 0.2f), new Color(0.7f, 0.4f, 1f)
        };

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var tool = (ShatterFracture)target;

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField(
                "Preview",
                tool.previewFragments != null
                    ? $"{tool.previewFragments.Count}/{tool.FragmentCount} fragments, {(tool.previewAnchors?.Count ?? 0)} anchored"
                    : "none - click Generate Preview",
                EditorStyles.boldLabel);

            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Generate Preview", GUILayout.Height(28)))
                {
                    tool.GeneratePreview();
                    SceneView.RepaintAll();
                }

                if (GUILayout.Button("Clear Preview", GUILayout.Height(28)))
                {
                    tool.ClearPreview();
                    SceneView.RepaintAll();
                }
            }

            EditorGUILayout.Space(4);
            var prevColor = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.5f, 1f, 0.5f);
            if (GUILayout.Button("Fracture (Bake)", GUILayout.Height(34)))
            {
                if (EditorUtility.DisplayDialog(
                    "Bake Fracture",
                    "This replaces this object with its fractured fragments. Continuing is recommended only once you're happy with the preview.",
                    "Fracture", "Cancel"))
                {
                    tool.BakeFragments();
                }
            }
            GUI.backgroundColor = prevColor;
        }

        private void OnSceneGUI()
        {
            var tool = (ShatterFracture)target;
            if (tool.previewFragments == null) return;

            Matrix4x4 localToWorld = tool.transform.localToWorldMatrix;

            // Fragment wireframes
            for (int f = 0; f < tool.previewFragments.Count; f++)
            {
                Handles.color = FragmentColors[f % FragmentColors.Length];
                var data = tool.previewFragments[f];

                foreach (var sub in data.subMeshes)
                {
                    var tris = sub.triangles;
                    for (int i = 0; i < tris.Count; i += 3)
                    {
                        Vector3 a = localToWorld.MultiplyPoint3x4(data.vertices[tris[i]].position);
                        Vector3 b = localToWorld.MultiplyPoint3x4(data.vertices[tris[i + 1]].position);
                        Vector3 c = localToWorld.MultiplyPoint3x4(data.vertices[tris[i + 2]].position);
                        Handles.DrawLine(a, b);
                        Handles.DrawLine(b, c);
                        Handles.DrawLine(c, a);
                    }
                }
            }

            // Precompute fragment centroids for connectivity/anchor drawing
            var centroids = new Vector3[tool.previewFragments.Count];
            for (int i = 0; i < tool.previewFragments.Count; i++)
                centroids[i] = localToWorld.MultiplyPoint3x4(tool.previewFragments[i].ComputeBounds().center);

            // Connectivity edges
            if (tool.previewGraph != null)
            {
                Handles.color = Color.yellow;
                var drawn = new HashSet<(int, int)>();
                foreach (int node in tool.previewGraph.Nodes)
                {
                    foreach (int neighbor in tool.previewGraph.GetNeighbors(node))
                    {
                        var key = node < neighbor ? (node, neighbor) : (neighbor, node);
                        if (drawn.Contains(key)) continue;
                        drawn.Add(key);
                        if (node < centroids.Length && neighbor < centroids.Length)
                            Handles.DrawLine(centroids[node], centroids[neighbor]);
                    }
                }
            }

            // Anchor markers
            if (tool.previewAnchors != null)
            {
                Handles.color = Color.cyan;
                foreach (int idx in tool.previewAnchors)
                {
                    if (idx < 0 || idx >= centroids.Length) continue;
                    Handles.CubeHandleCap(0, centroids[idx], Quaternion.identity, HandleUtility.GetHandleSize(centroids[idx]) * 0.15f, EventType.Repaint);
                }
            }

            // Seed points
            if (tool.previewSeeds != null)
            {
                Handles.color = Color.white;
                foreach (var seed in tool.previewSeeds)
                {
                    Vector3 worldSeed = localToWorld.MultiplyPoint3x4(seed);
                    Handles.SphereHandleCap(0, worldSeed, Quaternion.identity, HandleUtility.GetHandleSize(worldSeed) * 0.08f, EventType.Repaint);
                }
            }
        }
    }
}