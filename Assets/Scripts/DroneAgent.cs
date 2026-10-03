using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using System.Collections;
using System.Collections.Generic;

public class DroneAgent : Agent
{
    [Header("=== MODE SETTINGS ===")]
    public bool isTesting = true;

    [Header("Drone Movement Settings")]
    public float maxForwardSpeed = 6f;
    public float rotationSpeed = 160f;
    public float acceleration = 25f;
    public float strafeMultiplier = 0.6f;

    [Header("=== SIMPLIFIED REWARD STRUCTURE ===")]
    [Tooltip("Base reward per meter traveled")]
    public float movementRewardPerMeter = 0.12f;

    [Tooltip("Reward for discovering new cells")]
    public float newCellReward = 0.5f;

    [Tooltip("Small bonus for revisiting old cells")]
    public float revisitOldCellBonus = 0.05f;

    [Tooltip("Cooldown before cell considered 'old' (seconds)")]
    public float cellCooldown = 20f;

    [Tooltip("Penalty for revisiting recent cells")]
    public float recentRevisitPenalty = -0.03f;

    [Tooltip("Max visit count before area becomes 'exhausted'")]
    public int exhaustedVisitThreshold = 2;

    [Tooltip("Penalty multiplier for exhausted areas")]
    public float exhaustedAreaPenalty = 0.3f;

    [Header("=== COLLISION AVOIDANCE ===")]
    [Tooltip("Distance where danger starts (meters)")]
    public float dangerZone = 1.2f;

    [Tooltip("Critical distance - strong penalty zone")]
    public float criticalZone = 0.6f;

    [Tooltip("Penalty scaling in danger zone")]
    public float dangerPenaltyScale = 0.15f;

    [Tooltip("Minimum speed for proximity penalty to apply")]
    public float proximitySpeedThreshold = 0.7f;

    [Header("=== BEHAVIOR SHAPING ===")]
    [Tooltip("Penalty for being too slow")]
    public float slowMovementPenalty = 0.015f;

    [Tooltip("Speed threshold for 'slow' (m/s)")]
    public float slowSpeedThreshold = 0.5f;

    [Tooltip("Time before stagnation penalty kicks in")]
    public float stagnationTimeout = 10f;

    [Tooltip("Stagnation penalty rate")]
    public float stagnationPenalty = 0.12f;

    [Tooltip("Boundary zone distance from arena edge")]
    public float boundaryZone = 3f;

    [Tooltip("Penalty for staying near boundaries")]
    public float boundaryPenalty = 0.08f;

    [Header("=== PROGRESS INCENTIVES ===")]
    [Tooltip("Bonus for reaching coverage milestones")]
    public float coverageMilestoneBonus = 0.4f;

    [Tooltip("Coverage checkpoints")]
    private float[] coverageMilestones = { 0.2f, 0.4f, 0.6f, 0.8f };
    private bool[] milestoneReached = new bool[4];

    [Header("=== FRONTIER EXPLORATION ===")]
    [Tooltip("Reward for expanding exploration frontier")]
    public float frontierExpansionBonus = 0.2f;

    [Tooltip("Minimum distance to count as frontier expansion")]
    public float frontierThreshold = 2.5f;

    [Tooltip("Bonus for navigating narrow gaps successfully")]
    public float narrowGapBonus = 0.25f;

    [Tooltip("Narrow gap detection range (meters)")]
    public float narrowGapMaxDist = 1.2f;
    public float narrowGapMinDist = 0.4f;
    public float narrowGapSpeedThreshold = 0.2f;

    [Tooltip("Bonus for exploring center area")]
    public float centerExplorationBonus = 0.1f;

    [Tooltip("Center zone radius")]
    public float centerZoneRadius = 8f;

    [Header("References")]
    public DroneRaycastSensor raycastSensor;
    public TrainingProgressManager trainingManager;
    public float spawnSafeRadius = 3f;
    public float raycastMaxDistance = 10f;
    public float arenaMaxRadius = 25f;

    public YoloReceiver yolo;
    public Transform victim;

    // Core components
    private Rigidbody rb;
    private Vector3 startPosition;
    private Quaternion startRotation;
    private bool episodeEnded = false;
    private Coroutine currentResetCoroutine = null;

