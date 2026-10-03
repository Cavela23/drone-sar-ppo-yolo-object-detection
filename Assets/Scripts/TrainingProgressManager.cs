using UnityEngine;
using System.Collections.Generic;

public class TrainingProgressManager : MonoBehaviour
{
    [System.Serializable]
    public class TrainingPhase
    {
        public string phaseName;
        public int startEpisode;
        public int maxSteps;
        public Vector2 planeScale;
        public Vector2 spawnArea;
        public int minObstacles;
        public int maxObstacles;
        public bool useClusters;
        public bool useClearings;
        public float clearingRadius;
        public float rayLength;
        public float minDistanceFromCenter;
    }

    [Header("Training Phases Configuration")]
    public TrainingPhase[] phases = {
        // PHASE 1 - Basic Survival (30x30 units)
        new TrainingPhase {
            phaseName = "Basic Survival",
            startEpisode = 0,
            maxSteps = 800,
            planeScale = new Vector2(3f, 3f),
            spawnArea = new Vector2(12f, 12f),
            minObstacles = 15,
            maxObstacles = 25,
            useClusters = true,
            useClearings = true,
            clearingRadius = 6f,
            rayLength = 10f,
            minDistanceFromCenter = 2f
        },
        // PHASE 2 - Basic Navigation (50x50 units)  
        new TrainingPhase {
            phaseName = "Basic Navigation",
            startEpisode = 300,
            maxSteps = 1000,
            planeScale = new Vector2(5f, 5f),
            spawnArea = new Vector2(22f, 22f),
            minObstacles = 30,
            maxObstacles = 45,
            useClusters = true,
            useClearings = true,
            clearingRadius = 8f,
            rayLength = 15f,
            minDistanceFromCenter = 3f
        },
        // PHASE 3 - Intermediate (80x80 units)
        new TrainingPhase {
            phaseName = "Intermediate Navigation",
            startEpisode = 800,
            maxSteps = 1500,
            planeScale = new Vector2(8f, 8f),
            spawnArea = new Vector2(35f, 35f),
            minObstacles = 50,
            maxObstacles = 70,
            useClusters = true,
            useClearings = true,
            clearingRadius = 12f,
            rayLength = 20f,
            minDistanceFromCenter = 4f
        },
        // PHASE 4 - Advanced SAR (120x120 units)
        new TrainingPhase {
            phaseName = "Advanced SAR",
            startEpisode = 1600,
            maxSteps = 2000,
            planeScale = new Vector2(12f, 12f),
            spawnArea = new Vector2(55f, 55f),
            minObstacles = 80,
            maxObstacles = 110,
            useClusters = true,
            useClearings = true,
            clearingRadius = 15f,
            rayLength = 25f,
            minDistanceFromCenter = 5f
        },
        // PHASE 5 - SAR Master (160x160 units)
        new TrainingPhase {
            phaseName = "SAR Master",
            startEpisode = 3000,
            maxSteps = 3000,
            planeScale = new Vector2(16f, 16f),
            spawnArea = new Vector2(75f, 75f),
            minObstacles = 120,
            maxObstacles = 160,
            useClusters = true,
            useClearings = true,
            clearingRadius = 20f,
            rayLength = 30f,
            minDistanceFromCenter = 6f
        }
    };

    [Header("References")]
    public Transform ground;
    public ForestObstacleGenerator obstacleGenerator;
    public DroneAgent droneAgent;
    public DroneRaycastSensor raycastSensor;

    [Header("Current Progress")]
    public int totalEpisodes = 0;
    public int currentPhaseIndex = 0;
    public TrainingPhase currentPhase;
    private int currentStepCount = 0;
    private bool isInitialized = false;
    private bool phaseJustChanged = false;

    [Header("Training Options")]
    public bool spawnObstaclesPerEpisode = true;
    public bool useDeterministicObstacleLayout = true;
    public bool logObstacleStats = true;

    [Header("Debug Settings")]
    public bool showDebugMessages = true;
    public bool showStepDebug = false;
    public int logInterval = 100;

