using System;
using UnityEngine;
using UnityEngine.EventSystems; // Required for checking UI interaction

/// <summary>
/// Moves the camera vertically based on vertical touch swipes,
/// rotates it horizontally around a target object based on horizontal touch swipes,
/// and zooms in/out based on pinch gestures.
/// All movements (vertical, rotation, zoom) are smoothly interpolated.
/// Uses single-finger input for swipe/rotate, two-finger for pinch.
/// MOBILE ONLY.
/// </summary>
public class SwipeCameraMover : MonoBehaviour
{
    [Header("Target Object")]
    [Tooltip("The object the camera should orbit around horizontally and zoom towards. Assign this in the Inspector.")]
    [SerializeField] private Transform targetObject;

    [Header("Camera Settings")]
    [Tooltip("The camera to move and rotate. If null, defaults to Camera.main.")]
    [SerializeField] private Camera targetCamera;

    [Header("Movement Sensitivity")]
    [Tooltip("How sensitive the vertical movement is to the swipe speed.")]
    [SerializeField] private float moveSensitivity = 0.1f;
    [Tooltip("How sensitive the horizontal rotation is to the swipe speed.")]
    [SerializeField] private float rotationSensitivity = 0.5f;

    [Header("Smoothing Speed")]
    [Tooltip("How quickly the camera moves vertically to the target position.")]
    [SerializeField] private float verticalMoveSpeed = 10f;
    [Tooltip("How quickly the camera rotates horizontally to the target rotation.")]
    [SerializeField] private float rotationSpeed = 10f;
    [Tooltip("How quickly the camera zooms to the target distance.")]
    [SerializeField] private float zoomSpeed = 10f;

    [Header("Zoom Settings")]
    [Tooltip("How sensitive the zoom is to pinch gestures.")]
    [SerializeField] private float zoomSensitivity = 0.5f;
    [Tooltip("The minimum distance the camera can be from the target object (on XZ plane).")]
    [SerializeField] private float minZoomDistance = 5f;
    [Tooltip("The maximum distance the camera can be from the target object (on XZ plane).")]
    [SerializeField] private float maxZoomDistance = 50f;
    [Tooltip("Invert the direction of pinch to zoom.")]
    [SerializeField] private bool invertZoomDirection = false;

    [Tooltip("The fixed downward pitch angle for the camera in degrees. Positive values pitch downwards.")]
    [SerializeField] private float fixedDownwardPitchAngle = 15.0f;

    [Header("Clamping")]
    [Tooltip("Optional: Clamp Y position between min and max values.")]
    [SerializeField] private bool clampYPosition = true;
    [SerializeField] private float minYPosition = 0f;
    private float maxYPosition = 100f; // Will be calculated from Tower
    [SerializeField] [Tooltip("Optional: Maximum Y offset above calculated tower height.")]
    private float maxYOffset = 0f;

    [Header("Dependencies")]
    [SerializeField] private Tower towerData;

    // Internal State for Smooth Movement
    private float _targetY;
    private float _currentY;
    private float _targetYaw; // Y-axis rotation
    private float _currentYaw;
    private float _targetDistance;
    private float _currentDistance;

    private bool _isStartupAnimationActive = true;
    private bool _isInitialized = false;

    void Awake()
    {
        AdjustSensitivityForPlatform();

        if (towerData != null)
        {
            towerData.OnParametersChanged += OnTowerParametersChanged;
        }
        else
        {
            Debug.LogWarning("SwipeCameraMover: TowerData not assigned in Awake. Max Y position might not update dynamically.", this);
        }

        // Initial calculation of bounds and component checks
        if (!InitializeBaseParameters())
        {
            enabled = false; // Disable if critical components are missing
            return;
        }
    }

    void Start()
    {
        if (!enabled) return; // Don't proceed if Awake failed

        InitializeCameraState();
        EnsureCameraBoundsAreRespected(); // Apply initial clamping

        // If tower data is present, start at the top, otherwise stay at initial position
        if (towerData != null)
        {
            GoToTop();
        } else {
            // If no tower, startup animation is for current Y position (effectively no Y movement unless camera starts outside bounds)
            _targetY = _currentY; // Target current Y, allows startup smoothing to settle
            _isStartupAnimationActive = true; // Enable startup animation to smooth to initial clamped state
        }
        _isInitialized = true;
    }