    // 🔥 PERBAIKAN YOLO: state hovering & chasing
    private bool isHovering = false;
    private Vector3 hoverPosition;
    private bool victimFound = false;
    private float yoloLostTimer = 0f;
    private const float YOLO_LOST_TIMEOUT = 1.5f;
    private bool chaseEverStarted = false;
    // === CHASE CALIBRATION FIELDS ===
    private float lastKnownTargetX = 0.5f;
    private float initialBboxArea = -1f;
    private float prevBboxArea = 0f;
    private int chaseFrameCount = 0;
    private const int CHASE_MIN_FRAMES = 30;

    [Header("=== TIMEOUT & STATS ===")]
    public float maxEpisodeTime = 60f;

    private bool isTimeout = false;
    private static int testSuccessCount = 0;
    private static int testFailCount = 0;
    private static int testTotalCount = 0;

    // Grid-based exploration
    private class CellInfo
    {
        public float firstVisitTime;
        public float lastVisitTime;
        public int visitCount;
    }

    private Dictionary<Vector3Int, CellInfo> visitedCells = new Dictionary<Vector3Int, CellInfo>();
    private Vector3Int currentCell;
    private float totalDistanceTraveled = 0f;
    private int maxPossibleCells = 100;

    private Vector3 explorationCenter = Vector3.zero;
    private float maxDistanceFromCenter = 0f;

    private Vector3 lastPosition;
    private float stagnationTimer = 0f;
    private Vector3 stagnationCheckPos;

    private float smoothedClosestDist = 10f;
    private const float SMOOTH_FACTOR = 0.2f;

    private int collisionCount = 0;
    private float episodeStartTime = 0f;
    private int uniqueCellsVisited = 0;

    private float lastLogTime = 0f;
    private const float LOG_INTERVAL = 0.5f;

    private float rotationAccumulator = 0f;
    private float lastRotationReset = 0f;

    public override void Initialize()
    {
        rb = GetComponent<Rigidbody>();
        startPosition = transform.position;
        startRotation = transform.rotation;

        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.useGravity = false;
        rb.drag = 0f;
        rb.angularDrag = 0f;
        rb.constraints = RigidbodyConstraints.FreezeRotationX |
                        RigidbodyConstraints.FreezeRotationZ |
                        RigidbodyConstraints.FreezePositionY;

        if (raycastSensor == null)
            raycastSensor = GetComponent<DroneRaycastSensor>();

        if (!isTesting)
            trainingManager = FindObjectOfType<TrainingProgressManager>();

        ResetTracking();
        episodeStartTime = Time.time;
    }

    public override void OnEpisodeBegin()
    {
        if (isTesting || trainingManager == null)
        {
            StartCoroutine(SafeEpisodeBeginTesting());
            return;
        }

        trainingManager.OnEpisodeBegin();
        episodeEnded = false;
        episodeStartTime = Time.time;

        if (currentResetCoroutine != null)
            StopCoroutine(currentResetCoroutine);

        currentResetCoroutine = StartCoroutine(SafeEpisodeBegin());
    }

    private IEnumerator SafeEpisodeBeginTesting()
    {
        yield return new WaitForFixedUpdate();

        Vector3 jitter = new Vector3(Random.Range(-0.5f, 0.5f), 0f, Random.Range(-0.5f, 0.5f));
        rb.position = startPosition + jitter;
        transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        rb.rotation = transform.rotation;
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        transform.position = rb.position;

        ResetTracking();
    }

    private IEnumerator SafeEpisodeBegin()
    {
        yield return new WaitForFixedUpdate();

        ResetDroneImmediate();
        ResetTracking();

        currentResetCoroutine = null;
    }

