using UnityEngine;
using System.Collections.Generic;

public class ForestObstacleGenerator : MonoBehaviour
{
    [Header("=== FOREST-LIKE OBSTACLES ===")]
    public GameObject obstaclePrefab;

    [Header("Obstacle Quantity - REALISTIC DENSITY")]
    public int minObstacles = 15;
    public int maxObstacles = 25;

    [Header("Spawn Area - DYNAMIC FROM TRAINING MANAGER")]
    public Vector2 spawnArea = new Vector2(12f, 12f);
    public float minDistanceFromCenter = 5f;

    [Header("Obstacle Size - REALISTIC VARIATION")]
    public Vector2 scaleRange = new Vector2(0.5f, 3.0f);
    public float obstacleHeight = 5f;

    [Header("Spacing - TIGHT GAPS FOR CHALLENGE")]
    public float minDistanceBetween = 1.8f;
    public int maxAttempts = 300;

    [Header("Forest Distribution Settings")]
    public bool useNaturalDistribution = true;
    public float distributionRandomness = 0.8f;
    [Min(1)] public int gridCellSize = 6;

    [Header("Cluster Settings - DENSE CLUSTERS")]
    public bool useNaturalClusters = true;
    public int minClusters = 4;
    public int maxClusters = 8;
    public int minObstaclesPerCluster = 4;
    public int maxObstaclesPerCluster = 8;
    public float clusterSpread = 3.5f;

    [Header("Clearings - NATURAL BREAKS")]
    public bool createClearings = true;
    public int minClearings = 2;
    public int maxClearings = 4;
    public float clearingRadius = 6f;

    [Header("Advanced Settings")]
    public bool allowOverlapInClusters = true;
    public float clusterOverlapFactor = 0.7f;

    [Header("Performance & Debug")]
    public bool showDebugLogs = true;
    public bool useDeterministicSeed = false;
    public int randomSeed = 12345;

    private List<GameObject> currentObstacles = new List<GameObject>();
    private List<Vector3> clearingCenters = new List<Vector3>();
    private bool isGenerating = false;

    // Performance optimization buffers
    private Collider[] overlapBuffer = new Collider[32];

    // 🔴 PERBAIKAN: HAPUS Start() yang otomatis generate
    // void Start()
    // {
    //     if (useDeterministicSeed)
    //     {
    //         Random.InitState(randomSeed);
    //     }
    //
    //     GenerateForestObstacles();
    // }

    void Start()
    {
        // 🔴 PERBAIKAN: Tetap biarkan seed initialization, tapi jangan generate otomatis
        if (useDeterministicSeed)
        {
            Random.InitState(randomSeed);
        }

        // Opsional: Log bahwa generator ready
        if (showDebugLogs)
        {
            Debug.Log($"🌲 ForestObstacleGenerator Ready - Passive Mode");
            Debug.Log($"   • Spawn Area: {spawnArea.x}x{spawnArea.y}");
            Debug.Log($"   • Obstacle Prefab: {(obstaclePrefab != null ? "Set" : "NULL!")}");
        }
    }

    public void GenerateForestObstacles()
    {
        if (isGenerating) return;

        if (obstaclePrefab == null)
        {
            Debug.LogError("[ForestGenerator] obstaclePrefab is null!");
            return;
        }

        if (useDeterministicSeed)
        {
            Random.InitState(randomSeed);
        }

        isGenerating = true;

        ClearObstaclesImmediate();
        clearingCenters.Clear();

        int targetObstacles = Random.Range(minObstacles, maxObstacles + 1);
        int obstaclesCreated = 0;

        if (showDebugLogs)
        {
            Debug.Log($"🎯 Generating {targetObstacles} obstacles (Density: {CalculateDensity(targetObstacles):F2} obs/unit²)");
        }

        // Step 1: Generate clearings first (natural open spaces)
        if (createClearings)
        {
            GenerateClearings();
        }

        // Step 2: Generate dense clusters (forest groves)
        if (useNaturalClusters)
        {
            obstaclesCreated = GenerateNaturalClusters(targetObstacles);
        }

        // Step 3: Fill with distributed obstacles (background trees)
        if (obstaclesCreated < targetObstacles)
        {
            GenerateDistributedObstacles(targetObstacles - obstaclesCreated);
        }

        if (showDebugLogs)
        {
            Debug.Log($"🌳 REALISTIC FOREST: {currentObstacles.Count} obstacles, " +
                     $"{clearingCenters.Count} clearings, " +
                     $"Density: {CalculateDensity(currentObstacles.Count):F2} obs/unit²");
        }

        isGenerating = false;
    }