    // Episode completion tracking
    private Dictionary<string, int> episodeCompletionReasons = new Dictionary<string, int>
    {
        {"max_steps", 0},
        {"wall_collision", 0},
        {"coverage_complete", 0},
        {"agent_end", 0}
    };

    void Start()
    {
        ValidateRequiredTagsAndLayers();

        // Null safety checks
        if (ground == null)
            Debug.LogWarning("[TrainingManager] Ground reference missing!");
        if (obstacleGenerator == null)
            Debug.LogWarning("[TrainingManager] ObstacleGenerator reference missing!");
        if (droneAgent == null)
            Debug.LogWarning("[TrainingManager] DroneAgent reference missing!");
        if (raycastSensor == null)
            Debug.LogWarning("[TrainingManager] RaycastSensor reference missing!");

        InitializeManager();
    }

    private void ValidateRequiredTagsAndLayers()
    {
        bool hasIssue = false;

        // Cek layer
        if (LayerMask.NameToLayer("Obstacle") == -1)
        {
            Debug.LogError("[TrainingManager] ❌ Layer 'Obstacle' tidak ditemukan! Buat di: Project Settings > Tags and Layers");
            hasIssue = true;
        }

        if (LayerMask.NameToLayer("ArenaBoundary") == -1)
        {
            Debug.LogError("[TrainingManager] ❌ Layer 'Wall' tidak ditemukan! Buat di: Project Settings > Tags and Layers");
            hasIssue = true;
        }

        // Cek tag
        bool hasObstacleTag = false;
        bool hasWallTag = false;

        try
        {
#if UNITY_EDITOR
            var tags = UnityEditorInternal.InternalEditorUtility.tags;
            hasObstacleTag = System.Array.Exists(tags, tag => tag == "Obstacle");
            hasWallTag = System.Array.Exists(tags, tag => tag == "Wall");
#else
            GameObject testObj = new GameObject("__test_tag_check__");
            try
            {
                testObj.tag = "Obstacle";
                hasObstacleTag = true;
                testObj.tag = "Wall";
                hasWallTag = true;
            }
            catch
            {
                hasObstacleTag = false;
                hasWallTag = false;
            }
            Destroy(testObj);
#endif
        }
        catch
        {
            hasObstacleTag = true;
            hasWallTag = true;
        }

        if (!hasObstacleTag)
        {
            Debug.LogError("[TrainingManager] ❌ Tag 'Obstacle' tidak ditemukan! Buat di: Project Settings > Tags and Layers");
            hasIssue = true;
        }

        if (!hasWallTag)
        {
            Debug.LogError("[TrainingManager] ❌ Tag 'Wall' tidak ditemukan! Buat di: Project Settings > Tags and Layers");
            hasIssue = true;
        }

        if (!hasIssue && showDebugMessages)
        {
            Debug.Log("[TrainingManager] ✅ Semua tag dan layer tersedia");
        }
    }

    private void InitializeManager()
    {
        if (isInitialized) return;

        currentPhaseIndex = 0;
        currentPhase = phases[0];
        currentStepCount = 0;
        isInitialized = true;
        phaseJustChanged = false;

        // Reset completion tracking
        foreach (var key in new List<string>(episodeCompletionReasons.Keys))
        {
            episodeCompletionReasons[key] = 0;
        }

        Debug.Log($"🔄 Training Manager Initialized: Phase 1 - {currentPhase.phaseName}");

        ApplyEnvironmentChanges();
        ApplyDroneChanges();
        SetupGeneratorWithoutSpawning();

        Debug.Log($"[INIT] Manager ready. spawnObstaclesPerEpisode = {spawnObstaclesPerEpisode}");
    }

