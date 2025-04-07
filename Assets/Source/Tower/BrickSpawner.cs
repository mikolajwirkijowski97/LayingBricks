using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Responsible for rendering the bricks of a Tower procedural structure using GPU instancing.
/// Retrieves brick transformation data from TowerGeometryGenerator and prepares batches for rendering.
/// Optimised for static towers: Calculates and prepares batches only when Tower parameters change.
/// Listens to Tower parameter changes to automatically rebuild its geometry caches and batches.
/// </summary>
[RequireComponent(typeof(Tower))] // Ensure Tower component exists
public class TowerInstancedRenderer : MonoBehaviour
{
    // --- Constants ---
    private const int MAX_INSTANCES_PER_DRAW_CALL = 1023; // GPU Instancing limit for DrawMeshInstanced
    private const int LEVEL_GEOMETRY_SEED_MULTIPLIER = 1000; // Used for deterministic level geometry seeding
    private const int LEVEL_ROTATION_SEED_OFFSET = 1;      // Offset for rotation seed relative to geometry seed

    // --- Serialized Fields ---
    [Header("Dependencies")]
    [SerializeField]
    [Tooltip("The Tower component defining the structure to render. If null, attempts to find one on the same GameObject.")]
    private Tower _tower;

    [SerializeField]
    [Tooltip("The prefab used for each brick. Must contain a MeshFilter and MeshRenderer for instancing.")]
    private GameObject _brickPrefab;

    // --- Properties ---

    /// <summary>
    /// Gets or sets the Tower data source. Handles event subscription/unsubscription automatically.
    /// Marks the renderer as dirty if the Tower reference changes.
    /// </summary>
    public Tower Tower
    {
        get { return _tower; }
        set
        {
            if (_tower != value)
            {
                // Unsubscribe from the old tower's event
                if (_tower != null)
                {
                    _tower.OnParametersChanged -= HandleTowerParametersChanged;
                }

                _tower = value;

                // Subscribe to the new tower's event
                if (_tower != null)
                {
                    _tower.OnParametersChanged += HandleTowerParametersChanged;
                    MarkDirty(); // Mark dirty as tower data will be different
                }
                else
                {
                    // Clear caches if tower becomes null
                    ClearCachesAndBatches();
                }
            }
        }
    }

    /// <summary>
    /// Gets or sets the prefab used for instancing bricks.
    /// Re-caches essential components (Mesh, Material) and marks the renderer dirty if the prefab changes.
    /// </summary>
    public GameObject BrickPrefab
    {
        get { return _brickPrefab; }
        set
        {
            if (_brickPrefab != value)
            {
                _brickPrefab = value;
                CachePrefabComponents(); // Update cached mesh/material
                MarkDirty(); // Prefab change requires rebuild
            }
        }
    }

    // --- Private Fields ---

    // Cache for calculated level start heights to avoid recalculation
    private readonly Dictionary<int, float> _levelStartYCache = new Dictionary<int, float>();
    // Cache for generated matrices *during a single rebuild* to avoid redundant calls to generator
    private readonly Dictionary<int, Matrix4x4[]> _rebuildMatricesCache = new Dictionary<int, Matrix4x4[]>();


    // Cached components from the BrickPrefab for efficient rendering
    private Mesh _brickMesh;
    private Material _brickMaterial;

    // Flag indicating if caches and persistent batches need rebuilding due to parameter changes
    private bool _needsRebuild = true;

    // Stores the prepared batches of matrices ready for rendering
    private readonly List<Matrix4x4[]> _persistentBatches = new List<Matrix4x4[]>();


    // --- Unity Lifecycle Methods ---

    void Awake()
    {
        // Attempt to find Tower component if not assigned in Inspector
        if (_tower == null)
        {
            _tower = GetComponent<Tower>();
        }
        // Initial subscription and data population happens in OnEnable/Start
    }

    void Start()
    {
        if (_tower == null)
        {
            Debug.LogError($"{nameof(TowerInstancedRenderer)}: Tower component not found or assigned! Disabling.", this);
            enabled = false;
            return;
        }
        if (!CachePrefabComponents())
        {
            Debug.LogError($"{nameof(TowerInstancedRenderer)}: Brick Prefab has missing MeshFilter or MeshRenderer! Disabling.", this);
            enabled = false;
            return;
        }
        // Initial cache and batch build will happen on first Update via Rebuild...IfNeeded
        MarkDirty(); // Ensure initial build
    }

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
        if (_persistentBatches.Count == 0 && _needsRebuild) {
             // Avoid drawing if dirty and rebuild failed or resulted in zero batches.
             return;
        }

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

