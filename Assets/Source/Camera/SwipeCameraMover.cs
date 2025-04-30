using System;
using UnityEngine;
using UnityEngine.EventSystems; // Required for checking UI interaction
// using Unity.VisualScripting; // Removed as likely unused

/// <summary>
/// Moves the camera vertically based on vertical touch swipes,
/// rotates it horizontally around a target object based on horizontal touch swipes,
/// and zooms in/out based on pinch gestures.
/// Uses single-finger input for swipe/rotate, two-finger for pinch.
/// MOBILE ONLY - Mouse input removed.
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

    [Header("Zoom Settings")]
    [Tooltip("How sensitive the zoom is to pinch gestures.")]
    [SerializeField] private float zoomSensitivity = 0.5f;
    [Tooltip("The minimum distance the camera can be from the target object.")]
    [SerializeField] private float minZoomDistance = 5f;
    [Tooltip("The maximum distance the camera can be from the target object.")]
    [SerializeField] private float maxZoomDistance = 50f;
    // Removed: mouseZoomMultiplier

    [Header("Clamping")]
    [Tooltip("Optional: Clamp Y position between min and max values.")]
    [SerializeField] private bool clampYPosition = true;
    [SerializeField] private float minYPosition = 0f;
    private float maxYPosition = 100f; // Will be calculated from Tower
    [SerializeField] [Tooltip("Optional: Maximum Y offset above calculated tower height.")]
    private float maxYOffset = 0f;

    [Header("Dependencies")] // Grouped dependencies
    [SerializeField] private Tower towerData;

    // Internal State
    private bool startupAnimation = true;
    private GameObject _targetYPositionObject;

    void Awake()
    {
        if (towerData != null) {
             towerData.OnParametersChanged += UpdateInternals; // Subscribe to tower data changes
             towerData.OnParametersChanged += GoToTop; // Go to top when tower data changes
        } else {
             Debug.LogWarning("SwipeCameraMover: TowerData not assigned in Awake. Max Y position might not update dynamically.", this);
        }
        UpdateInternals(); // Initial setup
        GoToTop(); // Start at the top of the tower
    }

    void GoToTop() {
        var targetY = _targetYPositionObject.transform.position; // Set target Y to max Y position
        if(Math.Abs(targetY.y - maxYPosition) < 1f) return; // Avoid unnecessary updates

        targetY.y = maxYPosition;
        Debug.Log("SwipeCameraMover: GoToTop called. Target Y set to max Y position: " + targetY.y, this);
        _targetYPositionObject.transform.position = targetY; // Set target Y object to max Y position
        startupAnimation = true; // Reset startup animation flag
    }

    void UpdateInternals()
    {

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

        if (maxZoomDistance < minZoomDistance)
        {
             float tempMax = maxZoomDistance;
             maxZoomDistance = minZoomDistance;
             minZoomDistance = tempMax; // Swap them
             Debug.LogWarning($"SwipeCameraMover: minZoomDistance was greater than maxZoomDistance. They have been swapped. Min: {minZoomDistance}, Max: {maxZoomDistance}", this);
        }


        // --- Initialize Target Y GameObject ---
        if (_targetYPositionObject == null) // Only create if it doesn't exist
        {
             _targetYPositionObject = new GameObject("TargetYPosition_Internal");
             Instantiate(_targetYPositionObject); // Instantiate in the scene
             _targetYPositionObject.hideFlags = HideFlags.HideAndDontSave; // Prevent cluttering the hierarchy/saving
        }

        ClampCameraPosition(); // Apply zoom clamping and re-apply Y clamping

         Debug.Log($"SwipeCameraMover: Internals Updated. Startup animation flag: {startupAnimation}, Initial Target Y set to: {_targetYPositionObject.transform.position.y}, Clamped Y: {clampYPosition} ({minYPosition} - {maxYPosition}), Clamped Zoom: ({minZoomDistance} - {maxZoomDistance})", this);
    }


    void Update()
    {
         // Skip update if critical components are missing
         if (targetObject == null || targetCamera == null) return;

        if(transform.position != _targetYPositionObject.transform.position) // Check if the target Y position is different from the camera's position
        {
            Debug.Log("Camera position is set to: " + transform.position + " instead of target Y position: " + _targetYPositionObject.transform.position, this);
        }

        if (startupAnimation)
        {
            StartupUpdate();
        }
        else
        {
            // Check if interacting with UI - Ignore touches over UI
            if (IsPointerOverUIObject())
            {
                return; // Don't process swipes/zooms if interacting with UI
            }
            MainUpdate();
        }
    }

    /// <summary>
    /// Checks if any current touch pointer is over a UI element.
    /// </summary>
    /// <returns>True if a touch pointer is over UI, false otherwise.</returns>
    private bool IsPointerOverUIObject()
    {
        // Check for touch input
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
        // Removed mouse check: else if (EventSystem.current.IsPointerOverGameObject()) { return true; }
        return false;
    }


    void StartupUpdate()
    {
        // Slowly move the camera towards the target Y position (keeping X/Z)
        // The target Y position is now initialized to the camera's starting Y (or clamped starting Y)
        Vector3 currentCameraPos = targetCamera.transform.position;

        // Lerp only the Y axis to prevent unintended X/Z drift during this phase
        if(Time.deltaTime > 0.2f) // Avoid too fast lerp during startup
        {
            return;
        }
        float newY = Mathf.Lerp(currentCameraPos.y, _targetYPositionObject.transform.position.y, Time.deltaTime * moveSpeed);
        targetCamera.transform.position = new Vector3(currentCameraPos.x, newY, currentCameraPos.z);

        Debug.Log("SwipeCameraMover: Startup animation in progress. Current Y: " + newY, this);

        if (Mathf.Abs(targetCamera.transform.position.y - _targetYPositionObject.transform.position.y) < 0.01f) // Reduced threshold for faster transition
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

        // Ensure final position after startup is clamped (redundant safety check)
        ClampCameraPosition();

        Debug.Log("SwipeCameraMover: Startup complete. Transitioning to main update.");
    }

    void MainUpdate()
    {
        // Handle Touch Input (Mobile ONLY)
        HandleTouchInput();

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
            // if (touch.phase == TouchPhase.Began && IsPointerOverUIObject()) return; // Already checked globally at start of Update

            if (touch.phase == TouchPhase.Moved)
            {
                // --- Calculate Horizontal Rotation ---
                // Adjust sensitivity based on screen width? Might feel better.
                // float horizontalAngle = -touch.deltaPosition.x * rotationSensitivity * (1f / Screen.width) * Time.deltaTime;
                float horizontalAngle = -touch.deltaPosition.x * rotationSensitivity * Time.deltaTime;
                targetCamera.transform.RotateAround(targetObject.position, Vector3.up, horizontalAngle);

                // --- Calculate Vertical Movement Target ---
                // Adjust sensitivity based on screen height? Might feel better.
                // float verticalMovement = -touch.deltaPosition.y * moveSensitivity * (1f / Screen.height) * Time.deltaTime;
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
        // --- Pinch Zoom Logic (Two Fingers) ---
        else if (Input.touchCount == 2)
        {
            Touch touchZero = Input.GetTouch(0);
            Touch touchOne = Input.GetTouch(1);

            // Optional: Check if touches began over UI - Already checked globally at start of Update
            // if ((touchZero.phase == TouchPhase.Began || touchOne.phase == TouchPhase.Began) && IsPointerOverUIObject()) return;

            // Find the position in the previous frame of each touch.
            if (touchZero.phase == TouchPhase.Moved || touchOne.phase == TouchPhase.Moved)
            {
                 // Calculate previous positions based on current position and delta
                 Vector2 touchZeroPrevPosActual = touchZero.position - touchZero.deltaPosition;
                 Vector2 touchOnePrevPosActual = touchOne.position - touchOne.deltaPosition;

                 // Find the magnitude of the vector (distance) between the touches in each frame.
                 float prevTouchDeltaMag = (touchZeroPrevPosActual - touchOnePrevPosActual).magnitude;
                 float touchDeltaMag = (touchZero.position - touchOne.position).magnitude;

                 // Find the difference in the distances between each frame.
                 float deltaMagnitudeDiff = touchDeltaMag - prevTouchDeltaMag; // Positive when fingers move apart (zoom out)

                 // Calculate the zoom amount based on sensitivity and frame rate
                 // Note: Positive deltaMagnitudeDiff should move camera AWAY from target (zoom out)
                 // Need to invert the sign or adjust the direction. Let's move along negative direction.
                 float zoomAmount = deltaMagnitudeDiff * zoomSensitivity * Time.deltaTime;

                 // Calculate the direction FROM the target object TO the camera
                 Vector3 directionFromTarget = (targetCamera.transform.position - targetObject.position).normalized;
                 if (directionFromTarget == Vector3.zero) { // Handle case where camera is exactly at target
                     directionFromTarget = targetCamera.transform.forward; // Use camera's forward as fallback
                 }

                 // Move the camera along this direction (away from target for zoom out, towards for zoom in)
                 targetCamera.transform.position += directionFromTarget * zoomAmount;

                 // Clamping is handled globally in ClampCameraPosition() called in MainUpdate
            }
        }
    }

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
        // Optional: Snap directly if very close to prevent micro-movements
        else if (currentPos.y != targetY) // Check non-equality instead of Abs > 0f for precision
        {
             targetCamera.transform.position = new Vector3(currentPos.x, targetY, currentPos.z);
        }
    }

