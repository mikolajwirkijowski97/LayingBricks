using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Responsible for instantiating and rendering the bricks of a Tower procedural structure.
/// Uses GPU instancing for efficient rendering of many bricks. Batches multiple levels
/// into single DrawMeshInstanced calls where possible, respecting GPU limits.
/// Optimised for static towers: Calculates and prepares batches only when Tower parameters change.
/// Listens to Tower parameter changes to automatically rebuild its geometry caches and batches.
/// </summary>
[RequireComponent(typeof(Tower))] // Ensure Tower component exists
public class BrickInstancer : MonoBehaviour
{
    // --- Constants ---
    private const int MAX_INSTANCES_PER_DRAW_CALL = 1023; // GPU Instancing limit for DrawMeshInstanced
    private const int LEVEL_SEED_MULTIPLIER = 1000; // Used for deterministic level variation seeding
    private const int ROTATION_SEED_OFFSET = 1;     // Offset for rotation seed relative to height seed
    private const float MIN_BRICK_WIDTH_THRESHOLD = 0.001f; // Minimum width to consider a brick valid for rendering

    // --- Serialized Fields ---
    [Header("Dependencies")]
    [SerializeField]
    [Tooltip("The Tower component defining the structure to instance. If null, attempts to find one on the same GameObject.")]
    private Tower _tower;

    [SerializeField]
    [Tooltip("The prefab used for each brick. Must contain a MeshFilter and MeshRenderer.")]
    private GameObject _brickPrefab;

    // --- Properties ---

    /// <summary>
    /// Gets or sets the Tower data source. Handles event subscription/unsubscription automatically.
    /// Marks the instancer as dirty if the Tower reference changes.
    /// </summary>
    public Tower Tower {
        get { return _tower; }
        set {
            // Unsubscribe from the old tower's event before changing reference
            if (_tower != null) {
                _tower.OnParametersChanged -= HandleTowerParametersChanged;
            }

            _tower = value;

            // Subscribe to the new tower's event if it's not null
            if (_tower != null) {
                 _tower.OnParametersChanged += HandleTowerParametersChanged;
                 MarkDirty(); // Mark dirty as tower data will be different
            } else {
                 // Clear caches if tower becomes null
                 ClearCachesAndBatches();
            }
        }
    }

    /// <summary>
    /// Gets or sets the prefab used for instancing bricks.
    /// Re-caches essential components (Mesh, Material) and marks the instancer dirty if the prefab changes.
    /// </summary>
      public GameObject BrickPrefab {
        get { return _brickPrefab; }
        set {
            if (_brickPrefab != value) {
                _brickPrefab = value;
                CachePrefabComponents(); // Update cached mesh/material
                MarkDirty(); // Prefab change requires rebuild
            }
        }
     }

    // --- Private Fields ---

    // Caches for generated brick data to avoid recalculation every frame
    private readonly Dictionary<int, Matrix4x4[]> _matricesCache = new Dictionary<int, Matrix4x4[]>();
    private readonly Dictionary<int, float> _levelStartYCache = new Dictionary<int, float>();

    // Cached components from the BrickPrefab for efficient rendering
    private Mesh _brickMesh;
    private Material _brickMaterial;

    // Flag indicating if caches and persistent batches need rebuilding due to parameter changes
    private bool _needsRebuild = true;

    // --- NEW: Stores the prepared batches of matrices ready for rendering ---
    private readonly List<Matrix4x4[]> _persistentBatches = new List<Matrix4x4[]>();


    // --- Unity Lifecycle Methods ---

    /// <summary>
    /// Ensures the Tower reference is set, attempting to find it on the same GameObject if needed.
    /// </summary>
    void Awake()
    {
        // Attempt to find Tower component if not assigned in Inspector
        if (_tower == null) {
            _tower = GetComponent<Tower>();
        }
         // Initial subscription and data population happens in OnEnable/Start
    }

    /// <summary>
    /// Validates essential dependencies (Tower, Brick Prefab components) on startup.
    /// Disables the component if dependencies are missing.
    /// </summary>
    void Start()
    {
        if (_tower == null) {
            Debug.LogError("BrickInstancer: Tower component not found or assigned! Disabling.", this);
            enabled = false;
            return;
        }
        if (!CachePrefabComponents()) {
             Debug.LogError("BrickInstancer: Brick Prefab has missing MeshFilter or MeshRenderer! Disabling.", this);
            enabled = false;
            return;
        }
        // Initial cache and batch build will happen on first Update via Rebuild...IfNeeded
        MarkDirty(); // Ensure initial build
    }

