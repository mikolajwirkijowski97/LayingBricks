using UnityEngine;
using BeliefEngine.HealthKit;

public class Game : MonoBehaviour
{
    [SerializeField] private HealthKitManager healthKitManager;
    [SerializeField] private Tower towerData;
    [SerializeField] private TowerInstancedRenderer towerInstancedRenderer;
    [SerializeField] private BrickSpawner towerBrickSpawner;


    void Awake()
    {   
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        if (healthKitManager == null)
        {
            Debug.LogError("HealthKitManager reference is not set in the inspector.");
            return;
        }

        healthKitManager.HealthStore.Authorize(healthKitManager.DataTypes, delegate (bool success){
            if (success)
            {
                Debug.Log("HealthKit authorization successful.");
                InitializeInstancedTower();
                
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

    void InitializeInstancedTower()
    {
        towerInstancedRenderer.TurnOn();

    }
}
