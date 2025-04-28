using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

public class MainUI : MonoBehaviour
{

    Game game; // Reference to the Game script

    Label unclaimedBricksLabel; // Label to display the number of unclaimed bricks
    Label claimedBricksLabel; // Label to display the number of claimed bricks

    VisualElement root;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void OnEnable()
    {
        // Find the Game script in the scene
        game = FindFirstObjectByType<Game>(); // Find the Game script in the scene
        Assert.IsNotNull(game, "Game script not found in the scene."); // Assert that the Game script is not null
        
        // Get the root visual element from the UIDocument component
        root = GetComponent<UIDocument>().rootVisualElement;
        // Connect button to the claim bricks function
        Button claimBricksButton = root.Q<Button>("ClaimButton"); // Get the button from the UI
        Assert.IsNotNull(claimBricksButton, "Claim button not found in the UI."); // Assert that the button is not null
        claimBricksButton.clicked += () => {
            game.ClaimBricks(); // Call the ClaimBricks function when the button is clicked
            Debug.Log("Claim bricks clicked"); // Log the number of claimed bricks
        };

        // Set the label text to show the number of unclaimed bricks
        unclaimedBricksLabel = root.Q<Label>("UnclaimedBricksLabel"); // Get the label from the UI
        Assert.IsNotNull(unclaimedBricksLabel, "Unclaimed bricks label not found in the UI."); // Assert that the label is not null

        claimedBricksLabel = root.Q<Label>("ClaimedBricksLabel"); // Get the label from the UI
        Assert.IsNotNull(claimedBricksLabel, "Claimed bricks label not found in the UI."); // Assert that the label is not null
    }

    // Update is called once per frame
    void Update()
    {
        unclaimedBricksLabel.text = game.UnclaimedBricks.ToString(); // Update the label text with the number of unclaimed bricks
        claimedBricksLabel.text = game.ClaimedBricks.ToString(); // Update the label text with the number of claimed bricks
        
    }
}
