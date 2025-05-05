using Gilzoide.KeyValueStore.ICloudKvs;
using UnityEngine;
using UnityEngine.UIElements;

public class PermissionsUI : MonoBehaviour
{
    private VisualElement root;
    private Button allowButton;


    [SerializeField] private GameObject mainGameObject;
    private HealthKitManager healthKitManager; // Reference to the HealthKitManager

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {

        var kvs = new ICloudKeyValueStore();
        if(Application.platform != RuntimePlatform.IPhonePlayer || kvs.TryGetBool("FirstLaunch", out bool isFirstLaunch)){
            GoToMainGame();
            return;
        }


        healthKitManager = FindFirstObjectByType<HealthKitManager>(); // Find the HealthKitManager in the scene
        if (healthKitManager == null)
        {
            Debug.LogError("HealthKitManager not found in the scene. Please ensure it is present.");
            return;
        }

        root = GetComponent<UIDocument>().rootVisualElement;
        allowButton = root.Q<Button>("AllowButton"); // Get the button from the UI document
        allowButton.SetEnabled(false); // Disable the button initially
        Debug.Log(allowButton); // Log the button for debugging

        allowButton.clicked += () => {
            AuthorizeHealthKit(); // Call the method to authorize HealthKit when the button is clicked
        };

        InvokeRepeating("AuthorizeHealthKit" , 0f, 3f); // Call the method to authorize HealthKit on start

    }

    // Update is called once per frame
    void Update()
    {

    }

    void OnAuthorizationProceed() {
        ICloudKeyValueStore kvs = new ICloudKeyValueStore();
        kvs.SetBool("FirstLaunch", false);
    }

    void AuthorizeHealthKitIOS()
    {
        // Get authorization for healthkit
        healthKitManager.HealthStore.Authorize(healthKitManager.DataTypes, delegate (bool success){
            if (success)
            {
                OnAuthorizationProceed();
            }
            else
            {
                Debug.LogError("HealthKit authorization failed. ");
            }
        });
    }

    void AuthorizeHealthKitSpoofed()
    {
        // Simulate authorization for spoofed mode
        Debug.Log("HealthKit authorization successful (spoofed mode).");
        GoToMainGame(); // Call the method to create the main game
    }

    void GoToMainGame(){
        Instantiate(mainGameObject); // Instantiate the main game object after authorization
        Destroy(gameObject); // Destroy the permissions UI after authorization
    }

    void AuthorizeHealthKit()
    {
        if (Application.platform == RuntimePlatform.IPhonePlayer)
        {
            AuthorizeHealthKitIOS(); // Call the iOS authorization method
        }
        else
        {
            AuthorizeHealthKitSpoofed(); // Call the spoofed authorization method
        }
    }
}
