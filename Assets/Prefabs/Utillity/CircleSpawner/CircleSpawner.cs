using UnityEngine;
using System.Collections.Generic; // Required for using List<>

#if UNITY_EDITOR
using UnityEditor; // Required for EditorApplication, Undo, Handles
#endif

/// <summary>
/// Spawns objects chosen randomly from a list within a specified volume (Arc, Circle Area, Cylinder, Hollow Cylinder).
/// Allows scaling, rotation options, and updates dynamically in the editor.
/// Spawns objects on Start when in Play Mode.
/// </summary>
[ExecuteAlways] // Runs in Editor and Play mode
public class VolumeSpawner : MonoBehaviour
{
    // --- Enums ---
    public enum SpawnShape
    {
        CircleArcEdge,      // Spawn on the edge of an arc/circle
        CircleArea,         // Spawn within the area of a circle/sector
        CylinderVolume,     // Spawn within the volume of a cylinder/cylindrical sector
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

    [Header("Runtime Options")]
    [SerializeField]
    [Tooltip("Should objects be spawned automatically when entering Play Mode?")]
    private bool spawnOnStart = true;

    [SerializeField]
    [Tooltip("Seed for runtime randomization (0 uses time). Consistent seed gives consistent runtime results.")]
    private int runtimeRandomSeed = 0;


    // Internal state flag for editor preview spawning
    private bool isEditorSpawning = false;

    // --- Methods ---

    // Called when script loads or a value changes in the Inspector (Editor only)
    void OnValidate()
    {
        // Clamp values to prevent invalid states
        if (innerRadius >= outerRadius) innerRadius = Mathf.Max(0f, outerRadius - 0.1f);
        if (outerRadius < 0f) outerRadius = 0f;
        if (innerRadius < 0f) innerRadius = 0f;
        if (height < 0.01f) height = 0.01f;
        if (scaleFactor < 0.01f) scaleFactor = 0.01f;
        if (numberOfObjects < 1) numberOfObjects = 1;


        // --- Editor Preview Trigger ---
        #if UNITY_EDITOR
        // Prevent triggering editor updates during play mode, while already spawning,
        // or if the object isn't part of a valid scene (e.g., prefab asset view).
        if (Application.isPlaying || isEditorSpawning || !gameObject.scene.IsValid())
        {
            return;
        }

        // Schedule the EDITOR preview update for the next editor frame.
        // Using delayCall helps avoid issues during OnValidate.
        EditorApplication.delayCall -= DelayedUpdateSpawnEditorPreview; // Clear previous pending calls
        EditorApplication.delayCall += DelayedUpdateSpawnEditorPreview; // Schedule the update
        #endif
    }

    // Called once when the scene starts in Play Mode
    void Start()
    {
        // Only run spawn logic if in Play Mode and spawnOnStart is enabled
        if (Application.isPlaying && spawnOnStart)
        {
            // Initialize Random state for runtime. Use a specific seed or time-based.
            if (runtimeRandomSeed != 0)
            {
                 Random.InitState(runtimeRandomSeed);
            }
            else
            {
                 Random.InitState((int)System.DateTime.Now.Ticks);
            }

            // Spawn objects for the game
            SpawnObjects(false); // false indicates this is NOT an editor preview
        }
    }


    #if UNITY_EDITOR
    // --- Editor Preview Spawning ---
    /// <summary>
    /// Handles spawning objects specifically for editor preview.
    /// Clears previous preview objects and creates new temporary ones.
    /// </summary>
    void DelayedUpdateSpawnEditorPreview()
    {
        // Use a fixed seed for CONSISTENT editor previews across edits
        Random.InitState(42); // Use a fixed seed for editor preview consistency

        // Ensure component/GameObject still exists before proceeding
        if (this == null || gameObject == null || !gameObject.scene.IsValid()) return;

        // --- Cleanup PREVIOUS Editor Preview Objects ---
        ClearChildrenEditorPreview();

        // --- Validation (Run before spawning) ---
        if (!ValidateSpawnParameters()) return;

        // --- Spawning Editor Preview Objects ---
        isEditorSpawning = true; // Set flag to prevent recursion during spawn
        try
        {
            SpawnObjects(true); // true indicates this IS an editor preview
        }
        finally
        {
            isEditorSpawning = false; // Reset the flag
        }
    }
    #endif // UNITY_EDITOR


