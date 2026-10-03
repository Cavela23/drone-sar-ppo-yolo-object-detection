using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class DroneAgentTesting : MonoBehaviour
{
    [Header("Drone Movement Settings")]
    public float maxForwardSpeed = 6f;
    public float rotationSpeed = 160f;
    public float acceleration = 25f;

    [Header("=== TEST MODE SETTINGS ===")]
    public bool manualControl = true;
    public bool autoPilot = false;
    public float autoPilotForwardInput = 0.7f;
    public float autoPilotRotationInput = 0.3f;
    public float autoPilotDirectionChangeTime = 3f;
    public bool autoRestartTest = true;

    [Header("=== VISUAL FEEDBACK ===")]
    public bool showDebugInfo = true;
    public bool showVisitedCells = true;
    public bool showRaycasts = true;
    public Color safeColor = Color.green;
    public Color warningColor = Color.yellow;
    public Color dangerColor = Color.red;

    [Header("=== TEST PARAMETERS ===")]
    public float proximityThreshold = 3.0f;
    public int testMaxPhysicsTicks = 1000;

    [Header("References")]
    public DroneRaycastSensor raycastSensor;
    public ForestObstacleGenerator obstacleGenerator;

    // Private components
    private Rigidbody rb;
    private Vector3 startPosition;
    private Quaternion startRotation;

    // Movement tracking
    private float lastForwardInput = 0f;
    private float lastRotateInput = 0f;
    private Vector3 targetVelocity;

    // Test metrics
    private int currentPhysicsTick = 0;
    private int collisionCount = 0;
    private float totalDistanceTraveled = 0f;
    private Vector3 lastPosition;
    private float startTime;
    private bool testRunning = true;

    // Exploration tracking (for visualization)
    private Dictionary<Vector3Int, bool> visitedCells = new Dictionary<Vector3Int, bool>();
    private Vector3Int currentCell;

    // Auto-pilot
    private float autoPilotTimer = 0f;
    private float currentAutoRotation = 0f;
    private bool avoidingOnLeft = false;

    // Proximity tracking
    private float closestObstacleDistance = Mathf.Infinity;
    private bool isInDangerZone = false;
    private Vector3 obstacleAvoidanceDirection = Vector3.zero;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        startPosition = transform.position;
        startRotation = transform.rotation;

        // Rigidbody setup
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.useGravity = false;
        rb.drag = 0f;
        rb.angularDrag = 0f;
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

        if (raycastSensor == null)
            raycastSensor = GetComponent<DroneRaycastSensor>();

        // Initialize test
        InitializeTest();
    }

    void InitializeTest()
    {
        currentPhysicsTick = 0;
        collisionCount = 0;
        totalDistanceTraveled = 0f;
        lastPosition = transform.position;
        startTime = Time.time;
        visitedCells.Clear();
        testRunning = true;
        avoidingOnLeft = Random.value > 0.5f;

        // 🔴 SOLUSI BERSIH: Biarkan ray length diatur dari INSPECTOR saja
        // Tidak ada override di code, WYSIWYG (What You See Is What You Get)

        Debug.Log($"🚀 TEST MODE STARTED");
        Debug.Log($"   • Manual Control: {manualControl}");
        Debug.Log($"   • Auto Pilot: {autoPilot}");
        Debug.Log($"   • Max Physics Ticks: {testMaxPhysicsTicks}");
        Debug.Log($"   • Proximity Threshold: {proximityThreshold}m");
        Debug.Log($"   • Auto Restart: {autoRestartTest}");
        Debug.Log($"   • Ray Length: {(raycastSensor != null ? "Set in Inspector" : "No sensor")}");
    }

    void Update()
    {
        if (!testRunning) return;

        // Handle manual input
        HandleInput();

        // Update test metrics
        UpdateTestMetrics();

        // Update proximity detection
        UpdateProximityDetection();

        // Draw debug visuals
        if (showDebugInfo)
        {
            DrawDebugVisuals();
        }
    }

    void FixedUpdate()
    {
        if (!testRunning) return;

        currentPhysicsTick++;

        // Apply movement
        ApplyMovement();

        // Update auto-pilot
        if (autoPilot && !manualControl)
        {
            UpdateAutoPilot();
        }

        // Check test completion
        if (currentPhysicsTick >= testMaxPhysicsTicks)
        {
            CompleteTest($"Max physics ticks reached ({testMaxPhysicsTicks})");
            if (autoRestartTest)
            {
                InitializeTest();
            }
        }
    }

    void HandleInput()
    {
        if (!manualControl) return;

        lastForwardInput = 0f;
        lastRotateInput = 0f;

        // Forward/Backward
        if (Input.GetKey(KeyCode.W))
            lastForwardInput = 1f;
        else if (Input.GetKey(KeyCode.S))
            lastForwardInput = -0.5f;

        // Rotation
        if (Input.GetKey(KeyCode.A))
            lastRotateInput = -0.5f;
        else if (Input.GetKey(KeyCode.D))
            lastRotateInput = 0.5f;

        // Reset position
        if (Input.GetKeyDown(KeyCode.R))
            ResetToStart();

        // Toggle auto-pilot
        if (Input.GetKeyDown(KeyCode.P))
        {
            autoPilot = !autoPilot;
            Debug.Log($"Auto-pilot: {(autoPilot ? "ON" : "OFF")}");
        }

        // Generate new obstacles
        if (Input.GetKeyDown(KeyCode.G) && obstacleGenerator != null)
        {
            obstacleGenerator.GenerateForestNow();
            Debug.Log("Generated new obstacles");
        }

        // Toggle auto-restart
        if (Input.GetKeyDown(KeyCode.T))
        {
            autoRestartTest = !autoRestartTest;
            Debug.Log($"Auto-restart: {(autoRestartTest ? "ON" : "OFF")}");
        }
    }

    void UpdateAutoPilot()
    {
        autoPilotTimer += Time.deltaTime;

        // Basic wandering behavior
        if (autoPilotTimer >= autoPilotDirectionChangeTime)
        {
            autoPilotTimer = 0f;
            currentAutoRotation = Random.Range(-0.5f, 0.5f);
            avoidingOnLeft = Random.value > 0.5f;
        }

        // Obstacle-aware avoidance (basic)
        if (closestObstacleDistance < proximityThreshold)
        {
            if (closestObstacleDistance < proximityThreshold * 0.3f)
            {
                // Emergency: terlalu dekat, mundur dan belok
                lastForwardInput = -0.3f;

                // Belok berdasarkan arah yang ditentukan
                if (avoidingOnLeft)
                    lastRotateInput = 0.8f;
                else
                    lastRotateInput = -0.8f;
            }
            else
            {
                // Preventive: kurangi kecepatan dan belok perlahan
                lastForwardInput = Mathf.Lerp(0.3f, autoPilotForwardInput,
                    closestObstacleDistance / proximityThreshold);

                // Belok menjauhi obstacle
                if (avoidingOnLeft)
                    lastRotateInput = 0.5f * autoPilotRotationInput;
                else
                    lastRotateInput = -0.5f * autoPilotRotationInput;
            }
        }
        else
        {
            // Normal wandering
            lastForwardInput = autoPilotForwardInput;
            lastRotateInput = currentAutoRotation * autoPilotRotationInput;
        }

        // Visual: Debug obstacle avoidance direction
        if (showDebugInfo && closestObstacleDistance < proximityThreshold)
        {
            obstacleAvoidanceDirection = transform.right * (avoidingOnLeft ? 1f : -1f);
        }
    }

    void ApplyMovement()
    {
        if (manualControl && !autoPilot)
        {
            // Use manual input
        }
        else if (autoPilot)
        {
            // Use auto-pilot input
        }
        else
        {
            // No input - hover in place
            lastForwardInput = 0f;
            lastRotateInput = 0f;
        }

        // Calculate target velocity
        float moveForward = Mathf.Clamp(lastForwardInput, -0.5f, 1f);
        targetVelocity = transform.forward * (moveForward * maxForwardSpeed);

        // Smooth velocity
        Vector3 currentHorizontalVel = new Vector3(rb.velocity.x, 0, rb.velocity.z);
        Vector3 newVelocity = Vector3.Lerp(currentHorizontalVel, targetVelocity, acceleration * Time.fixedDeltaTime);
        rb.velocity = new Vector3(newVelocity.x, rb.velocity.y, newVelocity.z);

        // Apply rotation
        if (Mathf.Abs(lastRotateInput) > 0.05f)
        {
            float rotationAmount = lastRotateInput * rotationSpeed * Time.fixedDeltaTime;
            Quaternion deltaRotation = Quaternion.Euler(0f, rotationAmount, 0f);
            rb.MoveRotation(rb.rotation * deltaRotation);
        }

        // Record visited cell for visualization
        RecordVisitedCell();
    }

    void UpdateTestMetrics()
    {
        // Calculate distance traveled
        float distanceThisFrame = Vector3.Distance(transform.position, lastPosition);
        if (distanceThisFrame > 0.01f)
        {
            totalDistanceTraveled += distanceThisFrame;
            lastPosition = transform.position;
        }

        // Update proximity warning
        if (closestObstacleDistance < proximityThreshold)
        {
            isInDangerZone = true;
        }
        else if (closestObstacleDistance > proximityThreshold * 1.2f)
        {
            isInDangerZone = false;
        }
    }

    void UpdateProximityDetection()
    {
        if (raycastSensor != null)
        {
            closestObstacleDistance = raycastSensor.GetClosestObstacleDistance();

            if (showDebugInfo && Time.frameCount % 30 == 0 && closestObstacleDistance < proximityThreshold)
            {
                Debug.Log($"⚠️ Proximity: {closestObstacleDistance:F2}m / {proximityThreshold}m");
            }
        }
        else
        {
            // Fallback: simple raycast forward
            RaycastHit hit;
            if (Physics.Raycast(transform.position, transform.forward, out hit, 10f))
            {
                closestObstacleDistance = hit.distance;
            }
            else
            {
                closestObstacleDistance = 10f;
            }
        }
    }

    void RecordVisitedCell()
    {
        if (!showVisitedCells) return;

        Vector3Int cell = new Vector3Int(
            Mathf.RoundToInt(transform.position.x / 2f),
            0,
            Mathf.RoundToInt(transform.position.z / 2f)
        );

        if (!cell.Equals(currentCell))
        {
            currentCell = cell;
            if (!visitedCells.ContainsKey(cell))
            {
                visitedCells[cell] = true;
            }
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        if (!testRunning) return;

        if (collision.gameObject.CompareTag("Obstacle") ||
            collision.gameObject.CompareTag("Wall"))
        {
            collisionCount++;

            Debug.Log($"💥 COLLISION #{collisionCount} with {collision.gameObject.name}");
            Debug.Log($"   Impact force: {collision.impulse.magnitude:F2}");
            Debug.Log($"   Position: {transform.position}");
            Debug.Log($"   Velocity before: {rb.velocity.magnitude:F2}m/s");

            // Small bounce for visual feedback ONLY
            rb.velocity = -rb.velocity * 0.3f;

            Debug.Log($"   Velocity after: {rb.velocity.magnitude:F2}m/s");
            Debug.Log($"   [NOTE] This bounce is for testing only, not for ML training");
        }
    }

    void ResetToStart()
    {
        transform.position = startPosition;
        transform.rotation = startRotation;
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        Debug.Log("🔄 Reset to start position");
    }

    void CompleteTest(string reason)
    {
        if (!testRunning) return;

        testRunning = false;
        float testDuration = Time.time - startTime;
        float avgSpeed = totalDistanceTraveled / Mathf.Max(testDuration, 0.1f);

        Debug.Log($"🏁 TEST COMPLETE: {reason}");
        Debug.Log($"   Duration: {testDuration:F1}s");
        Debug.Log($"   Distance: {totalDistanceTraveled:F1}m");
        Debug.Log($"   Avg Speed: {avgSpeed:F2}m/s");
        Debug.Log($"   Collisions: {collisionCount}");
        Debug.Log($"   Cells Visited: {visitedCells.Count}");
        Debug.Log($"   Physics Ticks: {currentPhysicsTick}");
        Debug.Log($"   Average FPS: {currentPhysicsTick / Mathf.Max(testDuration, 0.1f):F1}");

        if (autoRestartTest)
        {
            Debug.Log("↻ Auto-restarting test in 1 second...");
            StartCoroutine(DelayedRestart(1f));
        }
    }

    IEnumerator DelayedRestart(float delay)
    {
        yield return new WaitForSeconds(delay);
        InitializeTest();
    }

    void DrawDebugVisuals()
    {
        // Draw forward ray
        if (showRaycasts)
        {
            Color rayColor = safeColor;
            if (closestObstacleDistance < proximityThreshold * 0.3f)
                rayColor = dangerColor;
            else if (closestObstacleDistance < proximityThreshold)
                rayColor = warningColor;

            Debug.DrawRay(transform.position,
                transform.forward * Mathf.Min(closestObstacleDistance, 10f),
                rayColor);
        }

        // Draw velocity
        Debug.DrawRay(transform.position, rb.velocity.normalized * 2f, Color.red);

        // Draw target velocity
        Debug.DrawRay(transform.position, targetVelocity.normalized * 1.5f, Color.green);

        // Draw obstacle avoidance direction
        if (autoPilot && closestObstacleDistance < proximityThreshold)
        {
            Debug.DrawRay(transform.position, obstacleAvoidanceDirection * 2f, Color.cyan);
        }

        // Draw proximity indicator
        Debug.DrawRay(transform.position, Vector3.up * 0.5f,
            isInDangerZone ? Color.red : Color.green);
    }

    void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying) return;

        // Draw visited cells
        if (showVisitedCells)
        {
            Gizmos.color = new Color(0, 1, 0, 0.2f);
            foreach (var cell in visitedCells.Keys)
            {
                Vector3 worldPos = new Vector3(cell.x * 2f, 0.1f, cell.z * 2f);
                Gizmos.DrawCube(worldPos, new Vector3(1.8f, 0.1f, 1.8f));
            }
        }

        // Draw proximity threshold sphere
        Gizmos.color = new Color(1, 0.5f, 0, 0.1f);
        Gizmos.DrawWireSphere(transform.position, proximityThreshold);

        // Draw closest obstacle distance
        if (closestObstacleDistance < Mathf.Infinity && closestObstacleDistance < 10f)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(transform.position, closestObstacleDistance);
        }
    }

    [ContextMenu("Print Test Status")]
    public void PrintTestStatus()
    {
        float testDuration = Time.time - startTime;
        float avgSpeed = totalDistanceTraveled / Mathf.Max(testDuration, 0.1f);

        string status = $"📊 TEST STATUS\n";
        status += $"   Running: {testRunning}\n";
        status += $"   Physics Ticks: {currentPhysicsTick}/{testMaxPhysicsTicks}\n";
        status += $"   Duration: {testDuration:F1}s\n";
        status += $"   Distance: {totalDistanceTraveled:F1}m\n";
        status += $"   Avg Speed: {avgSpeed:F2}m/s\n";
        status += $"   Collisions: {collisionCount}\n";
        status += $"   Cells Visited: {visitedCells.Count}\n";
        status += $"   Closest Obstacle: {closestObstacleDistance:F2}m\n";
        status += $"   Danger Zone: {(isInDangerZone ? "YES ⚠️" : "NO ✅")}\n";
        status += $"   Control: {(manualControl ? "MANUAL" : (autoPilot ? "AUTO-PILOT" : "NONE"))}\n";
        status += $"   Auto-restart: {(autoRestartTest ? "ON" : "OFF")}";

        Debug.Log(status);
    }

    [ContextMenu("Reset Test")]
    public void ResetTest()
    {
        ResetToStart();
        InitializeTest();
    }

    [ContextMenu("Toggle Auto-Pilot")]
    public void ToggleAutoPilot()
    {
        autoPilot = !autoPilot;
        Debug.Log($"Auto-pilot: {(autoPilot ? "ON" : "OFF")}");
    }

    [ContextMenu("Toggle Auto-Restart")]
    public void ToggleAutoRestart()
    {
        autoRestartTest = !autoRestartTest;
        Debug.Log($"Auto-restart: {(autoRestartTest ? "ON" : "OFF")}");
    }

    [ContextMenu("Generate New Obstacles")]
    public void GenerateNewObstacles()
    {
        if (obstacleGenerator != null)
        {
            obstacleGenerator.GenerateForestNow();
            Debug.Log("Generated new obstacles");
        }
        else
        {
            Debug.LogError("No obstacle generator assigned!");
        }
    }
}