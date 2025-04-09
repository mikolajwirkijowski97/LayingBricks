using UnityEngine;
using System.Collections.Generic; // Required for using List<>

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Spawns objects chosen randomly from a list along a specified arc of a circle.
/// Allows scaling and updates dynamically in the editor.
/// </summary>
[ExecuteInEditMode]
public class CircleSpawner : MonoBehaviour
{
    // --- Fields ---
    [Header("Spawning Setup")]
    [SerializeField]
    [Tooltip("List of prefabs to choose from when spawning. One will be picked randomly for each spot.")]
    private List<GameObject> prefabsToSpawn = new List<GameObject>();

    [SerializeField]
    [Tooltip("Number of objects to spawn along the arc.")]
    [Min(1)]
    private int numberOfObjects = 10;

    [Header("Circle Parameters")]
    [SerializeField]
    [Tooltip("Radius of the circle")]
    [Min(0f)]
    private float radius = 5f;

    [SerializeField]
    [Tooltip("Starting angle of the arc in degrees (0 = positive X axis, clockwise).")]
    private float startAngleDegrees = 0f;

    [SerializeField]
    [Tooltip("The angular span of the arc in degrees (e.g., 180 for a semi-circle, 360 for a full circle).")]
    [Range(0f, 360f)] // Clamp arc span between 0 and 360
    private float arcDegrees = 360f; // Default to full circle

    [Header("Spawn Transform Options")]
    [SerializeField]
    [Tooltip("Uniform scale multiplier applied to the spawned objects (relative to prefab's original scale).")]
    [Min(0.01f)]
    private float scaleFactor = 1.0f;

    [SerializeField]
    [Tooltip("Should the spawned objects face outwards from the center?")]
    private bool faceOutwards = true;

    // Flag to prevent potential issues if Instantiate triggers OnValidate indirectly
    private bool isSpawning = false;

    // --- Methods ---

    void OnValidate()
    {
        // Prevent running spawn logic during play mode, while already spawning,
        // or if the object isn't part of a valid scene (e.g., prefab asset view).
        if (Application.isPlaying || isSpawning || !gameObject.scene.IsValid())
        {
            return;
        }

        // Schedule the update for the next editor frame to avoid potential issues.
        #if UNITY_EDITOR
        EditorApplication.delayCall -= DelayedUpdateSpawn; // Clear previous pending calls
        EditorApplication.delayCall += DelayedUpdateSpawn; // Schedule the update
        #endif
    }

    void DelayedUpdateSpawn()
    {
        // Ensure component/GameObject still exists before proceeding
        if (this == null || gameObject == null || !gameObject.scene.IsValid()) return;

        // --- Cleanup First ---
        ClearChildren();

        // --- Validation ---
        // Check prefabs
        if (prefabsToSpawn == null || prefabsToSpawn.Count == 0) return;
        bool hasValidPrefab = false;
        foreach (var prefab in prefabsToSpawn) { if (prefab != null) { hasValidPrefab = true; break; } }
        if (!hasValidPrefab) return;

        // Check other parameters
        if (numberOfObjects <= 0 || radius < 0 || scaleFactor <= 0) return;

        // --- Spawning ---
        isSpawning = true;
        try
        {
            // Convert input degrees to radians for calculations
            float startAngleRad = startAngleDegrees * Mathf.Deg2Rad;
            float arcRad = arcDegrees * Mathf.Deg2Rad;

            for (int i = 0; i < numberOfObjects; i++)
            {
                // --- Select Prefab ---
                int randomIndex = Random.Range(0, prefabsToSpawn.Count);
                GameObject prefabToUse = prefabsToSpawn[randomIndex];
                if (prefabToUse == null) continue; // Skip if selected prefab slot is null

                // --- Calculate Angle for this Object ---
                float currentAngleRad;
                // Handle edge case: If only one object, place it exactly at the start angle.
                if (numberOfObjects == 1)
                {
                    currentAngleRad = startAngleRad;
                }
                // Handle edge case: If arc is effectively zero, place all objects at start angle.
                else if (arcDegrees <= 0.001f) // Use a small tolerance for floating point comparison
                {
                     currentAngleRad = startAngleRad;
                }
                // Handle edge case: If arc is a full circle (or very close to it)
                else if (arcDegrees >= 359.999f)
                {
                    // Standard full circle distribution, offset by start angle
                    currentAngleRad = startAngleRad + i * (Mathf.PI * 2f / numberOfObjects);
                }
                // Standard case: Distribute objects along the specified arc
                else
                {
                    // Divide the arc into (N-1) segments to place objects at start and end inclusively.
                    float angleStepRad = arcRad / (numberOfObjects - 1);
                    currentAngleRad = startAngleRad + i * angleStepRad;
                }

                // --- Calculate Position & Rotation ---
                // Direction vector from center based on current angle
                Vector3 spawnDirection = new Vector3(Mathf.Cos(currentAngleRad), 0, Mathf.Sin(currentAngleRad));
                // Position offset from the spawner's center
                Vector3 spawnPosition = transform.position + spawnDirection * radius;
                // Rotation based on settings
                Quaternion spawnRotation = faceOutwards
                    ? Quaternion.LookRotation(spawnDirection) // Face outwards
                    : Quaternion.Euler(0, -currentAngleRad * Mathf.Rad2Deg, 0); // Face tangentially


                // --- Instantiate & Scale ---
                GameObject spawnedObject = Instantiate(prefabToUse, spawnPosition, spawnRotation, transform);
                spawnedObject.name = $"{prefabToUse.name}_{i}";
                spawnedObject.transform.localScale = prefabToUse.transform.localScale * scaleFactor;

                #if UNITY_EDITOR
                Undo.RegisterCreatedObjectUndo(spawnedObject, "Spawn Circle Object");
                #endif
            }
        }
        finally
        {
            isSpawning = false; // Reset the flag
        }
    }

