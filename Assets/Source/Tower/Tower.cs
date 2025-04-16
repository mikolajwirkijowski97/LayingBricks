using UnityEngine;
using System; // Required for Action delegate

/// <summary>
/// Defines the geometric parameters and randomization settings for a procedural tower.
/// Notifies listeners via the OnParametersChanged event when relevant properties are modified.
/// </summary>
public class Tower : MonoBehaviour
{
    /// <summary>
    /// Event triggered when any parameter affecting the tower's structure or appearance changes.
    /// Listeners should subscribe to this event to react to tower modifications.
    /// </summary>
    public event Action OnParametersChanged;

    // --- Serialized Fields ---

    [Header("Tower Shape Parameters")]

    [SerializeField]
    [Tooltip("Number of vertical levels (floors) in the tower. Must be at least 1.")]
    [Min(1)] // Enforce minimum value directly in the inspector
    private int _height = 10;
    
    [SerializeField]
    [Tooltip("Whether the last level is ordered or scrambled.")]
    private bool _isLastLevelOrdered = true;

    [SerializeField]
    [Tooltip("The total amount of bricks in the tower. Other values are derived from this.")]
    [Range(0,1000000)]
    private int _totalBricks = 1000; 

    [SerializeField]
    [Tooltip("Radius of the tower's cylindrical shape. Must be positive.")]
    [Min(0.1f)] // Enforce minimum value directly in the inspector
    private float _radius = 5.0f;

    [SerializeField]
    [Tooltip("Calculated circumference of the tower (2 * PI * Radius). Read-only.")]
    private float _circumference; // Read-only, calculated internally

    [Header("Brick Dimensions & Arrangement")]

    [SerializeField]
    [Tooltip("Depth (thickness) of the individual bricks. Must be positive.")]
    [Min(0.01f)] // Enforce minimum value directly in the inspector
    private float _brickDepth = 1.0f;

    [SerializeField]
    [Tooltip("Minimum height for any brick in a level. Must be positive.")]
    [Min(0.01f)] // Enforce minimum value directly in the inspector
    private float _minBrickHeight = 0.8f;

    [SerializeField]
    [Tooltip("Maximum height for any brick in a level. Must be positive and >= MinBrickHeight.")]
    [Min(0.01f)] // Enforce minimum value directly in the inspector
    private float _maxBrickHeight = 1.2f;

    [SerializeField]
    [Tooltip("Number of bricks arranged horizontally around each level. Must be at least 1.")]
    [Min(1)] // Enforce minimum value directly in the inspector
    private int _bricksPerLevel = 8;

    [Header("Randomization")]

    [SerializeField]
    [Tooltip("Seed for the random number generator used for deterministic tower generation.")]
    private int _seed;

    [SerializeField]
    [Tooltip("Maximum percentage variation applied to brick widths (0 = uniform width, 0.9 = high variation).")]
    [Range(0f, 0.9f)] // Use Range attribute for inspector slider
    private float _brickWidthVariation = 0.5f;


    // --- Public Properties ---

    /// <summary>
    /// Gets or sets the number of vertical levels in the tower. Minimum value is 1.
    /// Invokes OnParametersChanged if the value changes.
    /// </summary>
    public int Height {
        get { return _height; }
        set {
            int clampedValue = Mathf.Max(1, value); // Enforce minimum in setter too
            if (_height != clampedValue) {
                _height = clampedValue;
                OnParametersChanged?.Invoke();
            }
        }
    }

    /// <summary>
    /// Gets or sets whether the last level of the tower is ordered or scrambled.
    /// Invokes OnParametersChanged if the value changes.
    /// </summary>
    public bool IsLastLevelOrdered {
        get { return _isLastLevelOrdered; }
        set {
            if (_isLastLevelOrdered != value) {
                _isLastLevelOrdered = value;
                OnParametersChanged?.Invoke();
            }
        }
    }

    /// <summary>
    /// Gets or sets the total number of bricks in the tower. Minimum value is 0.
    /// Updates Height and invokes OnParametersChanged if the value changes.
    /// </summary>
    public int TotalBricks {
        get { return _totalBricks; }
        set {
            int clampedValue = Mathf.Max(0, value); // Enforce minimum in setter too
            _totalBricks = clampedValue; // Update total bricks
            Height = clampedValue / _bricksPerLevel;
            if (_totalBricks != clampedValue) {
                RecalculateHeight(); // Update dependent value
                OnParametersChanged?.Invoke();
            }
        }
    }

    /// <summary>
    /// Gets or sets the radius of the tower cylinder. Minimum value is 0.1.
    /// Updates Circumference and invokes OnParametersChanged if the value changes.
    /// </summary>
    public float Radius {
        get { return _radius; }
        set {
             float clampedValue = Mathf.Max(0.1f, value); // Enforce minimum in setter too
             if (!Mathf.Approximately(_radius, clampedValue)) {
                _radius = clampedValue;
                RecalculateCircumference(); // Update dependent value
                OnParametersChanged?.Invoke();
            }
        }
    }

