using Unity.VisualScripting; // Can likely be removed if not used elsewhere
using UnityEngine;
using UnityEngine.EventSystems; // Required for checking UI interaction


/// <summary>
/// Moves the camera vertically based on vertical touch swipes,
/// rotates it horizontally around a target object based on horizontal touch swipes,
/// and zooms in/out based on pinch gestures or mouse scroll wheel.
/// Uses single-finger input for swipe/rotate, two-finger for pinch.
/// </summary>
public class SwipeCameraMover : MonoBehaviour
{
    [Header("Target Object")]
    [Tooltip("The object the camera should orbit around horizontally and zoom towards. Assign this in the Inspector.")]
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

    // --- NEW: Zoom Settings ---
    [Header("Zoom Settings")]
    [Tooltip("How sensitive the zoom is to pinch gestures or mouse scroll.")]
    [SerializeField] private float zoomSensitivity = 0.5f;
    [Tooltip("The minimum distance the camera can be from the target object.")]
    [SerializeField] private float minZoomDistance = 5f;
    [Tooltip("The maximum distance the camera can be from the target object.")]
    [SerializeField] private float maxZoomDistance = 50f;
    [Tooltip("Sensitivity multiplier for mouse scroll wheel zoom.")]
    [SerializeField] private float mouseZoomMultiplier = 10f; // Adjust for desired mouse scroll speed
    // --- End NEW ---

    [Header("Clamping")]
    [Tooltip("Optional: Clamp Y position between min and max values.")]
    [SerializeField] private bool clampYPosition = false;
    [SerializeField] private float minYPosition = 0f;
    private float maxYPosition = 100f; // Will be calculated from Tower
    [SerializeField] [Tooltip("Optional: Maximum Y offset above calculated tower height.")]
    private float maxYOffset = 0f;

    [Header("Dependencies")] // Grouped dependencies
    [SerializeField] private Tower towerData;

    // Internal State
    private bool startupAnimation = true;
    private GameObject _targetYPositionObject; // Renamed for clarity
    private Vector2 touchZeroPrevPos; // For pinch zoom calculation
    private Vector2 touchOnePrevPos;  // For pinch zoom calculation


    void Awake()
    {
        if (towerData != null) {
             towerData.OnParametersChanged += UpdateInternals; // Subscribe to tower data changes
        } else {
             Debug.LogWarning("SwipeCameraMover: TowerData not assigned in Awake. Max Y position might not update dynamically.", this);
        }
       
        UpdateInternals(); // Initial setup
    }

    void UpdateInternals()
    {
        startupAnimation = true; // Reset startup animation flag

        // --- Tower Data Check ---
        if (towerData != null)
        {
            TowerGeometryGenerator towerGeometryGenerator = new TowerGeometryGenerator(towerData);
            maxYPosition = towerGeometryGenerator.GetTopLevelHeight() + maxYOffset;
             Debug.Log($"SwipeCameraMover: Max camera Y Position calculated: {maxYPosition}", this);
        }
        else
        {
            Debug.LogWarning("SwipeCameraMover: TowerData not found or assigned. Max Y position not calculated.", this);
             // Optionally set a default maxYPosition here if needed when towerData is null
             // maxYPosition = 100f; // Example default
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
        if (maxYPosition < minYPosition && clampYPosition) // Only warn if clamping is enabled
        {
            minYPosition = maxYPosition;
            Debug.LogWarning($"SwipeCameraMover: minYPosition ({minYPosition}) was greater than maxYPosition ({maxYPosition}) while clamping is enabled. Setting minYPosition to maxYPosition.", this);
        }

        // --- NEW: Zoom Sanity Check ---
        if (maxZoomDistance < minZoomDistance)
        {
             float tempMax = maxZoomDistance;
             maxZoomDistance = minZoomDistance;
             minZoomDistance = tempMax; // Swap them
             Debug.LogWarning($"SwipeCameraMover: minZoomDistance was greater than maxZoomDistance. They have been swapped. Min: {minZoomDistance}, Max: {maxZoomDistance}", this);
        }
        // --- End NEW ---


        // --- Initialize Target Y GameObject ---
        if (_targetYPositionObject == null) // Only create if it doesn't exist
        {
             _targetYPositionObject = new GameObject("TargetYPosition_Internal");
             _targetYPositionObject.hideFlags = HideFlags.HideAndDontSave; // Prevent cluttering the hierarchy/saving
        }
        
        Vector3 initialCamPosition = targetCamera.transform.position;

        // Set target Y for startup animation
        Vector3 targetYPos = initialCamPosition;
        // Check if initial Y is already outside calculated bounds during startup
        if (clampYPosition) {
             targetYPos.y = Mathf.Clamp(maxYPosition, minYPosition, maxYPosition); // Start clamped to valid range
        } else {
             targetYPos.y = maxYPosition; // Start at the top if not clamping
        }
       
        _targetYPositionObject.transform.position = targetYPos; // Initialize target Y position object


        // Ensure initial Y position is clamped if needed
        // Also apply initial zoom clamping
        ClampCameraPosition(); // Use a helper function for clarity

         Debug.Log($"SwipeCameraMover: Internals Updated. Startup animation: {startupAnimation}, Clamped Y: {clampYPosition} ({minYPosition} - {maxYPosition}), Clamped Zoom: ({minZoomDistance} - {maxZoomDistance})", this);
    }


    void Update()
    {
         // Skip update if critical components are missing
         if (targetObject == null || targetCamera == null) return;

         // --- NEW: Check if interacting with UI ---
         if (IsPointerOverUIObject())
         {
             return; // Don't process swipes/zooms if interacting with UI
         }
         // --- End NEW ---


        if (startupAnimation)
        {
            StartupUpdate();
        }
        else
        {
            MainUpdate();
        }
    }

     // --- NEW: UI Check Function ---
    /// <summary>
    /// Checks if the current pointer (touch or mouse) is over a UI element.
    /// </summary>
    /// <returns>True if the pointer is over UI, false otherwise.</returns>
    private bool IsPointerOverUIObject()
    {
        // Check for touch input first
        if (Input.touchCount > 0)
        {
            for (int i = 0; i < Input.touchCount; i++)
            {
                if (EventSystem.current.IsPointerOverGameObject(Input.GetTouch(i).fingerId))
                {
                    return true;
                }
            }
        }
        // Check for mouse input (for editor/desktop)
        else if (EventSystem.current.IsPointerOverGameObject())
        {
            return true;
        }
        return false;
    }
     // --- End NEW ---


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
        Vector3 currentTargetPos = _targetYPositionObject.transform.position;
        currentTargetPos.y = targetCamera.transform.position.y;
        _targetYPositionObject.transform.position = currentTargetPos;

        // Ensure final position after startup is clamped
        ClampCameraPosition();

        Debug.Log("SwipeCameraMover: Startup complete. Transitioning to main update.");
    }