    private float CalculateDensity(int obstacleCount)
    {
        float area = (spawnArea.x * 2) * (spawnArea.y * 2);
        return obstacleCount / Mathf.Max(area, 0.1f);
    }

    private void GenerateClearings()
    {
        int clearingCount = Random.Range(minClearings, maxClearings + 1);

        for (int i = 0; i < clearingCount; i++)
        {
            if (TryGetRandomValidPosition(out Vector3 clearingCenter, 50, clearingRadius * 2f))
            {
                clearingCenters.Add(clearingCenter);
            }
        }
    }

    private int GenerateNaturalClusters(int targetObstacles)
    {
        int obstaclesCreated = 0;
        int clusterCount = Random.Range(minClusters, maxClusters + 1);

        for (int c = 0; c < clusterCount && obstaclesCreated < targetObstacles; c++)
        {
            if (!TryGetRandomValidPosition(out Vector3 clusterCenter, 50, clusterSpread * 2f))
                continue;

            if (IsInClearing(clusterCenter)) continue;

            int obstaclesInCluster = Random.Range(minObstaclesPerCluster, maxObstaclesPerCluster + 1);
            obstaclesInCluster = Mathf.Min(obstaclesInCluster, targetObstacles - obstaclesCreated);

            int clusterObstaclesCreated = 0;

            for (int i = 0; i < obstaclesInCluster && clusterObstaclesCreated < obstaclesInCluster; i++)
            {
                if (TryGenerateNaturalClusterPosition(clusterCenter, out Vector3 position))
                {
                    float clusterMinDistance = allowOverlapInClusters ?
                        minDistanceBetween * clusterOverlapFactor : minDistanceBetween;

                    if (IsPositionValidWithPhysics(position, clusterMinDistance) && !IsInClearing(position))
                    {
                        CreateObstacleAtPosition(position);
                        obstaclesCreated++;
                        clusterObstaclesCreated++;
                    }
                }
            }
        }

        return obstaclesCreated;
    }

    private void GenerateDistributedObstacles(int count)
    {
        int obstaclesCreated = 0;
        int attempts = 0;
        int maxTotalAttempts = count * 8;

        while (obstaclesCreated < count && attempts < maxTotalAttempts)
        {
            Vector3 position;

            if (useNaturalDistribution)
            {
                if (!TryGetGridBasedPosition(out position))
                {
                    if (!TryGetRandomValidPosition(out position, 30, minDistanceBetween))
                    {
                        attempts++;
                        continue;
                    }
                }
            }
            else
            {
                if (!TryGetRandomValidPosition(out position, 40, minDistanceBetween))
                {
                    attempts++;
                    continue;
                }
            }

            if (IsPositionValidWithPhysics(position) && !IsInClearing(position))
            {
                CreateObstacleAtPosition(position);
                obstaclesCreated++;
            }

            attempts++;
        }

        if (obstaclesCreated < count && showDebugLogs)
        {
            Debug.LogWarning($"Could only generate {obstaclesCreated}/{count} distributed obstacles (area too dense)");
        }
    }

    private bool TryGetGridBasedPosition(out Vector3 position)
    {
        position = Vector3.zero;

        int gridCells = Mathf.Max(1, Mathf.FloorToInt(spawnArea.x * 2 / gridCellSize));

        for (int attempt = 0; attempt < 80; attempt++)
        {
            int cellX = Random.Range(0, gridCells);
            int cellZ = Random.Range(0, gridCells);

            float posX = (cellX - gridCells / 2) * gridCellSize + Random.Range(-gridCellSize / 2, gridCellSize / 2) * distributionRandomness;
            float posZ = (cellZ - gridCells / 2) * gridCellSize + Random.Range(-gridCellSize / 2, gridCellSize / 2) * distributionRandomness;

            position = new Vector3(posX, obstacleHeight / 2f, posZ);

            if (Mathf.Abs(posX) > spawnArea.x || Mathf.Abs(posZ) > spawnArea.y)
                continue;

            if (position.magnitude < minDistanceFromCenter && Random.value > 0.1f)
                continue;

            if (IsPositionValidWithPhysics(position) && !IsInClearing(position))
                return true;
        }

        return false;
    }

