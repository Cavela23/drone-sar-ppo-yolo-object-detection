using UnityEngine;
using System.Collections.Generic;

public class DroneRaycastSensorTesting : MonoBehaviour
{
    [Header("=== TEST CONFIGURATION ===")]
    public bool autoTestOnStart = true;
    public float testDuration = 5f;
    public int testIterations = 100;

    [Header("=== RAYCAST SETTINGS (Overrides) ===")]
    public int testRayCount = 8;
    public float testRayLength = 15f;
    public float testRayAngle = 360f;
    [Tooltip("Note: rayAngle is defined but sensor may ignore it")]

    [Header("=== VISUALIZATION ===")]
    public bool showTestRays = true;
    public bool showHitPoints = true;
    public bool showStatistics = true;
    public Color testHitColor = Color.red;
    public Color testMissColor = Color.green;
    public Color statisticsColor = Color.yellow;

    [Header("=== TEST TARGETS ===")]
    public GameObject testTargetPrefab;
    public int testTargetCount = 5;
    public float testAreaRadius = 10f;

    [Header("References")]
    public DroneRaycastSensor raycastSensor;

    // Test variables
    private List<GameObject> testTargets = new List<GameObject>();
    private bool isTesting = false;
    private float testStartTime;
    private int currentIteration = 0;

    // Statistics
    private int totalRaycasts = 0;
    private int totalHits = 0;
    private float minHitDistance = Mathf.Infinity;
    private float maxHitDistance = 0f;
    private float totalHitDistance = 0f;
    private Dictionary<int, int> rayHitDistribution = new Dictionary<int, int>();

    void Start()
    {
        if (raycastSensor == null)
        {
            raycastSensor = GetComponent<DroneRaycastSensor>();
        }

        if (raycastSensor == null)
        {
            Debug.LogError("❌ DroneRaycastSensorTesting: No raycast sensor found!");
            return;
        }

        // Setup test environment
        SetupTestEnvironment();

        if (autoTestOnStart)
        {
            StartTest();
        }
    }

    void Update()
    {
        if (isTesting)
        {
            RunTestIteration();

            // Check if test is complete
            if (Time.time - testStartTime >= testDuration || currentIteration >= testIterations)
            {
                CompleteTest();
            }
        }

        // Manual test controls
        if (Input.GetKeyDown(KeyCode.T))
        {
            if (!isTesting)
                StartTest();
            else
                CompleteTest();
        }

        if (Input.GetKeyDown(KeyCode.C))
        {
            ClearTestTargets();
            SetupTestEnvironment();
        }

        if (Input.GetKeyDown(KeyCode.R))
        {
            ResetTest();
        }
    }

    void FixedUpdate()
    {
        // Run raycast updates in FixedUpdate for consistency
        if (isTesting)
        {
            // Optional: Move drone or targets for dynamic testing
            // MoveTestTargets();
        }
    }

    void SetupTestEnvironment()
    {
        ClearTestTargets();

        // Create test targets
        for (int i = 0; i < testTargetCount; i++)
        {
            Vector3 randomPos = transform.position +
                new Vector3(
                    Random.Range(-testAreaRadius, testAreaRadius),
                    0.5f,
                    Random.Range(-testAreaRadius, testAreaRadius)
                );

            GameObject target;
            if (testTargetPrefab != null)
            {
                target = Instantiate(testTargetPrefab, randomPos, Quaternion.identity);
            }
            else
            {
                target = GameObject.CreatePrimitive(PrimitiveType.Cube);
                target.transform.position = randomPos;
                target.transform.localScale = new Vector3(1f, 1f, 1f);

                // Set obstacle tag and layer
                target.tag = "Obstacle";
                target.layer = LayerMask.NameToLayer("Obstacle");

                // Add collider if not present
                if (target.GetComponent<Collider>() == null)
                {
                    target.AddComponent<BoxCollider>();
                }

                // Set color
                var renderer = target.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.material.color = Color.cyan;
                }
            }

            target.name = $"TestTarget_{i}";
            testTargets.Add(target);
        }