    void OnValidate()
    {
        // Re-cache prefab components if the reference changed in the editor
        CachePrefabComponents(); // Call unconditionally, it handles nulls/changes internally

        // Mark dirty whenever any inspector value changes for this component,
        // ensuring caches are checked/rebuilt on next Update. Handles null tower reference gracefully.
        MarkDirty();

         // If tower exists, ensure its internal validation runs which might trigger OnParametersChanged
        if(_tower != null) {
            // This call ensures the Tower component validates itself, which is important
            // if its OnValidate modified parameters that this component depends on.
             #if UNITY_EDITOR
             // Call OnValidate manually only in the editor context
             UnityEditor.EditorUtility.SetDirty(_tower); // Ensure tower recompiles internally if needed.
             _tower.SendMessage("OnValidate", SendMessageOptions.DontRequireReceiver);
             #endif
        }
    }

    // --- Event Handler ---

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
    public void MarkDirty()
    {
        _needsRebuild = true;
    }

    /// <summary>
    /// Gets the calculated starting Y position (height) for a specific tower level.
    /// Returns 0 if the level is invalid or data hasn't been cached.
    /// Ensures caches are up-to-date before returning the value.
    /// </summary>
    /// <param name="level">The zero-based index of the tower level.</param>
    /// <returns>The starting height (Y coordinate) of the specified level.</returns>
    public float GetLevelStartHeight(int level)
    {
        // Call rebuild first if direct access is needed outside Update, otherwise
        // Update loop ensures data is rebuilt before rendering.
        RebuildDataAndBatchesIfNeeded(); // Ensures _levelStartYCache is populated
        return _levelStartYCache.TryGetValue(level, out float height) ? height : 0f;
    }


    // --- Private Helper Methods ---

    /// <summary>
    /// Checks if caches and batches need rebuilding (`_needsRebuild` flag) and performs the rebuild if necessary.
    /// Clears existing caches/batches, recalculates level start heights, retrieves geometry data
    /// from TowerGeometryGenerator, and generates the new persistent batches for rendering.
    /// </summary>
    private void RebuildDataAndBatchesIfNeeded()
    {
        // Only rebuild if marked as dirty and the tower reference is valid
        if (!_needsRebuild || _tower == null)
        {
            return; // Nothing to do if not dirty or no tower
        }

        ClearCachesAndBatches(); // Clear height cache, temp matrix cache, and existing prepared batches
        CalculateAllLevelStartHeights(); // Recalculate Y positions based on current tower params

        // --- Generate and Store Persistent Batches ---
        // Use Tower height property which is now calculated based on TotalBricks
        int totalLevelsToBuild = _tower.Height; // Total theoretical levels
        var currentBatchMatrices = new List<Matrix4x4>(MAX_INSTANCES_PER_DRAW_CALL);
        int bricksBuilt = 0;
        int totalBricksToBuild = _tower.TotalBricks; // Use the actual total brick count

        for (int level = 0; level < totalLevelsToBuild && bricksBuilt < totalBricksToBuild; level++)
        {
            // Retrieve matrices for the current level using the static generator
            Matrix4x4[] currentLevelMatrices = GetOrGenerateLevelMatricesForRebuild(level);
            if (currentLevelMatrices == null || currentLevelMatrices.Length == 0) continue;

            // Determine how many bricks from this level we actually need to add
            int bricksToAddFromThisLevel = currentLevelMatrices.Length;
            bool isLastLevelBeingProcessed = (bricksBuilt + bricksToAddFromThisLevel >= totalBricksToBuild);

            if (isLastLevelBeingProcessed) {
                bricksToAddFromThisLevel = totalBricksToBuild - bricksBuilt;
                if (bricksToAddFromThisLevel <= 0) break; // Already built all required bricks

                // If the last level isn't ordered and we're only taking a part of it, shuffle first
                if (!_tower.IsLastLevelOrdered && bricksToAddFromThisLevel < currentLevelMatrices.Length)
                {
                    // Need to shuffle a *copy* if we intend to use the original cache later,
                    // or just shuffle in place if the cache is only for this rebuild.
                    // Shuffling in place is fine here as _rebuildMatricesCache is cleared each rebuild.
                    int levelGeoSeed = level * _tower.Seed * LEVEL_GEOMETRY_SEED_MULTIPLIER;
                    Shuffle(currentLevelMatrices, levelGeoSeed); // Use a deterministic seed for shuffling
                }
            }


            // Add bricks one by one or in chunks to the current batch, respecting MAX_INSTANCES_PER_DRAW_CALL
            int brickIndexInLevel = 0;
            while(brickIndexInLevel < bricksToAddFromThisLevel)
            {
                // If the current batch is full, store it and start a new one
                if (currentBatchMatrices.Count >= MAX_INSTANCES_PER_DRAW_CALL) {
                    _persistentBatches.Add(currentBatchMatrices.ToArray());
                    currentBatchMatrices.Clear();
                }

                // Calculate how many more bricks can fit in the current batch
                int remainingSpaceInBatch = MAX_INSTANCES_PER_DRAW_CALL - currentBatchMatrices.Count;
                // Calculate how many bricks are left to add from this level (considering total bricks limit)
                int bricksLeftInLevelToAdd = bricksToAddFromThisLevel - brickIndexInLevel;

                // Determine how many bricks to add in this step
                int countToAdd = Mathf.Min(remainingSpaceInBatch, bricksLeftInLevelToAdd);

                // Add the bricks to the batch
                for(int i = 0; i < countToAdd; ++i) {
                    currentBatchMatrices.Add(currentLevelMatrices[brickIndexInLevel + i]);
                }

                brickIndexInLevel += countToAdd; // Advance index within the level
                bricksBuilt += countToAdd;      // Increment total bricks built
            }
        }

        // After the loop, store any remaining matrices in the last partially filled batch.
        if (currentBatchMatrices.Count > 0)
        {
            _persistentBatches.Add(currentBatchMatrices.ToArray());
        }
        // --- End Batch Generation ---

        _needsRebuild = false; // Mark as clean after rebuild and batch preparation
        _rebuildMatricesCache.Clear(); // Clear the temporary cache used during rebuild
    }