    void OnDestroy()
    {
        if (towerData != null)
        {
            towerData.OnParametersChanged -= OnTowerParametersChanged;
        }
    }

    private void OnTowerParametersChanged()
    {
        Debug.Log("SwipeCameraMover: Tower parameters changed. Updating internals and going to top.", this);
        InitializeBaseParameters(); // Recalculate bounds like maxYPosition
        EnsureCameraBoundsAreRespected(); // Re-apply clamping with new bounds
        GoToTop(); // Move to the new top
    }

    private void AdjustSensitivityForPlatform()
    {
        // Simple check, can be expanded for more platforms
        if (Application.platform != RuntimePlatform.IPhonePlayer && Application.platform != RuntimePlatform.Android)
        {
            Debug.Log("SwipeCameraMover: Non-mobile platform detected. Adjusting sensitivity for desktop.", this);
            // Assuming Editor/Desktop might need higher sensitivity if emulating touch with mouse
            moveSensitivity *= 10f;
            rotationSensitivity *= 10f;
            // Zoom sensitivity for mouse wheel is usually handled differently,
            // but this component is mobile-only for input.
        }
    }

    /// <summary>
    /// Initializes camera, target, and calculates Y clamping bounds.
    /// Returns false if critical setup fails.
    /// </summary>
    private bool InitializeBaseParameters()
    {
        if (targetCamera == null) targetCamera = Camera.main;
        if (targetCamera == null)
        {
            Debug.LogError("SwipeCameraMover: No camera found or assigned! Disabling script.", this);
            return false;
        }
        if (targetObject == null)
        {
            Debug.LogError("SwipeCameraMover: No target object assigned! Rotation/Zoom will not work correctly. Disabling script.", this);
            return false;
        }

        if (towerData != null)
        {
            TowerGeometryGenerator towerGeometryGenerator = new TowerGeometryGenerator(towerData);
            maxYPosition = towerGeometryGenerator.GetTopLevelHeight() + maxYOffset;
            Debug.Log($"SwipeCameraMover: Max camera Y Position calculated: {maxYPosition}", this);
        }
        else
        {
            Debug.LogWarning("SwipeCameraMover: TowerData not found or assigned. Max Y position not calculated, using default.", this);
        }

        // Sanity checks for clamping values
        if (clampYPosition && maxYPosition < minYPosition)
        {
            minYPosition = maxYPosition; // Or swap, depending on desired behavior
            Debug.LogWarning($"SwipeCameraMover: minYPosition was greater than maxYPosition. minYPosition adjusted to {minYPosition}.", this);
        }
        if (maxZoomDistance < minZoomDistance)
        {
            (minZoomDistance, maxZoomDistance) = (maxZoomDistance, minZoomDistance); // Swap
            Debug.LogWarning($"SwipeCameraMover: minZoomDistance was greater than maxZoomDistance. They have been swapped.", this);
        }
        minZoomDistance = Mathf.Max(0.1f, minZoomDistance); // Ensure minZoomDistance is slightly positive

        Debug.Log($"SwipeCameraMover: Base parameters initialized. Clamped Y: {clampYPosition} ({minYPosition} - {maxYPosition}), Clamped Zoom: ({minZoomDistance} - {maxZoomDistance})", this);
        return true;
    }