    private void ResetTracking()
    {
        visitedCells.Clear();
        currentCell = WorldToCell(transform.position);
        lastPosition = transform.position;
        totalDistanceTraveled = 0f;
        stagnationTimer = 0f;
        stagnationCheckPos = transform.position;
        smoothedClosestDist = 10f;
        collisionCount = 0;
        uniqueCellsVisited = 0;
        rotationAccumulator = 0f;
        lastRotationReset = Time.time;

        explorationCenter = Vector3.zero;
        maxDistanceFromCenter = Vector3.Distance(transform.position, explorationCenter);

        lastLogTime = 0f;

        for (int i = 0; i < milestoneReached.Length; i++)
            milestoneReached[i] = false;

        // 🔥 Reset state hovering & chasing
        isHovering = false;
        victimFound = false;
        yoloLostTimer = 0f;   // ← tambahan penting
        initialBboxArea = -1f;
        chaseFrameCount = 0;
        prevBboxArea = 0f;
        lastKnownTargetX = 0.5f;
        chaseEverStarted = false;
        isTimeout = false;

        UpdateMaxPossibleCells();

        // Daftarkan cell spawn secara langsung
        visitedCells[currentCell] = new CellInfo
        {
            firstVisitTime = Time.time,
            lastVisitTime = Time.time,
            visitCount = 1
        };
        uniqueCellsVisited = 1;
        RegisterCell(transform.position);
    }

    private void UpdateMaxPossibleCells()
    {
        if (isTesting || trainingManager == null || trainingManager.currentPhase == null)
        {
            maxPossibleCells = 100;
            return;
        }

        Vector2 spawnArea = trainingManager.currentPhase.spawnArea;
        int cellsX = Mathf.CeilToInt(spawnArea.x * 2f / 2f);
        int cellsZ = Mathf.CeilToInt(spawnArea.y * 2f / 2f);
        maxPossibleCells = Mathf.Max(cellsX * cellsZ, 1);
    }

    private void ResetDroneImmediate()
    {
        Vector3 spawnPos = GetGuaranteedSpawnPosition();

        rb.position = spawnPos;
        rb.rotation = startRotation;
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        transform.position = spawnPos;
        transform.rotation = startRotation;
    }

    private Vector3 GetGuaranteedSpawnPosition()
    {
        if (isTesting || trainingManager == null || trainingManager.currentPhase == null)
            return startPosition;

        var phase = trainingManager.currentPhase;
        Vector2 spawnArea = phase.spawnArea;
        float minDistance = phase.minDistanceFromCenter;
        float safeBoundary = 2f;
        float maxX = Mathf.Max(spawnArea.x - safeBoundary, 1f);
        float maxZ = Mathf.Max(spawnArea.y - safeBoundary, 1f);

        for (int attempt = 0; attempt < 30; attempt++)
        {
            float x = Random.Range(-maxX, maxX);
            float z = Random.Range(-maxZ, maxZ);
            Vector3 candidate = new Vector3(x, startPosition.y, z);

            if (minDistance > 0)
            {
                Vector2 horizPos = new Vector2(candidate.x, candidate.z);
                if (horizPos.magnitude < minDistance)
                    continue;
            }

            if (IsSpawnPositionValid(candidate))
                return candidate;
        }

        return startPosition;
    }

    private bool IsSpawnPositionValid(Vector3 position)
    {
        if (isTesting || trainingManager == null || trainingManager.currentPhase == null)
            return false;

        Vector2 spawnArea = trainingManager.currentPhase.spawnArea;
        if (Mathf.Abs(position.x) >= spawnArea.x || Mathf.Abs(position.z) >= spawnArea.y)
            return false;

        Collider[] colliders = new Collider[16];
        int numColliders = Physics.OverlapSphereNonAlloc(
            position, spawnSafeRadius, colliders,
            LayerMask.GetMask("Obstacle", "Wall")
        );
        return numColliders == 0;
    }

    // ============= SIMPLIFIED REWARD SYSTEM =============

    private Vector3Int WorldToCell(Vector3 worldPos)
    {
        return new Vector3Int(
            Mathf.RoundToInt(worldPos.x / 2f),
            0,
            Mathf.RoundToInt(worldPos.z / 2f)
        );
    }