    /// <summary>
    /// Clears all child GameObjects, respecting the Undo system in the editor.
    /// </summary>
    void ClearChildren()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            GameObject child = transform.GetChild(i).gameObject;
            #if UNITY_EDITOR
            Undo.DestroyObjectImmediate(child);
            #else
            DestroyImmediate(child);
            #endif
        }
    }

    /// <summary>
    /// Draw gizmos in the scene view when the object is selected, showing the arc and spawn points.
    /// </summary>
    void OnDrawGizmosSelected()
    {
        if (radius <= 0) return;

        Vector3 center = transform.position;
        float startAngleRad = startAngleDegrees * Mathf.Deg2Rad;
        float arcRad = arcDegrees * Mathf.Deg2Rad;
        float endAngleRad = startAngleRad + arcRad;

        // --- Draw the Arc Outline ---
        Gizmos.color = Color.yellow;
        // Use a reasonable number of segments for the arc visualization
        int arcSegments = Mathf.Max(2, Mathf.CeilToInt(60 * (arcDegrees / 360f)));
        Vector3 prevPoint = center + new Vector3(Mathf.Cos(startAngleRad), 0, Mathf.Sin(startAngleRad)) * radius;

        for (int i = 1; i <= arcSegments; i++)
        {
            // Interpolate angle along the arc
            float currentRad = Mathf.Lerp(startAngleRad, endAngleRad, (float)i / arcSegments);
            Vector3 nextPoint = center + new Vector3(Mathf.Cos(currentRad), 0, Mathf.Sin(currentRad)) * radius;
            Gizmos.DrawLine(prevPoint, nextPoint);
            prevPoint = nextPoint;
        }

        // Draw lines from center to arc start/end for clarity, unless it's a full circle
         if (arcDegrees < 359.999f) // Avoid drawing overlapping lines for full circle
         {
            Gizmos.color = Color.gray;
            Gizmos.DrawLine(center, center + new Vector3(Mathf.Cos(startAngleRad), 0, Mathf.Sin(startAngleRad)) * radius);
            Gizmos.DrawLine(center, center + new Vector3(Mathf.Cos(endAngleRad), 0, Mathf.Sin(endAngleRad)) * radius);
         }


        // --- Draw markers at the calculated spawn points within the arc ---
        if (numberOfObjects > 0)
        {
            Gizmos.color = Color.cyan;
            float markerSize = Mathf.Max(0.1f, radius * 0.05f);

            // Replicate the angle calculation logic from DelayedUpdateSpawn for accuracy
            for (int i = 0; i < numberOfObjects; i++)
            {
                float currentAngleRad;
                if (numberOfObjects == 1) {
                    currentAngleRad = startAngleRad;
                } else if (arcDegrees <= 0.001f) {
                     currentAngleRad = startAngleRad;
                } else if (arcDegrees >= 359.999f) {
                    currentAngleRad = startAngleRad + i * (Mathf.PI * 2f / numberOfObjects);
                } else {
                    float angleStepRad = arcRad / (numberOfObjects - 1);
                    currentAngleRad = startAngleRad + i * angleStepRad;
                }

                Vector3 spawnPoint = center + new Vector3(Mathf.Cos(currentAngleRad), 0, Mathf.Sin(currentAngleRad)) * radius;
                Gizmos.DrawSphere(spawnPoint, markerSize);
            }
        }
    }
}