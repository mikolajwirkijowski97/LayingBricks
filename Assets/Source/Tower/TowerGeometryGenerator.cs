using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Provides static methods to calculate the geometric properties (position, rotation, scale)
/// of bricks for a procedural tower level based on specified parameters.
/// Ensures deterministic results based on provided seeds.
/// </summary>
public static class TowerGeometryGenerator
{
    // --- Constants ---
    private const float MIN_BRICK_WIDTH_THRESHOLD = 0.001f; // Minimum width to consider a brick valid

    /// <summary>
    /// Generates the transformation matrices for all bricks in a single tower level.
    /// Calculates position, rotation, and scale for each brick based on input parameters.
    /// </summary>
    /// <param name="level">The zero-based index of the tower level.</param>
    /// <param name="bricksPerLevel">Number of bricks for this level.</param>
    /// <param name="radius">The radius of the tower level.</param>
    /// <param name="circumference">The circumference of the tower level.</param>
    /// <param name="brickDepth">The depth (thickness) of the bricks.</param>
    /// <param name="levelBrickHeight">The height of the bricks for this specific level.</param>
    /// <param name="brickWidthVariation">Maximum percentage variation for brick widths (0 to 0.9).</param>
    /// <param name="levelStartY">The starting Y coordinate (height) for the base of this level.</param>
    /// <param name="levelSeed">A deterministic seed specific to this level's geometry generation.</param>
    /// <param name="levelRotationSeed">A deterministic seed specific to this level's rotation offset.</param>
    /// <returns>An array of Matrix4x4 transformations for the bricks in the level, or an empty array if parameters are invalid.</returns>
    public static Matrix4x4[] GenerateLevelMatrices(
        int level,
        int bricksPerLevel,
        float radius,
        float circumference,
        float brickDepth,
        float levelBrickHeight,
        float brickWidthVariation,
        float levelStartY,
        int levelSeed,
        int levelRotationSeed)
    {
        if (bricksPerLevel <= 0 || radius <= 0 || circumference <= 0 || levelBrickHeight <= 0)
        {
            return System.Array.Empty<Matrix4x4>(); // Return empty array for invalid inputs
        }

        // Calculate level-specific rotation offset
        float rotationOffset = CalculateDeterministicLevelRotationOffset(levelRotationSeed);

        // Generate and normalize brick widths for this level
        List<float> widths = GenerateRandomDistances(bricksPerLevel, circumference, brickWidthVariation, levelSeed);
        NormalizeDistances(widths, circumference);

        List<Matrix4x4> matrices = new List<Matrix4x4>(bricksPerLevel);
        float currentAngle = rotationOffset; // Start angle includes level offset

        for (int i = 0; i < widths.Count; i++)
        {
            float brickWidth = widths[i];
            // Skip bricks that are too narrow after normalization
            if (brickWidth <= MIN_BRICK_WIDTH_THRESHOLD) continue;

            // Calculate angle spanned by this brick's width
            // Angle = ArcLength / Radius
            float halfArcAngle = (brickWidth / 2f) / radius;

            // Position the brick at the center of its arc segment
            float centerAngle = currentAngle + halfArcAngle;
            centerAngle %= (2f * Mathf.PI); // Keep angle within 0 to 2PI

            // Calculate position in world space
            float x = Mathf.Cos(centerAngle) * radius;
            float z = Mathf.Sin(centerAngle) * radius;
            float yPos = levelStartY + levelBrickHeight / 2.0f; // Center brick vertically
            Vector3 position = new Vector3(x, yPos, z);

            // Calculate rotation to face outwards from the center
            // Look direction is from origin towards the point on the circle
            Vector3 lookDirection = new Vector3(x, 0, z).normalized;
            if (lookDirection == Vector3.zero) lookDirection = Vector3.forward; // Handle case at origin (shouldn't happen with radius > 0)
            Quaternion rotation = Quaternion.LookRotation(lookDirection, Vector3.up);

            // Define scale based on calculated dimensions
            Vector3 scale = new Vector3(brickWidth, levelBrickHeight, brickDepth);

            // Create the transformation matrix
            Matrix4x4 matrix = Matrix4x4.TRS(position, rotation, scale);
            matrices.Add(matrix);

            // Advance the angle for the next brick (move by the full width)
            currentAngle += halfArcAngle * 2f;
        }

        return matrices.ToArray();
    }