    private bool TryGenerateNaturalClusterPosition(Vector3 clusterCenter, out Vector3 position)
    {
        position = Vector3.zero;

        for (int attempts = 0; attempts < 35; attempts++)
        {
            Vector2 randomOffset = Random.insideUnitCircle * clusterSpread;

            if (Random.value > 0.2f)
            {
                randomOffset = randomOffset.normalized * Random.Range(0f, clusterSpread * 0.5f);
            }

            position = clusterCenter + new Vector3(randomOffset.x, 0f, randomOffset.y);
            position.y = obstacleHeight / 2f;

            float clusterSpacing = allowOverlapInClusters ?
                minDistanceBetween * clusterOverlapFactor : minDistanceBetween;

            if (IsPositionValidWithPhysics(position, clusterSpacing) && !IsInClearing(position))
                return true;
        }

        return false;
    }

    private bool TryGetRandomValidPosition(out Vector3 position, int maxAttempts, float minDistance)
    {
        position = Vector3.zero;

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            Vector3 candidate = new Vector3(
                Random.Range(-spawnArea.x, spawnArea.x),
                obstacleHeight / 2f,
                Random.Range(-spawnArea.y, spawnArea.y)
            );

            if (candidate.magnitude < minDistanceFromCenter && Random.value > 0.1f)
                continue;

            if (IsPositionValidWithPhysics(candidate, minDistance) && !IsInClearing(candidate))
            {
                position = candidate;
                return true;
            }
        }

