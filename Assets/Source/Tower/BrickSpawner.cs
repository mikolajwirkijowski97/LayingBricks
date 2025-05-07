using UnityEngine;
using System.Collections.Generic;
using DG.Tweening;
using System.Linq;

/// <summary>
/// This class takes in Matrix4x4 arrays and spawns GameObjects at the specified positions and rotations.
/// It is used to handle the spawning of bricks that are dynamic and can't be rendered using instancing.
/// (I mean they can, but its much easier to just spawn them and animate them with DOTween)
/// </summary>
public class BrickSpawner : MonoBehaviour
{
    [SerializeField]
    [Tooltip("The smoke puff prefab to get instantiated on animation end.")]
    private GameObject _smokePuffPrefab; // Prefab of the smoke puff to instantiate

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
    
    public GameObject BrickPrefab
    {
        get => _brickPrefab;
        set => _brickPrefab = value;
    }

    // --- Private Fields ---
    private TowerGeometryGenerator _geometryGenerator; // Instance to handle geometry calculation and caching

    private void InitializeGeometryGenerator()
    {
        Debug.Log("ReInitializing Geometry Generator in BrickSpawner", this);
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

    void Awake()
    {
     TowerData.OnParametersChanged += InitializeGeometryGenerator; // Subscribe to tower data changes   
    }
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        InitializeGeometryGenerator(); 
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void Add10Bricks()
    {
        int bricksToAdd = 42; // Number of bricks to add
        AddBricks(bricksToAdd); // Call the method to add bricks
    }

    public void AddBricks(int count){
        if(count <= 0)
        {
            if(count < 0) Debug.LogWarning("Count must be greater than zero!", this);
            return; // No bricks to add
        }
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
        // Debug log all used parameters that havent yet been logged
        Debug.Log($"Bricks to add: {count}", this);
        Debug.Log($"Bricks missing on last level: {bricksMissingOnLast}", this);
        Debug.Log($"Bricks on last level: {bricksOnLastLevel}", this);
        Debug.Log($"Starting level: {startingLevel}", this);
        Debug.Log($"Full levels to add: {fullLevelsToAdd}", this);
        Debug.Log($"Remaining bricks to add: {remainingBricks}", this);
        Debug.Log($"New bricks transforms count: {newBricksTransforms.Count}", this);
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
        const float DELAY_TIME = 3.0f; // Total time over which delays are spread
        const float ROTATION_SPEED = 8.0f; // Speed of rotation for the bricks

        float animationDuration = 1.2f; // Duration for move, scale, and shake


        int spawnCount = matrices.Length; // Number of bricks to spawn
        int maxSounds = 100;


        // Loop through each matrix in the array and apply TRS (Translation, Rotation, Scale) to spawn bricks
        for (int i = 0; i < spawnCount; i++) // Use index for potential future needs
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
            TrailRenderer trail = brickGO.GetComponent<TrailRenderer>();
            trail.startWidth = targetScale.x * 0.6f; // Set the start width of the trail

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
            var brickSequence = DOTween.Sequence();

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
            int iValue = i;
            // Optional: Set target for easier debugging or potential cleanup later
            brickSequence.SetTarget(brickGO);
            brickSequence.onComplete = () => {
                //Create a puff of smoke effect at the brick's position
                Vector3 puffSpawnPosition = new Vector3(position.x,
                    position.y - 0.2f, position.z);

                var spp = Instantiate(_smokePuffPrefab, puffSpawnPosition, Quaternion.identity);
                bool isLastBrick = iValue == i - 1; // Check if this is the last brick

                // Make last particle explosion bigger
                if (iValue == i-1)
                {
                    var particleSystem = spp.GetComponent<ParticleSystem>();
                    var main = particleSystem.main;
                    // Set the start size to a range between 0.08 and 0.15
                    main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.15f);
            
                }
                // Process Audio On Complete
                // If count of bricks is bigger than maxSounds, skip some sounds
                // So that the maxSounds sounds are split evenly over all bricks
                // but only the max amount of sounds are played
                float t = iValue / (float)spawnCount; // Calculate the pitch based on the brick index and total spawn count
                if (spawnCount > maxSounds && maxSounds > 0) // Check if we need to limit sounds and if maxSounds is valid
                {
                    // Calculate the interval: play a sound every 'skipInterval' bricks
                    // Use float division for accuracy, then floor to get the integer interval
                    int skipInterval = Mathf.FloorToInt((float)spawnCount / maxSounds);

                    // Ensure interval is at least 1 (otherwise no sounds might play if maxSounds is very small)
                    if (skipInterval < 1) skipInterval = 1;

                    // Only play sound if the current brick index is a multiple of the interval
                    // (or if it's the very last brick, always play its sound)
                    if (iValue % skipInterval != 0 && !isLastBrick)
                    {
                        return;
                    }
                    t = ((float)iValue / skipInterval) / maxSounds; // Calculate the pitch based on the brick index and maxSounds
                }
                else if (maxSounds <= 0) // Handle edge case where maxSounds is zero or negative
                {
                    return;
                }

                var audioSource = spp.GetComponent<AudioSource>();
                audioSource.time = 0.2f;
                audioSource.pitch = isLastBrick ? 1f : Mathf.Lerp(0.8f, 3.0f, t); // Set pitch
                audioSource.volume = isLastBrick ? 0.5f : 0.3f; // Set volume

                audioSource.Play(); // Play the sound
                Debug.Log("The pitch of the smoke puff is: " + spp.GetComponent<AudioSource>().pitch, this);
                Destroy(spp, 2.0f); // Destroy the smoke puff after 2 seconds

            };

            // Update cumulative delay for the next brick's calculation
             if (matrices.Length > 0) // Avoid division by zero if matrices array is empty
             {
                 cumDelay += DELAY_TIME / matrices.Length; // Increment based on total time and number of bricks
             }
        }
    }
}