    // --- Core Spawning Logic ---
    /// <summary>
    /// Instantiates the objects based on current settings.
    /// </summary>
    /// <param name="isEditorPreview">If true, objects are marked temporary for editor only.</param>
    void SpawnObjects(bool isEditorPreview)
    {
        // Perform validation again right before spawning (important for runtime call)
        if (!ValidateSpawnParameters()) return;

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

            switch (spawnShape)
            {
                case SpawnShape.CircleArcEdge:
                    spawnPosition = CalculatePositionOnArcEdge(startAngleRad, arcRad, i);
                    spawnDirection = (spawnPosition - transform.position).normalized;
                    if (spawnDirection == Vector3.zero) spawnDirection = transform.forward; // Avoid zero vector
                    break;
                case SpawnShape.CircleArea:
                    spawnPosition = CalculatePositionInCircleArea(startAngleRad, endAngleRad);
                    spawnDirection = (spawnPosition - transform.position).normalized;
                    if (spawnDirection == Vector3.zero) spawnDirection = transform.forward;
                    break;
                case SpawnShape.CylinderVolume:
                    spawnPosition = CalculatePositionInCylinder(startAngleRad, endAngleRad, 0f, outerRadius);
                    // Horizontal direction from center axis for rotation purposes
                    spawnDirection = (new Vector3(spawnPosition.x, transform.position.y, spawnPosition.z) - transform.position).normalized;
                    if (spawnDirection == Vector3.zero) spawnDirection = transform.forward;
                    break;
                case SpawnShape.HollowCylinderVolume:
                    spawnPosition = CalculatePositionInCylinder(startAngleRad, endAngleRad, innerRadius, outerRadius);
                    spawnDirection = (new Vector3(spawnPosition.x, transform.position.y, spawnPosition.z) - transform.position).normalized;
                    if (spawnDirection == Vector3.zero) spawnDirection = transform.forward;
                    break;
            }

            // --- Calculate Rotation ---
            Quaternion spawnRotation = Quaternion.identity;
            if (randomizeRotation)
            {
                spawnRotation = Random.rotation;
            }
            else if (faceOutwards)
            {
                // For volumes, use the horizontal component of the direction from the central Y axis.
                Vector3 lookDirection = (spawnShape == SpawnShape.CircleArcEdge)
                    ? spawnDirection
                    : new Vector3(spawnDirection.x, 0, spawnDirection.z).normalized;

                if (lookDirection != Vector3.zero) // Avoid LookRotation error if direction is zero
                {
                    spawnRotation = Quaternion.LookRotation(lookDirection);
                }
                else // Fallback if lookDirection is zero (e.g., spawning exactly at the center)
                {
                   spawnRotation = prefabToUse.transform.rotation;
                }
            }
            else // Keep prefab's default rotation
            {
                spawnRotation = prefabToUse.transform.rotation;
            }

            // --- Instantiate, Scale & Parent ---
            GameObject spawnedObject = Instantiate(prefabToUse, spawnPosition, spawnRotation, transform);
            spawnedObject.name = $"{prefabToUse.name}_{i}"; // Simple naming
            spawnedObject.transform.localScale = prefabToUse.transform.localScale * scaleFactor;

            // --- Apply Editor-Only Settings ---
            #if UNITY_EDITOR
            if (isEditorPreview)
            {
                // Mark editor preview objects as temporary and hidden in hierarchy
                spawnedObject.hideFlags = HideFlags.HideAndDontSave; // HideInHierarchy | DontSave = HideAndDontSave

                // Register Undo for the creation of this preview object
                Undo.RegisterCreatedObjectUndo(spawnedObject, $"Spawn Preview {spawnShape} Object");
            }
            // Runtime objects (isEditorPreview == false) will have default HideFlags (None)
            // and will be saved with the scene if changes are made *after* they spawn.
            // They are NOT registered with Undo.
            #endif
        }
    }