    /// <summary>
    /// Sets the initial _current and _target values for Y, Yaw, and Distance
    /// based on the camera's starting position in the scene.
    /// </summary>
    private void InitializeCameraState()
    {
        _currentY = targetCamera.transform.position.y;
        _targetY = _currentY; // Initially, target current Y

        Vector3 initialOffset = targetCamera.transform.position - targetObject.position;
        Vector3 initialOffsetXZ = new Vector3(initialOffset.x, 0, initialOffset.z);
        _currentDistance = initialOffsetXZ.magnitude;

        if (_currentDistance < 0.01f) // Camera is (almost) vertically aligned with target
        {
            _currentDistance = minZoomDistance; // Set to min distance
            _currentYaw = 0f; // Default yaw
             Debug.LogWarning("SwipeCameraMover: Camera started very close to target on XZ plane. Setting to minZoomDistance and default yaw.", this);
        }
        else
        {
            // Calculate yaw from target to camera.
            // A positive offset.z (camera further along Z than target) should be 0 yaw if standard coordinates.
            // Quaternion.LookRotation(direction_camera_is_facing)
            // Camera looks AT target, so its forward is target - camera.pos
            // We need yaw of offset vector from target.
            _currentYaw = Quaternion.LookRotation(initialOffsetXZ.normalized).eulerAngles.y;
        }

        _targetDistance = _currentDistance;
        _targetYaw = _currentYaw;

        Debug.Log($"SwipeCameraMover: Initial camera state: Y={_currentY:F2}, Yaw={_currentYaw:F2}, Dist={_currentDistance:F2}", this);
    }

    /// <summary>
    /// Clamps current and target Y and Distance values to their defined limits
    /// and immediately updates the camera transform. Called after bounds might change.
    /// </summary>
    private void EnsureCameraBoundsAreRespected()
    {
        if (clampYPosition)
        {
            _currentY = Mathf.Clamp(_currentY, minYPosition, maxYPosition);
            _targetY = Mathf.Clamp(_targetY, minYPosition, maxYPosition);
        }

        _currentDistance = Mathf.Clamp(_currentDistance, minZoomDistance, maxZoomDistance);
        _targetDistance = Mathf.Clamp(_targetDistance, minZoomDistance, maxZoomDistance);

        // Apply these potentially modified _current values to the camera transform immediately
        UpdateCameraTransform();
        Debug.Log($"SwipeCameraMover: Camera bounds enforced. CurrentY={_currentY:F2}, TargetY={_targetY:F2}, CurrentDist={_currentDistance:F2}, TargetDist={_targetDistance:F2}", this);
    }


    public void GoToTop()
    {
        if (!_isInitialized && enabled) {
            // This can happen if GoToTop is called from an external script before Start()
            // We need to ensure base parameters and initial state are set up.
            if (!InitializeBaseParameters()) { enabled = false; return; }
            InitializeCameraState();
            _isInitialized = true; // Mark as initialized to prevent re-init
             Debug.LogWarning("SwipeCameraMover: GoToTop called before Start(), ensuring initialization.", this);
        } else if (!enabled) {
            return; // Script is disabled
        }

        _targetY = maxYPosition;
        _isStartupAnimationActive = true;
        Debug.Log($"SwipeCameraMover: GoToTop called. Target Y set to {maxYPosition}. Startup animation active.", this);
    }

    void Update()
    {
        if (!enabled || targetObject == null || targetCamera == null) return;

        // Skip input processing if interacting with UI
        if (IsPointerOverUIObject())
        {
            return;
        }

        if (!_isStartupAnimationActive) // Only process input if not in startup animation
        {
            ProcessInput();
        }
    }

    void LateUpdate()
    {
        if (!enabled || targetObject == null || targetCamera == null) return;

        float deltaTime = Time.deltaTime;
        if (deltaTime <= 0f) return; // Avoid issues with zero or negative delta time

        if (_isStartupAnimationActive)
        {
            PerformStartupVerticalMovement(deltaTime);
        }
        else
        {
            ApplySmoothedStateChanges(deltaTime);
        }
        UpdateCameraTransform();
    }

    private bool IsPointerOverUIObject()
    {
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
        return false;
    }

    private void PerformStartupVerticalMovement(float deltaTime)
    {
        _currentY = Mathf.Lerp(_currentY, _targetY, verticalMoveSpeed * deltaTime);

        if (Mathf.Abs(_currentY - _targetY) < 0.01f)
        {
            _currentY = _targetY; // Snap to final position
            TransitionToMainLogic();
        }
        // Camera transform is updated in LateUpdate's main call to UpdateCameraTransform
    }

    private void TransitionToMainLogic()
    {
        _isStartupAnimationActive = false;
        // Ensure target Y is current Y to prevent further lerping if it wasn't exact
        _targetY = _currentY;
        // Target Yaw and Distance should already be aligned with Current Yaw/Distance
        // from initialization or previous operations.
        Debug.Log("SwipeCameraMover: Startup animation complete. Transitioning to main logic.");
    }