    void MainUpdate()
    {
        // Handle Touch Input (Mobile) OR Mouse Input (Desktop/Editor)
        HandleTouchInput();
        HandleMouseInput(); // Add mouse input handling

        // Apply smooth vertical movement (only affects Y) AFTER input is processed
        ApplySmoothVerticalMovement();

        // Apply clamping AFTER all movements have been potentially calculated for the frame
        ClampCameraPosition();
    }


    void HandleTouchInput()
    {
        // --- Swipe Rotation and Vertical Movement Logic (Single Finger) ---
        if (Input.touchCount == 1)
        {
            Touch touch = Input.GetTouch(0);

            // Optional: Check if the touch just began over UI, might prevent accidental swipes
            // if (touch.phase == TouchPhase.Began && IsPointerOverUIObject()) return;

            if (touch.phase == TouchPhase.Moved)
            {
                // --- Calculate Horizontal Rotation ---
                float horizontalAngle = -touch.deltaPosition.x * rotationSensitivity * Time.deltaTime;
                targetCamera.transform.RotateAround(targetObject.position, Vector3.up, horizontalAngle);

                // --- Calculate Vertical Movement Target ---
                float verticalMovement = -touch.deltaPosition.y * moveSensitivity * Time.deltaTime;
                // Modify the target position, which ApplySmoothVerticalMovement will follow
                Vector3 targetPos = _targetYPositionObject.transform.position;
                targetPos.y += verticalMovement;

                // Apply Y clamping directly to the target position if enabled
                 if (clampYPosition)
                 {
                     targetPos.y = Mathf.Clamp(targetPos.y, minYPosition, maxYPosition);
                 }
                _targetYPositionObject.transform.position = targetPos;

            }
        }
        // --- NEW: Pinch Zoom Logic (Two Fingers) ---
        else if (Input.touchCount == 2)
        {
            Touch touchZero = Input.GetTouch(0);
            Touch touchOne = Input.GetTouch(1);

            // Optional: Check if touches began over UI
            // if ((touchZero.phase == TouchPhase.Began || touchOne.phase == TouchPhase.Began) && IsPointerOverUIObject()) return;


            // Find the position in the previous frame of each touch.
            if (touchZero.phase == TouchPhase.Moved || touchOne.phase == TouchPhase.Moved)
            {
                 // Use stored previous positions if available, otherwise use current pos minus delta
                Vector2 touchZeroPrevPosActual = touchZero.position - touchZero.deltaPosition;
                Vector2 touchOnePrevPosActual = touchOne.position - touchOne.deltaPosition;


                // Find the magnitude of the vector (distance) between the touches in each frame.
                float prevTouchDeltaMag = (touchZeroPrevPosActual - touchOnePrevPosActual).magnitude;
                float touchDeltaMag = (touchZero.position - touchOne.position).magnitude;

                // Find the difference in the distances between each frame.
                float deltaMagnitudeDiff = touchDeltaMag - prevTouchDeltaMag; // Inverted: smaller distance = positive diff = zoom in

                // Calculate the zoom amount based on sensitivity and frame rate
                float zoomAmount = deltaMagnitudeDiff * zoomSensitivity * Time.deltaTime;

                // Calculate the direction towards the target object
                Vector3 zoomDirection = (targetObject.position - targetCamera.transform.position).normalized;

                // Move the camera
                targetCamera.transform.position += zoomDirection * zoomAmount;

                 // Update previous touch positions for the next frame - **Important for smooth zoom**
                 // Note: This logic assumes HandleTouchInput is called every frame where touchCount == 2.
                 // Storing them globally might be more robust if phases change rapidly, but this often works.
                 // We don't use the class members `touchZeroPrevPos`, `touchOnePrevPos` here,
                 // relying on deltaPosition provides the previous frame's position directly.
            }


        }
        // --- End NEW ---
    }


