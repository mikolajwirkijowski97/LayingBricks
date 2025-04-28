using UnityEngine;
using Gilzoide.KeyValueStore.ICloudKvs;
using System;
using UnityEngine.UIElements;
using DG.Tweening;

public class Game : MonoBehaviour
{
    [SerializeField] private HealthKitManager healthKitManager;
    [SerializeField] private Tower towerData;
    [SerializeField] private TowerInstancedRenderer towerInstancedRenderer;
    [SerializeField] private BrickSpawner towerBrickSpawner;

    private int totalDistance = -1; // Total distance in meters
    private DateTimeOffset startDate; // Start date for the distance calculation 

    private ICloudKeyValueStore kvs; // Key-Value Store for iCloud

    // The property is the difference between the total distance and the claimed distance
    public int ClaimedBricks => totalDistance - UnclaimedBricks; // Number of claimed bricks

    public Label UnclaimedBricksLabel;

    public int UnclaimedBricks = 0;
 
    private bool _isIphone;
    void Awake()
    {   
        _isIphone = Application.platform == RuntimePlatform.IPhonePlayer; // Check if the platform is iOS
        if(_isIphone) kvs = new ICloudKeyValueStore(); // Initialize the Key-Value Store
        SetStartDate(); // Set the start date
    }


    private void _StartSpoofed()
    {
       healthKitManager.GetTotalDistanceEver(startDate); // Call the method to fetch the total distance 
       UnclaimedBricks = 21; // Set unclaimed bricks to 0 for spoofed data
    }

    private void _StartIOS()
    {     
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
            }
        });

    }
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {

        if (_isIphone) {
            _StartIOS();
        } else {
            _StartSpoofed();
        }   
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    private void _FetchCalculateUnclaimedBrickIOS(int distance)
    {
        // Fetch how much distance the player has already claimed.
        if (kvs.TryGetInt("ClaimedBricks", out int claimedBricks)) {
            // If the claimed distance is fetched successfully, calculate the unclaimed bricks
            UnclaimedBricks = distance - claimedBricks; 

            if(UnclaimedBricks < 0) {
                Debug.LogError("Unclaimed bricks cannot be negative. Setting to 0.");
                UnclaimedBricks = 0; // Ensure unclaimed bricks is not negative
            }
            Debug.Log($"Claimed distance: {claimedBricks}");
            Debug.Log($"Fetched distance: {distance}");
            Debug.Log($"Unclaimed bricks: {UnclaimedBricks}");

        } else {
            UnclaimedBricks = distance; // If fetching fails, set unclaimed bricks to the fetched distance
            kvs.SetInt("ClaimedBricks", 0); // Set the claimed distance to the fetched distance
            Debug.LogError("Failed to fetch already claimed bricks from iCloud KeyValueStore.");
        }
    }

    private void _FetchCalculateUnclaimedBrickSpoofed(int distance)
    {
        // Spoofed data for testing purposes
        UnclaimedBricks = 21; // Set unclaimed bricks to a fixed value for testing
        Debug.Log($"Unclaimed bricks: {UnclaimedBricks}");
    }

    void FetchCalculateUnclaimedBricks(int distance) {
        if (_isIphone) {
            _FetchCalculateUnclaimedBrickIOS(distance); // Fetch and calculate unclaimed bricks for iOS
        } else {
            _FetchCalculateUnclaimedBrickSpoofed(distance); // Spoofed data for testing
        }  
    }

    void _SetStartDateIOS() {
        if(kvs.TryGetLong("StartDate", out long startDateLong))
        {
            startDate = DateTimeOffset.FromUnixTimeMilliseconds(startDateLong);
            Debug.Log($"Start date: {startDate}");
        }
        else 
        {
            startDate = DateTimeOffset.UtcNow; // Set the start date to the current time
            kvs.SetLong("StartDate", startDate.ToUnixTimeMilliseconds()); // Save the start date to iCloud KeyValueStore
            Debug.LogError("Failed to fetch start date from iCloud KeyValueStore. Setting to current time.");
        }
    }

    void _SetStartDateSpoofed() {
        startDate = new DateTimeOffset(2008, 10, 12, 0, 0, 0, TimeSpan.Zero); // Set the start date to the current time
        Debug.Log($"Start date: {startDate}");
    }

    void SetStartDate()
    {
        if (_isIphone) {
            _SetStartDateIOS(); // Set start date for iOS
        } else {
            _SetStartDateSpoofed(); // Spoofed data for testing
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

    public void _ClaimBricksSpoofed()
    {
        towerBrickSpawner.AddBricks(UnclaimedBricks); // Add the unclaimed bricks to the tower
        // Animate the claimed bricks to 0 using DOTween
        DOTween.To(() => UnclaimedBricks, x => UnclaimedBricks = x, 0, 1f);
        Debug.Log($"Claimed bricks are now at: {ClaimedBricks}");
    }

    public void _ClaimBricksIOS()
    {
        // Claim the bricks and update the claimed distance in iCloud KeyValueStore
        if (kvs.TryGetInt("ClaimedBricks", out int claimedBricks)) {
            kvs.SetInt("ClaimedBricks", totalDistance); // Update the claimed distance in iCloud KeyValueStore
        } else {
            Debug.LogError("Failed to fetch already claimed bricks from iCloud KeyValueStore.");
        }
        towerBrickSpawner.AddBricks(UnclaimedBricks); // Add the unclaimed bricks to the tower
        
        // Animate the claimed bricks to 0 using DOTween
        DOTween.To(() => UnclaimedBricks, x => UnclaimedBricks = x, 0, 1f);

        Debug.Log($"Claimed bricks are now at: {ClaimedBricks}");
    }

    public void ClaimBricks(){
        if (_isIphone) {
            _ClaimBricksIOS(); // Claim bricks for iOS
        } else {
            _ClaimBricksSpoofed(); // Spoofed data for testing
        }
    }



}
