using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Marks a <see cref="Light"/> (e.g. a lantern) as one that should also illuminate the
/// GPU-instanced grass. The grass is drawn with Graphics.DrawMeshInstanced and so never
/// receives Unity's additive forward lights — InstancedGrassRenderer reads the active
/// markers near the player each frame and injects them into the grass shader as faked
/// point lights (see CG_SimpleGrass.shader's _GrassPointLight* globals).
///
/// Drop this on the same GameObject as the Light. Live instances are tracked in
/// <see cref="All"/> via OnEnable/OnDisable, matching the project's AllInstances pattern.
/// </summary>
[RequireComponent(typeof(Light))]
public class GrassPointLight : MonoBehaviour
{
    [Tooltip("Multiplier on this light's contribution to the grass. The grass lighting is faked, so this lets you dial it independently of the real light's intensity. 1 = use the light's full intensity; lower for a subtler effect.")]
    public float grassStrength = 0.5f;

    public static readonly List<GrassPointLight> All = new List<GrassPointLight>();
    public Light Light { get; private set; }

    void Awake() { Light = GetComponent<Light>(); }
    void OnEnable() { if (Light == null) Light = GetComponent<Light>(); if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); }
}