    private void ProcessInput()
    {
        if (Input.touchCount == 1)
        {
            ProcessSingleFingerInput(Input.GetTouch(0));
        }
        else if (Input.touchCount >= 2) // Use >= 2 for pinch, in case of >2 touches
        {
            ProcessTwoFingerInput(Input.GetTouch(0), Input.GetTouch(1));
        }
    }

    private void ProcessSingleFingerInput(Touch touch)
    {
        if (touch.phase == TouchPhase.Moved)
        {
            // Horizontal Rotation
            float horizontalDelta = -touch.deltaPosition.x * rotationSensitivity * Time.deltaTime;
            _targetYaw += horizontalDelta * (360f / Screen.width); // Scale rotation slightly by screen width for consistency

            // Vertical Movement
            float verticalDelta = -touch.deltaPosition.y * moveSensitivity * Time.deltaTime;
            _targetY += verticalDelta * (maxYPosition - minYPosition) / Screen.height; // Scale by Y range and screen height

            if (clampYPosition)
            {
                _targetY = Mathf.Clamp(_targetY, minYPosition, maxYPosition);
            }
        }
    }

    private void ProcessTwoFingerInput(Touch touchZero, Touch touchOne)
    {
        if (touchZero.phase == TouchPhase.Moved || touchOne.phase == TouchPhase.Moved)
        {
            Vector2 touchZeroPrevPos = touchZero.position - touchZero.deltaPosition;
            Vector2 touchOnePrevPos = touchOne.position - touchOne.deltaPosition;

            float prevTouchDeltaMag = (touchZeroPrevPos - touchOnePrevPos).magnitude;
            float touchDeltaMag = (touchZero.position - touchOne.position).magnitude;

            float deltaMagnitudeDiff = touchDeltaMag - prevTouchDeltaMag;

            if (invertZoomDirection)
            {
                deltaMagnitudeDiff *= -1;
            }

            // Positive deltaMagnitudeDiff = fingers moving apart = zoom out (increase distance)
            // Negative deltaMagnitudeDiff = fingers moving together = zoom in (decrease distance)
            // The sign convention here is: more positive zoomAmount = further away
            float zoomAmount = deltaMagnitudeDiff * zoomSensitivity * Time.deltaTime;

            _targetDistance += zoomAmount * 10f; // Scale factor for reasonable zoom speed
            _targetDistance = Mathf.Clamp(_targetDistance, minZoomDistance, maxZoomDistance);
        }
    }

    private void ApplySmoothedStateChanges(float deltaTime)
    {
        _currentY = Mathf.Lerp(_currentY, _targetY, verticalMoveSpeed * deltaTime);
        _currentYaw = Mathf.LerpAngle(_currentYaw, _targetYaw, rotationSpeed * deltaTime);
        _currentDistance = Mathf.Lerp(_currentDistance, _targetDistance, zoomSpeed * deltaTime);
    }

    private void UpdateCameraTransform()
    {
        if (targetCamera == null || targetObject == null) return;

        // Calculate rotation as a quaternion
        Quaternion rotation = Quaternion.Euler(0, _currentYaw, 0);

        // Calculate position based on target, current distance, and current yaw
        // Offset is from target to camera. If yaw=0, camera is along positive Z relative to target's orientation
        Vector3 offset = rotation * (Vector3.forward * _currentDistance);
        Vector3 newPosition = targetObject.position - offset; // Subtract if forward means "in front of target"

        // Apply the calculated Y position
        newPosition.y = _currentY;

        targetCamera.transform.position = newPosition;

        Vector3 lookTargetForYaw = new Vector3(0f, newPosition.y, 0f);
        Vector3 directionToYawTarget;


        directionToYawTarget = (lookTargetForYaw - newPosition).normalized;
        targetCamera.transform.rotation = Quaternion.LookRotation(directionToYawTarget, Vector3.up);
        targetCamera.transform.rotation *= Quaternion.Euler(fixedDownwardPitchAngle, 0f, 0f);

    }
}