    // --- Validation Helper ---
    bool ValidateSpawnParameters()
    {
        // Check prefabs
        if (prefabsToSpawn == null || prefabsToSpawn.Count == 0)
        {
            // Debug.LogWarning("VolumeSpawner: No prefabs assigned to spawn.", this); // Optional warning
            return false;
        }
        bool hasValidPrefab = false;
        foreach (var prefab in prefabsToSpawn) { if (prefab != null) { hasValidPrefab = true; break; } }
        if (!hasValidPrefab)
        {
            // Debug.LogWarning("VolumeSpawner: Prefab list is empty or contains only null elements.", this); // Optional warning
            return false;
        }

        // Basic parameter check
        if (numberOfObjects <= 0 || outerRadius < 0 || scaleFactor <= 0) return false;
        // Shape specific checks
        if (spawnShape == SpawnShape.HollowCylinderVolume && innerRadius >= outerRadius) return false; // Invalid hollow cylinder
        if ((spawnShape == SpawnShape.CylinderVolume || spawnShape == SpawnShape.HollowCylinderVolume) && height <= 0) return false;

        return true; // Parameters seem valid
    }


    // --- Position Calculation Helpers --- (Keep these as they are)

    Vector3 CalculatePositionOnArcEdge(float startRad, float arcRad, int index)
    {
        float currentAngleRad;
        if (numberOfObjects <= 1) // Handle single object case or zero arc
        {
             currentAngleRad = startRad;
        }
        else if (arcDegrees >= 359.999f) // Full circle distribution
        {
            currentAngleRad = startRad + index * (Mathf.PI * 2f / numberOfObjects);
        }
        else // Distribute along the specified arc (inclusive start/end points)
        {
            float angleStepRad = arcRad / (numberOfObjects - 1);
            currentAngleRad = startRad + index * angleStepRad;
        }

        Vector3 direction = new Vector3(Mathf.Cos(currentAngleRad), 0, Mathf.Sin(currentAngleRad));
        // Apply spawner's rotation to the direction vector
        Vector3 rotatedDirection = transform.rotation * direction;
        return transform.position + rotatedDirection * outerRadius;
    }

    Vector3 CalculatePositionInCircleArea(float startRad, float endRad)
    {
        float randomAngleRad = Random.Range(startRad, endRad);
        float randomRadius = outerRadius * Mathf.Sqrt(Random.value); // Uniform area distribution

        Vector3 direction = new Vector3(Mathf.Cos(randomAngleRad), 0, Mathf.Sin(randomAngleRad));
        Vector3 rotatedDirection = transform.rotation * direction; // Apply spawner rotation
        return transform.position + rotatedDirection * randomRadius;
    }

    Vector3 CalculatePositionInCylinder(float startRad, float endRad, float minRadius, float maxRadius)
    {
        float randomAngleRad = Random.Range(startRad, endRad);
        float minRadiusSqr = minRadius * minRadius;
        float maxRadiusSqr = maxRadius * maxRadius;
        float randomRadius = Mathf.Sqrt(minRadiusSqr + Random.value * (maxRadiusSqr - minRadiusSqr)); // Uniform annular area distribution
        float randomY = Random.Range(-height / 2f, height / 2f);

        Vector3 direction = new Vector3(Mathf.Cos(randomAngleRad), 0, Mathf.Sin(randomAngleRad));
        Vector3 rotatedDirection = transform.rotation * direction; // Apply spawner rotation to XY plane
        Vector3 verticalOffset = transform.up * randomY; // Apply spawner rotation to Y offset

        return transform.position + rotatedDirection * randomRadius + verticalOffset;
    }


