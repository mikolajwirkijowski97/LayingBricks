using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Responsible for rendering the bricks of a Tower procedural structure using GPU instancing.
/// Creates and uses a TowerGeometryGenerator instance to get brick transformation data.
/// Prepares batches for efficient rendering via Graphics.DrawMeshInstanced.
/// Listens to Tower parameter changes to automatically trigger geometry recalculation.
/// </summary>
[RequireComponent(typeof(Tower))]
public class TowerInstancedRenderer : MonoBehaviour
{
    // --- Constants ---
    private const int MAX_INSTANCES_PER_DRAW_CALL = 1023;

    // --- Serialized Fields ---
    [Header("Dependencies")]
    [SerializeField]
    [Tooltip("The Tower component defining the structure to render. If null, attempts to find one on the same GameObject.")]
    private Tower _tower;

    [SerializeField]
    [Tooltip("The prefab used for each brick. Must contain a MeshFilter and MeshRenderer for instancing.")]
    private GameObject _brickPrefab;

    [SerializeField]
    [Tooltip("The shape for shadow casting.")]
    private GameObject _shadowCastingCylinderPrefab;

    // --- Private Fields ---
    private TowerGeometryGenerator _geometryGenerator; // Instance to handle geometry calculation and caching

    // Cached components from the BrickPrefab
    private Mesh _brickMesh;
    private Material _brickMaterial;

    // Stores the prepared batches of matrices ready for rendering
    private readonly List<Matrix4x4[]> _persistentBatches = new List<Matrix4x4[]>();
    private bool _needsBatchRebuild = true; // Separate flag for batching, triggered by generator being dirty
    
    // --- Properties ---

    public bool isOn = false; // Flag to control rendering state
    public Tower Tower
    {
        get { return _tower; }
        set
        {
            if (_tower != value)
            {
                // Unsubscribe from old tower
                if (_tower != null) _tower.OnParametersChanged -= HandleTowerParametersChanged;

                _tower = value;

                // Setup for new tower
                if (_tower != null)
                {
                    _tower.OnParametersChanged += HandleTowerParametersChanged;
                    // Create or update the generator for the new tower
                    InitializeGeometryGenerator();
                }
                else
                {
                    // Clear generator and batches if tower is removed
                    _geometryGenerator = null;
                    ClearBatches();
                    _needsBatchRebuild = true;
                }
            }
        }
    }

    public GameObject BrickPrefab
    {
        get { return _brickPrefab; }
        set
        {
            if (_brickPrefab != value)
            {
                _brickPrefab = value;
                // Re-cache mesh/material and mark batches dirty if prefab changes
                bool success = CachePrefabComponents();
                if(success) { // Only mark dirty if the new prefab is valid
                     MarkBatchesDirty();
                } else { // If new prefab is invalid, clear batches
                    ClearBatches();
                }
            }
        }
    }

    // --- Unity Lifecycle Methods ---

    void Awake()
    {
        if (_tower == null) _tower = GetComponent<Tower>();
        InitializeGeometryGenerator(); // Create generator instance early
    }

    void Start()
    {
        if (_tower == null)
        {
            Debug.LogError($"{nameof(TowerInstancedRenderer)}: Tower component missing! Disabling.", this);
            enabled = false; return;
        }
        if (_geometryGenerator == null) // Should be created in Awake, but safety check
        {
             InitializeGeometryGenerator();
             if (_geometryGenerator == null) { // If tower was null in Awake and Start
                 Debug.LogError($"{nameof(TowerInstancedRenderer)}: Could not initialize Geometry Generator! Disabling.", this);
                 enabled = false; return;
             }
        }
        if (!CachePrefabComponents())
        {
            Debug.LogError($"{nameof(TowerInstancedRenderer)}: Brick Prefab invalid! Disabling.", this);
            enabled = false; return;
        }
        MarkBatchesDirty(); // Ensure initial batch build
    }

    void OnEnable()
    {
        if (_tower != null)
        {
            _tower.OnParametersChanged -= HandleTowerParametersChanged; // Prevent double subscription
            _tower.OnParametersChanged += HandleTowerParametersChanged;
             // Ensure generator exists and mark batches dirty
             if (_geometryGenerator == null) InitializeGeometryGenerator();
             MarkBatchesDirty();
        }
        _shadowCastingCylinderPrefab = Instantiate(_shadowCastingCylinderPrefab); // Instantiate shadow casting cylinder prefab
        _shadowCastingCylinderPrefab.transform.SetParent(transform); // Set parent to this object
        _shadowCastingCylinderPrefab.transform.localPosition = Vector3.zero; // Reset position

    }

    void OnDisable()
    {
        if (_tower != null) _tower.OnParametersChanged -= HandleTowerParametersChanged;
        // delete _shadowCastingCylinderPrefab; // Destroy shadow casting cylinder prefab
        if (_shadowCastingCylinderPrefab != null)
        {
            Destroy(_shadowCastingCylinderPrefab); // Destroy the shadow casting cylinder prefab
        }
    }