    /// <summary>
    /// Clears level start height cache, the temporary rebuild matrix cache, and the prepared rendering batches.
    /// </summary>
    private void ClearCachesAndBatches()
    {
        _levelStartYCache.Clear();
        _rebuildMatricesCache.Clear(); // Clear temporary generation cache
        _persistentBatches.Clear();    // Clear the prepared rendering batches
    }

    /// <summary>
    /// Caches the Mesh and Material components from the assigned BrickPrefab.
    /// Logs errors and returns false if the prefab is null or missing required components.
    /// Updates internal mesh/material references.
    /// </summary>
    /// <returns>True if components were successfully cached/updated, false otherwise.</returns>
    private bool CachePrefabComponents()
    {
        if (_brickPrefab == null)
        {
            // If prefab is null, clear cached components and return false
            if (_brickMesh != null || _brickMaterial != null) MarkDirty(); // Need rebuild if prefab removed
            _brickMesh = null;
            _brickMaterial = null;
            return false;
        }

        MeshFilter mf = _brickPrefab.GetComponent<MeshFilter>();
        MeshRenderer mr = _brickPrefab.GetComponent<MeshRenderer>();

        // Validate essential components
        if (mf == null || mf.sharedMesh == null)
        {
            Debug.LogError($"{nameof(TowerInstancedRenderer)}: Brick prefab '{_brickPrefab.name}' is missing MeshFilter or its Mesh!", _brickPrefab);
             if (_brickMesh != null || _brickMaterial != null) MarkDirty();
            _brickMesh = null; _brickMaterial = null; return false;
        }
        if (mr == null || mr.sharedMaterial == null)
        {
            Debug.LogError($"{nameof(TowerInstancedRenderer)}: Brick prefab '{_brickPrefab.name}' is missing MeshRenderer or its Material!", _brickPrefab);
             if (_brickMesh != null || _brickMaterial != null) MarkDirty();
             _brickMesh = null; _brickMaterial = null; return false;
        }

        // Check if mesh or material actually changed
        bool changed = false;
        if (_brickMesh != mf.sharedMesh)
        {
            _brickMesh = mf.sharedMesh;
            changed = true;
        }
        if (_brickMaterial != mr.sharedMaterial)
        {
            _brickMaterial = mr.sharedMaterial;
            changed = true;
        }

        if(changed) MarkDirty(); // Mark for rebuild if relevant components changed

        return true; // Components are valid
    }