    /// <summary>
    /// Subscribes to the Tower's parameter change event when the component is enabled.
    /// Marks caches as dirty to ensure they reflect the latest Tower state.
    /// </summary>
    void OnEnable()
    {
        // Subscribe to the event when the component becomes enabled and active
        if (_tower != null)
        {
            // Ensure no double subscription
            _tower.OnParametersChanged -= HandleTowerParametersChanged;
            _tower.OnParametersChanged += HandleTowerParametersChanged;
             // Mark dirty on enable in case parameters changed while disabled or for initial setup
             MarkDirty();
        }
    }

    /// <summary>
    /// Unsubscribes from the Tower's parameter change event when the component is disabled.
    /// This is crucial to prevent errors and memory leaks if the Tower is destroyed.
    /// </summary>
    void OnDisable()
    {
        // Unsubscribe from the event when the component becomes disabled or inactive
        if (_tower != null)
        {
            _tower.OnParametersChanged -= HandleTowerParametersChanged;
        }
    }


    /// <summary>
    /// Rebuilds internal data and rendering batches if necessary, then draws the tower
    /// using the prepared, persistent batches via GPU instancing each frame.
    /// </summary>
    void Update()
    {
        // Ensure essential components are available before proceeding
        if (_tower == null || _brickMesh == null || _brickMaterial == null) return;

        // --- Rebuild data and batches ONLY if parameters have changed ---
        RebuildDataAndBatchesIfNeeded();

        // --- Render using the prepared, persistent batches ---
        foreach (Matrix4x4[] batchToDraw in _persistentBatches)
        {
            // Simple check for validity before drawing
            if (batchToDraw != null && batchToDraw.Length > 0)
            {
                Graphics.DrawMeshInstanced(
                    _brickMesh,
                    0, // submeshIndex
                    _brickMaterial,
                    batchToDraw, // Use the pre-calculated batch array
                    batchToDraw.Length
                );
            }
        }
    }

    /// <summary>
    /// Called in the editor when script properties are changed.
    /// Primarily used here to re-cache prefab components if the BrickPrefab field is changed.
    /// Also marks caches dirty to reflect potential visual changes from inspector tweaks.
    /// </summary>
    void OnValidate()
    {
        // Re-cache prefab components if the reference changed in the editor
        if (_brickPrefab != null) {
            // Check if cached components are still valid for the current prefab
            MeshFilter mf = _brickPrefab.GetComponent<MeshFilter>();
            MeshRenderer mr = _brickPrefab.GetComponent<MeshRenderer>();
            if (_brickMesh != mf?.sharedMesh || _brickMaterial != mr?.sharedMaterial) {
                CachePrefabComponents(); // Re-cache if mesh or material differs
            }
        } else {
            // Clear cached components if prefab is set to null
            _brickMesh = null;
            _brickMaterial = null;
        }

        // Mark dirty whenever any inspector value changes for this component,
        // ensuring caches are checked/rebuilt on next Update.
        MarkDirty();
    }


    // --- Event Handler ---

    /// <summary>
    /// Method called automatically when the subscribed Tower's OnParametersChanged event is invoked.
    /// Marks the internal caches and batches as dirty, triggering a rebuild on the next Update cycle.
    /// </summary>
    private void HandleTowerParametersChanged()
    {
        // When the tower signals a change, invalidate our caches and prepared batches
        MarkDirty();
    }


    // --- Public Methods ---

    /// <summary>
    /// Marks the internal geometry caches and persistent batches as invalid.
    /// This forces a recalculation and re-batching before the next rendering pass in Update.
    /// </summary>
    public void MarkDirty() {
        _needsRebuild = true;
    }

    /// <summary>
    /// Gets the calculated starting Y position (height) for a specific tower level.
    /// Returns 0 if the level is invalid or data hasn't been cached.
    /// Ensures caches are up-to-date before returning the value.
    /// </summary>
    /// <param name="level">The zero-based index of the tower level.</param>
    /// <returns>The starting height (Y coordinate) of the specified level.</returns>
    public float GetLevelStartHeight(int level) {
        // This is primarily used during the rebuild process now.
        // Call rebuild first if direct access is needed outside Update, otherwise
        // Update loop ensures data is rebuilt before rendering.
        RebuildDataAndBatchesIfNeeded();
        return _levelStartYCache.TryGetValue(level, out float height) ? height : 0f;
     }


