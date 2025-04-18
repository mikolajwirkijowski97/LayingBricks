using UnityEngine;
using UnityEngine.Events;
using System;
using BeliefEngine.HealthKit;
public class DistanceGetter : MonoBehaviour
{
    [Header("BEHealthKit References")]
    [Tooltip("Assign the GameObject with the HealthStore component.")]
    [SerializeField] private HealthStore healthStore;

    [Tooltip("Assign the GameObject with the HealthKitDataTypes component. Ensure 'Distance Walking/Running' is checked under Read Permissions.")]
    [SerializeField] private HealthKitDataTypes dataTypes;

    [Header("Events")]
    [Tooltip("Event triggered when the total distance (in KM) is successfully fetched.")]
    public UnityEvent<int> OnTotalDistanceFetched;

    [Tooltip("Event triggered if an error occurs during the process.")]
    public UnityEvent<string> OnErrorFetchingDistance;

    // Public method to initiate the process
    public void GetTotalDistanceEver()
    {
        // 1. --- Pre-checks ---
        if (healthStore == null)
        {
            string errorMsg = "HealthStore reference is not set in the inspector.";
            Debug.LogError(errorMsg);
            OnErrorFetchingDistance?.Invoke(errorMsg);
            return;
        }
        if (dataTypes == null)
        {
            string errorMsg = "HealthKitDataTypes reference is not set in the inspector.";
            Debug.LogError(errorMsg);
            OnErrorFetchingDistance?.Invoke(errorMsg);
            return;
        }

        // Ensure Distance Walking/Running is checked in the HealthKitDataTypes component in the Editor.
        // (Runtime check is complex, rely on editor setup as intended by the plugin design)

        if (!healthStore.IsHealthDataAvailable())
        {
            string errorMsg = "HealthKit is not available on this device.";
            Debug.LogWarning(errorMsg);
            // You might treat this as an error or just a state depending on your app
            OnErrorFetchingDistance?.Invoke(errorMsg);
            return;
        }

        // 2. --- Authorization ---
        // Request authorization. The user will see the iOS prompt if needed.
        // The actual read operation later will fail if permission is denied.
        Debug.Log("Requesting HealthKit authorization for required types...");
        healthStore.Authorize(dataTypes); // Call Authorize as per README basic usage

        // 3. --- Initiate Data Read ---
        // We call the read method immediately after Authorize.
        // The callback will handle success or permission errors.
        ReadTotalWalkRunDistance();
    }

    private void ReadTotalWalkRunDistance()
    {
        // Define the data type we want to read
        HKDataType dataType = HKDataType.HKQuantityTypeIdentifierDistanceWalkingRunning;

        // Define the time range: From the earliest possible date to now.
        // HealthKit data likely started around iOS 8 (2014), but using a very early date ensures we capture everything.
        // DateTimeOffset.MinValue might cause issues on some platforms/libraries, using a specific early date is safer.
        DateTimeOffset startDate = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset endDate = DateTimeOffset.UtcNow; // Up to the current moment

        Debug.Log($"Attempting to read total {dataType} from {startDate} to {endDate}");

        // Use ReadCombinedQuantitySamples as it directly returns the sum
        healthStore.ReadCombinedQuantitySamples(
            dataType,
            startDate,
            endDate,
            HandleDistanceDataResponse // Specify the callback method
        );
    }

    // 4. --- Handle the Response ---
    private void HandleDistanceDataResponse(double totalValue, Error error)
    {
        if (error != null)
        {
            // An error occurred. This could be due to lack of permissions,
            // the data type not being available, or other HealthKit issues.
            string errorMsg = $"Error reading total walking/running distance: {error.localizedDescription} (Code: {error.code})";
            Debug.LogError(errorMsg);
            OnErrorFetchingDistance?.Invoke(errorMsg);
            return;
        }

        // Success! We have the total value.
        // *** IMPORTANT: Assume the value is in METERS. Verify this assumption! ***
        // If BEHealthKit returns miles by default, the conversion factor would be different ( * 1.60934)
        double totalKilometers = totalValue / 1000.0;

        Debug.Log($"Successfully fetched total distance: {totalValue} meters = {totalKilometers:F2} kilometers.");

        // Notify listeners with the result in kilometers
        OnTotalDistanceFetched?.Invoke((int)totalKilometers);
    }

    // Example of how to trigger this from another script or a UI Button
    // You could call this in Start() to run automatically, or link it to a button's onClick event.
    void Start()
    {
        GetTotalDistanceEver();
    }
}