    /// <summary>
    /// Calculates and caches the starting Y position for all levels up to the Tower's height.
    /// Populates the `_levelStartYCache`. Uses the static TowerGeometryGenerator for height calculation.
    /// </summary>
    private void CalculateAllLevelStartHeights()
    {
        _levelStartYCache.Clear();
        if (_tower == null) return; // Cannot calculate without tower data

        float accumulatedHeight = 0f;
        _levelStartYCache[0] = 0f; // Level 0 starts at height 0

        int levelsToCalculate = _tower.Height; // Use tower's calculated height
        for (int i = 0; i < levelsToCalculate; i++)
        {
            // Calculate the height of the current level's bricks using the generator
            int heightSeed = i * _tower.Seed; // Consistent seeding for height
            float currentLevelHeight = TowerGeometryGenerator.CalculateDeterministicLevelBrickHeight(
                _tower.MinBrickHeight,
                _tower.MaxBrickHeight,
                heightSeed
            );

            // Store the start height for the current level before adding its height
             if (!_levelStartYCache.ContainsKey(i)) { // Should already contain 0
                 _levelStartYCache[i] = accumulatedHeight;
             }

            // Accumulate height to find the start of the *next* level
            accumulatedHeight += currentLevelHeight;

            // Cache the starting height for the next level (index i+1)
             if (!_levelStartYCache.ContainsKey(i + 1)) {
                 _levelStartYCache[i + 1] = accumulatedHeight;
             } else {
                 // This case should ideally not happen with linear calculation, but safety check:
                 _levelStartYCache[i + 1] = accumulatedHeight;
             }
        }
         // Ensure the start height for the level *after* the last one is cached if needed elsewhere
         if (!_levelStartYCache.ContainsKey(levelsToCalculate)) {
             _levelStartYCache[levelsToCalculate] = accumulatedHeight;
         }
    }

    /// <summary>
    /// Retrieves the transformation matrices for a given level *during a rebuild*.
    /// First checks the temporary `_rebuildMatricesCache`. If not found, calls the static
    /// `TowerGeometryGenerator.GenerateLevelMatrices`, caches the result, and returns it.
    /// </summary>
    /// <param name="level">The zero-based index of the tower level.</param>
    /// <returns>An array of Matrix4x4 transformations, or null if generation failed or level is invalid.</returns>
    private Matrix4x4[] GetOrGenerateLevelMatricesForRebuild(int level)
    {
         // Check temporary cache first
        if (_rebuildMatricesCache.TryGetValue(level, out var cachedMatrices))
        {
            return cachedMatrices;
        }

        // Need Tower parameters to call the static generator
        if (_tower == null || !_levelStartYCache.ContainsKey(level))
        {
            Debug.LogError($"Cannot generate matrices for level {level}: Tower data or start height missing.", this);
            return null;
        }

        // Prepare parameters for the static generator
        float startY = _levelStartYCache[level];
        int levelGeoSeed = level * _tower.Seed * LEVEL_GEOMETRY_SEED_MULTIPLIER; // Seed for geometry
        int levelRotSeed = levelGeoSeed + LEVEL_ROTATION_SEED_OFFSET;           // Separate seed for rotation
        int heightSeed = level * _tower.Seed;                                   // Seed used for height calculation

        // Get the deterministic height for *this* specific level
        float levelHeight = TowerGeometryGenerator.CalculateDeterministicLevelBrickHeight(
             _tower.MinBrickHeight,
             _tower.MaxBrickHeight,
             heightSeed
        );

        // Call the static generator method
        Matrix4x4[] generatedMatrices = TowerGeometryGenerator.GenerateLevelMatrices(
            level: level,
            bricksPerLevel: _tower.BricksPerLevel,
            radius: _tower.Radius,
            circumference: _tower.Circumference,
            brickDepth: _tower.BrickDepth,
            levelBrickHeight: levelHeight, // Use the calculated height for this level
            brickWidthVariation: _tower.BrickWidthVariation,
            levelStartY: startY,
            levelSeed: levelGeoSeed,
            levelRotationSeed: levelRotSeed
        );

        // Cache the result in the temporary cache for this rebuild pass
        _rebuildMatricesCache[level] = generatedMatrices;
        return generatedMatrices;
    }

    /// <summary>
    /// Shuffles the elements of a list in place using the Fisher-Yates algorithm
    /// and a specific seed for deterministic results.
    /// </summary>
    /// <typeparam name="T">The type of elements in the list.</typeparam>
    /// <param name="list">The list to shuffle.</param>
    /// <param name="seed">The seed for the Random Number Generator.</param>
    private void Shuffle<T>(IList<T> list, int seed) {
        // Initialize the random number generator with the provided seed
        Random.InitState(seed);
        int n = list.Count;
        while (n > 1) {
            n--;
            int k = Random.Range(0, n + 1); // Use UnityEngine.Random
            T value = list[k];
            list[k] = list[n];
            list[n] = value;
        }
    }
}