    private void RegisterCell(Vector3 position)
    {
        Vector3Int cell = WorldToCell(position);

        if (cell.Equals(currentCell))
            return;

        currentCell = cell;
        float currentTime = Time.time;

        if (!visitedCells.ContainsKey(cell))
        {
            visitedCells[cell] = new CellInfo
            {
                firstVisitTime = currentTime,
                lastVisitTime = currentTime,
                visitCount = 1
            };

            if (!isTesting) AddReward(newCellReward);
            uniqueCellsVisited++;

            CheckCoverageMilestones();
            CheckFrontierExpansion(position);
        }
        else
        {
            CellInfo info = visitedCells[cell];
            float timeSinceLastVisit = currentTime - info.lastVisitTime;

            if (timeSinceLastVisit > cellCooldown)
            {
                AddReward(revisitOldCellBonus);
            }
            else
            {
                AddReward(recentRevisitPenalty);
            }

            info.lastVisitTime = currentTime;
            info.visitCount++;
        }
    }

    private void CheckFrontierExpansion(Vector3 position)
    {
        float distFromCenter = Vector3.Distance(position, explorationCenter);

        if (distFromCenter > maxDistanceFromCenter + frontierThreshold)
        {
            float expansion = distFromCenter - maxDistanceFromCenter;
            AddReward(frontierExpansionBonus * (expansion / frontierThreshold));

            maxDistanceFromCenter = distFromCenter;

            if (!isTesting)
            {
                Debug.Log($"🚀 Frontier expanded! Distance: {distFromCenter:F1}m (+{expansion:F1}m)");
            }
        }
    }

    private void CheckCoverageMilestones()
    {
        if (maxPossibleCells <= 0) return;

        float coverage = (float)visitedCells.Count / maxPossibleCells;

        for (int i = 0; i < coverageMilestones.Length; i++)
        {
            if (!milestoneReached[i] && coverage >= coverageMilestones[i])
            {
                milestoneReached[i] = true;
                AddReward(coverageMilestoneBonus);

                if (!isTesting)
                    Debug.Log($"🎯 Milestone: {coverageMilestones[i] * 100}% (+{coverageMilestoneBonus})");
            }
        }
    }

    private void CalculateMovementRewards()
    {
        Vector3 currentPos = transform.position;
        float distMoved = Vector3.Distance(currentPos, lastPosition);

        if (distMoved > 0.05f)
        {
            totalDistanceTraveled += distMoved;
            AddReward(movementRewardPerMeter * distMoved);

            if (distMoved > 0.1f)
            {
                RegisterCell(currentPos);
            }

            stagnationTimer = 0f;
            stagnationCheckPos = currentPos;
        }
        else
        {
            float speed = rb.velocity.magnitude;
            if (speed < slowSpeedThreshold)
            {
                AddReward(-slowMovementPenalty * Time.fixedDeltaTime);
            }
        }

        lastPosition = currentPos;
    }

    private void CheckCollisionRisk()
    {
        if (raycastSensor == null) return;

        float closestDist = raycastSensor.GetClosestObstacleDistance();

        if (float.IsInfinity(closestDist) || closestDist <= 0f)
        {
            closestDist = 10f;
        }

        smoothedClosestDist = Mathf.Lerp(smoothedClosestDist, closestDist, SMOOTH_FACTOR);

        float speed = rb.velocity.magnitude;
        float speedRatio = speed / maxForwardSpeed;

        if (smoothedClosestDist >= narrowGapMinDist &&
            smoothedClosestDist <= narrowGapMaxDist &&
            speedRatio > narrowGapSpeedThreshold)
        {
            float gapDifficulty = 1f - ((smoothedClosestDist - narrowGapMinDist) / (narrowGapMaxDist - narrowGapMinDist));
            AddReward(narrowGapBonus * gapDifficulty * speedRatio * Time.fixedDeltaTime);
        }

        if (speedRatio > proximitySpeedThreshold)
        {
            if (smoothedClosestDist < criticalZone)
            {
                float danger = 1f - (smoothedClosestDist / criticalZone);
                AddReward(-dangerPenaltyScale * 2f * danger * speedRatio * Time.fixedDeltaTime);
            }
            else if (smoothedClosestDist < dangerZone)
            {
                float danger = 1f - (smoothedClosestDist / dangerZone);
                AddReward(-dangerPenaltyScale * danger * speedRatio * Time.fixedDeltaTime);
            }
        }
    }