    // --- Private Helper Methods ---

    /// <summary>
    /// Checks if caches and batches need rebuilding (`_needsRebuild` flag) and performs the rebuild if necessary.
    /// Clears existing caches/batches, recalculates level start heights, and generates the new persistent batches.
    /// </summary>
    private void RebuildDataAndBatchesIfNeeded() {
        // Only rebuild if marked as dirty and the tower reference is valid
        if (!_needsRebuild || _tower == null) {
             return; // Nothing to do if not dirty or no tower
        }

        ClearCachesAndBatches(); // Clear geometry caches and existing prepared batches
        CalculateAllLevelStartHeights(); // Recalculate Y positions

        // --- Generate and Store Persistent Batches ---
        int totalLevelsToRender = Mathf.Min(_tower.Height, _levelStartYCache.Count);
        var currentBatchMatrices = new List<Matrix4x4>(MAX_INSTANCES_PER_DRAW_CALL); // Local list for building batches

        for (int level = 0; level < totalLevelsToRender; level++)
        {
            Matrix4x4[] currentLevelMatrices = GetOrGenerateLevelMatrices(level);
            if (currentLevelMatrices == null || currentLevelMatrices.Length == 0) continue;

            // If adding this level would exceed the limit...
            if (currentBatchMatrices.Count + currentLevelMatrices.Length > MAX_INSTANCES_PER_DRAW_CALL)
            {
                // ...store the current batch (if any matrices are in it)...
                if (currentBatchMatrices.Count > 0)
                {
                    _persistentBatches.Add(currentBatchMatrices.ToArray());
                }
                // ...clear the temp list, and add the current level's matrices to start a new batch.
                currentBatchMatrices.Clear();
                currentBatchMatrices.AddRange(currentLevelMatrices);
            }
            else
            {
                // Otherwise, add the current level's matrices to the existing batch.
                currentBatchMatrices.AddRange(currentLevelMatrices);
            }

            // If the batch is now exactly full, store it and clear the temp list.
            if (currentBatchMatrices.Count == MAX_INSTANCES_PER_DRAW_CALL)
            {
                 _persistentBatches.Add(currentBatchMatrices.ToArray());
                 currentBatchMatrices.Clear();
            }
        }

        // After the loop, store any remaining matrices in the last partially filled batch.
        if (currentBatchMatrices.Count > 0)
        {
            _persistentBatches.Add(currentBatchMatrices.ToArray());
        }
        // --- End Batch Generation ---

        _needsRebuild = false; // Mark as clean after rebuild and batch preparation
     }

     /// <summary>
     /// Clears all internal geometry caches and the prepared rendering batches.
     /// </summary>
     private void ClearCachesAndBatches()
     {
            _matricesCache.Clear();
            _levelStartYCache.Clear();
            _persistentBatches.Clear(); // Also clear the prepared batches
     }

    /// <summary>
    /// Caches the Mesh and Material components from the assigned BrickPrefab.
    /// Logs errors and returns false if the prefab is null or missing required components.
    /// </summary>
    /// <returns>True if components were successfully cached, false otherwise.</returns>
    private bool CachePrefabComponents() {
        if (_brickPrefab == null) {
             _brickMesh = null; _brickMaterial = null; return false;
        }

        MeshFilter mf = _brickPrefab.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) {
            Debug.LogError($"BrickInstancer: Brick prefab '{_brickPrefab.name}' is missing MeshFilter or its Mesh!", _brickPrefab);
            _brickMesh = null; _brickMaterial = null; return false;
        }

        MeshRenderer mr = _brickPrefab.GetComponent<MeshRenderer>();
        if (mr == null || mr.sharedMaterial == null) {
            Debug.LogError($"BrickInstancer: Brick prefab '{_brickPrefab.name}' is missing MeshRenderer or its Material!", _brickPrefab);
             _brickMesh = null; _brickMaterial = null; return false;
        }

