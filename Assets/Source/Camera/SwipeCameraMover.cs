using Unity.VisualScripting;
using UnityEngine;

/// <summary>
/// Moves the camera vertically based on vertical touch swipes,
/// and rotates it horizontally around a target object based on horizontal touch swipes.
/// Uses single-finger input.
/// </summary>
public class SwipeCameraMover : MonoBehaviour
{
    [Header("Target Object")]
    [Tooltip("The object the camera should orbit around horizontally. Assign this in the Inspector.")]
    [SerializeField] private Transform targetObject;

    [Header("Camera Settings")]
    [Tooltip("The camera to move and rotate. If null, defaults to Camera.main.")]
    [SerializeField] private Camera targetCamera;

    [Header("Movement Settings")]
    [Tooltip("How sensitive the vertical movement is to the swipe speed.")]
    [SerializeField] private float moveSensitivity = 0.1f;

    [Tooltip("How quickly the camera moves to the target position.")]
    [SerializeField] private float moveSpeed = 10f;

    [Tooltip("How sensitive the horizontal rotation is to the swipe speed.")]
    [SerializeField] private float rotationSensitivity = 0.5f; 

    [Header("Clamping")]
    [Tooltip("Optional: Minimum Y position the camera can reach.")]
    [SerializeField] private bool clampYPosition = false;
    [SerializeField] private float minYPosition = 0f;
    private float maxYPosition = 100f;

    [SerializeField] [Tooltip("Optional: Maximum Y offset above maxY the camera can reach.")]
    private float maxYOffset = 0f;

    private bool startupAnimation = true;
    private GameObject _targetYPosition;


    void Awake()
    {
        Tower tower = FindFirstObjectByType<Tower>(); // Find the first Tower object in the scene
        TowerGeometryGenerator towerGeometryGenerator = new TowerGeometryGenerator(tower);
        if (towerGeometryGenerator != null)
        {
            maxYPosition = towerGeometryGenerator.GetTopLevelHeight() + maxYOffset; // Set max Y position based on tower height
            Debug.Log("Max camera Y Position: " + maxYPosition);
        }
        // Default to the main camera if none is assigned
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        if (targetCamera == null)
        {
            Debug.LogError("SwipeCameraMover: No camera found or assigned! Disabling script.", this);
            enabled = false;
            return; // Stop execution if no camera
        }

        if (targetObject == null)
        {
            Debug.LogError("SwipeCameraMover: No target object assigned! Rotation will not work. Disabling script.", this);
            enabled = false;
            return; // Stop execution if no target for rotation
        }

        if (maxYPosition < minYPosition) {
            minYPosition = maxYPosition; // Ensure min Y position is not greater than max Y position
            Debug.LogWarning("SwipeCameraMover: minYPosition is greater than maxYPosition. Setting minYPosition to maxYPosition.", this);
        }
        _targetYPosition = new GameObject("TargetYPosition"); // Create a new GameObject to hold the Y position
        Vector3 newCamPosition = targetCamera.transform.position;
        newCamPosition.y = maxYPosition; // Set the camera's Y position to the max Y position
        _targetYPosition.transform.position = newCamPosition; // Initialize target Y position

        newCamPosition.y = minYPosition; // Set the camera's Y position to the min Y position
        targetCamera.transform.position = newCamPosition; // Update camera position



    }

    void Update()
    {
     if(startupAnimation)
        {
            StartupUpdate(); // Call the startup update method
        }
        else
        {
            MainUpdate(); // Call the main update method
        }
    }
    void StartupUpdate()
    {
        // Slowly move the camera to the target Y position
        Vector3 targetPosition = _targetYPosition.transform.position;
        targetPosition.x = targetCamera.transform.position.x; // Keep the X position unchanged
        targetPosition.z = targetCamera.transform.position.z; // Keep the Z position unchanged
        targetCamera.transform.position = Vector3.Lerp(targetCamera.transform.position, targetPosition, Time.deltaTime * moveSpeed); // Smoothly move to the new position
        // Check if the camera has reached the target position
        if (Vector3.Distance(targetCamera.transform.position, targetPosition) < 0.1f)
        {
            // If the camera is close enough to the target position, transition to the main update loop
            TransitionToMainUpdate();
        }
    }

    void TransitionToMainUpdate()
    {
        // Transition from startup animation to main update loop
        startupAnimation = false;

    }
    void MainUpdate()
    {
        // Ensure we have a target and camera before proceeding
        if (targetObject == null || targetCamera == null) return;

        // Check if there is exactly one touch on the screen
        if (Input.touchCount == 1)
        {
            Touch touch = Input.GetTouch(0); // Get the first touch

            // Check if the touch phase is 'Moved' (i.e., the finger is dragging)
            if (touch.phase == TouchPhase.Moved)
            {
                // --- Calculate Horizontal Rotation ---
                // touch.deltaPosition.x gives the horizontal change since the last frame
                // Negative sign rotates naturally (swipe right orbits right) around world up axis
                float horizontalAngle = -touch.deltaPosition.x * rotationSensitivity * Time.deltaTime;

                // Rotate the camera around the target's position, using world UP axis
                targetCamera.transform.RotateAround(targetObject.position, Vector3.up, horizontalAngle);

                // --- Calculate Vertical Movement ---
                // touch.deltaPosition.y gives the vertical change since the last frame
                // Negative sign moves camera down when swiping up, and vice-versa (natural feel)
                float verticalMovement = -touch.deltaPosition.y * moveSensitivity * Time.deltaTime;

                // --- Apply Vertical Movement ---
                // Move the camera target position in world space
                _targetYPosition.transform.Translate(0f, verticalMovement, 0f, Space.World);

                // --- Optional Vertical Clamping ---
                if (clampYPosition)
                {
                    Vector3 currentPosition = _targetYPosition.transform.position;
                    currentPosition.y = Mathf.Clamp(currentPosition.y, minYPosition, maxYPosition); // Clamp Y position
                    _targetYPosition.transform.position = currentPosition;
                }
            }
        }

        // Update the camera's position to follow the target Y position using lerp for smoothness
        Vector3 targetPosition = _targetYPosition.transform.position;
        targetPosition.x = targetCamera.transform.position.x; // Keep the X position unchanged
        targetPosition.z = targetCamera.transform.position.z; // Keep the Z position unchanged
        targetCamera.transform.position = Vector3.Lerp(targetCamera.transform.position, targetPosition, Time.deltaTime * moveSpeed); // Smoothly move to the new position
    }
}