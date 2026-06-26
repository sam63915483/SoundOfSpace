using UnityEngine;

public static class TrailerAimAttach
{
    public static string Execute()
    {
        var existing = Object.FindObjectOfType<_TrailerSunAimer>();
        if (existing != null) return "Aimer already present.";
        var go = new GameObject("[TrailerSunAimer]");
        go.AddComponent<_TrailerSunAimer>();
        return "Attached _TrailerSunAimer — camera will face the sun within a frame.";
    }
}
