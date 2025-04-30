using BeliefEngine.HealthKit;
using UnityEngine;
using UnityEngine.UIElements;

public class PermissionsUI : MonoBehaviour
{
    private VisualElement root;
    private Button allowButton;
    private Label titleLabel;
    private Label bodyLabel;

    [SerializeField] private GameObject mainGameObject;
    private HealthKitManager healthKitManager; // Reference to the HealthKitManager

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        healthKitManager = FindFirstObjectByType<HealthKitManager>(); // Find the HealthKitManager in the scene
        if (healthKitManager == null)
        {
            Debug.LogError("HealthKitManager not found in the scene. Please ensure it is present.");
            return;
        }

        root = GetComponent<UIDocument>().rootVisualElement;
        allowButton = root.Q<Button>("AllowButton"); // Get the button from the UI document
        titleLabel = root.Q<Label>("TitleLabel"); // Get the title label from the UI document
        bodyLabel = root.Q<Label>("BodyLabel"); // Get the body label from the UI document
        allowButton.SetEnabled(false); // Disable the button initially
        Debug.Log(allowButton); // Log the button for debugging

        allowButton.clicked += () => {
            AuthorizeHealthKit(); // Call the method to authorize HealthKit when the button is clicked
            return;
        };

        InvokeRepeating("AuthorizeHealthKit" , 0f, 3f); // Call the method to authorize HealthKit on start

    }

    // Update is called once per frame
    void Update()
    {

    }

    void OnAuthorizationProceed() {
                var status = healthKitManager.HealthStore.AuthorizationStatusForType(HKDataType.HKQuantityTypeIdentifierDistanceWalkingRunning);

        if(status == HKAuthorizationStatus.SharingAuthorized)
        {
            // The user has authorized the app to read the data type
            // We can now proceed to the game
            Debug.Log("HealthKit authorization status: Authorized");
                CreateMainGame(); // Call the method to create the main game
                Destroy(gameObject); // Destroy the permissions UI after authorization
                Debug.Log("HealthKit authorization successful. Commiting sudoku.");
                // The above method will trigger an event which in result will trigger the tower appearing
        }
        else if (status == HKAuthorizationStatus.SharingDenied)
        {
            // The user has denied the app permission to read the data type
            // Show instruction to enable permissions in settings
            Debug.Log("HealthKit authorization status: Denied");
            titleLabel.text = "Enable Apple Health Access To Continue"; // Change the title label text
            bodyLabel.text = "To calculate your brick rewards, Fit Bricks needs access to your Walking + Running Distance from Apple Health."
            + " It looks like access was previously denied.\n" +
            "1. Go to Settings > Privacy & Security > Health.\n" +
            "2. Tap Stacking Bricks\n" +
            "3. Turn ON \"Walking + Running Distance\"."; // Change the body label text
        }
        else if (status == HKAuthorizationStatus.NotDetermined){
            // Not determined means the user has not yet been asked for permission
            allowButton.SetEnabled(true); // Enable the button to allow the user to authorize
            allowButton.text = "Allow Access"; // Change the button text to indicate action
        }
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
        CreateMainGame(); // Call the method to create the main game
        Destroy(gameObject); // Destroy the permissions UI after authorization
    }

    void CreateMainGame(){
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