        return false;
    }

    private bool IsInClearing(Vector3 position)
    {
        foreach (Vector3 clearingCenter in clearingCenters)
        {
            if (Vector3.Distance(position, clearingCenter) < clearingRadius)
                return true;
        }
        return false;
    }

    private bool IsPositionValidWithPhysics(Vector3 position, float customMinDistance = -1f)
    {
        float checkDistance = customMinDistance > 0 ? customMinDistance : minDistanceBetween;

        foreach (var obstacle in currentObstacles)
        {
            if (obstacle == null) continue;

            // Gunakan max extents X/Z sebagai radius planar
            Collider col = obstacle.GetComponent<Collider>();
            float obstacleRadius;

            if (col != null)
            {
                obstacleRadius = Mathf.Max(col.bounds.extents.x, col.bounds.extents.z);
            }
            else
            {
                obstacleRadius = obstacle.transform.localScale.x * 0.5f;
            }

            float distance = Vector3.Distance(position, obstacle.transform.position);
            float minAllowed = obstacleRadius + checkDistance;

            if (distance < minAllowed)
                return false;
        }

        float checkRadius = (scaleRange.y * 0.5f + checkDistance) * 1.05f;

        for (int i = 0; i < overlapBuffer.Length; i++)
            overlapBuffer[i] = null;

        int found = Physics.OverlapSphereNonAlloc(
            position,
            checkRadius,
            overlapBuffer,
            LayerMask.GetMask("Obstacle", "Wall")
        );

        return found == 0;
    }

    private void CreateObstacleAtPosition(Vector3 position)
    {
        if (obstaclePrefab == null)
        {
            Debug.LogError("Cannot create obstacle: prefab is null!");
            return;
        }

        GameObject obstacle = Instantiate(obstaclePrefab, position, Quaternion.identity, this.transform);

        float scale = GetNaturalSize();
        obstacle.transform.localScale = new Vector3(scale, obstacleHeight, scale);

        obstacle.name = $"Tree_{currentObstacles.Count + 1}";

        // Set layer dengan safety check
        int layer = LayerMask.NameToLayer("Obstacle");
        if (layer != -1)
            obstacle.layer = layer;
        else if (showDebugLogs)
            Debug.LogWarning("[ForestGenerator] Layer 'Obstacle' not found. Please create it in Project Settings > Tags & Layers.");

        // Set tag dengan safety check
        try
        {
            obstacle.tag = "Obstacle";
        }
        catch
        {
            if (showDebugLogs)
                Debug.LogWarning("[ForestGenerator] Tag 'Obstacle' not found. Create it in Project Settings > Tags & Layers.");
        }

        currentObstacles.Add(obstacle);

        var renderer = obstacle.GetComponent<Renderer>();
        if (renderer != null)
        {
            float greenVariation = Random.Range(0.2f, 0.6f);
            float brownVariation = Random.Range(0.1f, 0.3f);
            renderer.material.color = Color.red;
        }

        if (obstacle.GetComponent<Collider>() == null)
        {
            Debug.LogWarning($"Obstacle {obstacle.name} has no collider! Adding default capsule.");
            CapsuleCollider capsule = obstacle.AddComponent<CapsuleCollider>();
            capsule.radius = scale * 0.5f;
            capsule.height = obstacleHeight;
        }
    }

    private float GetNaturalSize()
    {
        float rand = Random.value;

        if (rand < 0.15f)
            return Random.Range(scaleRange.x, scaleRange.x + (scaleRange.y - scaleRange.x) * 0.2f);
        else if (rand < 0.4f)
            return Random.Range(scaleRange.x + (scaleRange.y - scaleRange.x) * 0.2f, scaleRange.x + (scaleRange.y - scaleRange.x) * 0.5f);
        else if (rand < 0.8f)
            return Random.Range(scaleRange.x + (scaleRange.y - scaleRange.x) * 0.5f, scaleRange.x + (scaleRange.y - scaleRange.x) * 0.8f);
        else
            return Random.Range(scaleRange.x + (scaleRange.y - scaleRange.x) * 0.8f, scaleRange.y);
    }

    public void ClearObstaclesImmediate()
    {
        // Iterasi mundur untuk menghindari efek samping destroy
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (child != null)
            {
                if (Application.isPlaying)
                    Destroy(child.gameObject);
                else
                    DestroyImmediate(child.gameObject);
            }
        }

        currentObstacles.Clear();
        clearingCenters.Clear();
    }

    public void UpdateGeneratorSettings(Vector2 newSpawnArea, int newMinObs, int newMaxObs, bool useClusters, bool useClearings, float newClearingRadius)
    {
        spawnArea = newSpawnArea;
        minObstacles = newMinObs;
        maxObstacles = newMaxObs;
        useNaturalClusters = useClusters;
        createClearings = useClearings;
        clearingRadius = newClearingRadius;
    }

    [ContextMenu("🌳 GENERATE REALISTIC FOREST")]
    public void GenerateForestNow()
    {
        GenerateForestObstacles();
    }

    [ContextMenu("🧹 CLEAR FOREST")]
    public void ClearForest()
    {
        ClearObstaclesImmediate();
        Debug.Log("✅ Forest cleared completely!");
    }

    public void GenerateForestWithSeed(int seed)
    {
        Random.InitState(seed);
        useDeterministicSeed = true;
        randomSeed = seed;
        GenerateForestObstacles();
    }

    // 🔴 PERBAIKAN: HAPUS OnEpisodeBegin() atau jadikan manual
    // public void OnEpisodeBegin()
    // {
    //     GenerateForestObstacles();
    // }

    public void ManualEpisodeBegin()
    {
        if (showDebugLogs)
            Debug.Log("🌲 Forest Generator: Manual episode begin triggered");
        GenerateForestObstacles();
    }

    public bool HasClearObstaclesMethod()
    {
        return true;
    }

    public int GetObstacleCount()
    {
        return currentObstacles.Count;
    }

    public float GetAverageSpacing()
    {
        if (currentObstacles.Count < 2) return 0f;

        float totalDistance = 0f;
        int comparisons = 0;

        for (int i = 0; i < currentObstacles.Count; i++)
        {
            if (currentObstacles[i] == null) continue;

            for (int j = i + 1; j < currentObstacles.Count; j++)
            {
                if (currentObstacles[j] == null) continue;

                totalDistance += Vector3.Distance(
                    currentObstacles[i].transform.position,
                    currentObstacles[j].transform.position
                );
                comparisons++;
            }
        }

        return comparisons > 0 ? totalDistance / comparisons : 0f;
    }

    void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;

        Gizmos.color = Color.white;
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(spawnArea.x * 2, 1f, spawnArea.y * 2));

        Gizmos.color = Color.blue;
        foreach (Vector3 clearing in clearingCenters)
        {
            Gizmos.DrawWireSphere(clearing, clearingRadius);
        }

        Gizmos.color = Color.magenta;
        foreach (var obstacle in currentObstacles)
        {
            if (obstacle != null)
            {
                float size = obstacle.transform.localScale.x;
                Gizmos.DrawWireSphere(obstacle.transform.position, size * 0.3f);
            }
        }
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (minObstacles < 0) minObstacles = 0;
        if (maxObstacles < minObstacles) maxObstacles = minObstacles;
        if (gridCellSize < 1) gridCellSize = 1;
        if (minDistanceBetween < 0.1f) minDistanceBetween = 0.1f;
        if (scaleRange.x < 0.1f) scaleRange.x = 0.1f;
        if (scaleRange.y < scaleRange.x) scaleRange.y = scaleRange.x;
        if (clearingRadius < 0) clearingRadius = 0;
        
        // Buffer size konsisten
        if (overlapBuffer == null || overlapBuffer.Length < 32)
        {
            overlapBuffer = new Collider[32];
        }
    }
#endif
}