    private void CheckStagnation()
    {
        float distFromCheck = Vector3.Distance(transform.position, stagnationCheckPos);

        if (distFromCheck < 1.2f)
        {
            stagnationTimer += Time.fixedDeltaTime;

            if (stagnationTimer > stagnationTimeout)
            {
                float severity = Mathf.Min((stagnationTimer - stagnationTimeout) / 5f, 1f);

                Vector3Int cell = WorldToCell(transform.position);
                float exhaustedMultiplier = 1f;

                if (visitedCells.ContainsKey(cell) &&
                    visitedCells[cell].visitCount > exhaustedVisitThreshold)
                {
                    exhaustedMultiplier = 2f;
                }

                AddReward(-stagnationPenalty * severity * exhaustedMultiplier * Time.fixedDeltaTime);
            }
        }
        else
        {
            stagnationTimer = 0f;
            stagnationCheckPos = transform.position;
        }

        CheckBoundaryProximity();
    }

    private void CheckBoundaryProximity()
    {
        Vector2 spawnArea = Vector2.one * 50f;

        if (!isTesting && trainingManager != null && trainingManager.currentPhase != null)
        {
            spawnArea = trainingManager.currentPhase.spawnArea;
        }

        float distToEdgeX = spawnArea.x - Mathf.Abs(transform.position.x);
        float distToEdgeZ = spawnArea.y - Mathf.Abs(transform.position.z);
        float distToNearestBoundary = Mathf.Min(distToEdgeX, distToEdgeZ);

        if (distToNearestBoundary < boundaryZone)
        {
            float boundaryFactor = 1f - (distToNearestBoundary / boundaryZone);

            Vector3Int cell = WorldToCell(transform.position);
            float exhaustedMultiplier = 1f;

            if (visitedCells.ContainsKey(cell) &&
                visitedCells[cell].visitCount > exhaustedVisitThreshold)
            {
                exhaustedMultiplier = 3f;
            }

            AddReward(-boundaryPenalty * boundaryFactor * exhaustedMultiplier * Time.fixedDeltaTime);
        }
    }

    // ============= ML-AGENTS INTERFACE =============

    public override void CollectObservations(VectorSensor sensor)
    {
        if (raycastSensor == null)
        {
            for (int i = 0; i < 16; i++) sensor.AddObservation(1f);
            for (int i = 0; i < 10; i++) sensor.AddObservation(0f);
            return;
        }

        float[] rayDistances = raycastSensor.UpdateRays();

        if (rayDistances != null && rayDistances.Length > 0)
        {
            int raysPerSector = Mathf.Max(1, rayDistances.Length / 16);

            for (int s = 0; s < 16; s++)
            {
                float minDist = 1f;
                int startIdx = s * raysPerSector;
                int endIdx = Mathf.Min(startIdx + raysPerSector, rayDistances.Length);

                for (int i = startIdx; i < endIdx; i++)
                {
                    minDist = Mathf.Min(minDist, rayDistances[i]);
                }

                sensor.AddObservation(minDist);
            }
        }
        else
        {
            for (int i = 0; i < 16; i++) sensor.AddObservation(1f);
        }

        Vector3 vel = rb.velocity;
        sensor.AddObservation(Mathf.Clamp(vel.x / maxForwardSpeed, -1f, 1f));
        sensor.AddObservation(Mathf.Clamp(vel.z / maxForwardSpeed, -1f, 1f));
        sensor.AddObservation(Mathf.Clamp(vel.magnitude / maxForwardSpeed, 0f, 1f));

        sensor.AddObservation(Mathf.Clamp(rb.angularVelocity.y / (rotationSpeed * Mathf.Deg2Rad), -1f, 1f));

        if (!isTesting && trainingManager != null && trainingManager.currentPhase != null)
        {
            Vector2 spawnArea = trainingManager.currentPhase.spawnArea;
            sensor.AddObservation(Mathf.Clamp(transform.position.x / spawnArea.x, -1f, 1f));
            sensor.AddObservation(Mathf.Clamp(transform.position.z / spawnArea.y, -1f, 1f));
        }
        else
        {
            sensor.AddObservation(Mathf.Clamp(transform.position.x / 50f, -1f, 1f));
            sensor.AddObservation(Mathf.Clamp(transform.position.z / 50f, -1f, 1f));
        }

        sensor.AddObservation(transform.forward.x);
        sensor.AddObservation(transform.forward.z);

        float coverageRatio = maxPossibleCells > 0 ?
            (float)visitedCells.Count / maxPossibleCells : 0f;
        sensor.AddObservation(Mathf.Clamp01(coverageRatio));
        sensor.AddObservation(Mathf.Clamp01(totalDistanceTraveled / 100f));
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        // ============= ⏱️ TIMEOUT CHECK =============
        if (!isTimeout && !isHovering)
        {
            float elapsed = Time.time - episodeStartTime;
            if (elapsed > maxEpisodeTime)
            {
                isTimeout = true;
                testTotalCount++;
                testFailCount++;

                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                isHovering = true;
                hoverPosition = transform.position;

                Debug.Log($"⏱️ TIMEOUT — Episode {testTotalCount}/20 | " +
                          $"✅ Berhasil: {testSuccessCount} | ❌ Gagal: {testFailCount} | " +
                          $"Elapsed: {elapsed:F1}s");

                StartCoroutine(AutoResetAfterDelay(3f));
                return;
            }
        }

        if (episodeEnded) return;

        // ============= 🔥 LAYER 1: HOVER (prioritas tertinggi) =============
        if (isHovering)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.MovePosition(hoverPosition);
            return;
        }

