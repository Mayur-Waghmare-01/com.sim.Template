using UnityEditor;
using UnityEngine;
using Shatter.Core;

namespace Shatter.EditorTools
{
    [CustomEditor(typeof(MeshEditTools))]
    public class MeshEditToolsEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var tool = (MeshEditTools)target;

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Triangle Count", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Original:", tool.OriginalTriangleCount.ToString());
                EditorGUILayout.LabelField("Current:", tool.CurrentTriangleCount.ToString());
            }

            EditorGUILayout.Space(8);
            using (new EditorGUILayout.HorizontalScope())
            {
                var prevBg = GUI.backgroundColor;

                GUI.backgroundColor = new Color(0.55f, 0.75f, 1f);
                if (GUILayout.Button("Subdivide", GUILayout.Height(30)))
                    tool.SubdivideNow();

                GUI.backgroundColor = new Color(1f, 0.6f, 0.5f);
                if (GUILayout.Button("Revert", GUILayout.Height(30)))
                    tool.RevertToOriginal();

                GUI.backgroundColor = prevBg;
            }
        }
    }
}
