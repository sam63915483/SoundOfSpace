#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

public class DiagTentMesh
{
    public static void Execute()
    {
        // Find the tent in the scene
        GameObject[] all = Resources.FindObjectsOfTypeAll<GameObject>();
        foreach (GameObject go in all)
        {
            if (!go.name.Contains("Tent")) continue;
            if (EditorUtility.IsPersistent(go)) continue;

            MeshFilter mf = go.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;

            Mesh mesh = mf.sharedMesh;
            Color[] colors = mesh.colors;

            if (colors == null || colors.Length == 0)
            {
                Debug.Log($"{go.name}: NO vertex colors on mesh '{mesh.name}' (verts={mesh.vertexCount})");
                continue;
            }

            float minA = 1f, maxA = 0f, minR = 1f, maxR = 0f;
            foreach (Color c in colors)
            {
                if (c.a < minA) minA = c.a;
                if (c.a > maxA) maxA = c.a;
                if (c.r < minR) minR = c.r;
                if (c.r > maxR) maxR = c.r;
            }

            Debug.Log($"{go.name}: mesh='{mesh.name}' verts={mesh.vertexCount} colors={colors.Length} alpha=[{minA:F3}..{maxA:F3}] red=[{minR:F3}..{maxR:F3}]");
            // Log first 5 colors for inspection
            for (int i = 0; i < Mathf.Min(5, colors.Length); i++)
                Debug.Log($"  vert[{i}] = RGBA({colors[i].r:F3},{colors[i].g:F3},{colors[i].b:F3},{colors[i].a:F3})");
        }
    }
}
#endif