        // Configure sensor with test settings
        if (raycastSensor != null)
        {
            // Use reflection to set sensor properties safely
            SetSensorProperty("rayCount", testRayCount);
            SetSensorProperty("rayLength", testRayLength);

            // Note: rayAngle might not be directly settable in original sensor
            // We'll work with whatever angle is set

            Debug.Log($"✅ Test Environment Setup:");
            Debug.Log($"   • Targets: {testTargets.Count}");
            Debug.Log($"   • Ray Count: {testRayCount}");
            Debug.Log($"   • Ray Length: {testRayLength}m");
            Debug.Log($"   • Test Area: {testAreaRadius}m radius");
        }
    }

    void SetSensorProperty(string propertyName, object value)
    {
        if (raycastSensor == null) return;

        var prop = raycastSensor.GetType().GetProperty(propertyName);
        if (prop != null && prop.CanWrite)
        {
            prop.SetValue(raycastSensor, value);
            return;
        }

        var field = raycastSensor.GetType().GetField(propertyName);
        if (field != null)
        {
            field.SetValue(raycastSensor, value);
        }
    }

    void StartTest()
    {
        if (raycastSensor == null) return;

        isTesting = true;
        testStartTime = Time.time;
        currentIteration = 0;

        ResetStatistics();

        Debug.Log($"🧪 TEST STARTED");
        Debug.Log($"   • Duration: {testDuration}s");
        Debug.Log($"   • Iterations: {testIterations}");
        Debug.Log($"   • Sensor: {raycastSensor.GetType().Name}");

        // Enable debug visualization on sensor
        var setDebugMethod = raycastSensor.GetType().GetMethod("SetDebugRays");
        if (setDebugMethod != null)
        {
            setDebugMethod.Invoke(raycastSensor, new object[] { showTestRays });
        }
    }

    void RunTestIteration()
    {
        if (raycastSensor == null) return;

        currentIteration++;
        totalRaycasts += testRayCount;

        // Get raycast data
        float[] rayDistances = raycastSensor.UpdateRays();

        // Process results
        if (rayDistances != null && rayDistances.Length > 0)
        {
            for (int i = 0; i < Mathf.Min(rayDistances.Length, testRayCount); i++)
            {
                float normalizedDist = rayDistances[i];
                float worldDistance = normalizedDist * testRayLength;

                if (normalizedDist < 1f) // Hit detected
                {
                    totalHits++;
                    totalHitDistance += worldDistance;

                    minHitDistance = Mathf.Min(minHitDistance, worldDistance);
                    maxHitDistance = Mathf.Max(maxHitDistance, worldDistance);

                    // Update hit distribution
                    if (rayHitDistribution.ContainsKey(i))
                        rayHitDistribution[i]++;
                    else
                        rayHitDistribution[i] = 1;

                    // Visualize hit
                    if (showHitPoints)
                    {
                        Vector3 hitPoint = GetRayHitPoint(i, worldDistance);
                        Debug.DrawLine(transform.position, hitPoint, testHitColor, 0.1f);
                        DebugDrawSphere(hitPoint, 0.2f, testHitColor, 0.1f);
                    }
                }
            }
        }

        // Log progress periodically
        if (currentIteration % 10 == 0 && showStatistics)
        {
            LogTestProgress();
        }
    }

    Vector3 GetRayHitPoint(int rayIndex, float distance)
    {
        if (raycastSensor == null) return transform.position;

        // Get ray directions
        var directionsMethod = raycastSensor.GetType().GetMethod("GetAllRayDirections");
        if (directionsMethod != null)
        {
            Vector3[] directions = (Vector3[])directionsMethod.Invoke(raycastSensor, null);
            if (directions != null && rayIndex < directions.Length)
            {
                // Get ray origin
                var originMethod = raycastSensor.GetType().GetMethod("GetRayOriginWorldPosition");
                if (originMethod != null)
                {
                    Vector3 origin = (Vector3)originMethod.Invoke(raycastSensor, null);
                    return origin + directions[rayIndex] * distance;
                }
            }
        }

        // Fallback
        return transform.position + transform.forward * distance;
    }

    void LogTestProgress()
    {
        float progress = Mathf.Clamp01((Time.time - testStartTime) / testDuration);
        float hitRate = totalRaycasts > 0 ? (float)totalHits / totalRaycasts * 100f : 0f;
        float avgHitDistance = totalHits > 0 ? totalHitDistance / totalHits : 0f;

        string progressBar = GetProgressBar(progress, 20);

        string progressText = string.Format("📊 Test Progress: {0} {1:P0}", progressBar, progress);
        Debug.Log(progressText);

        Debug.Log($"   • Iterations: {currentIteration}/{testIterations}");
        Debug.Log($"   • Raycasts: {totalRaycasts}");
        Debug.Log($"   • Hits: {totalHits} ({hitRate:F1}%)");
        Debug.Log($"   • Avg Hit Distance: {avgHitDistance:F2}m");
        Debug.Log($"   • Min/Max Distance: {minHitDistance:F2}m / {maxHitDistance:F2}m");
    }

    string GetProgressBar(float progress, int length)
    {
        int filled = Mathf.RoundToInt(progress * length);
        int empty = length - filled;

        string bar = "[";
        bar += new string('█', filled);
        bar += new string('░', empty);
        bar += "]";

        return bar;
    }

    void CompleteTest()
    {
        if (!isTesting) return;

        isTesting = false;
        float testTime = Time.time - testStartTime;

        // Calculate final statistics
        float hitRate = totalRaycasts > 0 ? (float)totalHits / totalRaycasts * 100f : 0f;
        float avgHitDistance = totalHits > 0 ? totalHitDistance / totalHits : 0f;
        float raysPerSecond = totalRaycasts / Mathf.Max(testTime, 0.1f);

        Debug.Log($"🏁 TEST COMPLETE");
        Debug.Log($"   • Total Time: {testTime:F2}s");
        Debug.Log($"   • Iterations: {currentIteration}");
        Debug.Log($"   • Total Raycasts: {totalRaycasts}");
        Debug.Log($"   • Rays/Second: {raysPerSecond:F0}");
        Debug.Log($"   • Hit Rate: {hitRate:F1}%");
        Debug.Log($"   • Average Hit Distance: {avgHitDistance:F2}m");
        Debug.Log($"   • Distance Range: {minHitDistance:F2}m - {maxHitDistance:F2}m");

        // Log ray hit distribution
        if (rayHitDistribution.Count > 0 && showStatistics)
        {
            Debug.Log("   • Ray Hit Distribution:");
            for (int i = 0; i < testRayCount; i++)
            {
                int hits = rayHitDistribution.ContainsKey(i) ? rayHitDistribution[i] : 0;
                float rayHitRate = currentIteration > 0 ? (float)hits / currentIteration * 100f : 0f;
                Debug.Log($"      Ray {i}: {hits} hits ({rayHitRate:F1}%)");
            }
        }

        // Test sensor-specific functionality
        TestSensorFeatures();
    }

    void TestSensorFeatures()
    {
        if (raycastSensor == null) return;

        Debug.Log("🔍 Sensor Feature Tests:");

        try
        {
            // Test GetClosestObstacleDistance
            float closestDist = raycastSensor.GetClosestObstacleDistance();
            Debug.Log($"   • Closest Obstacle: {closestDist:F2}m");

            // Test GetClosestObstacleRayIndex
            int closestIndex = raycastSensor.GetClosestObstacleRayIndex();
            Debug.Log($"   • Closest Ray Index: {closestIndex}");

            // Test HasObstacleInFront
            bool hasFrontObstacle = raycastSensor.HasObstacleInFront(3f);
            Debug.Log($"   • Obstacle in Front (<3m): {hasFrontObstacle}");

            // Test GetRayCountInRange
            int raysInRange = raycastSensor.GetRayCountInRange(1f, 5f);
            Debug.Log($"   • Rays in 1-5m range: {raysInRange}");

            // Test specific direction raycast
            float forwardDist = raycastSensor.GetRayDistanceInDirection(transform.forward);
            Debug.Log($"   • Forward Ray Distance: {forwardDist:F2}m");

            Debug.Log("✅ All sensor features functional");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"❌ Sensor test failed: {e.Message}");
        }
    }

    void ResetStatistics()
    {
        totalRaycasts = 0;
        totalHits = 0;
        minHitDistance = Mathf.Infinity;
        maxHitDistance = 0f;
        totalHitDistance = 0f;
        rayHitDistribution.Clear();
    }

    void ClearTestTargets()
    {
        foreach (var target in testTargets)
        {
            if (target != null)
                Destroy(target);
        }
        testTargets.Clear();
    }

    void ResetTest()
    {
        isTesting = false;
        ResetStatistics();
        ClearTestTargets();
        SetupTestEnvironment();

        Debug.Log("🔄 Test reset");
    }

    void MoveTestTargets()
    {
        // Simple movement for dynamic testing
        foreach (var target in testTargets)
        {
            if (target != null)
            {
                Vector3 pos = target.transform.position;
                pos.x += Mathf.Sin(Time.time + target.GetInstanceID()) * 0.01f;
                pos.z += Mathf.Cos(Time.time + target.GetInstanceID()) * 0.01f;
                target.transform.position = pos;
            }
        }
    }

    // Helper method to draw debug sphere (Unity doesn't have Debug.DrawSphere)
    void DebugDrawSphere(Vector3 center, float radius, Color color, float duration)
    {
        int segments = 12;
        float angleIncrement = 360f / segments;

        for (int i = 0; i < segments; i++)
        {
            float angle1 = i * angleIncrement * Mathf.Deg2Rad;
            float angle2 = (i + 1) * angleIncrement * Mathf.Deg2Rad;

            Vector3 point1 = center + new Vector3(Mathf.Cos(angle1), 0, Mathf.Sin(angle1)) * radius;
            Vector3 point2 = center + new Vector3(Mathf.Cos(angle2), 0, Mathf.Sin(angle2)) * radius;

            Debug.DrawLine(point1, point2, color, duration);
        }

        // Vertical circles
        for (int i = 0; i < segments; i++)
        {
            float angle1 = i * angleIncrement * Mathf.Deg2Rad;
            float angle2 = (i + 1) * angleIncrement * Mathf.Deg2Rad;

            Vector3 point1 = center + new Vector3(Mathf.Cos(angle1), Mathf.Sin(angle1), 0) * radius;
            Vector3 point2 = center + new Vector3(Mathf.Cos(angle2), Mathf.Sin(angle2), 0) * radius;

            Debug.DrawLine(point1, point2, color, duration);
        }
    }

    [ContextMenu("Start Test")]
    public void StartTestCommand()
    {
        StartTest();
    }

    [ContextMenu("Stop Test")]
    public void StopTestCommand()
    {
        CompleteTest();
    }

    [ContextMenu("Reset Test")]
    public void ResetTestCommand()
    {
        ResetTest();
    }

    [ContextMenu("Generate Test Report")]
    public void GenerateTestReport()
    {
        if (raycastSensor == null) return;

        string report = $"📋 RAYCAST SENSOR TEST REPORT\n";
        report += $"===============================\n";
        report += $"Timestamp: {System.DateTime.Now}\n";
        report += $"Sensor: {raycastSensor.GetType().Name}\n";
        report += $"Ray Count: {testRayCount}\n";
        report += $"Ray Length: {testRayLength}m\n";
        report += $"Test Targets: {testTargets.Count}\n";
        report += $"Test Area: {testAreaRadius}m radius\n";
        report += $"\n";
        report += $"Performance:\n";
        float hitRate = totalRaycasts > 0 ? (float)totalHits / totalRaycasts * 100f : 0f;
        report += $"• Total Raycasts: {totalRaycasts}\n";
        report += $"• Hit Rate: {hitRate:F1}%\n";
        float avgHitDist = totalHits > 0 ? totalHitDistance / totalHits : 0f;
        report += $"• Avg Hit Distance: {avgHitDist:F2}m\n";

        Debug.Log(report);
    }

    void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying) return;

        // Draw test area
        Gizmos.color = new Color(0, 1, 1, 0.1f);
        Gizmos.DrawWireSphere(transform.position, testAreaRadius);

        // Draw statistics visualization
        if (showStatistics && totalRaycasts > 0)
        {
            float hitRate = (float)totalHits / totalRaycasts;
            Gizmos.color = Color.Lerp(Color.green, Color.red, hitRate);
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 2f, 0.5f);

            // Draw hit distribution
            if (rayHitDistribution.Count > 0)
            {
                for (int i = 0; i < testRayCount; i++)
                {
                    if (rayHitDistribution.ContainsKey(i))
                    {
                        float intensity = (float)rayHitDistribution[i] / currentIteration;
                        Gizmos.color = new Color(1, 0, 0, intensity * 0.5f);
                        Vector3 dir = Quaternion.Euler(0, i * (360f / testRayCount), 0) * Vector3.forward;
                        Gizmos.DrawRay(transform.position, dir * 2f);
                    }
                }
            }
        }
    }

    void OnDestroy()
    {
        ClearTestTargets();
    }
}