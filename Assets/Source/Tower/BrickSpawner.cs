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

    void Add10Bricks(){
        int level = TowerData.Height - 1; // Get the current level of the tower
        Matrix4x4[] newBricksTransforms = _geometryGenerator.GetOrGenerateLevelMatrices(level); // Get the transforms for the new bricks
        if (newBricksTransforms != null && newBricksTransforms.Length > 0)
        {
            CreateAnimatedBricks(newBricksTransforms); // Create animated bricks using the transforms
        }
        else
        {
            Debug.LogWarning("No transforms available for the new bricks.", this);
        }
    }

    void CreateAnimatedBricks(Matrix4x4[] matrices)
    {
        float cumDelay = 0.0f;
        // Loop through each matrix in the array and apply TRS (Translation, Rotation, Scale) to spawn bricks
        foreach (Matrix4x4 matrix in matrices)
        {
            // Extract the position and rotation from the matrix
            Vector3 position = matrix.GetColumn(3);
            
            Quaternion rotation = Quaternion.LookRotation(matrix.GetColumn(2), matrix.GetColumn(1));
            
            
            // Instantiate the brick prefab at the specified position and rotation
            float HEIGHT_OFFSET = 20.0f; // Offset to fall from
            Vector3 startPosition = position + new Vector3(0, HEIGHT_OFFSET, 0); // Start the position above the target
            AnimatedBrick brick = new AnimatedBrick(startPosition, position, 0.0f, Instantiate(_brickPrefab, startPosition, rotation));
            // Extract the scale from the matrix and apply it to the brick prefab
            Vector3 scale = new Vector3(
                matrix.GetColumn(0).magnitude, // Represents the scale along the brick's local X axis
                matrix.GetColumn(1).magnitude, // Represents the scale along the brick's local Y axis
                matrix.GetColumn(2).magnitude  // Represents the scale along the brick's local Z axis
            );
            brick.brick.transform.localScale = scale; // Apply the scale to the brick prefab

            const float DELAY_TIME = 10.0f; 
            const int STREAM_COUNT = 3; // Number of streams of bricks falling
            float processed_delay = Mathf.Repeat(cumDelay, DELAY_TIME/STREAM_COUNT);

            brick.brick.transform.DOMove(position, 1.0f).SetEase(Ease.InSine).SetDelay(processed_delay); // Animate the brick to fall to its target position
            _animationQueue.Enqueue(brick); // Add the brick to the animation queue
            
            brick.brick.transform.DOShakeRotation(1.0f, new Vector3(45f, 45f, 45f))
            .SetDelay(processed_delay); // Start tumbling when it should start falling 

            cumDelay += DELAY_TIME/matrices.Count();
        }
    }
}