        // ============= 🔥 LAYER 2: CHASE =============
        if (yolo != null && yolo.yoloDetected)
        {
            yoloLostTimer = 0f;
            chaseEverStarted = true;
        }
        else
        {
            yoloLostTimer += Time.fixedDeltaTime;
        }

        bool shouldChase = yolo != null
                           && chaseEverStarted
                           && (yolo.yoloDetected || yoloLostTimer < YOLO_LOST_TIMEOUT)
                           && !victimFound;

        if (shouldChase)
        {
            float error = yolo.yoloDetected ? (yolo.targetX - 0.5f) : (lastKnownTargetX - 0.5f);
            float bboxArea = yolo.targetBboxArea;

            float closestDist = raycastSensor != null
                ? raycastSensor.GetClosestObstacleDistance() : 10f;
            if (float.IsInfinity(closestDist) || closestDist <= 0f)
                closestDist = 10f;

            // Kalibrasi otomatis
            if (initialBboxArea < 0f && yolo.yoloDetected && bboxArea > 0f)
            {
                initialBboxArea = bboxArea;
                Debug.Log($"📐 Initial bbox calibrated: {initialBboxArea:F4}");
            }

            chaseFrameCount++;

            float growthRatio = (initialBboxArea > 0f && bboxArea > 0f)
                ? bboxArea / initialBboxArea
                : 1f;

            float bboxGrowthRate = bboxArea - prevBboxArea;
            prevBboxArea = bboxArea;

            // Obstacle stop darurat
            bool obstacleStop = closestDist < criticalZone * 0.6f;

            if (obstacleStop)
            {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;

                testTotalCount++;
                testFailCount++;

                Debug.Log($"❌ FAIL (Obstacle) — Episode {testTotalCount}/20 | " +
                          $"✅ {testSuccessCount} | ❌ {testFailCount} | dist={closestDist:F2}m");

                if (testTotalCount >= 20)
                {
                    Debug.Log($"🏁 TEST SELESAI 20 EPISODE | " +
                              $"✅ {testSuccessCount}/20 berhasil ({testSuccessCount * 5f}%) | " +
                              $"❌ {testFailCount}/20 gagal");
                }

                StartCoroutine(AutoResetAfterDelay(3f));
                return;
            }

            // === HARD STOP (ANTI NEMPEL & OVER) ===
            if (bboxArea > 0.06f && Mathf.Abs(error) < 0.1f)
            {
                victimFound = true;
                isHovering = true;
                hoverPosition = transform.position;
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                AddReward(10f);

                testTotalCount++;
                testSuccessCount++;
                float timeUsed = Time.time - episodeStartTime;
                Debug.Log($"🔥 HOVER (CLOSE ENOUGH) — Episode {testTotalCount}/20 | " +
                          $"✅ Berhasil: {testSuccessCount} | ❌ Gagal: {testFailCount} | " +
                          $"Waktu: {timeUsed:F1}s/{maxEpisodeTime:F0}s | " +
                          $"ratio={growthRatio:F2}x area={bboxArea:F4}");

                if (testTotalCount >= 20)
                    Debug.Log($"🏁 TEST SELESAI 20 EPISODE | " +
                              $"✅ {testSuccessCount}/20 berhasil ({testSuccessCount * 5f}%) | " +
                              $"❌ {testFailCount}/20 gagal");

                StartCoroutine(AutoResetAfterDelay(3f));
                return;
            }

            // === SPEED BARU (BERDASARKAN JARAK + ALIGNMENT) ===
            float targetArea = 0.06f;
            float proximity = Mathf.Clamp01(bboxArea / targetArea);
            float speedFactor = (Mathf.Abs(error) < 0.1f) ? proximity : 0.3f;

            // Obstacle override
            if (closestDist < dangerZone)
                speedFactor *= Mathf.InverseLerp(criticalZone, dangerZone, closestDist);

            Vector3 chaseVel = transform.forward * (speedFactor * maxForwardSpeed);
            rb.velocity = Vector3.Lerp(rb.velocity, chaseVel, 5f * Time.fixedDeltaTime);

            // === STEERING dengan DEAD ZONE ===
            float steeringInput = 0f;
            if (Mathf.Abs(error) > 0.05f) // dead zone
            {
                steeringInput = Mathf.Clamp(error * 2f, -0.4f, 0.4f);
            }

            if (Mathf.Abs(steeringInput) > 0.01f)
            {
                rb.MoveRotation(rb.rotation * Quaternion.Euler(
                    0f,
                    steeringInput * rotationSpeed * Time.fixedDeltaTime,
                    0f
                ));
            }

            // Debug setiap 15 frame
            if (chaseFrameCount % 15 == 0)
                Debug.Log($"🎯 Chase frame={chaseFrameCount} ratio={growthRatio:F2}x " +
                          $"area={bboxArea:F4} speed={speedFactor:F2} err={error:F3}");

            CheckCollisionRisk();
            CalculateMovementRewards();
            return;
        }

