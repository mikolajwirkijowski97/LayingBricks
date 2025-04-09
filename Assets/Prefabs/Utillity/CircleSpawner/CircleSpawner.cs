using UnityEngine;
using System.Collections.Generic; // Required for using List<>

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Spawns a number of objects chosen randomly from a list in a circle around the GameObject.
/// Allows scaling of the spawned objects.
/// Updates dynamically in the editor when parameters are changed.
/// </summary>
[ExecuteInEditMode]
public class CircleSpawner : MonoBehaviour
{
    // --- Fields ---
    [Header("Spawning Setup")]
    [SerializeField]
    [Tooltip("List of prefabs to choose from when spawning. One will be picked randomly for each spot.")]
    private List<GameObject> prefabsToSpawn = new List<GameObject>(); // Initialize list

    [SerializeField]
    [Tooltip("Number of objects to spawn in a circle")]
    [Min(1)]
    private int numberOfObjects = 10;

    [SerializeField]
    [Tooltip("Radius of the circle")]
    [Min(0f)]
    private float radius = 5f;

    [Header("Spawn Transform Options")]
    [SerializeField]
    [Tooltip("Uniform scale multiplier applied to the spawned objects (relative to prefab's original scale).")]
    [Min(0.01f)] // Prevent zero or negative scale
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
        // 1. Check if the list itself exists and has items
        if (prefabsToSpawn == null || prefabsToSpawn.Count == 0)
        {
            // Debug.LogWarning("CircleSpawner: Prefab list is empty. Nothing to spawn.", this);
            return; // Exit if list is null or empty
        }

        // 2. Check if there's at least one non-null prefab in the list
        bool hasValidPrefab = false;
        foreach (var prefab in prefabsToSpawn)
        {
            if (prefab != null)
            {
                hasValidPrefab = true;
                break;
            }
        }
        if (!hasValidPrefab)
        {
            // Debug.LogWarning("CircleSpawner: All entries in the prefab list are null. Nothing to spawn.", this);
            return; // Exit if all list entries are null
        }

        // 3. Check other parameters
        if (numberOfObjects <= 0 || radius < 0 || scaleFactor <= 0)
        {
            return; // Exit if other parameters are invalid
        }

        // --- Spawning ---
        isSpawning = true;
        try
        {
            for (int i = 0; i < numberOfObjects; i++)
            {
                // --- Select Prefab ---
                // Get a random index from the list
                int randomIndex = Random.Range(0, prefabsToSpawn.Count);
                GameObject prefabToUse = prefabsToSpawn[randomIndex];

                // Important: Skip this iteration if the randomly selected slot is null
                if (prefabToUse == null)
                {
                    // Debug.LogWarning($"CircleSpawner: Prefab at index {randomIndex} is null. Skipping position {i}.", this);
                    continue; // Move to the next object/position
                }

                // --- Calculate Position & Rotation ---
                float angle = i * Mathf.PI * 2f / numberOfObjects;
                Vector3 spawnDirection = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                Vector3 spawnPosition = transform.position + spawnDirection * radius;
                Quaternion spawnRotation = faceOutwards
                    ? Quaternion.LookRotation(spawnDirection) // Face outwards
                    : Quaternion.Euler(0, -angle * Mathf.Rad2Deg, 0); // Face tangentially


                // --- Instantiate ---
                GameObject spawnedObject = Instantiate(prefabToUse, spawnPosition, spawnRotation, transform);
                spawnedObject.name = $"{prefabToUse.name}_{i}"; // Give a meaningful name

                // --- Apply Scale ---
                // Multiply the instantiated object's scale by the scaleFactor,
                // preserving the prefab's original relative scale.
                spawnedObject.transform.localScale = prefabToUse.transform.localScale * scaleFactor;


                #if UNITY_EDITOR
                // Register for Undo System
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
        // Iterate backwards as we are modifying the child collection
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            GameObject child = transform.GetChild(i).gameObject;
            #if UNITY_EDITOR
            // Use Undo-aware immediate destruction in editor
            Undo.DestroyObjectImmediate(child);
            #else
            // Fallback for runtime (though DelayUpdateSpawn primarily runs in editor)
            DestroyImmediate(child);
            #endif
        }
    }


    /// <summary>
    /// Draw gizmos in the scene view when the object is selected.
    /// (Remains unchanged as it visualizes positions, not specific prefabs/scales)
    /// </summary>
    void OnDrawGizmosSelected()
    {
        if (radius <= 0) return;

        Vector3 center = transform.position;
        Gizmos.color = Color.yellow;

        // Draw the circle outline
        int segments = 40;
        Vector3 prevPoint = center + new Vector3(1, 0, 0) * radius;

        for (int i = 1; i <= segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            Vector3 nextPoint = center + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius;
            Gizmos.DrawLine(prevPoint, nextPoint);
            prevPoint = nextPoint;
        }

        // Draw markers at the calculated spawn points
        if (numberOfObjects > 0)
        {
            Gizmos.color = Color.cyan;
            float markerSize = Mathf.Max(0.1f, radius * 0.05f); // Ensure minimum size

            for (int i = 0; i < numberOfObjects; i++)
            {
                float angle = i * Mathf.PI * 2f / numberOfObjects;
                Vector3 spawnPoint = center + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius;
                Gizmos.DrawSphere(spawnPoint, markerSize);
            }
        }
    }
}