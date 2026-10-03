using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class ObstacleGeneratorTesting : MonoBehaviour
{
    [Header("=== TEST CONFIGURATION ===")]
    public bool autoTestOnStart = true;
    public int numberOfTests = 5;
    public float delayBetweenTests = 2f;

    [Header("=== GENERATOR SETTINGS (Override) ===")]
    [Range(5, 100)] public int testMinObstacles = 15;
    [Range(10, 150)] public int testMaxObstacles = 25;
    [Min(1f)] public Vector2 testSpawnArea = new Vector2(12f, 12f);
    [Range(0.1f, 3f)] public float testMinDistanceBetween = 1.8f;

    [Header("=== DISTRIBUTION TESTING ===")]
    public bool testClusters = true;
    public bool testClearings = true;
    public bool testNaturalDistribution = true;

    [Header("=== PERFORMANCE TESTING ===")]
    public bool testPerformance = true;
    public int performanceIterations = 10;
    public bool logPerformanceDetails = false;

    [Header("=== VISUALIZATION ===")]
    public bool showTestVisuals = true;
    public bool showDensityHeatmap = true;
    public bool showSpacingLines = false;
    public Color densityLowColor = Color.green;
    public Color densityHighColor = Color.red;
    public Color spacingColor = Color.cyan;

    [Header("References")]
    public ForestObstacleGenerator obstacleGenerator;

    // Test variables
    private bool isTesting = false;
    private int currentTest = 0;
    private List<TestResult> testResults = new List<TestResult>();
    private Coroutine testCoroutine;

    // Performance metrics
    private float totalGenerationTime = 0f;
    private float fastestGenerationTime = Mathf.Infinity;
    private float slowestGenerationTime = 0f;
    private int totalObstaclesGenerated = 0;

    // Statistics
    private class TestResult
    {
        public int testNumber;
        public int obstacleCount;
        public float generationTime;
        public float averageSpacing;
        public float density;
        public bool success;
        public string notes;
    }

    void Start()
    {
        if (obstacleGenerator == null)
        {
            obstacleGenerator = GetComponent<ForestObstacleGenerator>();
        }

        if (obstacleGenerator == null)
        {
            Debug.LogError("❌ ObstacleGeneratorTesting: No obstacle generator found!");
            return;
        }

        // Validate generator has required methods
        ValidateGeneratorMethods();

        if (autoTestOnStart)
        {
            StartTesting();
        }
    }

    void Update()
    {
        // Manual test controls
        if (Input.GetKeyDown(KeyCode.T))
        {
            if (!isTesting)
                StartTesting();
            else
                StopTesting();
        }

        if (Input.GetKeyDown(KeyCode.R))
        {
            RunSingleTest();
        }

        if (Input.GetKeyDown(KeyCode.C))
        {
            ClearAllObstacles();
        }

        if (Input.GetKeyDown(KeyCode.P))
        {
            RunPerformanceTest();
        }

        if (Input.GetKeyDown(KeyCode.L))
        {
            LogCurrentStats();
        }
    }

    void OnDrawGizmos()
    {
        if (!Application.isPlaying || !showTestVisuals) return;

        if (obstacleGenerator == null) return;

        // Draw spawn area
        Gizmos.color = Color.yellow;
        Vector3 areaSize = new Vector3(testSpawnArea.x * 2, 0.1f, testSpawnArea.y * 2);
        Gizmos.DrawWireCube(Vector3.zero, areaSize);

        // Get current obstacles for visualization
        int obstacleCount = obstacleGenerator.GetObstacleCount();

        if (obstacleCount > 0 && showDensityHeatmap)
        {
            DrawDensityHeatmap();
        }

        if (obstacleCount > 1 && showSpacingLines)
        {
            DrawSpacingLines();
        }
    }

    void DrawDensityHeatmap()
    {
        // Simple density visualization
        float cellSize = 2f;
        int gridSize = Mathf.CeilToInt(testSpawnArea.x * 2 / cellSize);

        for (int x = 0; x < gridSize; x++)
        {
            for (int z = 0; z < gridSize; z++)
            {
                Vector3 cellCenter = new Vector3(
                    x * cellSize - testSpawnArea.x + cellSize / 2,
                    0.1f,
                    z * cellSize - testSpawnArea.y + cellSize / 2
                );

                int obstaclesInCell = CountObstaclesInArea(cellCenter, cellSize / 2);
                float density = Mathf.Clamp01((float)obstaclesInCell / 5f);

                Gizmos.color = Color.Lerp(densityLowColor, densityHighColor, density);
                Gizmos.DrawCube(cellCenter, new Vector3(cellSize * 0.9f, 0.1f, cellSize * 0.9f));
            }
        }
    }

    void DrawSpacingLines()
    {
        // Get obstacles positions (simplified - would need access to actual obstacles)
        // This is a placeholder - in reality you'd need to access the generator's obstacle list

        Gizmos.color = spacingColor;
        // Implementation would require accessing private obstacle list from generator
    }

    int CountObstaclesInArea(Vector3 center, float radius)
    {
        // This is a simplified version - would need actual obstacle positions
        // For now, we'll use Physics.OverlapSphere as a demonstration
        Collider[] colliders = Physics.OverlapSphere(center, radius,
            LayerMask.GetMask("Obstacle"));
        return colliders.Length;
    }

    void ValidateGeneratorMethods()
    {
        if (obstacleGenerator == null) return;

        Debug.Log("🔧 Validating generator methods...");

        bool hasAllMethods = true;

        // Check required methods
        var methods = new Dictionary<string, System.Type[]>
        {
            { "GenerateForestObstacles", new System.Type[0] },
            { "ClearObstaclesImmediate", new System.Type[0] },
            { "GetObstacleCount", new System.Type[0] },
            { "GetAverageSpacing", new System.Type[0] }
        };

        foreach (var method in methods)
        {
            var methodInfo = obstacleGenerator.GetType().GetMethod(method.Key,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                null, method.Value, null);

            if (methodInfo == null)
            {
                Debug.LogError($"❌ Missing method: {method.Key}");
                hasAllMethods = false;
            }
            else
            {
                Debug.Log($"✅ Method found: {method.Key}");
            }
        }

        if (hasAllMethods)
        {
            Debug.Log("✅ All required methods are available");
        }
        else
        {
            Debug.LogError("❌ Some required methods are missing!");
        }
    }

    [ContextMenu("Start Testing")]
    public void StartTesting()
    {
        if (isTesting) return;

        testResults.Clear();
        currentTest = 0;
        totalGenerationTime = 0f;
        fastestGenerationTime = Mathf.Infinity;
        slowestGenerationTime = 0f;
        totalObstaclesGenerated = 0;

        testCoroutine = StartCoroutine(RunTestSequence());

        Debug.Log($"🧪 STARTING OBSTACLE GENERATOR TESTING");
        Debug.Log($"   • Number of tests: {numberOfTests}");
        Debug.Log($"   • Delay between tests: {delayBetweenTests}s");
        Debug.Log($"   • Spawn area: {testSpawnArea.x}x{testSpawnArea.y}");
        Debug.Log($"   • Min/Max obstacles: {testMinObstacles}-{testMaxObstacles}");
    }

    [ContextMenu("Stop Testing")]
    public void StopTesting()
    {
        if (!isTesting) return;

        if (testCoroutine != null)
        {
            StopCoroutine(testCoroutine);
            testCoroutine = null;
        }

        isTesting = false;

        Debug.Log($"🛑 TESTING STOPPED");
        LogTestSummary();
    }

    [ContextMenu("Run Single Test")]
    public void RunSingleTest()
    {
        if (isTesting)
        {
            Debug.LogWarning("Cannot run single test while testing sequence is running");
            return;
        }

        ClearAllObstacles();

        // Configure generator for test
        ConfigureGeneratorForTest();

        // Run single generation
        float startTime = Time.realtimeSinceStartup;
        obstacleGenerator.GenerateForestObstacles();
        float generationTime = Time.realtimeSinceStartup - startTime;

        // Get results
        int obstacleCount = obstacleGenerator.GetObstacleCount();
        float averageSpacing = obstacleGenerator.GetAverageSpacing();
        float density = obstacleCount > 0 ?
            obstacleCount / (testSpawnArea.x * 2 * testSpawnArea.y * 2) : 0f;

        // Log results
        Debug.Log($"🧪 SINGLE TEST COMPLETE");
        Debug.Log($"   • Generation time: {generationTime:F3}s");
        Debug.Log($"   • Obstacles generated: {obstacleCount}");
        Debug.Log($"   • Average spacing: {averageSpacing:F2}m");
        Debug.Log($"   • Density: {density:F3} obs/unit²");
        Debug.Log($"   • Success: {obstacleCount >= testMinObstacles}");

        if (obstacleCount < testMinObstacles)
        {
            Debug.LogWarning($"   ⚠️ Generated only {obstacleCount} obstacles (min: {testMinObstacles})");
        }
    }

    [ContextMenu("Run Performance Test")]
    public void RunPerformanceTest()
    {
        if (!testPerformance) return;

        Debug.Log($"⏱️ STARTING PERFORMANCE TEST ({performanceIterations} iterations)");

        ClearAllObstacles();
        ConfigureGeneratorForTest();

        float totalTime = 0f;
        float fastest = Mathf.Infinity;
        float slowest = 0f;
        List<float> times = new List<float>();

        for (int i = 0; i < performanceIterations; i++)
        {
            ClearAllObstacles();

            float startTime = Time.realtimeSinceStartup;
            obstacleGenerator.GenerateForestObstacles();
            float genTime = Time.realtimeSinceStartup - startTime;

            times.Add(genTime);
            totalTime += genTime;
            fastest = Mathf.Min(fastest, genTime);
            slowest = Mathf.Max(slowest, genTime);

            if (logPerformanceDetails)
            {
                Debug.Log($"   Iteration {i + 1}: {genTime:F4}s - {obstacleGenerator.GetObstacleCount()} obstacles");
            }
        }

        // Calculate statistics
        float averageTime = totalTime / performanceIterations;

        // Calculate standard deviation
        float variance = 0f;
        foreach (float time in times)
        {
            variance += Mathf.Pow(time - averageTime, 2);
        }
        variance /= performanceIterations;
        float stdDev = Mathf.Sqrt(variance);

        Debug.Log($"🏁 PERFORMANCE TEST COMPLETE");
        Debug.Log($"   • Iterations: {performanceIterations}");
        Debug.Log($"   • Average time: {averageTime:F4}s");
        Debug.Log($"   • Fastest time: {fastest:F4}s");
        Debug.Log($"   • Slowest time: {slowest:F4}s");
        Debug.Log($"   • Standard deviation: {stdDev:F4}s");
        Debug.Log($"   • Total time: {totalTime:F2}s");

        // Performance rating
        string rating;
        if (averageTime < 0.1f) rating = "EXCELLENT ⭐⭐⭐";
        else if (averageTime < 0.3f) rating = "GOOD ⭐⭐";
        else if (averageTime < 0.5f) rating = "ACCEPTABLE ⭐";
        else if (averageTime < 1.0f) rating = "SLOW 🐢";
        else rating = "VERY SLOW 🐌";

        Debug.Log($"   • Performance rating: {rating}");
    }

    IEnumerator RunTestSequence()
    {
        isTesting = true;

        for (currentTest = 1; currentTest <= numberOfTests; currentTest++)
        {
            Debug.Log($"\n🧪 TEST {currentTest}/{numberOfTests}");

            // Clear previous obstacles
            ClearAllObstacles();

            // Configure generator
            ConfigureGeneratorForTest();

            // Run test
            TestResult result = RunSingleTestInternal(currentTest);
            testResults.Add(result);

            // Update performance metrics
            UpdatePerformanceMetrics(result);

            // Log test result
            LogTestResult(result);

            // Wait before next test
            if (currentTest < numberOfTests)
            {
                yield return new WaitForSeconds(delayBetweenTests);
            }
        }

        // Testing complete
        isTesting = false;
        Debug.Log($"\n🏁 ALL TESTS COMPLETE");
        LogTestSummary();
    }

    TestResult RunSingleTestInternal(int testNumber)
    {
        TestResult result = new TestResult
        {
            testNumber = testNumber
        };

        try
        {
            // Measure generation time
            float startTime = Time.realtimeSinceStartup;
            obstacleGenerator.GenerateForestObstacles();
            result.generationTime = Time.realtimeSinceStartup - startTime;

            // Get results
            result.obstacleCount = obstacleGenerator.GetObstacleCount();
            result.averageSpacing = obstacleGenerator.GetAverageSpacing();

            // Calculate density
            float area = (testSpawnArea.x * 2) * (testSpawnArea.y * 2);
            result.density = result.obstacleCount / Mathf.Max(area, 0.1f);

            // Check success criteria
            result.success = result.obstacleCount >= testMinObstacles;

            // Add notes if needed
            if (result.obstacleCount < testMinObstacles)
            {
                result.notes = $"Only generated {result.obstacleCount} obstacles (min: {testMinObstacles})";
            }
            else if (result.obstacleCount > testMaxObstacles)
            {
                result.notes = $"Generated {result.obstacleCount} obstacles (max: {testMaxObstacles})";
            }
            else
            {
                result.notes = "Success";
            }
        }
        catch (System.Exception e)
        {
            result.success = false;
            result.notes = $"Error: {e.Message}";
            Debug.LogError($"❌ Test {testNumber} failed: {e.Message}");
        }

        return result;
    }

    void ConfigureGeneratorForTest()
    {
        if (obstacleGenerator == null) return;

        // Update generator settings using reflection
        UpdateGeneratorSetting("minObstacles", testMinObstacles);
        UpdateGeneratorSetting("maxObstacles", testMaxObstacles);
        UpdateGeneratorSetting("spawnArea", testSpawnArea);
        UpdateGeneratorSetting("minDistanceBetween", testMinDistanceBetween);
        UpdateGeneratorSetting("useNaturalClusters", testClusters);
        UpdateGeneratorSetting("createClearings", testClearings);
        UpdateGeneratorSetting("useNaturalDistribution", testNaturalDistribution);

        // Log configuration
        if (currentTest == 1)
        {
            Debug.Log($"   • Configuration applied for testing");
        }
    }

    void UpdateGeneratorSetting(string fieldName, object value)
    {
        if (obstacleGenerator == null) return;

        var field = obstacleGenerator.GetType().GetField(fieldName);
        if (field != null)
        {
            field.SetValue(obstacleGenerator, value);
        }
        else
        {
            var property = obstacleGenerator.GetType().GetProperty(fieldName);
            if (property != null && property.CanWrite)
            {
                property.SetValue(obstacleGenerator, value);
            }
        }
    }

    void UpdatePerformanceMetrics(TestResult result)
    {
        totalGenerationTime += result.generationTime;
        totalObstaclesGenerated += result.obstacleCount;
        fastestGenerationTime = Mathf.Min(fastestGenerationTime, result.generationTime);
        slowestGenerationTime = Mathf.Max(slowestGenerationTime, result.generationTime);
    }

    void LogTestResult(TestResult result)
    {
        string status = result.success ? "✅ PASS" : "❌ FAIL";

        Debug.Log($"   {status} - Test {result.testNumber}");
        Debug.Log($"     • Time: {result.generationTime:F3}s");
        Debug.Log($"     • Obstacles: {result.obstacleCount}");
        Debug.Log($"     • Spacing: {result.averageSpacing:F2}m");
        Debug.Log($"     • Density: {result.density:F3} obs/unit²");

        if (!string.IsNullOrEmpty(result.notes))
        {
            Debug.Log($"     • Notes: {result.notes}");
        }
    }

    void LogTestSummary()
    {
        if (testResults.Count == 0)
        {
            Debug.Log("No test results to summarize");
            return;
        }

        int passedTests = 0;
        int failedTests = 0;
        int totalObstacles = 0;
        float totalTime = 0f;

        foreach (var result in testResults)
        {
            if (result.success) passedTests++;
            else failedTests++;

            totalObstacles += result.obstacleCount;
            totalTime += result.generationTime;
        }

        float avgObstacles = (float)totalObstacles / testResults.Count;
        float avgTime = totalTime / testResults.Count;
        float successRate = (float)passedTests / testResults.Count * 100f;

        Debug.Log($"📊 TEST SUMMARY");
        Debug.Log($"   • Total tests: {testResults.Count}");
        Debug.Log($"   • Passed: {passedTests}");
        Debug.Log($"   • Failed: {failedTests}");
        Debug.Log($"   • Success rate: {successRate:F1}%");
        Debug.Log($"   • Average generation time: {avgTime:F3}s");
        Debug.Log($"   • Fastest generation: {fastestGenerationTime:F3}s");
        Debug.Log($"   • Slowest generation: {slowestGenerationTime:F3}s");
        Debug.Log($"   • Average obstacles per test: {avgObstacles:F1}");
        Debug.Log($"   • Total obstacles generated: {totalObstaclesGenerated}");

        // Overall rating
        string rating;
        if (successRate >= 90f && avgTime < 0.2f) rating = "EXCELLENT ⭐⭐⭐";
        else if (successRate >= 80f && avgTime < 0.4f) rating = "GOOD ⭐⭐";
        else if (successRate >= 70f) rating = "ACCEPTABLE ⭐";
        else rating = "NEEDS IMPROVEMENT ⚠️";

        Debug.Log($"   • Overall rating: {rating}");
    }

    [ContextMenu("Clear All Obstacles")]
    public void ClearAllObstacles()
    {
        if (obstacleGenerator == null) return;

        obstacleGenerator.ClearObstaclesImmediate();
        Debug.Log("🧹 All obstacles cleared");
    }

    [ContextMenu("Log Current Stats")]
    public void LogCurrentStats()
    {
        if (obstacleGenerator == null) return;

        int obstacleCount = obstacleGenerator.GetObstacleCount();
        float averageSpacing = obstacleGenerator.GetAverageSpacing();
        float density = obstacleCount > 0 ?
            obstacleCount / (testSpawnArea.x * 2 * testSpawnArea.y * 2) : 0f;

        Debug.Log($"📈 CURRENT STATISTICS");
        Debug.Log($"   • Obstacles: {obstacleCount}");
        Debug.Log($"   • Average spacing: {averageSpacing:F2}m");
        Debug.Log($"   • Density: {density:F3} obs/unit²");
        Debug.Log($"   • Spawn area: {testSpawnArea.x}x{testSpawnArea.y}");

        // Distribution analysis
        if (obstacleCount > 0)
        {
            AnalyzeDistribution();
        }
    }

    void AnalyzeDistribution()
    {
        // This would analyze obstacle distribution patterns
        // For now, we'll just log a placeholder
        Debug.Log($"   • Distribution: Mixed (clusters + clearings)");
        Debug.Log($"   • Min spacing: {testMinDistanceBetween}m");
    }

    [ContextMenu("Generate Test Report")]
    public void GenerateTestReport()
    {
        string report = $"📋 OBSTACLE GENERATOR TEST REPORT\n";
        report += $"=====================================\n";
        report += $"Timestamp: {System.DateTime.Now}\n";
        report += $"Generator: {obstacleGenerator.GetType().Name}\n";
        report += $"Tests conducted: {testResults.Count}\n";
        report += $"\n";

        if (testResults.Count > 0)
        {
            report += $"PERFORMANCE SUMMARY:\n";
            report += $"• Average generation time: {totalGenerationTime / testResults.Count:F3}s\n";
            report += $"• Fastest generation: {fastestGenerationTime:F3}s\n";
            report += $"• Slowest generation: {slowestGenerationTime:F3}s\n";
            report += $"• Total obstacles generated: {totalObstaclesGenerated}\n";
            report += $"\n";

            report += $"TEST RESULTS:\n";
            foreach (var result in testResults)
            {
                string status = result.success ? "PASS" : "FAIL";
                report += $"Test {result.testNumber}: {status} - {result.obstacleCount} obstacles, {result.generationTime:F3}s\n";
            }
        }
        else
        {
            report += "No test results available.\n";
        }

        report += $"\n";
        report += $"CONFIGURATION:\n";
        report += $"• Spawn area: {testSpawnArea.x}x{testSpawnArea.y}\n";
        report += $"• Min/Max obstacles: {testMinObstacles}-{testMaxObstacles}\n";
        report += $"• Min distance between: {testMinDistanceBetween}m\n";
        report += $"• Clusters enabled: {testClusters}\n";
        report += $"• Clearings enabled: {testClearings}\n";
        report += $"• Natural distribution: {testNaturalDistribution}\n";

        Debug.Log(report);
    }
}