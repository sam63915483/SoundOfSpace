using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class Universe {
    public const float gravitationalConstant = 0.0001f;

    // Was `const`. Now a settable field so InputSettings.ApplyPhysicsRate can
    // change the physics tick at runtime. NBodySimulation.Awake seeds
    // Time.fixedDeltaTime from this value once; pause-menu changes call
    // ApplyPhysicsRate which updates BOTH this field AND Time.fixedDeltaTime
    // in lockstep so the next FixedUpdate uses the new rate.
    //
    // Default 0.01 = 100 Hz physics ("Ultra"). The pause-menu QUALITY section
    // exposes "Balanced" (0.02 = 50 Hz) and "Low" (0.025 = 40 Hz).
    public static float physicsTimeStep = 0.01f;

    public const bool cheatsEnabled = true;
}