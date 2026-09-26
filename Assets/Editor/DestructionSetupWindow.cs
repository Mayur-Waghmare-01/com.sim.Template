using UnityEditor;
using UnityEngine;
using Shatter.Core;

// Tools > Destruction Setup
// Select raw meshes (walls, crates) to fracture + wire up for Cowsins in one
// click, or select already-fractured ShatterCluster objects to just wire
// them. Mixing both in one selection works too.
public class DestructionSetupWindow : EditorWindow
{
    int fragmentCount = 40;
    float chunkHealth = 100f;

    [MenuItem("Tools/Destruction Setup")]
    static void Open() => GetWindow<DestructionSetupWindow>("Destruction Setup");

    void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "Select mesh objects to fracture, or existing ShatterCluster objects to (re)wire.",
            MessageType.Info);

        fragmentCount = EditorGUILayout.IntField("Fragment Count", fragmentCount);
        chunkHealth = EditorGUILayout.FloatField("Chunk Health", chunkHealth);

        if (GUILayout.Button("Setup Selected")) SetupSelection();
    }

    void SetupSelection()
    {
        var targets = Selection.gameObjects; // snapshot - fracturing replaces objects
        int fractured = 0, wired = 0, skipped = 0;

        foreach (var go in targets)
        {
            if (go == null) continue;

            var cluster = go.GetComponent<ShatterCluster>();

            if (cluster == null)
            {
                var mf = go.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) { skipped++; continue; }

                cluster = FractureToCluster(go, fragmentCount);
                if (cluster == null) { skipped++; continue; }
                fractured++;
            }

            WireCowsins(cluster, chunkHealth);
            ValidateCluster(cluster);
            wired++;
        }

        Debug.Log($"[Destruction Setup] Fractured {fractured}, wired {wired}, skipped {skipped} (no mesh / no cluster).");
    }

    ShatterCluster FractureToCluster(GameObject go, int fragCount)
    {
        Transform parent = go.transform.parent;
        string sourceName = go.name;

        var fracture = go.GetComponent<ShatterFracture>();
        if (fracture == null) fracture = Undo.AddComponent<ShatterFracture>(go);

        var so = new SerializedObject(fracture);
        so.FindProperty("fragmentCount").intValue = fragCount;
        so.ApplyModifiedProperties();

        fracture.GeneratePreview();
        fracture.BakeFragments(); // destroys 'go', creates "<name>_cluster" as a sibling

        Transform clusterT = parent != null
            ? parent.Find(sourceName + "_cluster")
            : GameObject.Find(sourceName + "_cluster")?.transform;

        if (clusterT == null)
        {
            Debug.LogWarning($"[Destruction Setup] Fracture ran but couldn't find baked cluster for '{sourceName}'.");
            return null;
        }

        return clusterT.GetComponent<ShatterCluster>();
    }

    void WireCowsins(ShatterCluster cluster, float health)
    {
        var setup = cluster.GetComponent<ClusterCowsinsSetup>();
        if (setup == null) setup = Undo.AddComponent<ClusterCowsinsSetup>(cluster.gameObject);

        var so = new SerializedObject(setup);
        so.FindProperty("chunkHealth").floatValue = health;
        so.ApplyModifiedProperties();

        EditorUtility.SetDirty(cluster.gameObject);
    }

    // Catches the two most common "nothing happens in Play mode" causes
    // before you ever press Play.
    void ValidateCluster(ShatterCluster cluster)
    {
        var go = cluster.gameObject;

        var colliders = go.GetComponentsInChildren<Collider>();
        if (colliders.Length == 0)
            Debug.LogWarning($"[Destruction Setup] '{go.name}' has no fragment colliders - bullets have nothing to hit.", go);

        var rb = go.GetComponent<Rigidbody>();
        if (rb != null && !rb.isKinematic)
            Debug.LogWarning($"[Destruction Setup] '{go.name}' has no anchored fragments - the whole cluster will fall the instant Play starts, before you can shoot it. Increase Anchor Bottom Threshold on ShatterFracture and re-fracture.", go);

        Bounds b = colliders.Length > 0 ? colliders[0].bounds : new Bounds(go.transform.position, Vector3.one);
        foreach (var c in colliders) b.Encapsulate(c.bounds);

        if (!Physics.Raycast(b.center, Vector3.down, out _, b.extents.y + 5f))
            Debug.LogWarning($"[Destruction Setup] No collider found below '{go.name}' within 5 units - add a ground/floor collider or fragments may fall forever.", go);
    }
}