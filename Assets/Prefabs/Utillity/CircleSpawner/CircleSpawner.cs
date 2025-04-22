using UnityEngine;
using System.Collections.Generic; // Required for using List<>

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Spawns objects chosen randomly from a list within a specified volume (Arc, Circle Area, Cylinder, Hollow Cylinder).
/// Allows scaling, rotation options, and updates dynamically in the editor based on the 'Update Automatically' flag.
/// </summary>
[ExecuteInEditMode]
public class VolumeSpawner : MonoBehaviour
{
    // --- Enums ---
    public enum SpawnShape
    {
        CircleArcEdge,  // Original behavior: Spawn on the edge of an arc/circle
        CircleArea,     // Spawn within the area of a circle/sector
        CylinderVolume, // Spawn within the volume of a cylinder/cylindrical sector
        HollowCylinderVolume // Spawn within the volume between two cylinders (a pipe/ring sector)
    }

    // --- Fields ---
    [Header("Spawning Setup")]
    [SerializeField]
    [Tooltip("Enable this to automatically regenerate objects when parameters change in the editor. Disable before saving/committing to prevent unwanted scene changes.")]
    private bool updateAutomatically = false; // Renamed from doUpdate for clarity

    [SerializeField]
    [Tooltip("List of prefabs to choose from when spawning. One will be picked randomly for each spot.")]
    private List<GameObject> prefabsToSpawn = new List<GameObject>();

    [SerializeField]
    [Tooltip("Number of objects to spawn within the defined volume.")]
    [Min(1)]
    private int numberOfObjects = 10;

    [SerializeField]
    [Tooltip("The shape of the volume to spawn objects within.")]
    private SpawnShape spawnShape = SpawnShape.CircleArcEdge;

    [Header("Shape Parameters")]
    [SerializeField]
    [Tooltip("Outer radius of the shape.")]
    [Min(0f)]
    private float outerRadius = 5f;

    [SerializeField]
    [Tooltip("Inner radius for Hollow Cylinder volume. Must be less than Outer Radius.")]
    [Min(0f)]
    private float innerRadius = 2f; // Only used for HollowCylinderVolume

    [SerializeField]
    [Tooltip("Height of the Cylinder or Hollow Cylinder volume (centered vertically on the spawner).")]
    [Min(0.01f)]
    private float height = 4f; // Only used for CylinderVolume and HollowCylinderVolume

    [SerializeField]
    [Tooltip("Starting angle of the arc/sector in degrees (0 = positive X axis, clockwise).")]
    private float startAngleDegrees = 0f;

    [SerializeField]
    [Tooltip("The angular span of the arc/sector in degrees (e.g., 180 for a semi-circle, 360 for a full circle/cylinder).")]
    [Range(0f, 360f)]
    private float arcDegrees = 360f;

    [Header("Spawn Transform Options")]
    [SerializeField]
    [Tooltip("Uniform scale multiplier applied to the spawned objects (relative to prefab's original scale).")]
    [Min(0.01f)]
    private float scaleFactor = 1.0f;

    [SerializeField]
    [Tooltip("Should the spawned objects face outwards from the center axis? (Ignored if Randomize Rotation is true)")]
    private bool faceOutwards = true;

    [SerializeField]
    [Tooltip("Should the spawned objects have a random rotation?")]
    private bool randomizeRotation = false;


    // Internal state flag
    private bool isSpawning = false;

    // --- Methods ---

    void OnValidate()
    {
        // Clamp inner radius to be less than outer radius
        if (innerRadius >= outerRadius)
        {
            innerRadius = Mathf.Max(0f, outerRadius - 0.1f);
        }
        // Clamp minimums
        if (outerRadius < 0f) outerRadius = 0f;
        if (innerRadius < 0f) innerRadius = 0f;
        if (height < 0.01f) height = 0.01f;
        if (scaleFactor < 0.01f) scaleFactor = 0.01f;
        if (numberOfObjects < 1) numberOfObjects = 1;


        // Check the flag before attempting to spawn automatically
        if (!updateAutomatically)
        {
            return; // Don't spawn if automatic updates are disabled
        }

        // Prevent running spawn logic during play mode, while already spawning,
        // or if the object isn't part of a valid scene (e.g., prefab asset view).
        if (Application.isPlaying || isSpawning || !gameObject.scene.IsValid())
        {
            return;
        }

        // Schedule the update for the next editor frame to avoid potential issues.
        #if UNITY_EDITOR
        // Ensure previous calls are cleared before scheduling a new one to avoid stacking updates
        EditorApplication.delayCall -= DelayedUpdateSpawn;
        EditorApplication.delayCall += DelayedUpdateSpawn;
        #endif
    }