/// <summary>
    /// Clamps the camera's position based on HORIZONTAL (XZ) distance constraints
    /// and VERTICAL (Y) constraints independently.
    /// </summary>
    void ClampCameraPosition()
    {
        if (targetCamera == null || targetObject == null) return;

        Vector3 currentPos = targetCamera.transform.position;
        Vector3 targetPos = targetObject.position; // Cache target position

        // --- 1. Horizontal (XZ) Distance Clamping ---
        Vector3 offset = currentPos - targetPos;
        Vector3 horizontalOffset = offset;
        horizontalOffset.y = 0; // Project onto the XZ plane

        float currentHorizontalDistanceSqr = horizontalOffset.sqrMagnitude; // Use squared for efficiency
        float minZoomDistSqr = minZoomDistance * minZoomDistance;
        float maxZoomDistSqr = maxZoomDistance * maxZoomDistance;

        // Check if clamping is needed (and avoid division by zero if distance is ~0)
        if (currentHorizontalDistanceSqr < minZoomDistSqr - 0.001f || currentHorizontalDistanceSqr > maxZoomDistSqr + 0.001f)
        {
            float currentHorizontalDistance = Mathf.Sqrt(currentHorizontalDistanceSqr);
            float clampedHorizontalDistance = Mathf.Clamp(currentHorizontalDistance, minZoomDistance, maxZoomDistance);

            if (currentHorizontalDistance > 0.001f) // Check magnitude before normalizing
            {
                Vector3 horizontalDirection = horizontalOffset / currentHorizontalDistance; // Normalized horizontal direction
                Vector3 clampedHorizontalPosition = targetPos + horizontalDirection * clampedHorizontalDistance;

                // Apply the clamped XZ position, keeping the original Y
                currentPos.x = clampedHorizontalPosition.x;
                currentPos.z = clampedHorizontalPosition.z;
            }
            else
            {
                // Camera is directly above or below the target. Avoid NaN.
                // Place it at min distance along camera's local right (or forward) axis on the XZ plane.
                Vector3 fallbackHorizontalDir = targetCamera.transform.right;
                fallbackHorizontalDir.y = 0;
                fallbackHorizontalDir.Normalize();
                if (fallbackHorizontalDir == Vector3.zero) fallbackHorizontalDir = Vector3.right; // Absolute fallback

                Vector3 clampedHorizontalPosition = targetPos + fallbackHorizontalDir * minZoomDistance;
                currentPos.x = clampedHorizontalPosition.x;
                currentPos.z = clampedHorizontalPosition.z;
                 Debug.LogWarning("SwipeCameraMover: Camera was directly above/below target during horizontal clamping. Applying minimal horizontal offset.", this);
            }
        }

        // --- 2. Vertical (Y) Clamping ---
        if (clampYPosition)
        {
            currentPos.y = Mathf.Clamp(currentPos.y, minYPosition, maxYPosition);

            // Also clamp the target Y object to prevent it drifting out of bounds
            // when user stops swiping at the vertical limit.
            Vector3 yTargetPos = _targetYPositionObject.transform.position;
            float clampedYTarget = Mathf.Clamp(yTargetPos.y, minYPosition, maxYPosition);
            // Only update if it actually needs clamping to avoid overriding during smooth movement
            if (yTargetPos.y != clampedYTarget) {
                 _targetYPositionObject.transform.position = new Vector3(yTargetPos.x, clampedYTarget, yTargetPos.z);
            }
        }

        // --- 3. Apply the Final Clamped Position ---
        // Use sqrMagnitude for efficient comparison to avoid tiny adjustments causing constant updates
        if ((targetCamera.transform.position - currentPos).sqrMagnitude > 0.0001f)
        {
             targetCamera.transform.position = currentPos;
        }
    }

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