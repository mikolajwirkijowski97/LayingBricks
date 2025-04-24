using UnityEngine;

public class RotatesAroundOriginComponent : MonoBehaviour
{
    private float rotationSpeed = 2f;
    private bool speedRandom = true; 

    void Start()
    {
        if (speedRandom)  rotationSpeed = Random.Range(0, rotationSpeed);
        
    }

    // Update is called once per frame
    void Update()
    {
        // Rotate the object around the origin (0, 0, 0) at a speed of 10 degrees per second
        transform.RotateAround(Vector3.zero, Vector3.up, rotationSpeed * Time.deltaTime);   
    }
}
