using UnityEngine;
using BeliefEngine.HealthKit;
using Gilzoide.KeyValueStore.ICloudKvs;
using System;
using TMPro;

public class Game : MonoBehaviour
{
    [SerializeField] private HealthKitManager healthKitManager;
    [SerializeField] private Tower towerData;
    [SerializeField] private TowerInstancedRenderer towerInstancedRenderer;
    [SerializeField] private BrickSpawner towerBrickSpawner;

    private int unclaimedBricks = 21; // Number of unclaimed bricks
    private int totalDistance = -1; // Total distance in meters
    private DateTimeOffset startDate; // Start date for the distance calculation 

    private ICloudKeyValueStore kvs; // Key-Value Store for iCloud

    // The property is the difference between the total distance and the claimed distance
    public int ClaimedBricks => totalDistance - unclaimedBricks; // Number of claimed bricks

    public TextMeshProUGUI unclaimedBricksTextDisplay;

    public int UnclaimedBricks {
        get => unclaimedBricks; // Getter for unclaimed bricks
        set {
            unclaimedBricks = value; // Setter for unclaimed bricks
            unclaimedBricksTextDisplay.text = unclaimedBricks.ToString(); // Update the unclaimed bricks value in the UI
        }
    }
 
    private bool _isIphone;
    void Awake()
    {   
        _isIphone = Application.platform == RuntimePlatform.IPhonePlayer; // Check if the platform is iOS
        if(_isIphone) kvs = new ICloudKeyValueStore(); // Initialize the Key-Value Store
        SetStartDate(); // Set the start date
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {

        if (!_isIphone) {
            healthKitManager.GetTotalDistanceEver(startDate); // Call the method with no auth, it will return a spoofed value
            return; // If not iOS, exit the method
        }
        // Iphone related things
        if (healthKitManager == null)
        {
            Debug.LogError("HealthKitManager reference is not set in the inspector.");
            return;
        }
        // Get authorization for healthkit
        healthKitManager.HealthStore.Authorize(healthKitManager.DataTypes, delegate (bool success){
            if (success)
            {
                Debug.Log("HealthKit authorization successful.");
                healthKitManager.GetTotalDistanceEver(startDate); // Call the method to fetch the total distance
                // The above method will trigger an event which in result will trigger the tower appearing.

                
            }
            else
            {
                Debug.LogError("HealthKit authorization failed.");
                // Handle the failure case
            }
        }); // Request authorization for HealthKit data types
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    void FetchCalculateUnclaimedBricks(int distance) {
        
        UnclaimedBricks = 21;
        Debug.Log($"Unclaimed bricks: {unclaimedBricks}");
        // If the platform is not iOS, exit the method
        if(!_isIphone) return;


        // Fetch how much distance the player has already claimed.
        if (kvs.TryGetInt("ClaimedDistance", out int claimedBricks)) {
            // If the claimed distance is fetched successfully, calculate the unclaimed bricks
            UnclaimedBricks = distance - claimedBricks; 

            if(UnclaimedBricks < 0) {
                Debug.LogError("Unclaimed bricks cannot be negative. Setting to 0.");
                UnclaimedBricks = 0; // Ensure unclaimed bricks is not negative
            }
            Debug.Log($"Claimed distance: {claimedBricks}");
            Debug.Log($"Fetched distance: {distance}");
            Debug.Log($"Unclaimed bricks: {unclaimedBricks}");

        } else {
            kvs.SetInt("ClaimedBricks", 0); // Set the claimed distance to the fetched distance
            Debug.LogError("Failed to fetch already claimed bricks from iCloud KeyValueStore.");
        }
    }

    void SetStartDate(){
        if(!_isIphone) return; // If not iOS, exit the method

        startDate.ToUnixTimeMilliseconds();
        if(Application.platform == RuntimePlatform.IPhonePlayer && kvs.TryGetLong("StartDate", out long startDateLong)) {
            startDate = DateTimeOffset.FromUnixTimeMilliseconds(startDateLong);
            Debug.Log($"Start date: {startDate}");
        } else {
            startDate = DateTimeOffset.UtcNow; // Set the start date to the current time
            kvs.SetLong("StartDate", startDate.ToUnixTimeMilliseconds()); // Save the start date to iCloud KeyValueStore
            Debug.LogError("Failed to fetch start date from iCloud KeyValueStore. Setting to current time.");
        }
    }

    public void OnHKDistanceFetched(int distance)
    {   
        Debug.Log($"Distance fetched: {distance} meters");
        FetchCalculateUnclaimedBricks(distance);
        totalDistance = distance; // Set the total distance

        // Setup instancer graphics
        towerInstancedRenderer.setInstancedTowerSize(ClaimedBricks); // Set the size of the tower based on claimed bricks
    }

    public void ClaimBricks(){
        // Claim the bricks and update the claimed distance in iCloud KeyValueStore
        if (Application.platform == RuntimePlatform.IPhonePlayer) {
            kvs.SetInt("ClaimedBricks", totalDistance); // Set the claimed distance to the iCloud KeyValueStore
        }
         else {
            Debug.LogError("ClaimBricks method is only available on iOS platform.");
        }
            towerBrickSpawner.AddBricks(unclaimedBricks); // Add the unclaimed bricks to the tower
            UnclaimedBricks = 0;

            Debug.Log($"Claimed bricks are now at: {ClaimedBricks}");
    }

}
