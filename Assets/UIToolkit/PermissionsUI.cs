using UnityEngine;
using UnityEngine.UIElements;

public class PermissionsUI : MonoBehaviour
{
    private VisualElement root;
    private Button allowButton;

    [SerializeField] private GameObject mainGameObject;
    [SerializeField] private HealthKitManager healthKitManager; // Reference to the HealthKitManager

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        root = GetComponent<UIDocument>().rootVisualElement;
        allowButton = root.Q<Button>("AllowButton"); // Get the button from the UI document
        Debug.Log(allowButton); // Log the button for debugging

        allowButton.clicked += () => {
            AuthorizeHealthKit(); // Call the method to authorize HealthKit when the button is clicked
            return;
        };

        AuthorizeHealthKit(); // Call the method to authorize HealthKit on start

    }

    // Update is called once per frame
    void Update()
    {

    }

    void AuthorizeHealthKitIOS()
    {
        // Get authorization for healthkit
        healthKitManager.HealthStore.Authorize(healthKitManager.DataTypes, delegate (bool success){
            if (success)
            {   

                Debug.Log("HealthKit authorization successful.");
                CreateMainGame(); // Call the method to create the main game
                Destroy(gameObject); // Destroy the permissions UI after authorization
                // The above method will trigger an event which in result will trigger the tower appearing.

                
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
        CreateMainGame(); // Call the method to create the main game
        Destroy(gameObject); // Destroy the permissions UI after authorization
    }

    void CreateMainGame(){
         mainGameObject.GetComponent<Game>().HealthKitManager = healthKitManager; // Assign the HealthKitManager to the main game object
        Instantiate(mainGameObject); // Instantiate the main game object after authorization

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
