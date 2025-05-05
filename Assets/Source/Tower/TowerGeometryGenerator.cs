using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Calculates and caches geometric properties (position, rotation, scale, height)
/// for the bricks of a specific procedural Tower instance.
/// Ensures deterministic results based on the Tower's seed and parameters.
/// Recalculates caches when the associated Tower's parameters change.
/// </summary>
public class TowerGeometryGenerator
{
    // --- Constants ---
    private const float MIN_BRICK_WIDTH_THRESHOLD = 0.001f;
    private const int LEVEL_GEOMETRY_SEED_MULTIPLIER = 1000;
    private const int LEVEL_ROTATION_SEED_OFFSET = 1;

    // --- Private Fields ---
    private readonly Tower _tower; // Reference to the tower definition

    // Caches managed by this instance
    private readonly Dictionary<int, float> _levelStartYCache = new Dictionary<int, float>();
    private readonly Dictionary<int, float> _levelHeightCache = new Dictionary<int, float>();
    private readonly Dictionary<int, Matrix4x4[]> _matricesCache = new Dictionary<int, Matrix4x4[]>();
    private bool _needsRebuild = true;

    /// <summary>
    /// Creates a geometry generator instance for a specific Tower.
    /// </summary>
    /// <param name="tower">The Tower component defining the structure.</param>
    public TowerGeometryGenerator(Tower tower)
    {
        if (tower == null)
        {
            throw new System.ArgumentNullException(nameof(tower), "Tower reference cannot be null for Geometry Generator.");
        }
        _tower = tower;
        _tower.OnParametersChanged -= HandleTowerParametersChanged; // Prevent double subscription
        _tower.OnParametersChanged += HandleTowerParametersChanged;
        // Initial calculation will happen on first request or explicit rebuild call
    }

    /// <summary>
    /// Marks the internal caches as dirty, forcing recalculation on the next data request.
    /// Should be called when the associated Tower's parameters change.
    /// </summary>
    public void MarkDirty()
    {

        _needsRebuild = true;
    }

    /// <summary>
    /// Ensures internal caches (start heights, level heights) are populated.
    /// Performs calculations only if marked as dirty.
    /// </summary>
    public void RebuildCachesIfNeeded()
    {
        if (!_needsRebuild) return;
        if (_tower == null) // Should not happen due to constructor check, but safety first
        {
             Debug.LogError("TowerGeometryGenerator cannot rebuild caches: Tower reference is null.");
             return;
        }

        

        Debug.Log($"Rebuilding caches for Tower: {_tower.name}");
        // Clear all existing caches before recalculating
        _levelStartYCache.Clear();
        _levelHeightCache.Clear();
        _matricesCache.Clear();

        // --- Recalculate Heights and Start Positions ---
        float accumulatedHeight = 0f;
        _levelStartYCache[0] = 0f; // Level 0 always starts at 0

        int levelsToCalculate = _tower.Height;
        for (int i = 0; i < levelsToCalculate; i++)
        {
            // Calculate and cache height for level i
            int heightSeed = i * _tower.Seed;
            float currentLevelHeight = CalculateDeterministicLevelBrickHeightInternal(_tower.MinBrickHeight, _tower.MaxBrickHeight, heightSeed);
            _levelHeightCache[i] = currentLevelHeight;

            // Cache start Y for level i (using previously accumulated height)
             if (!_levelStartYCache.ContainsKey(i)) _levelStartYCache[i] = accumulatedHeight;
             else _levelStartYCache[i] = accumulatedHeight; // Should be safe to overwrite

            // Accumulate height for the next level's start Y
            accumulatedHeight += currentLevelHeight;

            // Cache start Y for level i+1
             if (!_levelStartYCache.ContainsKey(i + 1)) _levelStartYCache[i + 1] = accumulatedHeight;
             else _levelStartYCache[i + 1] = accumulatedHeight;
        }
        // Ensure cache entry exists for height *after* the last level
        if (!_levelStartYCache.ContainsKey(levelsToCalculate)) {
             _levelStartYCache[levelsToCalculate] = accumulatedHeight;
        }
        // --- End Height Calculation ---


        _needsRebuild = false; // Caches are now up-to-date
    }


