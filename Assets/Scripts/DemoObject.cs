using UnityEngine;

public class DemoObject : MonoBehaviour
{
    public Vector3[] Positions;
    public float TravelDuration = 2f;
    public Vector3 RotationPerFrame = new Vector3(0f, 5f, 0f);

    private int currentPositionIndex = 0;
    private int nextPositionIndex = 1;
    private float startTime;

    void Start()
    {
        // Initialize position
        transform.position = Positions[0];
        startTime = Time.time;
    }

    void Update()
    {
        if (Positions == null || Positions.Length < 2)
        {
            return;
        }

        float timeElapsed = Time.time - startTime;
        float t = Mathf.Clamp01(timeElapsed / TravelDuration); // 0 to 1

        // Interpolate position
        transform.position = Vector3.Lerp(Positions[currentPositionIndex], Positions[nextPositionIndex], t);

        // Apply rotation
        transform.Rotate(RotationPerFrame * Time.deltaTime);

        // Check if reached next position
        if (t >= 1f)
        {
            // Advance to the next positions
            currentPositionIndex = nextPositionIndex;
            nextPositionIndex = (nextPositionIndex + 1) % Positions.Length; // Loop back to start

            // Reset timer
            startTime = Time.time;
        }
    }
}