    private void SetupGeneratorWithoutSpawning()
    {
        if (obstacleGenerator != null)
        {
            obstacleGenerator.UpdateGeneratorSettings(
                currentPhase.spawnArea,
                currentPhase.minObstacles,
                currentPhase.maxObstacles,
                currentPhase.useClusters,
                currentPhase.useClearings,
                currentPhase.clearingRadius
            );

            if (showDebugMessages)
            {
                Debug.Log($"🌳 Generator configured (spawn delayed): {currentPhase.minObstacles}-{currentPhase.maxObstacles} obstacles");
            }
        }
    }

    // 🔴 PERBAIKAN: HAPUS RegisterNewEpisode() dari OnEpisodeBegin()
    // Karena kita akan inline logika episode counting

    private void LogTrainingStats()
    {
        string stats = $"📊 Episode: {totalEpisodes}, Phase: {currentPhase.phaseName} ({currentPhaseIndex + 1}/{phases.Length}), ";
        stats += $"MaxSteps: {currentPhase.maxSteps}, ";
        stats += $"Area: {currentPhase.planeScale.x * 10}x{currentPhase.planeScale.y * 10}, ";
        stats += $"Completion Reasons: ";

        int totalCompletions = 0;
        foreach (var kvp in episodeCompletionReasons)
            totalCompletions += kvp.Value;

        if (totalCompletions > 0)
        {
            bool first = true;
            foreach (var kvp in episodeCompletionReasons)
            {
                if (kvp.Value > 0)
                {
                    if (!first) stats += " | ";
                    float percentage = (float)kvp.Value / totalCompletions * 100f;
                    stats += $"{kvp.Key}: {kvp.Value} ({percentage:F1}%)";
                    first = false;
                }
            }
        }
        else
        {
            stats += "No completions yet";
        }

        Debug.Log(stats);
    }

    public void RegisterStep()
    {
        if (!isInitialized) return;

        // 🔴 DEBUG: Cek maxSteps valid
        if (currentPhase.maxSteps <= 0)
        {
            Debug.LogError($"[RegisterStep] ❌ currentPhase.maxSteps is {currentPhase.maxSteps} — INVALID! Phase: {currentPhase.phaseName}");
            currentPhase.maxSteps = 500;
        }

        currentStepCount++;

        if (showStepDebug && currentStepCount % 100 == 0)
        {
            Debug.Log($"Step: {currentStepCount}/{currentPhase.maxSteps}");
        }

        // Episode end by max steps
        if (currentStepCount >= currentPhase.maxSteps && droneAgent != null)
        {
            RecordEpisodeCompletion("max_steps");

            if (showDebugMessages)
            {
                Debug.Log($"🏁 Episode {totalEpisodes} ended by max steps: {currentStepCount}/{currentPhase.maxSteps}");
            }

            droneAgent.SafeEndEpisode(0.5f);
            ResetStepCount();
            return;
        }
    }

    public void ResetStepCount()
    {
        currentStepCount = 0;
    }

    private void CheckPhaseAdvancement()
    {
        if (!isInitialized) return;

        int targetPhaseIndex = 0;

        for (int i = phases.Length - 1; i >= 0; i--)
        {
            if (totalEpisodes >= phases[i].startEpisode)
            {
                targetPhaseIndex = i;
                break;
            }
        }

        if (targetPhaseIndex != currentPhaseIndex)
        {
            phaseJustChanged = true;
            ApplyPhase(targetPhaseIndex);
        }
    }

    private void ApplyPhase(int phaseIndex)
    {
        if (phaseIndex < 0 || phaseIndex >= phases.Length)
        {
            Debug.LogError($"Invalid phase index: {phaseIndex}");
            return;
        }

        currentPhaseIndex = phaseIndex;
        currentPhase = phases[phaseIndex];
        currentStepCount = 0;

        // 🔴 DEBUG LOG PENTING
        Debug.Log($"[APPLY PHASE] Now Phase {phaseIndex + 1} - {currentPhase.phaseName} (maxSteps={currentPhase.maxSteps}, episode={totalEpisodes})");

        ApplyEnvironmentChanges();
        ApplyDroneChanges();

        if (obstacleGenerator != null)
        {
            obstacleGenerator.UpdateGeneratorSettings(
                currentPhase.spawnArea,
                currentPhase.minObstacles,
                currentPhase.maxObstacles,
                currentPhase.useClusters,
                currentPhase.useClearings,
                currentPhase.clearingRadius
            );

            Debug.Log($"[PHASE UPDATE] Generator updated for {currentPhase.phaseName}");

            if (spawnObstaclesPerEpisode)
            {
                GenerateObstaclesWithSeed();
            }
        }
    }

