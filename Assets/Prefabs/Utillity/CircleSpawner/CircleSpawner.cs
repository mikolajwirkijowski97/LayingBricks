using UnityEngine;
using System.Collections.Generic; // Required for using List<>

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Spawns objects chosen randomly from a list within a specified volume (Arc, Circle Area, Cylinder, Hollow Cylinder).
/// Allows scaling, rotation options, and updates dynamically in the editor.
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
        Random.InitState(69420); // Seed for consistent randomization in editor
        
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

        // Basic parameter check
        if (numberOfObjects <= 0 || outerRadius < 0 || scaleFactor <= 0) return;
        // Shape specific checks
        if (spawnShape == SpawnShape.HollowCylinderVolume && innerRadius >= outerRadius) return; // Invalid hollow cylinder
        if ((spawnShape == SpawnShape.CylinderVolume || spawnShape == SpawnShape.HollowCylinderVolume) && height <= 0) return;


        // --- Spawning ---
        isSpawning = true;
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
                       spawnRotation = Quaternion.LookRotation(lookDirection);
                    }
                }
                else
                {
                    // Keep prefab's default rotation if not randomizing or facing outwards
                    spawnRotation = prefabToUse.transform.rotation;
                }

                // --- Instantiate, Scale & Parent ---
                GameObject spawnedObject = Instantiate(prefabToUse, spawnPosition, spawnRotation, transform);
                spawnedObject.name = $"{prefabToUse.name}_{i}";
                spawnedObject.transform.localScale = prefabToUse.transform.localScale * scaleFactor;

                #if UNITY_EDITOR
                Undo.RegisterCreatedObjectUndo(spawnedObject, $"Spawn {spawnShape} Object");
                #endif
            }
        }
        finally
        {
            isSpawning = false; // Reset the flag
        }
    }

    // --- Position Calculation Helpers ---

    Vector3 CalculatePositionOnArcEdge(float startRad, float arcRad, int index)
    {
        float currentAngleRad;
        // Handle edge case: If only one object, place it exactly at the start angle.
        if (numberOfObjects == 1)
        {
            currentAngleRad = startRad;
        }
        // Handle edge case: If arc is effectively zero, place all objects at start angle.
        else if (arcDegrees <= 0.001f)
        {
             currentAngleRad = startRad;
        }
        // Handle edge case: If arc is a full circle (or very close to it)
        else if (arcDegrees >= 359.999f)
        {
            // Standard full circle distribution, offset by start angle
            currentAngleRad = startRad + index * (Mathf.PI * 2f / numberOfObjects);
        }
        // Standard case: Distribute objects along the specified arc
        else
        {
            // Divide the arc into (N-1) segments to place objects at start and end inclusively.
            float angleStepRad = arcRad / (numberOfObjects - 1);
            currentAngleRad = startRad + index * angleStepRad;
        }

        Vector3 direction = new Vector3(Mathf.Cos(currentAngleRad), 0, Mathf.Sin(currentAngleRad));
        return transform.position + direction * outerRadius;
    }

    Vector3 CalculatePositionInCircleArea(float startRad, float endRad)
    {
        // Random angle within the arc/sector
        float randomAngleRad = Random.Range(startRad, endRad);

        // Random radius - Use Sqrt for uniform *area* distribution
        float randomRadius = outerRadius * Mathf.Sqrt(Random.value); // Random.value is 0..1

        Vector3 direction = new Vector3(Mathf.Cos(randomAngleRad), 0, Mathf.Sin(randomAngleRad));
        return transform.position + direction * randomRadius;
    }

     Vector3 CalculatePositionInCylinder(float startRad, float endRad, float minRadius, float maxRadius)
    {
        // Random angle within the arc/sector
        float randomAngleRad = Random.Range(startRad, endRad);

        // Random radius within the specified range (minRadius to maxRadius)
        // Use formula derived from uniform area distribution for annulus / circle segment
        // r = sqrt( R_inner^2 + t * (R_outer^2 - R_inner^2) ) where t is uniform 0..1
        float minRadiusSqr = minRadius * minRadius;
        float maxRadiusSqr = maxRadius * maxRadius;
        float randomRadius = Mathf.Sqrt(minRadiusSqr + Random.value * (maxRadiusSqr - minRadiusSqr));

        // Random height within the cylinder bounds
        float randomY = Random.Range(-height / 2f, height / 2f);

        Vector3 direction = new Vector3(Mathf.Cos(randomAngleRad), 0, Mathf.Sin(randomAngleRad));
        return transform.position + direction * randomRadius + new Vector3(0, randomY, 0);
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
            DestroyImmediate(child); // Use DestroyImmediate for editor cleanup outside play mode
            #endif
        }
    }

    /// <summary>
    /// Draw gizmos in the scene view when the object is selected.
    /// </summary>
    void OnDrawGizmosSelected()
    {
        if (outerRadius <= 0 && spawnShape != SpawnShape.CircleArcEdge) return; // Need radius for most shapes
        if (outerRadius <= 0 && spawnShape == SpawnShape.CircleArcEdge && numberOfObjects > 0) {} // Allow zero radius for arc edge if spawning happens
        else if (outerRadius <= 0) return;


        Vector3 center = transform.position;
        float startAngleRad = startAngleDegrees * Mathf.Deg2Rad;
        float arcRad = arcDegrees * Mathf.Deg2Rad;
        float endAngleRad = startAngleRad + arcRad;
        int arcSegments = Mathf.Max(2, Mathf.CeilToInt(60 * (arcDegrees / 360f)));

        // Store current Handles matrix and color
        #if UNITY_EDITOR
        Color defaultHandlesColor = Handles.color;
        Matrix4x4 defaultMatrix = Handles.matrix;
        Handles.matrix = Matrix4x4.TRS(center, transform.rotation, Vector3.one); // Apply spawner transform
        #endif

        // Draw based on shape
        switch (spawnShape)
        {
            case SpawnShape.CircleArcEdge:
                DrawArcGizmo(center, startAngleRad, arcRad, outerRadius, arcSegments, Color.yellow);
                DrawSpawnPointMarkersArc(center, startAngleRad, arcRad, outerRadius, Color.cyan);
                break;

            case SpawnShape.CircleArea:
                 #if UNITY_EDITOR
                 Handles.color = new Color(0.8f, 0.8f, 0.1f, 0.2f); // Semi-transparent yellow
                 Handles.DrawSolidArc(Vector3.zero, Vector3.up, // Use Vector3.zero because matrix handles position
                                     new Vector3(Mathf.Cos(startAngleRad), 0, Mathf.Sin(startAngleRad)),
                                     arcDegrees, outerRadius);
                DrawArcGizmo(center, startAngleRad, arcRad, outerRadius, arcSegments, Color.yellow, false); // Draw outline
                 #else
                 DrawArcGizmo(center, startAngleRad, arcRad, outerRadius, arcSegments, Color.yellow); // Fallback for no Handles
                 #endif
                break;

            case SpawnShape.CylinderVolume:
                DrawCylinderGizmo(center, startAngleRad, arcRad, 0f, outerRadius, height, arcSegments, Color.blue, new Color(0.1f, 0.1f, 0.8f, 0.1f));
                break;

            case SpawnShape.HollowCylinderVolume:
                 DrawCylinderGizmo(center, startAngleRad, arcRad, innerRadius, outerRadius, height, arcSegments, Color.green, new Color(0.1f, 0.8f, 0.1f, 0.1f));
                break;
        }

        // Restore Handles matrix and color
         #if UNITY_EDITOR
         Handles.color = defaultHandlesColor;
         Handles.matrix = defaultMatrix;
         #endif
    }

    // --- Gizmo Drawing Helpers ---

    void DrawArcGizmo(Vector3 center, float startRad, float arcRad, float radius, int segments, Color color, bool drawCenterLines = true)
    {
        if (radius <= 0) return;
        float endRad = startRad + arcRad;

        Gizmos.color = color;
        Vector3 prevPoint = center + transform.rotation * (new Vector3(Mathf.Cos(startRad), 0, Mathf.Sin(startRad)) * radius);

        for (int i = 1; i <= segments; i++)
        {
            float currentRad = Mathf.Lerp(startRad, endRad, (float)i / segments);
            Vector3 nextPoint = center + transform.rotation * (new Vector3(Mathf.Cos(currentRad), 0, Mathf.Sin(currentRad)) * radius);
            Gizmos.DrawLine(prevPoint, nextPoint);
            prevPoint = nextPoint;
        }

        // Draw lines from center to arc start/end for clarity, unless it's a full circle
        if (drawCenterLines && arcDegrees < 359.999f)
        {
            Gizmos.color = Color.grey;
             Gizmos.DrawLine(center, center + transform.rotation * (new Vector3(Mathf.Cos(startRad), 0, Mathf.Sin(startRad)) * radius));
             Gizmos.DrawLine(center, center + transform.rotation * (new Vector3(Mathf.Cos(endRad), 0, Mathf.Sin(endRad)) * radius));
        }
    }

    void DrawCylinderGizmo(Vector3 center, float startRad, float arcRad, float iRadius, float oRadius, float h, int segments, Color outlineColor, Color volumeColor)
    {
        float halfHeight = h / 2f;
        Vector3 topCenter = Vector3.up * halfHeight; // Relative to Handles matrix center
        Vector3 bottomCenter = Vector3.down * halfHeight; // Relative to Handles matrix center

        // Use Handles for drawing arcs and caps
        #if UNITY_EDITOR
        Handles.color = volumeColor;
        Vector3 arcStartDir = new Vector3(Mathf.Cos(startRad), 0, Mathf.Sin(startRad));

        // Draw solid caps (or sectors)
         if (oRadius > 0) {
             Handles.DrawSolidArc(topCenter, Vector3.up, arcStartDir, arcDegrees, oRadius);
             Handles.DrawSolidArc(bottomCenter, Vector3.up, arcStartDir, arcDegrees, oRadius);
         }
        // If hollow, "erase" the inner part by drawing with background color (or just skip if transparent)
        // Note: True transparency blending with Handles can be tricky. This gives a visual cue.
         if (iRadius > 0 && iRadius < oRadius) {
             Handles.color = Color.clear; // Or a color that contrasts well if needed
             Handles.DrawSolidArc(topCenter, Vector3.up, arcStartDir, arcDegrees, iRadius);
             Handles.DrawSolidArc(bottomCenter, Vector3.up, arcStartDir, arcDegrees, iRadius);
         }


        Handles.color = outlineColor; // Switch to outline color
         // Draw top and bottom arcs (outer)
        if (oRadius > 0) {
             Handles.DrawWireArc(topCenter, Vector3.up, arcStartDir, arcDegrees, oRadius);
             Handles.DrawWireArc(bottomCenter, Vector3.up, arcStartDir, arcDegrees, oRadius);
        }
        // Draw top and bottom arcs (inner)
        if (iRadius > 0 && iRadius < oRadius) {
             Handles.DrawWireArc(topCenter, Vector3.up, arcStartDir, arcDegrees, iRadius);
             Handles.DrawWireArc(bottomCenter, Vector3.up, arcStartDir, arcDegrees, iRadius);
        }

         // Draw connecting lines for sectors
         if (arcDegrees < 359.999f)
         {
             float endRad = startRad + arcRad;
             Vector3 arcEndDir = new Vector3(Mathf.Cos(endRad), 0, Mathf.Sin(endRad));
             // Outer edges
             Handles.DrawLine(bottomCenter + arcStartDir * oRadius, topCenter + arcStartDir * oRadius);
             Handles.DrawLine(bottomCenter + arcEndDir * oRadius, topCenter + arcEndDir * oRadius);
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

        #else // Fallback if Handles are not available (e.g., Gizmos drawing without Editor context)
        // Draw top/bottom arcs using Gizmos (less fancy)
        DrawArcGizmo(center + transform.up * halfHeight, startRad, arcRad, oRadius, segments, outlineColor, arcDegrees < 359.999f);
        DrawArcGizmo(center + transform.up * halfHeight, startRad, arcRad, iRadius, segments, outlineColor, false);
        DrawArcGizmo(center - transform.up * halfHeight, startRad, arcRad, oRadius, segments, outlineColor, arcDegrees < 359.999f);
        DrawArcGizmo(center - transform.up * halfHeight, startRad, arcRad, iRadius, segments, outlineColor, false);
        // Draw some connecting lines (less accurate without Handles matrix)
        // ... (implementation would be more complex here)
        #endif
    }


    void DrawSpawnPointMarkersArc(Vector3 center, float startRad, float arcRad, float radius, Color color)
    {
        // Only draw markers if spawning on the arc edge
        if (spawnShape != SpawnShape.CircleArcEdge || numberOfObjects <= 0 || radius <= 0) return;

        Gizmos.color = color;
        float markerSize = Mathf.Max(0.1f, radius * 0.05f);

        // Replicate the angle calculation logic from DelayedUpdateSpawn for accuracy
        for (int i = 0; i < numberOfObjects; i++)
        {
            Vector3 spawnPoint = CalculatePositionOnArcEdge(startRad, arcRad, i); // Use the exact calculation
            Gizmos.DrawSphere(spawnPoint, markerSize);
        }
    }
}