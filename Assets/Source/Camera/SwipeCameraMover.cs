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

    [Header("Zoom Settings")]
    [Tooltip("How sensitive the zoom is to the pinch gesture or scroll wheel.")]
    [SerializeField] private float zoomSensitivity = 0.02f; // Adjusted sensitivity for distance change
    [Tooltip("Minimum distance the camera can be from the target object.")]
    [SerializeField] private float minZoomDistance = 5f;
    [Tooltip("Maximum distance the camera can be from the target object.")]
    [SerializeField] private float maxZoomDistance = 50f;

    [Tooltip("How sensitive the zoom is to the mouse scroll wheel in the Editor.")]
    [SerializeField] private float editorZoomSensitivity = 1f; // Separate sensitivity for scroll wheel

    [Header("Clamping")]
    [Tooltip("Optional: Clamp Y position between min and max values.")]
    [SerializeField] private bool clampYPosition = false;
    [SerializeField] private float minYPosition = 0f;
    private float maxYPosition = 100f; // Will be calculated from Tower
    [SerializeField] [Tooltip("Optional: Maximum Y offset above calculated tower height.")]
    private float maxYOffset = 0f;

    // Internal State
    private bool startupAnimation = true;
    private GameObject _targetYPositionObject; // Renamed for clarity
    private float _initialPinchDistance;       // Stores distance between fingers at pinch start
    private float _initialZoomDistance;        // Stores camera distance at pinch start


    void Awake()
    {
        // --- Existing Awake Logic ---
        Tower tower = FindFirstObjectByType<Tower>();
        if (tower != null) {
            TowerGeometryGenerator towerGeometryGenerator = new TowerGeometryGenerator(tower); // Assuming constructor exists
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
        if (maxZoomDistance < minZoomDistance)
        {
            minZoomDistance = maxZoomDistance;
            Debug.LogWarning("SwipeCameraMover: minZoomDistance was greater than maxZoomDistance. Setting minZoomDistance to maxZoomDistance.", this);
        }

        // --- Initialize Target Y GameObject ---
        _targetYPositionObject = new GameObject("TargetYPosition_Internal"); // More descriptive name
        Vector3 initialCamPosition = targetCamera.transform.position;

        // Clamp initial camera distance within zoom limits
        float initialDistance = Vector3.Distance(initialCamPosition, targetObject.position);
        float clampedInitialDistance = Mathf.Clamp(initialDistance, minZoomDistance, maxZoomDistance);
        Vector3 directionToCamera = (initialCamPosition - targetObject.position).normalized;
        initialCamPosition = targetObject.position + directionToCamera * clampedInitialDistance;


        // Set target Y for startup animation
        Vector3 targetYPos = initialCamPosition;
        targetYPos.y = maxYPosition;
        _targetYPositionObject.transform.position = targetYPos; // Initialize target Y position object

        // Set initial camera Y for startup animation start
        initialCamPosition.y = minYPosition;
        targetCamera.transform.position = initialCamPosition; // Update camera start position

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

        HandleEditorInput();

        // Apply smooth vertical movement (only affects Y)
        ApplySmoothVerticalMovement();
    }

    void HandleTouchInput()
    {
        // --- Pinch Zoom Logic ---
        if (Input.touchCount == 2)
        {
            Touch touchZero = Input.GetTouch(0);
            Touch touchOne = Input.GetTouch(1);

            // Find the position in the previous frame of each touch.
            Vector2 touchZeroPrevPos = touchZero.position - touchZero.deltaPosition;
            Vector2 touchOnePrevPos = touchOne.position - touchOne.deltaPosition;

            // Find the magnitude of the vector (the distance) between the touches in each frame.
            float prevTouchDeltaMag = (touchZeroPrevPos - touchOnePrevPos).magnitude;
            float touchDeltaMag = (touchZero.position - touchOne.position).magnitude;

            // Find the difference in the distances between each frame.
            float deltaMagnitudeDiff = prevTouchDeltaMag - touchDeltaMag; // Inverted: Positive when spreading, negative when pinching

            // Calculate zoom amount (adjust sensitivity as needed)
            float zoomAmount = deltaMagnitudeDiff * zoomSensitivity * Time.deltaTime;

            // Apply zoom
            ApplyZoom(zoomAmount);

        }
        // --- Swipe Rotation and Vertical Movement Logic ---
        else if (Input.touchCount == 1)
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

    void HandleEditorInput()
    {
        // Simulate zoom with mouse scroll wheel
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.01f) // Check if scroll wheel moved
        {
            // Positive scroll zooms in (reduces distance), negative zooms out
            float zoomAmount = -scroll * editorZoomSensitivity; // Apply sensitivity
            ApplyZoom(zoomAmount);
        }

         // Optional: Simulate swipes with mouse drag (e.g., holding right mouse button)
        if (Input.GetMouseButton(1)) // Right mouse button held down
        {
             // --- Calculate Horizontal Rotation ---
            float mouseXDelta = Input.GetAxis("Mouse X");
            float horizontalAngle = -mouseXDelta * rotationSensitivity * 50f * Time.deltaTime; // Adjust multiplier for mouse sensitivity
            targetCamera.transform.RotateAround(targetObject.position, Vector3.up, horizontalAngle);

            // --- Calculate Vertical Movement Target ---
            float mouseYDelta = Input.GetAxis("Mouse Y");
            float verticalMovement = -mouseYDelta * moveSensitivity * 50f * Time.deltaTime; // Adjust multiplier for mouse sensitivity
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


    /// <summary>
    /// Applies zoom by moving the camera towards or away from the target object.
    /// </summary>
    /// <param name="amount">Positive value zooms out, negative zooms in.</param>
    void ApplyZoom(float amount)
    {
        // Calculate direction from target to camera
        Vector3 direction = (targetCamera.transform.position - targetObject.position);
        float currentDistance = direction.magnitude;
        direction.Normalize();

        // Calculate new distance, clamping it within bounds
        float newDistance = Mathf.Clamp(currentDistance + amount, minZoomDistance, maxZoomDistance);

        // Calculate the new camera position
        Vector3 newPosition = targetObject.position + direction * newDistance;

        // Only update if the new position is actually different (avoids jitter at limits)
        // Use a small epsilon for floating point comparison
        if(Vector3.Distance(targetCamera.transform.position, newPosition) > 0.001f)
        {
             targetCamera.transform.position = newPosition;
        }

        // Important: If zooming changes the camera's Y position significantly,
        // we might want the target Y position to follow it to prevent sudden jumps
        // when swiping vertically again. Let's update the target Y to match.
        Vector3 currentTargetPos = _targetYPositionObject.transform.position;
        currentTargetPos.y = targetCamera.transform.position.y;

        // Also clamp this target Y position if clamping is enabled
        if (clampYPosition)
        {
           currentTargetPos.y = Mathf.Clamp(currentTargetPos.y, minYPosition, maxYPosition);
        }
       _targetYPositionObject.transform.position = currentTargetPos;

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