    private void ApplyEnvironmentChanges()
    {
        if (ground != null)
        {
            ground.localScale = new Vector3(currentPhase.planeScale.x, 1f, currentPhase.planeScale.y);

            if (showDebugMessages)
            {
                Debug.Log($"Ground scaled to: {currentPhase.planeScale.x * 10}x{currentPhase.planeScale.y * 10} units");
            }
        }

        RepositionWalls();
    }

    private void RepositionWalls()
    {
        GameObject[] wallObjects = GameObject.FindGameObjectsWithTag("Wall");
        List<GameObject> walls = new List<GameObject>();

        foreach (GameObject wall in wallObjects)
        {
            string wallName = wall.name.ToLower();
            if (wallName.Contains("north") || wallName.Contains("south") ||
                wallName.Contains("east") || wallName.Contains("west"))
            {
                walls.Add(wall);
            }
        }

        if (walls.Count == 0)
        {
            Debug.LogWarning("[TrainingManager] No walls found with 'Wall' tag and direction names.");
            return;
        }

        float areaSize = currentPhase.planeScale.x * 10f;
        float wallPosition = areaSize / 2f;

        foreach (GameObject wall in walls)
        {
            string wallName = wall.name.ToLower();

            if (wallName.Contains("north"))
            {
                wall.transform.position = new Vector3(0, 1.5f, wallPosition);
                wall.transform.localScale = new Vector3(areaSize, 3f, 1f);
            }
            else if (wallName.Contains("south"))
            {
                wall.transform.position = new Vector3(0, 1.5f, -wallPosition);
                wall.transform.localScale = new Vector3(areaSize, 3f, 1f);
            }
            else if (wallName.Contains("east"))
            {
                wall.transform.position = new Vector3(wallPosition, 1.5f, 0);
                wall.transform.localScale = new Vector3(1f, 3f, areaSize);
            }
            else if (wallName.Contains("west"))
            {
                wall.transform.position = new Vector3(-wallPosition, 1.5f, 0);
                wall.transform.localScale = new Vector3(1f, 3f, areaSize);
            }
        }

        if (showDebugMessages)
        {
            Debug.Log($"Repositioned {walls.Count} walls for {areaSize}x{areaSize} area");
        }
    }

    private void GenerateObstaclesWithSeed()
    {
        if (obstacleGenerator != null)
        {
            if (obstacleGenerator.HasClearObstaclesMethod())
            {
                obstacleGenerator.ClearObstaclesImmediate();
            }

            if (useDeterministicObstacleLayout)
            {
                int seed = totalEpisodes * 100 + currentPhaseIndex;
                obstacleGenerator.GenerateForestWithSeed(seed);

                if (showDebugMessages)
                {
                    Debug.Log($"🌳 Generated deterministic obstacles (seed={seed}) for episode {totalEpisodes}");
                }
            }
            else
            {
                obstacleGenerator.GenerateForestObstacles();

                if (showDebugMessages)
                {
                    Debug.Log($"🌳 Generated random obstacles for episode {totalEpisodes}");
                }
            }

            if (logObstacleStats)
            {
                int obstacleCount = obstacleGenerator.GetObstacleCount();
                float avgSpacing = obstacleGenerator.GetAverageSpacing();

                Debug.Log($"📊 Obstacle Stats: {obstacleCount} obstacles, avg spacing: {avgSpacing:F2}m");
            }
        }
    }