    /// <summary>
    /// Gets the calculated height for bricks within a specific level.
    /// Ensures caches are up-to-date before returning.
    /// </summary>
    /// <param name="level">The zero-based index of the tower level.</param>
    /// <returns>The calculated height for bricks in this level. Returns 0 if level is invalid or caches couldn't be built.</returns>
    public float GetLevelHeight(int level)
    {
        RebuildCachesIfNeeded(); // Ensure height cache is populated
        if (_levelHeightCache.TryGetValue(level, out float height))
        {
            return height;
        }
        // Level might be out of bounds (e.g., negative or >= tower.Height)
        // Debug.LogWarning($"Could not find cached height for level {level}. Tower height is {_tower?.Height ?? -1}.");
        return 0f;
    }

     /// <summary>
    /// Gets the calculated starting Y position (height) for the base of a specific tower level.
    /// Ensures caches are up-to-date before returning.
    /// </summary>
    /// <param name="level">The zero-based index of the tower level.</param>
    /// <returns>The starting Y coordinate of the specified level. Returns 0 if level is invalid or caches couldn't be built.</returns>
     public float GetLevelStartHeight(int level) {
        RebuildCachesIfNeeded(); // Ensure start Y cache is populated
        return _levelStartYCache.TryGetValue(level, out float height) ? height : 0f;
     }


    /// <summary>
    /// Calculates the total height of the tower structure (Y-coordinate of the top surface of the highest level).
    /// Ensures caches are up-to-date before returning.
    /// </summary>
    /// <returns>The total height of the tower. Returns 0 if caches couldn't be built.</returns>
    public float GetTopLevelHeight()
    {
        RebuildCachesIfNeeded(); // Ensure caches are populated

        // The start height of the level *after* the last one is the total height
        int levels = _tower?.Height ?? 0;
        Debug.Log("Total height of the tower: " + levels);
        return _levelStartYCache.TryGetValue(levels, out float totalHeight) ? totalHeight : 0f;
    }


    /// <summary>
    /// Gets or generates the transformation matrices for all bricks in a single tower level.
    /// Uses cached results if available after ensuring caches are rebuilt.
    /// </summary>
    /// <param name="level">The zero-based index of the tower level.</param>
    /// <returns>An array of Matrix4x4 transformations for the bricks in the level, or an empty array if parameters are invalid.</returns>
    public Matrix4x4[] GetOrGenerateLevelMatrices(int level)
    {
        RebuildCachesIfNeeded(); // Ensure all caches (_levelStartYCache, _levelHeightCache) are populated

        // Check matrix cache first
        if (_matricesCache.TryGetValue(level, out var cachedMatrices))
        {
            return cachedMatrices;
        }

        // --- Generate Matrices if not cached ---
        if (_tower == null) return System.Array.Empty<Matrix4x4>(); // Safety check

        // Retrieve necessary cached values, if not found, recalculate the values
        if (!_levelStartYCache.TryGetValue(level, out float levelStartY) ||
            !_levelHeightCache.TryGetValue(level, out float levelBrickHeight))
        {
            levelStartY = GetLevelStartHeight(level); // Recalculate if not found
            levelBrickHeight = GetLevelHeight(level); // Recalculate if not found
        }

        // Get parameters from the Tower object
        int bricksPerLevel = _tower.BricksPerLevel;
        float radius = _tower.Radius;
        float circumference = _tower.Circumference;
        float brickDepth = _tower.BrickDepth;
        float brickWidthVariation = _tower.BrickWidthVariation;

        // Validate essential parameters
        if (bricksPerLevel <= 0 || radius <= 0 || circumference <= 0 || levelBrickHeight <= 0)
        {
            _matricesCache[level] = System.Array.Empty<Matrix4x4>(); // Cache empty result
            return System.Array.Empty<Matrix4x4>();
        }

        // Calculate level-specific seeds
        int levelGeoSeed = level * _tower.Seed * LEVEL_GEOMETRY_SEED_MULTIPLIER;
        int levelRotSeed = levelGeoSeed + LEVEL_ROTATION_SEED_OFFSET;

        float rotationOffset = CalculateDeterministicLevelRotationOffset(levelRotSeed);
        List<float> widths = GenerateRandomDistances(bricksPerLevel, circumference, brickWidthVariation, levelGeoSeed);
        NormalizeDistances(widths, circumference);

        List<Matrix4x4> matrices = new List<Matrix4x4>(bricksPerLevel);
        float currentAngle = rotationOffset;

        for (int i = 0; i < widths.Count; i++)
        {
            float brickWidth = widths[i];
            if (brickWidth <= MIN_BRICK_WIDTH_THRESHOLD) continue;

            float halfArcAngle = (brickWidth / 2f) / radius;
            float centerAngle = currentAngle + halfArcAngle;
            centerAngle %= (2f * Mathf.PI);

            float x = Mathf.Cos(centerAngle) * radius;
            float z = Mathf.Sin(centerAngle) * radius;
            float yPos = levelStartY + levelBrickHeight / 2.0f;
            Vector3 position = new Vector3(x, yPos, z);

            Vector3 lookDirection = new Vector3(x, 0, z).normalized;
            if (lookDirection == Vector3.zero) lookDirection = Vector3.forward;
            Quaternion rotation = Quaternion.LookRotation(lookDirection, Vector3.up);

            Vector3 scale = new Vector3(brickWidth, levelBrickHeight, brickDepth);
            Matrix4x4 matrix = Matrix4x4.TRS(position, rotation, scale);
            matrices.Add(matrix);

            currentAngle += halfArcAngle * 2f;
        }

        Matrix4x4[] result = matrices.ToArray();
        _matricesCache[level] = result; // Cache the generated matrices
        return result;
    }