    // This method now contains the core spawning logic, called via delayCall from OnValidate
    void DelayedUpdateSpawn()
    {
        // Ensure component/GameObject still exists before proceeding
        // Also check the flag again, in case it was disabled between OnValidate and the delayed call
        if (this == null || gameObject == null || !gameObject.scene.IsValid() || !updateAutomatically || isSpawning) return;

        // --- Cleanup First ---
        ClearChildren();

        // --- Validation ---
        // Check prefabs
        if (prefabsToSpawn == null || prefabsToSpawn.Count == 0) return;
        bool hasValidPrefab = false;
        foreach (var prefab in prefabsToSpawn) { if (prefab != null) { hasValidPrefab = true; break; } }
        if (!hasValidPrefab) return;

        // Basic parameter check
        if (numberOfObjects <= 0 || outerRadius < 0 || scaleFactor <= 0) return;
        // Shape specific checks
        if (spawnShape == SpawnShape.HollowCylinderVolume && innerRadius >= outerRadius) return; // Invalid hollow cylinder
        if ((spawnShape == SpawnShape.CylinderVolume || spawnShape == SpawnShape.HollowCylinderVolume) && height <= 0) return;


        // --- Spawning ---
        isSpawning = true;
        #if UNITY_EDITOR
        // Register root object for Undo, so the whole generation can be undone
        Undo.RegisterCompleteObjectUndo(this, "Update Spawned Objects");
        #endif
        try
        {
            // Convert input degrees to radians for calculations
            float startAngleRad = startAngleDegrees * Mathf.Deg2Rad;
            float arcRad = arcDegrees * Mathf.Deg2Rad;
            float endAngleRad = startAngleRad + arcRad;

            for (int i = 0; i < numberOfObjects; i++)
            {
                // --- Select Prefab ---
                int randomIndex = Random.Range(0, prefabsToSpawn.Count);
                GameObject prefabToUse = prefabsToSpawn[randomIndex];
                if (prefabToUse == null) continue; // Skip if selected prefab slot is null

                // --- Calculate Position ---
                Vector3 spawnPosition = transform.position; // Start at spawner center
                Vector3 spawnDirection = Vector3.forward; // Default direction

                // Calculate random position based on SpawnShape
                switch (spawnShape)
                {
                    case SpawnShape.CircleArcEdge:
                        spawnPosition = CalculatePositionOnArcEdge(startAngleRad, arcRad, i);
                        spawnDirection = (spawnPosition - transform.position).normalized; // Direction from center
                         // Ensure direction isn't zero vector if at center (unlikely with ArcEdge)
                        if (spawnDirection == Vector3.zero) spawnDirection = transform.forward;
                       break;

                    case SpawnShape.CircleArea:
                        spawnPosition = CalculatePositionInCircleArea(startAngleRad, endAngleRad);
                        spawnDirection = (spawnPosition - transform.position).normalized;
                         if (spawnDirection == Vector3.zero) spawnDirection = transform.forward;
                        break;

                    case SpawnShape.CylinderVolume:
                        spawnPosition = CalculatePositionInCylinder(startAngleRad, endAngleRad, 0f, outerRadius);
                        spawnDirection = (new Vector3(spawnPosition.x, transform.position.y, spawnPosition.z) - transform.position).normalized; // Horizontal direction from center axis
                         if (spawnDirection == Vector3.zero) spawnDirection = transform.forward;
                       break;

                    case SpawnShape.HollowCylinderVolume:
                        spawnPosition = CalculatePositionInCylinder(startAngleRad, endAngleRad, innerRadius, outerRadius);
                        spawnDirection = (new Vector3(spawnPosition.x, transform.position.y, spawnPosition.z) - transform.position).normalized; // Horizontal direction from center axis
                         if (spawnDirection == Vector3.zero) spawnDirection = transform.forward;
                        break;
                }


                // --- Calculate Rotation ---
                Quaternion spawnRotation = Quaternion.identity; // Default rotation
                if (randomizeRotation)
                {
                    spawnRotation = Random.rotation;
                }
                else if (faceOutwards)
                {
                   // For ArcEdge, direction is directly from center.
                   // For volumes, use the horizontal component of the direction from the central Y axis.
                   Vector3 lookDirection = (spawnShape == SpawnShape.CircleArcEdge)
                        ? spawnDirection
                        : new Vector3(spawnDirection.x, 0, spawnDirection.z).normalized;

                    if (lookDirection != Vector3.zero) // Avoid LookRotation error if direction is zero
                    {
                       // Apply parent rotation offset
                       spawnRotation = Quaternion.LookRotation(lookDirection) * Quaternion.Inverse(transform.rotation);
                       spawnRotation = transform.rotation * Quaternion.LookRotation(lookDirection);
                    }
                    else {
                        spawnRotation = transform.rotation; // Fallback to parent rotation if look direction is zero
                    }
                }
                else
                {
                    // Keep parent's rotation if not randomizing or facing outwards
                    spawnRotation = transform.rotation;
                }

                // --- Instantiate, Scale & Parent ---
                #if UNITY_EDITOR
                 // Use PrefabUtility.InstantiatePrefab for better prefab connection in editor
                GameObject spawnedObject = (GameObject)PrefabUtility.InstantiatePrefab(prefabToUse, transform);
                spawnedObject.transform.position = spawnPosition;
                spawnedObject.transform.rotation = spawnRotation; // Apply calculated world rotation
                Undo.RegisterCreatedObjectUndo(spawnedObject, $"Spawn {spawnShape} Object");

                #else
                // Runtime instantiation
                GameObject spawnedObject = Instantiate(prefabToUse, spawnPosition, spawnRotation, transform);
                #endif

                spawnedObject.name = $"{prefabToUse.name}_{i}";
                // Scale relatively to the prefab's original scale AFTER parenting
                spawnedObject.transform.localScale = prefabToUse.transform.localScale * scaleFactor;

            }
        }
        finally
        {
            isSpawning = false; // Reset the flag
            #if UNITY_EDITOR
            // Ensure scene is marked dirty so changes are saved
             if (!Application.isPlaying)
             {
                 UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
             }
            #endif
        }
    }