    /// <summary>
    /// Gets the calculated circumference of the tower (2 * PI * Radius).
    /// </summary>
    public float Circumference {
        get { return _circumference; }
        // Private setter ensures it's only modified internally
        private set { _circumference = value; }
    }

    /// <summary>
    /// Gets or sets the depth (thickness) of the bricks. Minimum value is 0.01.
    /// Invokes OnParametersChanged if the value changes.
    /// </summary>
     public float BrickDepth {
        get { return _brickDepth; }
        set {
            float clampedValue = Mathf.Max(0.01f, value); // Enforce minimum in setter too
             if (!Mathf.Approximately(_brickDepth, clampedValue)) {
                _brickDepth = clampedValue;
                OnParametersChanged?.Invoke();
             }
        }
    }

    /// <summary>
    /// Gets or sets the minimum height for a brick. Minimum value is 0.01.
    /// Ensures MaxBrickHeight is adjusted if necessary to maintain Min <= Max constraint.
    /// Invokes OnParametersChanged if MinBrickHeight or MaxBrickHeight changes.
    /// </summary>
    public float MinBrickHeight {
        get { return _minBrickHeight; }
        set {
            float clampedValue = Mathf.Max(0.01f, value); // Enforce minimum
            float originalMax = _maxBrickHeight;
            bool maxChanged = false;

            // Ensure Min <= Max constraint
            if (clampedValue > _maxBrickHeight) {
                 _maxBrickHeight = clampedValue; // Adjust max to match new min
                 maxChanged = true;
            }

            // Check if min value itself changed
             bool minChanged = !Mathf.Approximately(_minBrickHeight, clampedValue);

             if (minChanged || maxChanged) {
                 _minBrickHeight = clampedValue;
                 OnParametersChanged?.Invoke(); // Notify if either min or max (due to constraint) changed
             }
        }
    }

    /// <summary>
    /// Gets or sets the maximum height for a brick. Minimum value is 0.01.
    /// Ensures MinBrickHeight is adjusted if necessary to maintain Min <= Max constraint.
    /// Invokes OnParametersChanged if MaxBrickHeight or MinBrickHeight changes.
    /// </summary>
     public float MaxBrickHeight {
        get { return _maxBrickHeight; }
        set {
             float clampedValue = Mathf.Max(0.01f, value); // Enforce minimum
             float originalMin = _minBrickHeight;
             bool minChanged = false;

             // Ensure Min <= Max constraint
            if (clampedValue < _minBrickHeight) {
                 _minBrickHeight = clampedValue; // Adjust min to match new max
                 minChanged = true;
             }

            // Check if max value itself changed
             bool maxChanged = !Mathf.Approximately(_maxBrickHeight, clampedValue);

             if (maxChanged || minChanged) {
                _maxBrickHeight = clampedValue;
                 OnParametersChanged?.Invoke(); // Notify if either max or min (due to constraint) changed
             }
        }
    }

    /// <summary>
    /// Gets or sets the number of bricks arranged horizontally around each level. Minimum value is 1.
    /// Invokes OnParametersChanged if the value changes.
    /// </summary>
    public int BricksPerLevel {
        get { return _bricksPerLevel; }
        set {
             int clampedValue = Mathf.Max(1, value); // Enforce minimum in setter too
             if (_bricksPerLevel != clampedValue) {
                _bricksPerLevel = clampedValue;
                OnParametersChanged?.Invoke();
             }
        }
    }

    /// <summary>
    /// Gets or sets the seed for random number generation.
    /// Invokes OnParametersChanged if the value changes.
    /// </summary>
     public int Seed {
        get { return _seed; }
        set {
             if (_seed != value) {
                _seed = value;
                 OnParametersChanged?.Invoke();
            }
        }
    }

    /// <summary>
    /// Gets or sets the maximum percentage variation in brick width (0 = uniform width, 0.9 = high variation).
    /// Clamped between 0.0 and 0.9.
    /// Invokes OnParametersChanged if the value changes.
    /// </summary>
    public float BrickWidthVariation {
        get { return _brickWidthVariation; }
        set {
             float clampedValue = Mathf.Clamp(value, 0.0f, 0.9f); // Enforce range
             if (!Mathf.Approximately(_brickWidthVariation, clampedValue)) {
                _brickWidthVariation = clampedValue;
                 OnParametersChanged?.Invoke();
            }
        }
    }

    // --- Unity Methods ---

    /// <summary>
    /// Initializes the tower state, calculating circumference and setting a default seed if necessary.
    /// </summary>
    void Awake()
    {
        RecalculateCircumference();
        // Assign a random seed if none is set, for initial variation
        if (_seed == 0)
        {
            _seed = UnityEngine.Random.Range(1, 100000);
        }
    }