        // Reset kalibrasi saat keluar chase
        if (!shouldChase && !isHovering)
        {
            initialBboxArea = -1f;
            chaseFrameCount = 0;
            prevBboxArea = 0f;
        }

        // ============= LAYER 3: EXPLORE (PPO/LSTM) =============
        if (!isTesting && trainingManager != null)
            trainingManager.RegisterStep();

        var cont = actions.ContinuousActions;

        // Movement controls
        float forward = Mathf.Clamp01(cont[0]);
        float strafe = Mathf.Clamp(cont[1], -1f, 1f);
        float rotatePpo = Mathf.Clamp(cont[2], -1f, 1f);

        if (forward < 0.15f && Mathf.Abs(strafe) < 0.15f)
        {
            rotatePpo *= 0.4f;
        }

        Vector3 targetVel =
            transform.forward * (forward * maxForwardSpeed) +
            transform.right * (strafe * maxForwardSpeed * strafeMultiplier);

        Vector3 currentVel = new Vector3(rb.velocity.x, 0, rb.velocity.z);
        Vector3 newVel = Vector3.Lerp(currentVel, targetVel, acceleration * Time.fixedDeltaTime);
        rb.velocity = new Vector3(newVel.x, rb.velocity.y, newVel.z);

        if (Mathf.Abs(rotatePpo) > 0.05f)
        {
            float rotAmount = rotatePpo * rotationSpeed * Time.fixedDeltaTime;
            rb.MoveRotation(rb.rotation * Quaternion.Euler(0f, rotAmount, 0f));
        }

        // === REWARD CALCULATION ===
        CalculateMovementRewards();
        CheckCollisionRisk();
        CheckStagnation();

        float angularSpeed = Mathf.Abs(rb.angularVelocity.y);
        if (angularSpeed > 2.0f && rb.velocity.magnitude < 1.5f)
        {
            AddReward(-0.002f * Time.fixedDeltaTime);
        }

        rotationAccumulator += angularSpeed * Time.fixedDeltaTime;