    // --- Position Calculation Helpers ---
    // These helpers now return world positions directly, considering parent transform

     Vector3 CalculatePositionOnArcEdge(float startRad, float arcRad, int index)
    {
        float currentAngleRad;
        float localArcDegrees = arcDegrees; // Use the serialized field value for logic

        if (numberOfObjects == 1) { currentAngleRad = startRad; }
        else if (localArcDegrees <= 0.001f) { currentAngleRad = startRad; }
        else if (localArcDegrees >= 359.999f) { currentAngleRad = startRad + index * (Mathf.PI * 2f / numberOfObjects); }
        else { float angleStepRad = arcRad / (numberOfObjects - 1); currentAngleRad = startRad + index * angleStepRad; }

        Vector3 localDirection = new Vector3(Mathf.Cos(currentAngleRad), 0, Mathf.Sin(currentAngleRad));
        Vector3 worldOffset = transform.rotation * (localDirection * outerRadius);
        return transform.position + worldOffset;
    }

    Vector3 CalculatePositionInCircleArea(float startRad, float endRad)
    {
        float randomAngleRad = Random.Range(startRad, endRad);
        float randomRadius = outerRadius * Mathf.Sqrt(Random.value);
        Vector3 localDirection = new Vector3(Mathf.Cos(randomAngleRad), 0, Mathf.Sin(randomAngleRad));
        Vector3 worldOffset = transform.rotation * (localDirection * randomRadius);
        return transform.position + worldOffset;
    }

     Vector3 CalculatePositionInCylinder(float startRad, float endRad, float minRadius, float maxRadius)
    {
        float randomAngleRad = Random.Range(startRad, endRad);
        float minRadiusSqr = minRadius * minRadius;
        float maxRadiusSqr = maxRadius * maxRadius;
        float randomRadius = Mathf.Sqrt(minRadiusSqr + Random.value * (maxRadiusSqr - minRadiusSqr));
        float randomY = Random.Range(-height / 2f, height / 2f);

        Vector3 localDirection = new Vector3(Mathf.Cos(randomAngleRad), 0, Mathf.Sin(randomAngleRad));
        // Combine horizontal offset and vertical offset in local space before rotating
        Vector3 localOffset = (localDirection * randomRadius) + (Vector3.up * randomY);
        Vector3 worldOffset = transform.rotation * localOffset;
        return transform.position + worldOffset;
    }