    void Update()
    {   
        if (!isOn) return; // Skip rendering if not enabled

        // Basic checks for essential components
        if (_geometryGenerator == null || _brickMesh == null || _brickMaterial == null) return;

        // Ensure geometry caches are up-to-date (generator handles internal check)
        _geometryGenerator.RebuildCachesIfNeeded();

        // Rebuild rendering batches if needed
        RebuildBatchesIfNeeded();

        // Render using the prepared batches
        if (_persistentBatches.Count == 0 && _needsBatchRebuild) return; // Avoid drawing if dirty and rebuild failed

        RenderParams rp = new RenderParams(_brickMaterial);
        rp.shadowCastingMode = ShadowCastingMode.Off; // Set shadow casting mode
        
        foreach (Matrix4x4[] batchToDraw in _persistentBatches)
        {
            // If the last transform in batchToDraw is further than maxDistance, skip drawing
            // This is a simple optimization to avoid drawing far away bricks
            float maxDistance = 200f; // Example maximum distance
            Vector3 cameraPosition = Camera.main.transform.position;
            Vector3 lastBrickPosition = batchToDraw[batchToDraw.Length - 1].GetColumn(3);
            if (Vector3.Distance(cameraPosition, lastBrickPosition) > maxDistance)
            {
                continue;
            }

            if (batchToDraw != null && batchToDraw.Length > 0 )
            {
                Graphics.RenderMeshInstanced(rp, _brickMesh, 0, batchToDraw, batchToDraw.Length);
            }
        }
    }

     void OnValidate()
    {
        // Update tower reference / generator if changed in editor
        if(this.Tower != _tower) // Check if the property setter logic needs to run
        {
            this.Tower = _tower; // Use property setter to handle subscriptions/generator creation
        } else if (_tower != null && _geometryGenerator == null) {
            // Case where tower exists but generator wasn't created yet (e.g. script recompile)
            InitializeGeometryGenerator();
        }

        // Re-cache prefab components if changed in editor
        CachePrefabComponents();

        // Always mark batches dirty on validate in editor
        MarkBatchesDirty();
        // Tell generator to mark its caches dirty too
        _geometryGenerator?.MarkDirty();


    }

    // --- Event Handler ---

    private void HandleTowerParametersChanged()
    {
        // Tell the generator its source data changed
        _geometryGenerator?.MarkDirty();

        
        // Dont rebuild batches for now.
        // Mark that our rendering batches need to be rebuilt based on new geometry
        //MarkBatchesDirty();
    }

    // --- Private Helper Methods ---

    private void InitializeGeometryGenerator()
    {
        // Clear old batches if generator is being replaced/created
         ClearBatches();

         if (_tower != null)
         {
             try {
                  _geometryGenerator = new TowerGeometryGenerator(_tower);
                  MarkBatchesDirty(); // Need to rebuild batches with new generator data
             } catch (System.ArgumentNullException e) {
                 Debug.LogError($"Failed to initialize TowerGeometryGenerator: {e.Message}", this);
                 _geometryGenerator = null;
                 enabled = false; // Disable if generator fails
             }
         }
         else
         {
              _geometryGenerator = null; // Tower is null, no generator
         }
    }

    private void MarkBatchesDirty()
    {
        _needsBatchRebuild = true;
    }

    private void ClearBatches()
    {
         _persistentBatches.Clear();
         _needsBatchRebuild = true; // Ensure rebuild is triggered next update
    }

    public void TurnOn()
    {
        ClearBatches(); // Clear any existing batches on start
        // Ensure the geometry generator is initialized and caches are rebuilt
        if (_geometryGenerator != null)
        {
            _geometryGenerator.RebuildCachesIfNeeded();
            RebuildBatchesIfNeeded(); // Rebuild batches after cache update
        }
        isOn = true;
    }

    public void TurnOff()
    {
        isOn = false; // Disable rendering
        ClearBatches(); // Clear batches to free up resources
    }



    public void setInstancedTowerSize(int bricks) {
        if(bricks == _tower.TotalBricks) return; // No change in size, no need to update

        _tower.TotalBricks = bricks; // Set the total bricks based on fetched distance
        ClearBatches(); // Clear batches to force a rebuild with new data
        TurnOn(); // Re-enable rendering when distance is fetched
    }

