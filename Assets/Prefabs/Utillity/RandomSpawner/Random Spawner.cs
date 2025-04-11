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

    private void OnValidate()
    {
        // Ensure scale is not negative in the editor
        if (spawnScale < 0) spawnScale = 0;

        // Use delayCall to avoid issues with modifying hierarchy during OnValidate
        if (!Application.isPlaying)
        {
            #if UNITY_EDITOR
            // Schedule the clear and spawn operations for the next editor update
            UnityEditor.EditorApplication.delayCall += () =>
            {
                // Ensure the component and GameObject still exist when the call happens
                if (this != null && this.gameObject != null)
                {
                    ClearSpawnedObjects();
                    SpawnObjects();
                }
            };
            #endif
        }
    }

    private void SpawnObjects()
    {
        if (objectToSpawn == null)
        {
            Debug.LogWarning("Object To Spawn is not assigned.", this);
            return;
        }

        // Ensure scale is not negative during runtime spawn (if ever used)
        float effectiveScale = Mathf.Max(0f, spawnScale);

        for (int i = 0; i < spawnCount; i++)
        {
            // 1. Generate random angle (theta) between 0 and 2*PI radians
            float angle = Random.Range(0f, 2f * Mathf.PI);

            // 2. Generate random distance (r) from the center (0 to radius)
            //    This "naive" approach clusters points towards the center as requested.
            float distance = Random.Range(0f, radius);

            // 3. Convert polar coordinates (angle, distance) to Cartesian coordinates (x, z)
            float x = distance * Mathf.Cos(angle);
            float z = distance * Mathf.Sin(angle);

            // 4. Create the position vector relative to the spawner's position
            Vector3 spawnPosition = transform.position + new Vector3(x, 0f, z); // Assuming Y=0 for spawn plane

            // 5. Instantiate the object
            GameObject spawnedObject = Instantiate(objectToSpawn, spawnPosition, Quaternion.identity, transform);
            spawnedObject.name = $"{objectToSpawn.name}_{i}"; // Give unique names

            // 6. Apply the scale
            //    We use Vector3.one * scale for uniform scaling on all axes.
            spawnedObject.transform.localScale = Vector3.one * effectiveScale;

            #if UNITY_EDITOR
            // This prevents the spawned object from being saved with the scene asset
            spawnedObject.hideFlags = HideFlags.DontSaveInEditor;
            #endif
        }
    }

    /// <summary>
    /// Clears all child GameObjects created by this spawner,
    /// respecting the Undo system in the editor.
    /// </summary>
    void ClearSpawnedObjects()
    {
        string objectNamePrefix = (objectToSpawn != null) ? objectToSpawn.name : "Spawned_";

        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            GameObject child = transform.GetChild(i).gameObject;

            if (child != null && child.name.StartsWith(objectNamePrefix))
            {
                #if UNITY_EDITOR
                Undo.DestroyObjectImmediate(child);
                #else
                DestroyImmediate(child);
                #endif
            }
        }
    }
}