    /// <summary>
    /// Clears all child GameObjects, respecting the Undo system in the editor.
    /// </summary>
    void ClearChildren()
    {
        // Record Undo for the clearing action itself when called from DelayedUpdateSpawn
        // No need to register the root object again here, as DelayedUpdateSpawn already does.
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            GameObject child = transform.GetChild(i).gameObject;
            #if UNITY_EDITOR
            // Record destruction of each child for Undo
            Undo.DestroyObjectImmediate(child);
            #else
            DestroyImmediate(child); // Use DestroyImmediate for editor cleanup outside play mode
            #endif
        }
         #if UNITY_EDITOR
         // Scene dirty marking is handled in DelayedUpdateSpawn after potential regeneration
        #endif
    }

     /// <summary>
    /// Draw gizmos in the scene view when the object is selected.
    /// </summary>
    void OnDrawGizmosSelected()
    {
        // Clamp values used in Gizmos drawing locally to avoid issues if OnValidate hasn't run yet
        float validOuterRadius = Mathf.Max(0f, outerRadius);
        float validInnerRadius = Mathf.Clamp(innerRadius, 0f, validOuterRadius);
        float validHeight = Mathf.Max(0.01f, height);
        float validArcDegrees = Mathf.Clamp(arcDegrees, 0f, 360f);

        if (validOuterRadius <= 0 && spawnShape != SpawnShape.CircleArcEdge) return; // Need radius for most shapes
        if (validOuterRadius <= 0 && spawnShape == SpawnShape.CircleArcEdge && numberOfObjects > 0) {} // Allow zero radius for arc edge if spawning happens
        else if (validOuterRadius <= 0) return;


        Vector3 center = transform.position;
        Quaternion rotation = transform.rotation; // Use spawner's rotation
        float startAngleRad = startAngleDegrees * Mathf.Deg2Rad;
        float arcRad = validArcDegrees * Mathf.Deg2Rad;
        float endAngleRad = startAngleRad + arcRad;
        int arcSegments = Mathf.Max(2, Mathf.CeilToInt(60 * (validArcDegrees / 360f)));

        // Store current Handles matrix and color
        #if UNITY_EDITOR
        Color defaultHandlesColor = Handles.color;
        Matrix4x4 defaultMatrix = Handles.matrix;
        // Set Handles matrix to match object's transform
        Handles.matrix = Matrix4x4.TRS(center, rotation, Vector3.one);
        #endif

        // Draw based on shape
        switch (spawnShape)
        {
            case SpawnShape.CircleArcEdge:
                // Use Handles for wire arc for consistency and rotation handling
                #if UNITY_EDITOR
                Handles.color = Color.yellow;
                Handles.DrawWireArc(Vector3.zero, Vector3.up, // Position relative to matrix
                                   new Vector3(Mathf.Cos(startAngleRad), 0, Mathf.Sin(startAngleRad)),
                                   validArcDegrees, validOuterRadius);
                 // Draw lines from center to arc start/end for clarity, unless it's a full circle
                if (validArcDegrees < 359.999f)
                {
                    Handles.color = Color.grey;
                    Handles.DrawLine(Vector3.zero, new Vector3(Mathf.Cos(startAngleRad), 0, Mathf.Sin(startAngleRad)) * validOuterRadius);
                    Handles.DrawLine(Vector3.zero, new Vector3(Mathf.Cos(endAngleRad), 0, Mathf.Sin(endAngleRad)) * validOuterRadius);
                }
                #else // Fallback Gizmos drawing
                DrawArcGizmoGizmos(center, rotation, startAngleRad, arcRad, validOuterRadius, arcSegments, Color.yellow);
                #endif
                DrawSpawnPointMarkersArc(center, rotation, startAngleRad, arcRad, validOuterRadius, Color.cyan); // Use Gizmos for markers
                break;

            case SpawnShape.CircleArea:
                 #if UNITY_EDITOR
                 Handles.color = new Color(0.8f, 0.8f, 0.1f, 0.2f); // Semi-transparent yellow
                 Handles.DrawSolidArc(Vector3.zero, Vector3.up, // Position relative to matrix
                                     new Vector3(Mathf.Cos(startAngleRad), 0, Mathf.Sin(startAngleRad)),
                                     validArcDegrees, validOuterRadius);
                // Draw outline arc
                Handles.color = Color.yellow;
                Handles.DrawWireArc(Vector3.zero, Vector3.up, new Vector3(Mathf.Cos(startAngleRad), 0, Mathf.Sin(startAngleRad)), validArcDegrees, validOuterRadius);
                 // Draw lines from center to arc start/end for clarity, unless it's a full circle
                if (validArcDegrees < 359.999f)
                {
                    Handles.color = Color.grey;
                    Handles.DrawLine(Vector3.zero, new Vector3(Mathf.Cos(startAngleRad), 0, Mathf.Sin(startAngleRad)) * validOuterRadius);
                    Handles.DrawLine(Vector3.zero, new Vector3(Mathf.Cos(endAngleRad), 0, Mathf.Sin(endAngleRad)) * validOuterRadius);
                }
                 #else // Fallback Gizmos drawing
                 DrawArcGizmoGizmos(center, rotation, startAngleRad, arcRad, validOuterRadius, arcSegments, Color.yellow);
                 #endif
                break;

            case SpawnShape.CylinderVolume:
                DrawCylinderGizmo(startAngleRad, arcRad, 0f, validOuterRadius, validHeight, validArcDegrees, arcSegments, Color.blue, new Color(0.1f, 0.1f, 0.8f, 0.1f));
                break;

            case SpawnShape.HollowCylinderVolume:
                 DrawCylinderGizmo(startAngleRad, arcRad, validInnerRadius, validOuterRadius, validHeight, validArcDegrees, arcSegments, Color.green, new Color(0.1f, 0.8f, 0.1f, 0.1f));
                break;
        }

        // Restore Handles matrix and color
         #if UNITY_EDITOR
         Handles.color = defaultHandlesColor;
         Handles.matrix = defaultMatrix;
         #endif
    }

    // --- Gizmo Drawing Helpers --- (Copied from previous version, ensure they are compatible)

    #if !UNITY_EDITOR
    // Fallback Gizmos drawing for Arc (used when Handles are not available)
    void DrawArcGizmoGizmos(Vector3 center, Quaternion rotation, float startRad, float arcRad, float radius, int segments, Color color, bool drawCenterLines = true)
    {
        if (radius <= 0) return;
        float endRad = startRad + arcRad;
        float validArcDegrees = arcRad * Mathf.Rad2Deg; // Need degrees for comparison

        Gizmos.color = color;
        Vector3 startDir = new Vector3(Mathf.Cos(startRad), 0, Mathf.Sin(startRad));
        Vector3 prevPoint = center + rotation * (startDir * radius);

        for (int i = 1; i <= segments; i++)
        {
            float currentRad = Mathf.Lerp(startRad, endRad, (float)i / segments);
            Vector3 nextPoint = center + rotation * (new Vector3(Mathf.Cos(currentRad), 0, Mathf.Sin(currentRad)) * radius);
            Gizmos.DrawLine(prevPoint, nextPoint);
            prevPoint = nextPoint;
        }

        // Draw lines from center to arc start/end for clarity, unless it's a full circle
        if (drawCenterLines && validArcDegrees < 359.999f)
        {
            Vector3 endDir = new Vector3(Mathf.Cos(endRad), 0, Mathf.Sin(endRad));
            Gizmos.color = Color.grey;
             Gizmos.DrawLine(center, center + rotation * (startDir * radius));
             Gizmos.DrawLine(center, center + rotation * (endDir * radius));
        }
    }
    #endif


    // Note: This helper now expects angles in radians and degrees separately for clarity
    void DrawCylinderGizmo(float startRad, float arcRad, float iRadius, float oRadius, float h, float arcDeg, int segments, Color outlineColor, Color volumeColor)
    {
        // This function relies heavily on Handles, so only implement the Handles version
        #if UNITY_EDITOR
        float halfHeight = h / 2f;
        Vector3 topCenter = Vector3.up * halfHeight; // Relative to Handles matrix center (which includes position/rotation)
        Vector3 bottomCenter = Vector3.down * halfHeight; // Relative to Handles matrix center

        Handles.color = volumeColor;
        Vector3 arcStartDir = new Vector3(Mathf.Cos(startRad), 0, Mathf.Sin(startRad));

        // Draw solid caps (or sectors) - Outer
         if (oRadius > 0) {
             Handles.DrawSolidArc(topCenter, Vector3.up, arcStartDir, arcDeg, oRadius);
             Handles.DrawSolidArc(bottomCenter, Vector3.up, arcStartDir, arcDeg, oRadius);
         }

         // If hollow, draw inner caps with clear color to "erase" the inside (visual cue)
         if (iRadius > 0 && iRadius < oRadius) {
             // Using a very transparent version of the outline color can sometimes look better than Color.clear
             Color innerCapColor = outlineColor; innerCapColor.a = 0.01f; // Almost clear
             Handles.color = innerCapColor;
             Handles.DrawSolidArc(topCenter, Vector3.up, arcStartDir, arcDeg, iRadius);
             Handles.DrawSolidArc(bottomCenter, Vector3.up, arcStartDir, arcDeg, iRadius);
         }


        Handles.color = outlineColor; // Switch to outline color
         // Draw top and bottom arcs (outer)
        if (oRadius > 0) {
             Handles.DrawWireArc(topCenter, Vector3.up, arcStartDir, arcDeg, oRadius);
             Handles.DrawWireArc(bottomCenter, Vector3.up, arcStartDir, arcDeg, oRadius);
        }
        // Draw top and bottom arcs (inner)
        if (iRadius > 0 && iRadius < oRadius) {
             Handles.DrawWireArc(topCenter, Vector3.up, arcStartDir, arcDeg, iRadius);
             Handles.DrawWireArc(bottomCenter, Vector3.up, arcStartDir, arcDeg, iRadius);
        }

         // Draw connecting lines for sectors
         if (arcDeg < 359.999f)
         {
             float endRad = startRad + arcRad;
             Vector3 arcEndDir = new Vector3(Mathf.Cos(endRad), 0, Mathf.Sin(endRad));
             // Outer edges
             if(oRadius > 0) {
                 Handles.DrawLine(bottomCenter + arcStartDir * oRadius, topCenter + arcStartDir * oRadius);
                 Handles.DrawLine(bottomCenter + arcEndDir * oRadius, topCenter + arcEndDir * oRadius);
             }
             // Inner edges
             if (iRadius > 0 && iRadius < oRadius) {
                 Handles.DrawLine(bottomCenter + arcStartDir * iRadius, topCenter + arcStartDir * iRadius);
                 Handles.DrawLine(bottomCenter + arcEndDir * iRadius, topCenter + arcEndDir * iRadius);
             }
         }
         // Draw some vertical lines along the cylinder wall for better shape definition
         int wallLines = Mathf.Max(4, segments / 4);
         for (int i = 0; i <= wallLines; i++)
         {
             float currentRad = Mathf.Lerp(startRad, startRad + arcRad, (float)i / wallLines);
             Vector3 wallDir = new Vector3(Mathf.Cos(currentRad), 0, Mathf.Sin(currentRad));
             if (oRadius > 0) Handles.DrawLine(bottomCenter + wallDir * oRadius, topCenter + wallDir * oRadius);
             if (iRadius > 0 && iRadius < oRadius) Handles.DrawLine(bottomCenter + wallDir * iRadius, topCenter + wallDir * iRadius);
         }

        #else
        // Non-editor fallback would be complex and less accurate - omit for brevity or use basic Gizmos approximations
        Gizmos.color = outlineColor;
        Vector3 size = new Vector3(oRadius*2, h, oRadius*2);
        Gizmos.matrix = Matrix4x4.TRS(center, rotation, Vector3.one);
        Gizmos.DrawWireCube(Vector3.zero, size);
        Gizmos.matrix = Matrix4x4.identity;

        #endif
    }


    // Use Gizmos for drawing markers as they are simpler spheres
    void DrawSpawnPointMarkersArc(Vector3 center, Quaternion rotation, float startRad, float arcRad, float radius, Color color)
    {
        // Only draw markers if spawning on the arc edge
        if (spawnShape != SpawnShape.CircleArcEdge || numberOfObjects <= 0 || radius <= 0) return;

        Gizmos.color = color;
        float markerSize = Mathf.Max(0.1f, radius * 0.05f);
        float validArcDegrees = arcRad * Mathf.Rad2Deg; // Need degrees for calculation logic

        // Replicate the angle calculation logic from CalculatePositionOnArcEdge for accuracy
        for (int i = 0; i < numberOfObjects; i++)
        {
            float currentAngleRad;
             if (numberOfObjects == 1) { currentAngleRad = startRad; }
             else if (validArcDegrees <= 0.001f) { currentAngleRad = startRad; }
             else if (validArcDegrees >= 359.999f) { currentAngleRad = startRad + i * (Mathf.PI * 2f / numberOfObjects); }
             else { float angleStepRad = arcRad / (numberOfObjects - 1); currentAngleRad = startRad + i * angleStepRad; }

            Vector3 direction = new Vector3(Mathf.Cos(currentAngleRad), 0, Mathf.Sin(currentAngleRad));
            // Apply parent rotation to the calculated offset direction
            Vector3 offset = rotation * (direction * radius);
            Vector3 spawnPoint = center + offset;
            Gizmos.DrawSphere(spawnPoint, markerSize);
        }
    }
}