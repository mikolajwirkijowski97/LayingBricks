using UnityEngine;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine.UI;
using System.Linq;
using System;
/// <summary>
/// This class takes in Matrix4x4 arrays and spawns GameObjects at the specified positions and rotations.
/// It is used to handle the spawning of bricks that are dynamic and can't be rendered using instancing.
/// </summary>

public class BrickSpawner : MonoBehaviour
{

    public Button SpawnButton; // Button to trigger the spawning of bricks

    // Referenc to tower data object
    [SerializeField]
    [Tooltip("The tower data object.")]
    private Tower _towerData; // Reference to the tower data object
    public Tower TowerData
    {
        get { 
            return _towerData; 
            }
        set {
            _towerData = value;
            if (_towerData != null)
            {
                InitializeGeometryGenerator(); // Initialize the geometry generator with the tower data
                Debug.Log($"Tower data set: {_towerData}", this);
            }
            else
            {
                _geometryGenerator = null; // Tower is null, no generator
            }
        } 
    }

    private class AnimatedBrick
    {
        public Vector3 startPosition; // Starting position of the brick
        public Vector3 targetPosition; // Target position of the brick
        public float delay; // Delay before the animation starts
        public GameObject brick; // The brick GameObject to animate
    
        public AnimatedBrick(Vector3 start, Vector3 target, float delay, GameObject brick)
        {
            this.startPosition = start;
            this.targetPosition = target;
            this.delay = delay;
            this.brick = brick;
        }
    }

    [SerializeField]
    [Tooltip("The prefab of the brick to spawn.")]
    private GameObject _brickPrefab; // Prefab of the brick to spawn

    private Queue<AnimatedBrick> _animationQueue = new Queue<AnimatedBrick>(); // Queue for brick falling animations
    private List<GameObject> _bricks = new List<GameObject>(); // List of spawned bricks
    
    public GameObject BrickPrefab
    {
        get => _brickPrefab;
        set => _brickPrefab = value;
    }

    // --- Private Fields ---
    private TowerGeometryGenerator _geometryGenerator; // Instance to handle geometry calculation and caching