    private void ApplyDroneChanges()
    {
        if (raycastSensor != null)
        {
            raycastSensor.rayLength = currentPhase.rayLength;

            if (showDebugMessages)
            {
                Debug.Log($"📡 Raycast length updated to: {currentPhase.rayLength}");
            }
        }
        else
        {
            Debug.LogWarning("[TrainingManager] RaycastSensor reference is null!");
        }
    }

    // 🔴 PERBAIKAN KRITIS: Urutan yang BENAR
    public void OnEpisodeBegin()
    {
        if (!isInitialized)
        {
            InitializeManager();
        }

        ResetStepCount();

        // 🔴 1️⃣ TAMBAH EPISODE TERLEBIH DAHULU
        totalEpisodes++;

        // 🔴 2️⃣ CEK PHASE BERDASARKAN EPISODE TERBARU
        CheckPhaseAdvancement();

        // 🔴 3️⃣ GENERATE OBSTACLES (jika diperlukan)
        if (spawnObstaclesPerEpisode)
        {
            GenerateObstaclesWithSeed();
        }

        // 🔴 4️⃣ LOG PERIODIC STATS
        if (showDebugMessages && totalEpisodes % logInterval == 0)
        {
            LogTrainingStats();
        }

        // 🔴 5️⃣ LOG EPISODE READY (INI PASTI SUDAH BENAR)
        if (showDebugMessages)
        {
            Debug.Log($"[EPISODE READY] Episode {totalEpisodes} | Phase: {currentPhase.phaseName} | MaxSteps: {currentPhase.maxSteps} | Steps: {currentStepCount}");
        }

        phaseJustChanged = false;
    }

    public void RecordEpisodeCompletion(string reason)
    {
        if (episodeCompletionReasons.ContainsKey(reason))
        {
            episodeCompletionReasons[reason]++;
        }
        else
        {
            Debug.LogWarning($"[TrainingManager] Unknown completion reason: {reason}");
            episodeCompletionReasons[reason] = 1;
        }

        if (showDebugMessages)
        {
            Debug.Log($"📝 Episode completion recorded: {reason} (Total: {episodeCompletionReasons[reason]})");
        }
    }

    private void ResetToPhase1Immediate()
    {
        currentPhaseIndex = 0;
        currentPhase = phases[0];
        currentStepCount = 0;
        isInitialized = true;
        phaseJustChanged = false;

        foreach (var key in new List<string>(episodeCompletionReasons.Keys))
        {
            episodeCompletionReasons[key] = 0;
        }

        Debug.Log($"🔄 MANUAL RESET TO PHASE 1: {currentPhase.phaseName}");

        ApplyEnvironmentChanges();
        ApplyDroneChanges();
        SetupGeneratorWithoutSpawning();
    }

    [ContextMenu("Force Next Phase")]
    public void ForceNextPhase()
    {
        if (currentPhaseIndex < phases.Length - 1)
        {
            int nextPhase = currentPhaseIndex + 1;
            phaseJustChanged = true;
            ApplyPhase(nextPhase);
        }
        else
        {
            Debug.Log("Already at final phase!");
        }
    }

    [ContextMenu("Reset to Phase 1 (Keep Episodes)")]
    public void ResetToPhase1KeepEpisodes()
    {
        ResetToPhase1Immediate();
        Debug.Log($"Phase reset to 1. Episode count remains: {totalEpisodes}");
    }

    [ContextMenu("Reset to Phase 1 (New Training)")]
    public void ResetToPhase1NewTraining()
    {
        totalEpisodes = 0;
        ResetToPhase1Immediate();
        Debug.Log($"⚠️ COMPLETE RESET: Phase 1, Episodes = 0 (new training)");
    }

    [ContextMenu("Generate Obstacles Now")]
    public void GenerateObstaclesNow()
    {
        if (obstacleGenerator != null)
        {
            GenerateObstaclesWithSeed();
        }
        else
        {
            Debug.LogError("Cannot generate obstacles: obstacleGenerator is null!");
        }
    }