    /// <summary>
    /// Calculates a deterministic height for bricks within a specific level.
    /// </summary>
    /// <param name="minBrickHeight">Minimum allowed height.</param>
    /// <param name="maxBrickHeight">Maximum allowed height.</param>
    /// <param name="heightSeed">Seed for deterministic height calculation.</param>
    /// <returns>The calculated height for bricks in this level.</returns>
    public static float CalculateDeterministicLevelBrickHeight(float minBrickHeight, float maxBrickHeight, int heightSeed)
    {
        // Initialize RNG with a seed specific to this level's height
        Random.InitState(heightSeed);

        // Ensure max is not less than min
        if (minBrickHeight > maxBrickHeight) maxBrickHeight = minBrickHeight;

        // Return min height if range is negligible, otherwise random within range
        if (Mathf.Approximately(minBrickHeight, maxBrickHeight)) return minBrickHeight;
        return Random.Range(minBrickHeight, maxBrickHeight);
    }

    // --- Private Static Helper Methods ---

    /// <summary>
    /// Generates a list of pseudo-random distances (representing brick widths).
    /// </summary>
    /// <param name="brickCount">Number of bricks for the level.</param>
    /// <param name="circumference">Total circumference to fill.</param>
    /// <param name="widthVariation">Allowed width variation percentage.</param>
    /// <param name="randomSeed">Seed for deterministic random generation.</param>
    /// <returns>A list of calculated brick widths.</returns>
    private static List<float> GenerateRandomDistances(int brickCount, float circumference, float widthVariation, int randomSeed)
    {
        // Initialize RNG with a seed specific to this level for deterministic results
        Random.InitState(randomSeed);
        List<float> distances = new List<float>(brickCount);
        if (brickCount <= 0 || circumference <= 0) return distances; // Handle invalid input

        // Calculate average and range for randomized widths
        float avgDistance = circumference / brickCount;
        float minDistance = avgDistance * (1.0f - widthVariation);
        float maxDistance = avgDistance * (1.0f + widthVariation);

        // Ensure minDistance is not negative (can happen if widthVariation is high)
        minDistance = Mathf.Max(0f, minDistance);

        // Generate random widths within the calculated range
        float totalWidth = 0f;
        for (int i = 0; i < brickCount; i++)
        {
            float width = Random.Range(minDistance, maxDistance);
            distances.Add(width);
            totalWidth += width;
        }

        // Basic check to avoid division by zero if totalWidth ends up being ~0
        if (totalWidth <= Mathf.Epsilon)
        {
            // Handle degenerate case: Assign equal widths if randomization failed
            distances.Clear();
            float equalWidth = circumference / brickCount;
            for (int i = 0; i < brickCount; i++)
            {
                distances.Add(equalWidth);
            }
        }

        return distances;
    }

    /// <summary>
    /// Normalizes a list of distances (brick widths) so that their sum equals a target sum (circumference).
    /// Modifies the input list directly.
    /// </summary>
    /// <param name="distances">The list of distances to normalize.</param>
    /// <param name="targetSum">The desired sum of the distances.</param>
    private static void NormalizeDistances(List<float> distances, float targetSum)
    {
        if (distances == null || distances.Count == 0 || targetSum <= 0) return;

        // Calculate the current sum of distances
        float currentSum = 0f;
        foreach (float d in distances) { currentSum += d; }

        // Avoid division by zero or normalizing if sum is already correct or invalid
        if (currentSum <= Mathf.Epsilon || Mathf.Approximately(currentSum, targetSum)) return;

        // Calculate scaling factor and apply it to each distance
        float scale = targetSum / currentSum;
        for (int i = 0; i < distances.Count; i++)
        {
            distances[i] *= scale;
        }
    }

    /// <summary>
    /// Calculates a deterministic rotation offset (in radians) for a specific level's rotation seed.
    /// </summary>
    /// <param name="rotationSeed">Seed for deterministic rotation generation.</param>
    /// <returns>The rotation offset in radians.</returns>
    private static float CalculateDeterministicLevelRotationOffset(int rotationSeed)
    {
        // Initialize RNG with the specific seed for rotation
        Random.InitState(rotationSeed);
        // Return a random rotation offset around the Y axis (0 to 360 degrees in radians)
        return Random.Range(0f, 2f * Mathf.PI);
    }
}