        // Store references to the mesh and material
        _brickMesh = mf.sharedMesh;
        _brickMaterial = mr.sharedMaterial;
        return true;
     }

    /// <summary>
    /// Generates a list of pseudo-random distances (representing brick widths)
    /// based on tower parameters for a specific level.
    /// </summary>
    /// <param name="brickCount">Number of bricks for the level.</param>
    /// <param name="circumference">Total circumference to fill.</param>
    /// <param name="widthVariation">Allowed width variation percentage.</param>
    /// <param name="randomSeed">Seed for deterministic random generation.</param>
    /// <returns>A list of calculated brick widths.</returns>
    private List<float> GenerateRandomDistances(int brickCount, float circumference, float widthVariation, int randomSeed) {
        // Initialize RNG with a seed specific to this level for deterministic results
        Random.InitState(randomSeed);
        List<float> distances = new List<float>(brickCount);
        if (brickCount <= 0 || circumference <= 0) return distances; // Handle invalid input

        // Calculate average and range for randomized widths
        float avgDistance = circumference / brickCount;
        float minDistance = avgDistance * (1.0f - widthVariation);
        float maxDistance = avgDistance * (1.0f + widthVariation);

        // Generate random widths within the calculated range
        for (int i = 0; i < brickCount; i++) {
            distances.Add(Random.Range(minDistance, maxDistance));
        }
        return distances;
     }

    /// <summary>
    /// Normalizes a list of distances (brick widths) so that their sum equals a target sum (circumference).
    /// Modifies the input list directly.
    /// </summary>
    /// <param name="distances">The list of distances to normalize.</param>
    /// <param name="targetSum">The desired sum of the distances.</param>
    private void NormalizeDistances(List<float> distances, float targetSum) {
        if (distances == null || distances.Count == 0) return;

        // Calculate the current sum of distances
        float currentSum = 0f;
        foreach (float d in distances) { currentSum += d; }

        // Avoid division by zero or normalizing if sum is already correct
        if (currentSum <= 0 || Mathf.Approximately(currentSum, targetSum)) return;

        // Calculate scaling factor and apply it to each distance
        float scale = targetSum / currentSum;
        for (int i = 0; i < distances.Count; i++) {
            distances[i] *= scale;
        }
     }

    /// <summary>
    /// Calculates a deterministic height for bricks within a specific level,
    /// based on the Tower's Min/Max height settings and seed.
    /// </summary>
    /// <param name="level">The zero-based index of the tower level.</param>
    /// <returns>The calculated height for bricks in this level.</returns>
    private float CalculateDeterministicLevelBrickHeight(int level) {
        // Combine seeds for unique, deterministic height per level
        int seed = _tower.GetInstanceID() + level * LEVEL_SEED_MULTIPLIER + _tower.Seed;
        Random.InitState(seed);

        // Use Tower's min/max height parameters
        float minH = _tower.MinBrickHeight;
        float maxH = _tower.MaxBrickHeight;

        // Ensure max is not less than min (though Tower properties should handle this)
        if (minH > maxH) maxH = minH;

        // Return min height if range is negligible, otherwise random within range
        if (Mathf.Approximately(minH, maxH)) return minH;
        return Random.Range(minH, maxH);
     }

    /// <summary>
    /// Calculates a deterministic rotation offset (in radians) for a specific level,
    /// ensuring levels aren't perfectly aligned vertically. Uses a slightly different seed than height calculation.
    /// </summary>
    /// <param name="level">The zero-based index of the tower level.</param>
    /// <returns>The rotation offset in radians.</returns>
    private float CalculateDeterministicLevelRotationOffset(int level) {
        // Combine seeds for unique, deterministic rotation per level (offset seed from height)
        int seed = _tower.GetInstanceID() + level * LEVEL_SEED_MULTIPLIER + _tower.Seed + ROTATION_SEED_OFFSET; // Use constant
        Random.InitState(seed);
        // Return a random rotation offset around the Y axis
        return Random.Range(0f, 2f * Mathf.PI);
     }

    /// <summary>
    /// Calculates and caches the starting Y position for all levels up to the Tower's height.
    /// Populates the `_levelStartYCache`.
    /// </summary>
    private void CalculateAllLevelStartHeights() {
        _levelStartYCache.Clear();
        float accumulatedHeight = 0f;
        _levelStartYCache[0] = 0f; // Level 0 starts at height 0

        int levelsToCalculate = _tower.Height;
        for (int i = 0; i < levelsToCalculate; i++) {
            // Calculate the height of the current level's bricks
            float currentLevelHeight = CalculateDeterministicLevelBrickHeight(i);
            // Accumulate height to find the start of the *next* level
            accumulatedHeight += currentLevelHeight;
            // Cache the starting height for the next level (index i+1)
             if (!_levelStartYCache.ContainsKey(i + 1)) {
                _levelStartYCache[i + 1] = accumulatedHeight;
             }
        }
     }

    /// <summary>
    /// Retrieves the cached transformation matrices for a given level.
    /// If matrices are not cached, it generates and caches them first. Used during batch preparation.
    /// </summary>
    /// <param name="level">The zero-based index of the tower level.</param>
    /// <returns>An array of Matrix4x4 transformations for the bricks in the level, or null if invalid.</returns>
    private Matrix4x4[] GetOrGenerateLevelMatrices(int level) {
        // Return cached matrices if available
        if (_matricesCache.TryGetValue(level, out var cachedMatrices)) {
            return cachedMatrices;
        }
        // Otherwise, generate, cache, and return new matrices
        return GenerateAndCacheSingleLevelMatrices(level);
     }

    /// <summary>
    /// Generates, caches, and returns the transformation matrices for all bricks in a single level.
    /// Calculates position, rotation, and scale for each brick based on Tower parameters.
    /// </summary>
    /// <param name="level">The zero-based index of the tower level.</param>
    /// <returns>An array of Matrix4x4 transformations for the bricks, or an empty array if parameters are invalid.</returns>
    private Matrix4x4[] GenerateAndCacheSingleLevelMatrices(int level) {
        // Combine seeds for deterministic generation specific to this level's geometry
        int seed = _tower.GetInstanceID() + level * LEVEL_SEED_MULTIPLIER + _tower.Seed;

        // Get necessary parameters from the Tower component
        float circumference = _tower.Circumference;
        int brickCount = _tower.BricksPerLevel;
        // --- Important Check ---
        if (brickCount > MAX_INSTANCES_PER_DRAW_CALL) {
            Debug.LogWarning($"BrickInstancer: BricksPerLevel ({brickCount}) for Tower '{_tower.name}' exceeds the maximum ({MAX_INSTANCES_PER_DRAW_CALL}) for a single DrawMeshInstanced call. Rendering for level {level} might fail or be incomplete.", this);
        }
        // --- End Check ---

        float radius = _tower.Radius;
        float brickDepth = _tower.BrickDepth;
        float rotationOffset = CalculateDeterministicLevelRotationOffset(level);
        float brickHeight = CalculateDeterministicLevelBrickHeight(level);
        // Directly access cache here; Rebuild...IfNeeded ensures it's populated before batching starts
        float startY = _levelStartYCache.TryGetValue(level, out float height) ? height : 0f;


        List<float> widths = GenerateRandomDistances(brickCount, circumference, _tower.BrickWidthVariation, seed);
        NormalizeDistances(widths, circumference);

        List<Matrix4x4> matrices = new List<Matrix4x4>(brickCount);
        float currentAngle = rotationOffset;

        for (int i = 0; i < widths.Count; i++) {
            float brickWidth = widths[i];
            if (brickWidth <= MIN_BRICK_WIDTH_THRESHOLD) continue;

            float halfArcAngle = (brickWidth / 2f) / radius;
            currentAngle += halfArcAngle;
            currentAngle %= (2f * Mathf.PI);

            float x = Mathf.Cos(currentAngle) * radius;
            float z = Mathf.Sin(currentAngle) * radius;
            float yPos = startY + brickHeight / 2.0f;
            Vector3 position = new Vector3(x, yPos, z);

            Vector3 lookDirection = new Vector3(x, 0, z).normalized;
            if (lookDirection == Vector3.zero) lookDirection = Vector3.forward;
            Quaternion rotation = Quaternion.LookRotation(lookDirection, Vector3.up);

            Vector3 scale = new Vector3(brickWidth, brickHeight, brickDepth);

            Matrix4x4 matrix = Matrix4x4.TRS(position, rotation, scale);
            matrices.Add(matrix);

            currentAngle += halfArcAngle;
        }

        Matrix4x4[] result = matrices.ToArray();
        _matricesCache[level] = result; // Cache the generated matrices for this level
        return result;
     }

} // End of BrickInstancer class