    #if UNITY_EDITOR
    // --- Editor Preview Cleanup ---
    /// <summary>
    /// Clears child GameObjects CREATED BY THE EDITOR PREVIEW, respecting the Undo system.
    /// It specifically looks for objects marked with HideFlags.DontSave.
    /// </summary>
    void ClearChildrenEditorPreview()
    {
        // Iterate backwards as we are removing items from the collection
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            GameObject child = transform.GetChild(i).gameObject;
            // IMPORTANT: Only destroy children that were part of the editor preview
            // Check HideFlags - HideAndDontSave includes DontSave
             if ((child.hideFlags & HideFlags.DontSave) != 0)
             {
                 // Use Undo-aware destruction for editor objects
                 Undo.DestroyObjectImmediate(child);
             }
        }
    }
    #endif // UNITY_EDITOR

    // --- Gizmo Drawing --- (Keep these as they are, they correctly use Handles within #if UNITY_EDITOR)

    void OnDrawGizmosSelected()
    {
        // Basic validation for gizmo drawing
         if (outerRadius <= 0 && (spawnShape == SpawnShape.CircleArea || spawnShape == SpawnShape.CylinderVolume || spawnShape == SpawnShape.HollowCylinderVolume)) return;
         if (outerRadius <= 0 && innerRadius <= 0 && spawnShape == SpawnShape.HollowCylinderVolume) return;
         if (outerRadius <= 0 && spawnShape == SpawnShape.CircleArcEdge && numberOfObjects > 0) { /* Allow zero radius for ArcEdge gizmo if objects are > 0 */ }
         else if (outerRadius <= 0 && spawnShape == SpawnShape.CircleArcEdge) return;


        Vector3 center = transform.position;
        Quaternion rotation = transform.rotation; // Use spawner's rotation
        float startAngleRad = startAngleDegrees * Mathf.Deg2Rad;
        float arcRad = arcDegrees * Mathf.Deg2Rad;
        int arcSegments = Mathf.Max(3, Mathf.CeilToInt(60 * (arcDegrees / 360f))); // Ensure at least 3 segments

        #if UNITY_EDITOR
        // Store current Handles state
        Color defaultHandlesColor = Handles.color;
        Matrix4x4 defaultMatrix = Handles.matrix;
        // Set Handles matrix to match the spawner's transform
        Handles.matrix = Matrix4x4.TRS(center, rotation, Vector3.one);

        // Draw based on shape (using relative coordinates because Handles.matrix is set)
        switch (spawnShape)
        {
            case SpawnShape.CircleArcEdge:
                Handles.color = Color.yellow;
                Handles.DrawWireArc(Vector3.zero, Vector3.up, GetVectorFromAngle(startAngleRad), arcDegrees, outerRadius);
                 if (arcDegrees < 359.99f) // Draw lines from center if not a full circle
                 {
                    Handles.color = Color.grey;
                    Handles.DrawLine(Vector3.zero, GetVectorFromAngle(startAngleRad) * outerRadius);
                    Handles.DrawLine(Vector3.zero, GetVectorFromAngle(startAngleRad + arcRad) * outerRadius);
                 }
                DrawSpawnPointMarkersArcEditor(startAngleRad, arcRad, outerRadius, Color.cyan); // Draw markers relative
                break;

            case SpawnShape.CircleArea:
                Handles.color = new Color(0.8f, 0.8f, 0.1f, 0.2f); // Semi-transparent yellow
                Handles.DrawSolidArc(Vector3.zero, Vector3.up, GetVectorFromAngle(startAngleRad), arcDegrees, outerRadius);
                Handles.color = Color.yellow; // Outline
                Handles.DrawWireArc(Vector3.zero, Vector3.up, GetVectorFromAngle(startAngleRad), arcDegrees, outerRadius);
                 if (arcDegrees < 359.99f)
                 {
                    Handles.color = Color.grey;
                    Handles.DrawLine(Vector3.zero, GetVectorFromAngle(startAngleRad) * outerRadius);
                    Handles.DrawLine(Vector3.zero, GetVectorFromAngle(startAngleRad + arcRad) * outerRadius);
                 }
                break;

            case SpawnShape.CylinderVolume:
                DrawCylinderGizmoEditor(startAngleRad, arcRad, 0f, outerRadius, height, arcSegments, Color.blue, new Color(0.1f, 0.1f, 0.8f, 0.1f));
                break;

            case SpawnShape.HollowCylinderVolume:
                if (innerRadius < outerRadius) // Only draw if valid
                {
                     DrawCylinderGizmoEditor(startAngleRad, arcRad, innerRadius, outerRadius, height, arcSegments, Color.green, new Color(0.1f, 0.8f, 0.1f, 0.1f));
                } else { // Draw outer radius in red if invalid
                     DrawCylinderGizmoEditor(startAngleRad, arcRad, 0f, outerRadius, height, arcSegments, Color.red, new Color(0.8f, 0.1f, 0.1f, 0.1f));
                }

                break;
        }

        // Restore Handles state
        Handles.matrix = defaultMatrix;
        Handles.color = defaultHandlesColor;
        #else
        // Fallback Gizmos drawing if not in Editor (less accurate for rotation/complex shapes)
        Gizmos.color = Color.yellow;
        // Basic sphere placeholder if not in editor context
        Gizmos.DrawWireSphere(center, outerRadius * 0.1f);
        #endif
    }


    #if UNITY_EDITOR
    // --- Gizmo Drawing Helpers (Editor Only) ---

    Vector3 GetVectorFromAngle(float angleRad)
    {
        return new Vector3(Mathf.Cos(angleRad), 0, Mathf.Sin(angleRad));
    }

    void DrawCylinderGizmoEditor(float startRad, float arcRad, float iRadius, float oRadius, float h, int segments, Color outlineColor, Color volumeColor)
    {
        float halfHeight = h / 2f;
        Vector3 topCenter = Vector3.up * halfHeight;
        Vector3 bottomCenter = Vector3.down * halfHeight;
        Vector3 arcStartDir = GetVectorFromAngle(startRad);
        float endRad = startRad + arcRad;
        Vector3 arcEndDir = GetVectorFromAngle(endRad);

        // Draw Volume Caps (Solid Arcs)
        Handles.color = volumeColor;
        if (oRadius > 0) {
            Handles.DrawSolidArc(topCenter, Vector3.up, arcStartDir, arcDegrees, oRadius);
            Handles.DrawSolidArc(bottomCenter, Vector3.up, arcStartDir, arcDegrees, oRadius);
        }
        // Erase inner part if hollow (use clear or background color)
         if (iRadius > 0 && iRadius < oRadius) {
             // Using a separate color to subtract might not work perfectly with transparency.
             // Drawing wire arcs for inner radius is often clearer. Let's focus on wireframe.
             // Handles.color = Handles.backgroundColor; // Or Color.clear
             // Handles.DrawSolidArc(topCenter, Vector3.up, arcStartDir, arcDegrees, iRadius);
             // Handles.DrawSolidArc(bottomCenter, Vector3.up, arcStartDir, arcDegrees, iRadius);
         }


        // Draw Wireframe Arcs and Lines
        Handles.color = outlineColor;
        // Outer arcs
        if (oRadius > 0) {
            Handles.DrawWireArc(topCenter, Vector3.up, arcStartDir, arcDegrees, oRadius);
            Handles.DrawWireArc(bottomCenter, Vector3.up, arcStartDir, arcDegrees, oRadius);
        }
        // Inner arcs
        if (iRadius > 0 && iRadius < oRadius) {
            Handles.DrawWireArc(topCenter, Vector3.up, arcStartDir, arcDegrees, iRadius);
            Handles.DrawWireArc(bottomCenter, Vector3.up, arcStartDir, arcDegrees, iRadius);
        }

        // Vertical lines connecting arcs
        if (arcDegrees < 359.99f) // Sector edges
        {
            if (oRadius > 0) {
                 Handles.DrawLine(bottomCenter + arcStartDir * oRadius, topCenter + arcStartDir * oRadius);
                 Handles.DrawLine(bottomCenter + arcEndDir * oRadius, topCenter + arcEndDir * oRadius);
            }
            if (iRadius > 0 && iRadius < oRadius) {
                 Handles.DrawLine(bottomCenter + arcStartDir * iRadius, topCenter + arcStartDir * iRadius);
                 Handles.DrawLine(bottomCenter + arcEndDir * iRadius, topCenter + arcEndDir * iRadius);
            }
        } else // Full cylinder vertical lines (draw a few for shape)
        {
            int numVerticalLines = 4;
            for(int i=0; i < numVerticalLines; i++)
            {
                float angle = i * (Mathf.PI * 2f / numVerticalLines);
                Vector3 dir = GetVectorFromAngle(angle);
                if (oRadius > 0) Handles.DrawLine(bottomCenter + dir * oRadius, topCenter + dir * oRadius);
                if (iRadius > 0 && iRadius < oRadius) Handles.DrawLine(bottomCenter + dir * iRadius, topCenter + dir * iRadius);
            }
        }

         // Lines connecting inner and outer radii on caps for sectors
         if (arcDegrees < 359.99f && iRadius > 0 && iRadius < oRadius)
         {
            Handles.DrawLine(topCenter + arcStartDir * iRadius, topCenter + arcStartDir * oRadius);
            Handles.DrawLine(topCenter + arcEndDir * iRadius, topCenter + arcEndDir * oRadius);
            Handles.DrawLine(bottomCenter + arcStartDir * iRadius, bottomCenter + arcStartDir * oRadius);
            Handles.DrawLine(bottomCenter + arcEndDir * iRadius, bottomCenter + arcEndDir * oRadius);
         }

    }

    // Draw markers using Handles relative to the spawner's transform
    void DrawSpawnPointMarkersArcEditor(float startRad, float arcRad, float radius, Color color)
    {
        // Only draw markers if spawning on the arc edge for editor preview
        if (spawnShape != SpawnShape.CircleArcEdge || numberOfObjects <= 0 || radius <= 0) return;

        Handles.color = color;
        // Calculate marker size relative to radius, ensure it's visible
        float markerSize = Mathf.Max(0.02f, radius * 0.03f);

        // Replicate the angle calculation logic from CalculatePositionOnArcEdge for accuracy
        for (int i = 0; i < numberOfObjects; i++)
        {
            // Calculate position *relative* to the spawner center using the same logic
             float currentAngleRad;
             if (numberOfObjects <= 1) { currentAngleRad = startRad; }
             else if (arcDegrees >= 359.999f) { currentAngleRad = startRad + i * (Mathf.PI * 2f / numberOfObjects); }
             else { float angleStepRad = arcRad / (numberOfObjects - 1); currentAngleRad = startRad + i * angleStepRad; }

            Vector3 direction = GetVectorFromAngle(currentAngleRad);
            Vector3 spawnPointRelative = direction * radius; // Position relative to center (Handles matrix handles world pos/rot)

            // Use Handles.SphereHandleCap for a 3D marker, or DrawSolidDisc for flat
             // Handles.SphereHandleCap(0, spawnPointRelative, Quaternion.identity, markerSize * 2, EventType.Repaint);
             Handles.DrawSolidDisc(spawnPointRelative, Vector3.up, markerSize); // Flat disc marker
        }
    }
    #endif // UNITY_EDITOR
}