    // --- Private Static Helper Methods --- (These remain static as they don't depend on instance state)

    private static float CalculateDeterministicLevelBrickHeightInternal(float minBrickHeight, float maxBrickHeight, int heightSeed)
    {
        Random.InitState(heightSeed);
        if (minBrickHeight > maxBrickHeight) maxBrickHeight = minBrickHeight;
        if (Mathf.Approximately(minBrickHeight, maxBrickHeight)) return minBrickHeight;
        if (minBrickHeight >= maxBrickHeight) return minBrickHeight; // Handles potential float precision issue with Approx
        return Random.Range(minBrickHeight, maxBrickHeight);
    }

    private static List<float> GenerateRandomDistances(int brickCount, float circumference, float widthVariation, int randomSeed)
    {
        Random.InitState(randomSeed);
        List<float> distances = new List<float>(brickCount);
        if (brickCount <= 0 || circumference <= 0) return distances;

        float avgDistance = circumference / brickCount;
        float minDistance = avgDistance * (1.0f - widthVariation);
        float maxDistance = avgDistance * (1.0f + widthVariation);
        minDistance = Mathf.Max(0f, minDistance);

        float totalWidth = 0f;
        for (int i = 0; i < brickCount; i++)
        {
            float width = Random.Range(minDistance, maxDistance);
            distances.Add(width);
            totalWidth += width;
        }

        if (totalWidth <= Mathf.Epsilon)
        {
            distances.Clear();
            float equalWidth = circumference / brickCount;
            for (int i = 0; i < brickCount; i++) distances.Add(equalWidth);
        }
        return distances;
    }

    private static void NormalizeDistances(List<float> distances, float targetSum)
    {
        if (distances == null || distances.Count == 0 || targetSum <= 0) return;
        float currentSum = 0f;
        foreach (float d in distances) { currentSum += d; }
        if (currentSum <= Mathf.Epsilon || Mathf.Approximately(currentSum, targetSum)) return;
        float scale = targetSum / currentSum;
        for (int i = 0; i < distances.Count; i++) { distances[i] *= scale; }
    }

    private static float CalculateDeterministicLevelRotationOffset(int rotationSeed)
    {
        Random.InitState(rotationSeed);
        return Random.Range(0f, 2f * Mathf.PI);
    }

        /// <summary>
    /// Shuffles the elements of a list in place using the Fisher-Yates algorithm
    /// and a specific seed for deterministic results. Public static as it's a general utility.
    /// </summary>
    public static void Shuffle<T>(IList<T> list, int seed) {
        Random.InitState(seed);
        int n = list.Count;
        while (n > 1) {
            n--;
            int k = Random.Range(0, n + 1);
            T value = list[k];
            list[k] = list[n];
            list[n] = value;
        }
    }

    // --- Event Handler ---
    private void HandleTowerParametersChanged()
    {
        // Tell the generator its source data changed
        Debug.Log($"Tower parameters changed, marking geometry generator as dirty for Tower: {_tower.name}");
        MarkDirty();
    }

}