    /// <summary>
    /// Rebuilds the list of Matrix4x4[] batches used for DrawMeshInstanced calls.
    /// This is done only when _needsBatchRebuild is true.
    /// Retrieves geometry data from the _geometryGenerator instance.
    /// </summary>
    private void RebuildBatchesIfNeeded()
    {

        // Set the shadow casting cylinder size
        if (_shadowCastingCylinderPrefab != null)
        {
            float cylinderHeight = _geometryGenerator.GetTopLevelHeight() + 0.25f;
            float cylinderDiameter = _tower.Radius * 2f + 2*_tower.BrickDepth;
            _shadowCastingCylinderPrefab.transform.localScale = new Vector3(cylinderDiameter, cylinderHeight, cylinderDiameter);
        }

        if (!_needsBatchRebuild || _geometryGenerator == null) return;

        ClearBatches(); // Clear previous batches before creating new ones
        _needsBatchRebuild = false; // Assume success unless we fail

         // Generator should already have caches rebuilt via Update calling RebuildCachesIfNeeded

        // --- Generate and Store Persistent Batches ---
        int totalLevelsToBuild = _tower.Height;
        var currentBatchMatrices = new List<Matrix4x4>(MAX_INSTANCES_PER_DRAW_CALL);
        int bricksBuilt = 0;
        int totalBricksToBuild = _tower.TotalBricks;
        Debug.Log($"Rebuilding batches(cause needed)\nTotal bricks to build: {totalBricksToBuild}");

        for (int level = 0; level < totalLevelsToBuild && bricksBuilt < totalBricksToBuild; level++)
        {
            // Get matrices directly from the generator instance
            Matrix4x4[] currentLevelMatrices = _geometryGenerator.GetOrGenerateLevelMatrices(level);
            if (currentLevelMatrices == null || currentLevelMatrices.Length == 0) continue;

            int bricksToAddFromThisLevel = currentLevelMatrices.Length;
            bool isLastLevelBeingProcessed = (bricksBuilt + bricksToAddFromThisLevel >= totalBricksToBuild);

            if (isLastLevelBeingProcessed)
            {
                bricksToAddFromThisLevel = totalBricksToBuild - bricksBuilt;
                if (bricksToAddFromThisLevel <= 0) break;

                if (!_tower.IsLastLevelOrdered && bricksToAddFromThisLevel < currentLevelMatrices.Length)
                {
                    // Need a *copy* to shuffle if the generator might reuse the original cached array later.
                    // Or, if GetOrGenerateLevelMatrices always returns a new array or allows modification, shuffle in place.
                    // Assuming GetOrGenerate returns potentially cached array, make a copy.
                    var matricesToShuffle = new Matrix4x4[currentLevelMatrices.Length];
                    System.Array.Copy(currentLevelMatrices, matricesToShuffle, currentLevelMatrices.Length);

                    int shuffleSeed = level * _tower.Seed + 123;
                    TowerGeometryGenerator.Shuffle(matricesToShuffle, shuffleSeed); // Use static Shuffle utility
                    currentLevelMatrices = matricesToShuffle; // Use the shuffled copy for processing
                }
            }

            int brickIndexInLevel = 0;
            while (brickIndexInLevel < bricksToAddFromThisLevel)
            {
                if (currentBatchMatrices.Count >= MAX_INSTANCES_PER_DRAW_CALL)
                {
                    _persistentBatches.Add(currentBatchMatrices.ToArray());
                    currentBatchMatrices.Clear();
                }

                int remainingSpaceInBatch = MAX_INSTANCES_PER_DRAW_CALL - currentBatchMatrices.Count;
                int bricksLeftInLevelToAdd = bricksToAddFromThisLevel - brickIndexInLevel;
                int countToAdd = Mathf.Min(remainingSpaceInBatch, bricksLeftInLevelToAdd);

                for (int i = 0; i < countToAdd; ++i)
                {
                    if (brickIndexInLevel + i < currentLevelMatrices.Length) {
                        currentBatchMatrices.Add(currentLevelMatrices[brickIndexInLevel + i]);
                    } else {
                        Debug.LogError($"Index out of bounds during batching: Level {level}, Index {brickIndexInLevel + i}, Array Size {currentLevelMatrices.Length}");
                        break;
                    }
                }
                brickIndexInLevel += countToAdd;
                bricksBuilt += countToAdd;
                if (bricksBuilt >= totalBricksToBuild) break;
            }
            if (bricksBuilt >= totalBricksToBuild) break;
        }

        if (currentBatchMatrices.Count > 0)
        {
            _persistentBatches.Add(currentBatchMatrices.ToArray());
        }

        // If after all this, no batches were created, maybe set needs rebuild true again?
        // Or just let it be empty. Current logic handles empty batches fine.
        // _needsBatchRebuild remains false as we completed the process.
    }


    /// <summary>
    /// Caches prefab components. Returns true if valid, false otherwise.
    /// Does NOT mark batches dirty here; caller should do that if needed.
    /// </summary>
    private bool CachePrefabComponents()
    {
        if (_brickPrefab == null)
        {
            _brickMesh = null; _brickMaterial = null; return false;
        }

        MeshFilter mf = _brickPrefab.GetComponent<MeshFilter>();
        MeshRenderer mr = _brickPrefab.GetComponent<MeshRenderer>();

        if (mf == null || mf.sharedMesh == null || mr == null || mr.sharedMaterial == null)
        {
             _brickMesh = null; _brickMaterial = null;
             // Error logging moved to Start to avoid console spam
            return false;
        }


        _brickMesh = mf.sharedMesh;
        _brickMaterial = mr.sharedMaterial;

        // If components changed, the caller (property setter or OnValidate) is responsible
        // for marking batches dirty. This function just reports success/failure.
        return true;
    }
}