    // --- NEW: Mouse Input Handling ---
    void HandleMouseInput()
    {
        // Use Mouse Scroll Wheel for Zooming (Alternative/Desktop)
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.01f) // Check if there's noticeable scroll input
        {
             // Calculate zoom amount - multiply by extra factor for better control
            float zoomAmount = scroll * zoomSensitivity * mouseZoomMultiplier * Time.deltaTime; // Inverted scroll for natural feel


            // Calculate the direction towards the target object
            Vector3 zoomDirection = (targetObject.position - targetCamera.transform.position).normalized;


            // Move the camera
            targetCamera.transform.position += zoomDirection * zoomAmount;


            // Note: Clamping is handled globally in ClampCameraPosition() called in MainUpdate
        }


         // Optional: Add mouse drag for rotation/pan if needed (similar to touch logic)
         // if (Input.GetMouseButton(0)) { ... } // Check for left mouse button hold
         // if (Input.GetMouseButton(1)) { ... } // Check for right mouse button hold etc.
    }
    // --- End NEW ---

    /// <summary>
    /// Smoothly moves the camera's Y position towards the target Y position.
    /// Leaves X and Z untouched to allow for rotation and zoom.
    /// </summary>
    void ApplySmoothVerticalMovement()
    {
        Vector3 currentPos = targetCamera.transform.position;
        float targetY = _targetYPositionObject.transform.position.y; // Already clamped if clampYPosition is true

        // Only lerp if the difference is noticeable
        if (Mathf.Abs(currentPos.y - targetY) > 0.01f)
        {
            float newY = Mathf.Lerp(currentPos.y, targetY, Time.deltaTime * moveSpeed);
            targetCamera.transform.position = new Vector3(currentPos.x, newY, currentPos.z);
        }
        // Optional: Snap directly if very close
        else if (Mathf.Abs(currentPos.y - targetY) > 0f) {
             targetCamera.transform.position = new Vector3(currentPos.x, targetY, currentPos.z);
        }
    }

     // --- NEW: Centralized Clamping Function ---
     /// <summary>
     /// Clamps the camera's position based on Y constraints and zoom distance constraints.
     /// </summary>
    void ClampCameraPosition()
    {
         if (targetCamera == null || targetObject == null) return;


         Vector3 currentPos = targetCamera.transform.position;
         Vector3 targetPos = targetObject.position;


         // 1. Clamp Y Position
         if (clampYPosition)
         {
             currentPos.y = Mathf.Clamp(currentPos.y, minYPosition, maxYPosition);
             // Also clamp the target Y object to prevent it drifting out of bounds
             Vector3 yTargetPos = _targetYPositionObject.transform.position;
              yTargetPos.y = Mathf.Clamp(yTargetPos.y, minYPosition, maxYPosition);
             _targetYPositionObject.transform.position = yTargetPos;
         }


         // 2. Clamp Zoom Distance
         Vector3 directionToTarget = currentPos - targetPos;
         float currentDistance = directionToTarget.magnitude;


         if (currentDistance < minZoomDistance || currentDistance > maxZoomDistance)
         {
             float clampedDistance = Mathf.Clamp(currentDistance, minZoomDistance, maxZoomDistance);
             // Calculate the clamped position by moving along the direction vector
             // Normalize the direction vector (or reuse if already normalized from zoom calc)
             Vector3 directionNormalized = directionToTarget.normalized;
             // Special case: If camera is exactly at target, prevent division by zero/NaN direction.
             // Place it at min distance slightly offset (e.g., upwards).
             if (directionNormalized == Vector3.zero)
             {
                 directionNormalized = Vector3.up; // Or Vector3.forward, etc.
                 Debug.LogWarning("SwipeCameraMover: Camera was exactly at target position during clamping. Applying minimal offset.", this);
             }


             currentPos = targetPos + directionNormalized * clampedDistance;
         }


         // Apply the potentially clamped position
         // Only update if the position actually changed to avoid unnecessary assignments
         if (targetCamera.transform.position != currentPos)
         {
              targetCamera.transform.position = currentPos;
         }
    }
     // --- End NEW ---


    void OnDestroy()
    {
        // Clean up the internally created GameObject
        if (_targetYPositionObject != null)
        {
            Destroy(_targetYPositionObject);
        }
        // Unsubscribe from events to prevent memory leaks
         if (towerData != null) {
             towerData.OnParametersChanged -= UpdateInternals;
         }
    }
}