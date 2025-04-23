using Unity.VisualScripting;
using UnityEngine;

/// <summary>
/// Moves the camera vertically based on vertical touch swipes,
/// rotates it horizontally around a target object based on horizontal touch swipes,
/// and zooms in/out based on pinch gestures or mouse scroll wheel.
/// Uses single-finger input for swipe/rotate, two-finger for pinch.
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
    [Tooltip("How quickly the camera moves vertically to the target position.")]
    [SerializeField] private float moveSpeed = 10f;
    [Tooltip("How sensitive the horizontal rotation is to the swipe speed.")]
    [SerializeField] private float rotationSensitivity = 0.5f;

    [Header("Clamping")]
    [Tooltip("Optional: Clamp Y position between min and max values.")]
    [SerializeField] private bool clampYPosition = false;
    [SerializeField] private float minYPosition = 0f;
    private float maxYPosition = 100f; // Will be calculated from Tower
    [SerializeField] [Tooltip("Optional: Maximum Y offset above calculated tower height.")]
    private float maxYOffset = 0f;

    [SerializeField] private Tower towerData;

    // Internal State
    private bool startupAnimation = true;
    private GameObject _targetYPositionObject; // Renamed for clarity


    void Awake()
    {
        towerData.OnParametersChanged += UpdateInternals; // Subscribe to tower data changes
        
        UpdateInternals(); // Initial setup

    }

    void UpdateInternals() {

        startupAnimation = true; // Reset startup animation flag
        // --- Tower Data Check ---
        if (towerData != null) {
            TowerGeometryGenerator towerGeometryGenerator = new TowerGeometryGenerator(towerData); // Assuming constructor exists
            if (towerGeometryGenerator != null)
            {
                maxYPosition = towerGeometryGenerator.GetTopLevelHeight() + maxYOffset;
                Debug.Log("Max camera Y Position: " + maxYPosition);
            }
        } else {
             Debug.LogWarning("SwipeCameraMover: No Tower found in scene. Max Y position not calculated.", this);
        }


        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        if (targetCamera == null)
        {
            Debug.LogError("SwipeCameraMover: No camera found or assigned! Disabling script.", this);
            enabled = false;
            return;
        }

        if (targetObject == null)
        {
            Debug.LogError("SwipeCameraMover: No target object assigned! Rotation/Zoom will not work correctly. Disabling script.", this);
            enabled = false;
            return;
        }

        // --- Sanity Checks ---
        if (maxYPosition < minYPosition)
        {
            minYPosition = maxYPosition;
            Debug.LogWarning("SwipeCameraMover: minYPosition was greater than maxYPosition. Setting minYPosition to maxYPosition.", this);
        }
        // --- Initialize Target Y GameObject ---
        _targetYPositionObject = new GameObject("TargetYPosition_Internal"); // More descriptive name
        Vector3 initialCamPosition = targetCamera.transform.position;


        // Set target Y for startup animation
        Vector3 targetYPos = initialCamPosition;
        targetYPos.y = maxYPosition;
        _targetYPositionObject.transform.position = targetYPos; // Initialize target Y position object


        // Ensure initial Y position is clamped if needed
        if (clampYPosition)
        {
            Vector3 pos = targetCamera.transform.position;
            pos.y = Mathf.Clamp(pos.y, minYPosition, maxYPosition);
            targetCamera.transform.position = pos;

            pos = _targetYPositionObject.transform.position;
            pos.y = Mathf.Clamp(pos.y, minYPosition, maxYPosition);
           _targetYPositionObject.transform.position = pos;
        }
    }

    void Update()
    {
        if (startupAnimation)
        {
            StartupUpdate();
        }
        else
        {
            MainUpdate();
        }
    }

    void StartupUpdate()
    {
        // Slowly move the camera to the target Y position (keeping X/Z)
        Vector3 targetPosition = targetCamera.transform.position; // Start with current X/Z
        targetPosition.y = _targetYPositionObject.transform.position.y; // Use target Y

        targetCamera.transform.position = Vector3.Lerp(targetCamera.transform.position, targetPosition, Time.deltaTime * moveSpeed);

        // Check if the camera has reached the target Y position (focus on Y)
        if (Mathf.Abs(targetCamera.transform.position.y - targetPosition.y) < 0.1f)
        {
            TransitionToMainUpdate();
        }
    }

    void TransitionToMainUpdate()
    {
        startupAnimation = false;
        // Set the target Y object's position to match the camera's final startup Y
        // This prevents a jump when the first vertical swipe occurs.
        Vector3 currentTargetPos = _targetYPositionObject.transform.position;
        currentTargetPos.y = targetCamera.transform.position.y;
        _targetYPositionObject.transform.position = currentTargetPos;
         Debug.Log("SwipeCameraMover: Startup complete. Transitioning to main update.");
    }

    void MainUpdate()
    {
        // Ensure we have a target and camera before proceeding
        if (targetObject == null || targetCamera == null) return;

        // Handle Touch Input (Mobile)
        HandleTouchInput();

        // Apply smooth vertical movement (only affects Y)
        ApplySmoothVerticalMovement();
    }

    void HandleTouchInput()
    {
        // --- Swipe Rotation and Vertical Movement Logic ---
        if (Input.touchCount == 1)
        {
            Touch touch = Input.GetTouch(0);

            if (touch.phase == TouchPhase.Moved)
            {
                // --- Calculate Horizontal Rotation ---
                float horizontalAngle = -touch.deltaPosition.x * rotationSensitivity * Time.deltaTime;
                targetCamera.transform.RotateAround(targetObject.position, Vector3.up, horizontalAngle);

                // --- Calculate Vertical Movement Target ---
                // Move the target Y position, not the camera directly
                float verticalMovement = -touch.deltaPosition.y * moveSensitivity * Time.deltaTime;
                _targetYPositionObject.transform.Translate(0f, verticalMovement, 0f, Space.World);

                // Clamp the target Y position if needed
                if (clampYPosition)
                {
                    Vector3 currentTargetPos = _targetYPositionObject.transform.position;
                    currentTargetPos.y = Mathf.Clamp(currentTargetPos.y, minYPosition, maxYPosition);
                    _targetYPositionObject.transform.position = currentTargetPos;
                }
            }
        }
    }

    /// <summary>
    /// Smoothly moves the camera's Y position towards the target Y position.
    /// Leaves X and Z untouched to allow for rotation and zoom.
    /// </summary>
    void ApplySmoothVerticalMovement()
    {
        // Update the camera's Y position to follow the target Y position using lerp for smoothness
        Vector3 currentPos = targetCamera.transform.position;
        float targetY = _targetYPositionObject.transform.position.y;

        // Only lerp if the difference is noticeable to avoid unnecessary calculations
        if (Mathf.Abs(currentPos.y - targetY) > 0.01f)
        {
            float newY = Mathf.Lerp(currentPos.y, targetY, Time.deltaTime * moveSpeed);
            targetCamera.transform.position = new Vector3(currentPos.x, newY, currentPos.z);
        }
        // Optional: Snap directly if very close to prevent endless small lerping
        // else if (Mathf.Abs(currentPos.y - targetY) > 0f) {
        //     targetCamera.transform.position = new Vector3(currentPos.x, targetY, currentPos.z);
        // }
    }

     void OnDestroy()
    {
        // Clean up the internally created GameObject when this script is destroyed
        if (_targetYPositionObject != null)
        {
            Destroy(_targetYPositionObject);
        }
    }
}