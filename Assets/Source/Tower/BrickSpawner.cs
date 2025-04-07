using UnityEngine;
using System.Collections.Generic;
using UnityEditor.Rendering.Universal;
/// <summary>
/// This class takes in Matrix4x4 arrays and spawns GameObjects at the specified positions and rotations.
/// It is used to handle the spawning of bricks that are dynamic and can't be rendered using instancing.
/// </summary>

public class BrickSpawner : MonoBehaviour
{
    // Referenc to tower data object
    [SerializeField]
    [Tooltip("The tower data object.")]
    private Tower _towerData; // Reference to the tower data object
    public Tower TowerData
    {
        get => _towerData;
        set => _towerData = value;
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

    private Queue<AnimatedBrick> _animationQueue; // Queue for brick falling animations
    private List<GameObject> _bricks; // List of spawned bricks
    
    public GameObject BrickPrefab
    {
        get => _brickPrefab;
        set => _brickPrefab = value;
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    void AddNewBrick(int n){
        int level = TowerData.Height - 1; // Get the current level of the tower
        //Matrix4x4[] newBricksTransforms = TowerGeometryGenerator.GenerateLevelMatrices();
    }

    void CreateAnimatedBricks(Matrix4x4[] matrices)
    {
        // Loop through each matrix in the array and apply TRS (Translation, Rotation, Scale) to spawn bricks
        foreach (Matrix4x4 matrix in matrices)
        {
            // Extract the position and rotation from the matrix
            Vector3 position = matrix.GetColumn(3);
            
            Quaternion rotation = Quaternion.LookRotation(matrix.GetColumn(2), matrix.GetColumn(1));
            // Extract the scale from the matrix and apply it to the brick prefab
            // TODO: Handle scale
            
            // Instantiate the brick prefab at the specified position and rotation
            float HEIGHT_OFFSET = 5.0f; // Offset to fall from
            Vector3 startPosition = position + new Vector3(0, HEIGHT_OFFSET, 0); // Start the position above the target
            AnimatedBrick brick = new AnimatedBrick(startPosition, position, 0.0f, Instantiate(_brickPrefab, startPosition, rotation));
            _animationQueue.Enqueue(brick); // Add the brick to the animation queue
        }
    }
}