    /// <summary>
    /// Called in the editor when a script property is modified.
    /// Ensures constraints (like Min/Max height) are enforced and notifies listeners if validation causes changes.
    /// This primarily handles changes made directly in the Inspector window.
    /// </summary>
    void OnValidate()
    {
        // Store original values before validation to check if changes occurred
        bool needsNotification = false;
        bool originalLastLevelOrdered = _isLastLevelOrdered;
        int originalTotalBricks = _totalBricks;
        int originalHeight = _height;
        float originalRadius = _radius;
        float originalBrickDepth = _brickDepth;
        float originalMinHeight = _minBrickHeight;
        float originalMaxHeight = _maxBrickHeight;
        int originalBricksPerLevel = _bricksPerLevel;
        int originalSeed = _seed;
        float originalWidthVariation = _brickWidthVariation;


        // Apply Min/Max and Range attributes constraints (redundant with attributes but safe)
        _height = Mathf.Max(1, _height);
        _totalBricks = Mathf.Max(0, _totalBricks);
        _radius = Mathf.Max(0.1f, _radius);
        _brickDepth = Mathf.Max(0.01f, _brickDepth);
        _bricksPerLevel = Mathf.Max(1, _bricksPerLevel);
        _brickWidthVariation = Mathf.Clamp(_brickWidthVariation, 0.0f, 0.9f);
        _minBrickHeight = Mathf.Max(0.01f, _minBrickHeight);
        _maxBrickHeight = Mathf.Max(0.01f, _maxBrickHeight);

        // Enforce Min <= Max height constraint specifically
        if (_minBrickHeight > _maxBrickHeight) {
             _maxBrickHeight = _minBrickHeight; // Adjust max to match min
        } else if (_maxBrickHeight < _minBrickHeight) {
             _minBrickHeight = _maxBrickHeight; // Adjust min to match max (handles direct max edit)
        }

        // Update calculated values like circumference and height
        RecalculateCircumference(); // 
        RecalculateHeight();

        // Check if any relevant property actually changed value during validation/clamping
        needsNotification = _height != originalHeight ||
                       !Mathf.Approximately(_radius, originalRadius) ||
                       !Mathf.Approximately(_brickDepth, originalBrickDepth) ||
                       !Mathf.Approximately(_minBrickHeight, originalMinHeight) ||
                       !Mathf.Approximately(_maxBrickHeight, originalMaxHeight) ||
                       _bricksPerLevel != originalBricksPerLevel ||
                       _seed != originalSeed ||
                       !Mathf.Approximately(_brickWidthVariation, originalWidthVariation);

        // If validation caused any change, invoke the event
        if (needsNotification)
        {
            // Use try-catch for editor safety, as event listeners might throw exceptions
            try {
                 // Use ?.Invoke() for safety even if we expect listeners
                 OnParametersChanged?.Invoke();
            } catch (Exception e) {
                // Log error if a listener causes issues during OnValidate
                Debug.LogError($"Error invoking OnParametersChanged during OnValidate: {e.Message}\n{e.StackTrace}", this);
            }
        }
    }


    // --- Private Helper Methods ---

    /// <summary>
    /// Recalculates the tower's circumference based on the current radius.
    /// Only updates the internal field if the calculated value differs significantly.
    /// </summary>
    private void RecalculateCircumference() {
         float newCircumference = 2f * Mathf.PI * _radius;
         // Avoid unnecessary updates if the change is negligible
         if (!Mathf.Approximately(_circumference, newCircumference)) {
            Circumference = newCircumference; // Use the private setter
            // Note: The event notification is handled by the Radius property setter, not needed here.
         }
    }

    /// <summary>
    /// Recalculates the tower's height based on the total number of bricks and bricks per level.
    /// Only updates the internal field if the calculated value differs significantly.
    /// </summary>
    private void RecalculateHeight() {
        // New height is total bricks divided by bricks per level, rounded up so theres
        // always a level for the remaining bricks
         int newHeight = (int)Math.Ceiling(_totalBricks / (float)_bricksPerLevel);
         // Avoid unnecessary updates if the change is negligible
         if (!_height.Equals(newHeight)) {
            Height = newHeight; // Use the private setter
         }
    }

/// <summary>
/// Returns how many bricks are on the last level of the tower.
/// </summary>
/// <returns>Number of bricks on the last level.</returns>
public int GetBricksOnLastLevel()
{
    // Log all variables used in the calculation for debugging
    Debug.Log($"Total Bricks: {_totalBricks}, Bricks Per Level: {_bricksPerLevel}");
    Debug.Log($"Height: {_height}"); // Keep logging for context


    // If there are no bricks in total, there are 0 bricks on the last level.
    if (_totalBricks == 0)
    {
        Debug.Log("TotalBricks is 0, returning 0 bricks on last level.");
        return 0;
    }

    int remainder = _totalBricks % _bricksPerLevel;
    Debug.Log($"Total Bricks % Bricks Per Level: {remainder}");

    // If remainder is 0, it means the last level is full (contains _bricksPerLevel bricks).
    // Otherwise, the last level contains 'remainder' bricks.
    int result = (remainder == 0) ? _bricksPerLevel : remainder;
    Debug.Log($"Calculated bricks on last level: {result}");
    return result;
}
/// <summary>
/// Updates the count of bricks and height and all other parameters of the tower.
/// </summary>
/// <param name="count"> The number of bricks to add</param>
    public void AddBricks(int count){
        TotalBricks += count; // Update the total number of bricks
        // Recalculate height and notify listeners
        RecalculateHeight(); // Update dependent value
        OnParametersChanged?.Invoke(); // Notify listeners of the change

    }
}