    [ContextMenu("Print Current Status")]
    public void PrintStatus()
    {
        if (!isInitialized)
        {
            Debug.Log("Training Manager not initialized!");
            return;
        }

        string status = $"📈 Current Status - ";
        status += $"Episodes: {totalEpisodes}, ";
        status += $"Steps: {currentStepCount}/{currentPhase.maxSteps}, ";
        status += $"Phase: {currentPhase.phaseName} ({currentPhaseIndex + 1}/{phases.Length}), ";
        status += $"Area: {currentPhase.planeScale.x * 10}x{currentPhase.planeScale.y * 10}, ";
        status += $"Progress: {GetTrainingProgress():P0}";

        if (obstacleGenerator != null)
        {
            int obsCount = obstacleGenerator.GetObstacleCount();
            float avgSpacing = obstacleGenerator.GetAverageSpacing();
            status += $"\n🌳 Obstacles: {obsCount}, Avg spacing: {avgSpacing:F2}m";
        }

        Debug.Log(status);
    }

    [ContextMenu("Print Completion Stats")]
    public void PrintCompletionStats()
    {
        if (totalEpisodes == 0)
        {
            Debug.Log("No episodes completed yet.");
            return;
        }

        string stats = "📊 Episode Completion Stats:\n";
        int totalCompletions = 0;

        foreach (var kvp in episodeCompletionReasons)
            totalCompletions += kvp.Value;

        if (totalCompletions > 0)
        {
            foreach (var kvp in episodeCompletionReasons)
            {
                if (kvp.Value > 0)
                {
                    float percentage = (float)kvp.Value / totalCompletions * 100f;
                    stats += $"  {kvp.Key}: {kvp.Value} ({percentage:F1}%)\n";
                }
            }
        }
        else
        {
            stats += "  No completion records found\n";
        }

        stats += $"Total episodes: {totalEpisodes}";
        Debug.Log(stats);
    }

    public TrainingPhase GetCurrentPhase()
    {
        return currentPhase;
    }

    public int GetCurrentEpisode()
    {
        return totalEpisodes;
    }

    public float GetTrainingProgress()
    {
        if (!isInitialized || phases.Length == 0) return 0f;

        int totalPhases = phases.Length;
        float phaseProgress = (float)currentPhaseIndex / (totalPhases - 1);
        float episodeProgress = 0f;

        if (currentPhaseIndex < totalPhases - 1)
        {
            int phaseStart = phases[currentPhaseIndex].startEpisode;
            int nextPhaseStart = phases[currentPhaseIndex + 1].startEpisode;
            if (nextPhaseStart > phaseStart)
            {
                episodeProgress = Mathf.Clamp01((float)(totalEpisodes - phaseStart) / (nextPhaseStart - phaseStart));
            }
        }
        else
        {
            episodeProgress = 1f;
        }

        return (phaseProgress + episodeProgress) / 2f;
    }

    public int GetCurrentStepCount()
    {
        return currentStepCount;
    }

    public int GetMaxSteps()
    {
        return currentPhase != null ? currentPhase.maxSteps : 0;
    }

    void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying || !isInitialized || currentPhase == null) return;

        Gizmos.color = Color.magenta;
        float areaSize = currentPhase.planeScale.x * 10f;
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(areaSize, 0.1f, areaSize));

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(currentPhase.spawnArea.x * 2, 0.1f, currentPhase.spawnArea.y * 2));

        if (currentPhase.minDistanceFromCenter > 0)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(Vector3.zero, currentPhase.minDistanceFromCenter);
        }

        if (currentPhase.maxSteps > 0)
        {
            float progress = (float)currentStepCount / currentPhase.maxSteps;
            Gizmos.color = Color.Lerp(Color.green, Color.red, progress);
            Gizmos.DrawLine(
                new Vector3(-areaSize / 2, 0.1f, areaSize / 2 + 1),
                new Vector3(-areaSize / 2 + areaSize * progress, 0.1f, areaSize / 2 + 1)
            );
        }
    }
}