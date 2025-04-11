using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteInEditMode]
public class RandomSpawner : MonoBehaviour
{
    public GameObject objectToSpawn; // The prefab to spawn
    public float radius = 5f;       // Radius of the circular spawn area
    public int spawnCount = 10;       // Number of objects to spawn
    public float spawnScale = 1.0f;   // Uniform scale multiplier for spawned objects (1 = original size)

    // Called once when the script instance is enabled, AFTER Awake, ONLY in Play Mode
    void Start()
    {
        // Ensure we only run this logic during actual gameplay
        if (Application.isPlaying)
        {
            // Optional: Clear any potential remnants first (e.g., if Start was called unexpectedly)
            // ClearSpawnedObjects(); // Usually not needed if OnValidate handles editor cleanup

            // Spawn the objects specifically for the runtime session
            SpawnObjects();
        }
    }

    private void OnValidate()
    {
        if (spawnScale < 0) spawnScale = 0;

        // IMPORTANT: Only run editor spawning logic when NOT in Play Mode
        if (!Application.isPlaying)
        {
            #if UNITY_EDITOR
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null && this.gameObject != null)
                {
                    if (objectToSpawn != null) {
                         ClearSpawnedObjects();
                         SpawnObjects();
                    } else {
                         ClearSpawnedObjects();
                         // Warning moved out of loop for clarity
                    }
                }
            };
             // Put warning outside delayCall so it appears immediately if prefab is null
            if (objectToSpawn == null) {
                 Debug.LogWarning("Assign an 'Object To Spawn' to see spawned objects.", this);
            }
            #endif
        }
    }

    private void SpawnObjects()
    {
        if (objectToSpawn == null) {
             // Add runtime check as well
             if(Application.isPlaying) Debug.LogError("Cannot spawn objects: 'Object To Spawn' is not assigned.", this);
             return;
        }

        float effectiveScale = Mathf.Max(0f, spawnScale);

        for (int i = 0; i < spawnCount; i++)
        {
            float angle = Random.Range(0f, 2f * Mathf.PI);
            float distance = Random.Range(0f, radius);
            float x = distance * Mathf.Cos(angle);
            float z = distance * Mathf.Sin(angle);
            Vector3 spawnPosition = transform.position + new Vector3(x, 0f, z);

            GameObject spawnedObject = Instantiate(objectToSpawn, spawnPosition, Quaternion.identity, transform);
            spawnedObject.name = $"{objectToSpawn.name}_{i}";
            spawnedObject.transform.localScale = Vector3.one * effectiveScale;

            // Apply flag ONLY in the editor
            #if UNITY_EDITOR
            if (!Application.isPlaying) // Double check we are not in play mode here
            {
                 spawnedObject.hideFlags = HideFlags.DontSaveInEditor;
            }
            #endif
        }
    }

    void ClearSpawnedObjects()
    {
        string objectNamePrefix = (objectToSpawn != null) ? objectToSpawn.name : transform.name + "_Spawned_";

        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            GameObject child = transform.GetChild(i).gameObject;

            if (child != null)
            {
                // Check if the object seems to be one of ours
                // Using name prefix is usually okay. A dedicated component/tag is more robust.
                bool likelySpawned = child.name.StartsWith(objectNamePrefix);
                #if UNITY_EDITOR
                // Also check flag in editor, as object name could theoretically clash
                 if (!Application.isPlaying) {
                    likelySpawned = likelySpawned || child.hideFlags == HideFlags.DontSaveInEditor;
                 }
                #endif


                if (likelySpawned)
                {
                    #if UNITY_EDITOR
                    // Use Undo-aware DestroyImmediate in the editor
                    if (!Application.isPlaying) {
                         Undo.DestroyObjectImmediate(child);
                    } else {
                         // Use regular Destroy if clearing at runtime (e.g., in Start/OnDisable)
                         Destroy(child);
                    }
                    #else
                    // Use regular Destroy in builds/runtime
                    Destroy(child);
                    #endif
                }
            }
        }
    }
}