        if (Time.time - lastRotationReset > 8f)
        {
            if (rotationAccumulator > 10f)
            {
                AddReward(-0.1f);

                if (!isTesting)
                {
                    Debug.Log($"⚠️ Loop detected! Rotation: {rotationAccumulator:F1} rad in 8s");
                }
            }
            rotationAccumulator = 0f;
            lastRotationReset = Time.time;
        }

        if (!isTesting && Time.time - lastLogTime >= LOG_INTERVAL)
        {
            Academy.Instance.StatsRecorder.Add("Custom/CollisionsPerEpisode", collisionCount);
            Academy.Instance.StatsRecorder.Add("Custom/AvgClosestDist", smoothedClosestDist);
            Academy.Instance.StatsRecorder.Add("Custom/UniqueCells", (float)uniqueCellsVisited);
            Academy.Instance.StatsRecorder.Add("Custom/CoverageRatio",
                maxPossibleCells > 0 ? (float)visitedCells.Count / maxPossibleCells : 0f);
            Academy.Instance.StatsRecorder.Add("Custom/DistanceTraveled", totalDistanceTraveled);
            Academy.Instance.StatsRecorder.Add("Custom/FrontierDistance", maxDistanceFromCenter);

            lastLogTime = Time.time;
        }

        if (Time.frameCount % 300 == 0 && isTesting)
        {
            Debug.Log($"📊 Cells: {visitedCells.Count}/{maxPossibleCells} | " +
                     $"Frontier: {maxDistanceFromCenter:F1}m | " +
                     $"Dist: {smoothedClosestDist:F2}m | " +
                     $"Speed: {rb.velocity.magnitude:F2}");
        }

        if (!isTesting && trainingManager != null && maxPossibleCells > 0)
        {
            float coverage = (float)visitedCells.Count / maxPossibleCells;

            if (coverage >= 0.95f)
            {
                trainingManager.RecordEpisodeCompletion("full_coverage");
                SafeEndEpisode(5.0f);
            }
            else if (coverage >= 0.75f && totalDistanceTraveled > 80f)
            {
                trainingManager.RecordEpisodeCompletion("good_coverage");
                SafeEndEpisode(3.0f);
            }
        }
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var cont = actionsOut.ContinuousActions;

        cont[0] = Input.GetKey(KeyCode.W) ? 1f : 0f;
        cont[1] = Input.GetKey(KeyCode.D) ? 1f : Input.GetKey(KeyCode.A) ? -1f : 0f;
        cont[2] = Input.GetKey(KeyCode.E) ? 1f : Input.GetKey(KeyCode.Q) ? -1f : 0f;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (isTesting || trainingManager == null) return;
        if (episodeEnded) return;

        if (collision.gameObject.CompareTag("Obstacle") ||
            collision.gameObject.CompareTag("Wall"))
        {
            collisionCount++;
            trainingManager.RecordEpisodeCompletion("collision");
            SafeEndEpisode(-3f);
        }
    }

    public void SafeEndEpisode(float customReward = 0f)
    {
        if (isTesting || trainingManager == null) return;
        if (episodeEnded) return;

        if (customReward != 0f)
            AddReward(customReward);

        episodeEnded = true;
        EndEpisode();
    }

    private IEnumerator AutoResetAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        episodeStartTime = Time.time;
        StartCoroutine(SafeEpisodeBeginTesting());
    }

    void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;

        float gridY = transform.position.y + 0.05f;
        foreach (var kvp in visitedCells)
        {
            int visits = kvp.Value.visitCount;
            Gizmos.color = visits == 1
                ? new Color(0f, 1f, 0f, 0.35f)
                : visits <= exhaustedVisitThreshold
                    ? new Color(1f, 1f, 0f, 0.35f)
                    : new Color(1f, 0f, 0f, 0.35f);
            Vector3 worldPos = new Vector3(kvp.Key.x * 2f, gridY, kvp.Key.z * 2f);
            Gizmos.DrawCube(worldPos, new Vector3(1.9f, 0.1f, 1.9f));
        }

        Gizmos.color = Color.blue;
        Gizmos.DrawRay(transform.position, transform.forward * 3f);
    }
}