    private void InitializeGeometryGenerator()
    {

         if (_towerData != null)
         {
             try {
                  _geometryGenerator = new TowerGeometryGenerator(_towerData);
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
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        InitializeGeometryGenerator(); 
        SpawnButton.onClick.AddListener(Add10Bricks); // Add listener to the button to spawn bricks
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    void Add10Bricks()
    {
        int bricksToAdd = 11; // Number of bricks to add
        AddBricks(bricksToAdd); // Call the method to add bricks
    }
    void AddBricks(int count){
        Debug.Log($"Adding {count} bricks to the tower.", this);
        Debug.Log($"Tower height: {TowerData.Height}", this);
        Debug.Log($"Tower total bricks: {TowerData.TotalBricks}", this);

        int leftToAdd = count; // Number of bricks left to add
        int bricksMissingOnLast = TowerData.BricksPerLevel - TowerData.GetBricksOnLastLevel(); // Get the number of bricks on the last level
        int bricksOnLastLevel = TowerData.GetBricksOnLastLevel(); // Get the number of bricks on the last level
        int startingLevel = TowerData.Height; // Get the starting level of the tower
        TowerData.AddBricks(count); // Update the total number of bricks in the tower

        List<Matrix4x4> newBricksTransforms = new List<Matrix4x4>(); // Initialize the list for new bricks transforms
        if(bricksMissingOnLast > 0)
        {
            int level = startingLevel - 1; // Get the current level of the tower
            int bricksToAdd = Mathf.Min(count, bricksMissingOnLast); // Calculate the number of bricks to add
            newBricksTransforms.AddRange(_geometryGenerator.GetOrGenerateLevelMatrices(level).Skip(bricksOnLastLevel).Take(bricksToAdd)); // Get the transforms for the new bricks
            leftToAdd -= bricksToAdd; // Decrease the number of bricks left to add
        }
        
        int fullLevelsToAdd = leftToAdd / TowerData.BricksPerLevel; // Calculate the number of full levels to add
        for(int i = 0; i < fullLevelsToAdd; i++)
        {
            int level = startingLevel + i; // Get the current level of the tower
            newBricksTransforms.AddRange(_geometryGenerator.GetOrGenerateLevelMatrices(level)); // Get the transforms for the new bricks
        }

        int remainingBricks = leftToAdd % TowerData.BricksPerLevel; // Calculate the number of remaining bricks to add
        if(remainingBricks > 0)
        {
            int level = startingLevel + fullLevelsToAdd; // Get the current level of the tower
            newBricksTransforms.AddRange(_geometryGenerator.GetOrGenerateLevelMatrices(level).Take(remainingBricks)); // Get the transforms for the new bricks
        }


        if (newBricksTransforms != null && newBricksTransforms.Any())
        {
            CreateAnimatedBricks(newBricksTransforms.ToArray()); // Create animated bricks using the transforms
        }
        else
        {
            Debug.LogWarning("No transforms available for the new bricks.", this);
        }

    }

    // Method with combined tweens using DOTween Sequence
    void CreateAnimatedBricks(Matrix4x4[] matrices)
    {
        if (_brickPrefab == null)
        {
            Debug.LogError("Brick Prefab is not assigned!", this);
            return;
        }

        float cumDelay = 0.0f;
        const float DELAY_TIME = 5.0f; // Total time over which delays are spread
        const float ROTATION_SPEED = 8.0f; // Speed of rotation for the bricks

        float animationDuration = 1.2f; // Duration for move, scale, and shake

        // Loop through each matrix in the array and apply TRS (Translation, Rotation, Scale) to spawn bricks
        for (int i = 0; i < matrices.Length; i++) // Use index for potential future needs
        {
            Matrix4x4 matrix = matrices[i];

            // Extract the position and rotation from the matrix
            Vector3 position = matrix.GetColumn(3);
            Quaternion rotation = Quaternion.LookRotation(matrix.GetColumn(2), matrix.GetColumn(1));

            // Extract the target scale from the matrix
            Vector3 targetScale = new Vector3(
                matrix.GetColumn(0).magnitude, // Represents the scale along the brick's local X axis
                matrix.GetColumn(1).magnitude, // Represents the scale along the brick's local Y axis
                matrix.GetColumn(2).magnitude  // Represents the scale along the brick's local Z axis
            );

            // Instantiate the brick prefab slightly above the target position and rotated
            float HEIGHT_OFFSET = 10.0f; // Offset to fall from
            Vector3 startPosition = position + new Vector3(0, HEIGHT_OFFSET, 0);
            GameObject brickGO = Instantiate(_brickPrefab, startPosition, rotation);

            // Set the RandomSeed parameter of the brickGO material
            brickGO.GetComponent<Renderer>().material.SetFloat("_RandomSeed", UnityEngine.Random.Range(0.0f, 16.0f)); // Set random seed for the material

            // Set initial scale to zero for the animation
            brickGO.transform.localScale = targetScale * 0f; // Start with a small scale

            // Create an AnimatedBrick instance if you still need it for tracking/queueing
            // Note: The delay property in AnimatedBrick might become redundant if handled solely by the Sequence
            AnimatedBrick brick = new AnimatedBrick(startPosition, position, 0.0f, brickGO);
             _animationQueue.Enqueue(brick); // Keep if the queue is used elsewhere


            // --- Combine Tweens into a Sequence ---

            // Calculate the delay for this specific brick
            // This repeats the delay every 'delaySpread' seconds using 'cumDelay'
             float processed_delay = cumDelay;

            // Create the DOTween Sequence
            Sequence brickSequence = DOTween.Sequence();

            // 1. Apply the initial delay before any animation starts in the sequence
            //    Using SetDelay on the sequence applies it once at the beginning.
            brickSequence.SetDelay(processed_delay);

            // 3. Join the Scale animation to run concurrently with the Move animation
            //    Starts from the current scale (Vector3.zero) to targetScale
            brickSequence.Append(brickGO.transform.DOScale(targetScale, 0.3f)
                                        .SetEase(Ease.OutBack)); // Pop-in effect for scale

            // 2. Append the Move animation (starts after the sequence delay)
            brickSequence.Append(brickGO.transform.DOMove(position, animationDuration)
                                        .SetEase(Ease.InSine)); // Brick falls into place

            // 4. Join the Shake Rotation animation to also run concurrently
            brickSequence.Join(brickGO.transform.DOShakeRotation(animationDuration, Vector3.one*ROTATION_SPEED, 10, 90, false));
                                        // Parameters: duration, strength, vibrato, randomness, fadeout(false)

            // Optional: Set target for easier debugging or potential cleanup later
            brickSequence.SetTarget(brickGO);

            // Update cumulative delay for the next brick's calculation
             if (matrices.Length > 0) // Avoid division by zero if matrices array is empty
             {
                 cumDelay += DELAY_TIME / matrices.Length; // Increment based on total time and number of bricks
             }
        }
    }
}
