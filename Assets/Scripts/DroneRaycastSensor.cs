using UnityEngine;

public class DroneRaycastSensor : MonoBehaviour
{
    [Header("Raycast Settings")]
    [Range(1, 64)] public int rayCount = 32;              // ⬅️ MIN-MAX CLAMPED
    [Min(0.1f)] public float rayLength = 12f;           // ⬅️ MIN VALUE
    [Range(0f, 360f)] public float rayAngle = 360f;
    public LayerMask obstacleAndWallMask;               // ⬅️ GABUNG LAYER OBSTACLE + WALL

    [Header("Raycast Optimization")]
    public Vector3 rayOriginOffset = Vector3.up * 0.2f;
    public QueryTriggerInteraction queryTrigger = QueryTriggerInteraction.Ignore;

    [Header("Debug Visualization")]
    public bool showRays = true;                       // ⬅️ MATIKAN SAAT TRAINING
    public Color hitColor = Color.red;
    public Color missColor = Color.green;

    private float[] rayDistances;
    private Vector3[] rayDirections;
    private Vector3 rayOrigin;

    public float[] RayDistances => rayDistances;
    public int RayCount => rayCount;

    void Start()
    {
        ValidateAndResizeArrays();
        InitializeRayDirections();

        Debug.Log($"🔍 Raycast Sensor Initialized: {rayCount} rays, Length: {rayLength}");
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        // Clamp values
        if (rayCount < 1) rayCount = 1;
        if (rayCount > 64) rayCount = 64; // Limit for performance
        if (rayLength < 0.1f) rayLength = 0.1f;
        if (rayAngle < 0f) rayAngle = 0f;
        if (rayAngle > 360f) rayAngle = 360f;

        ValidateAndResizeArrays();
    }
#endif

    private void ValidateAndResizeArrays()
    {
        // Resize arrays if needed
        if (rayDistances == null || rayDistances.Length != rayCount)
            rayDistances = new float[rayCount];

        if (rayDirections == null || rayDirections.Length != rayCount)
            rayDirections = new Vector3[rayCount];
    }

    private void InitializeRayDirections()
    {
        UpdateRayDirections();
    }

    private void UpdateRayDirections()
    {
        // 🔴 PERBAIKAN: Gunakan TransformPoint untuk rotasi-aware offset
        rayOrigin = transform.TransformPoint(rayOriginOffset);

        // 🔴 PERBAIKAN: Hitung direction relatif terhadap yaw world
        float baseYaw = transform.eulerAngles.y;

        for (int i = 0; i < rayCount; i++)
        {
            float angle = i * (rayAngle / rayCount);
            float worldYaw = baseYaw + angle;
            rayDirections[i] = Quaternion.Euler(0f, worldYaw, 0f) * Vector3.forward;
        }
    }

    public float[] UpdateRays()
    {
        // Update directions berdasarkan rotasi drone saat ini
        UpdateRayDirections();

        // SATU RAYCAST PER ARAH
        for (int i = 0; i < rayCount; i++)
        {
            RaycastHit hit;

            // SATU raycast untuk obstacle + wall
            if (Physics.Raycast(rayOrigin, rayDirections[i], out hit, rayLength,
                obstacleAndWallMask, queryTrigger))
            {
                // Normalize distance to [0, 1]
                rayDistances[i] = Mathf.Clamp01(hit.distance / rayLength);

                // Debug visualization (ONLY if enabled)
                if (showRays)
                {
                    Debug.DrawRay(rayOrigin, rayDirections[i] * hit.distance, hitColor, 0.1f);
                }
            }
            else
            {
                // No hit = max distance (normalized to 1)
                rayDistances[i] = 1f;

                // Debug visualization (ONLY if enabled)
                if (showRays)
                {
                    Debug.DrawRay(rayOrigin, rayDirections[i] * rayLength, missColor, 0.1f);
                }
            }
        }

        return rayDistances;
    }

    public float GetClosestObstacleDistance()
    {
        // 🔴 PERBAIKAN: Null and bounds safety
        if (rayDistances == null || rayDistances.Length == 0)
            return rayLength;

        float closestNormalized = 1f; // Default: no obstacle

        // Cari jarak terdekat yang terdeteksi
        foreach (float dist in rayDistances)
        {
            if (dist < closestNormalized)
                closestNormalized = dist;
        }

        // Convert ke world distance
        return closestNormalized * rayLength;
    }

    public int GetClosestObstacleRayIndex()
    {
        // 🔴 PERBAIKAN: Safety check
        if (rayDistances == null || rayDistances.Length == 0)
            return 0;

        float closest = 1f;
        int closestIndex = 0;

        for (int i = 0; i < rayCount; i++)
        {
            if (i < rayDistances.Length && rayDistances[i] < closest)
            {
                closest = rayDistances[i];
                closestIndex = i;
            }
        }

        return closestIndex;
    }

    public Vector3 GetClosestObstacleDirection()
    {
        // 🔴 PERBAIKAN: Update direction sebelum mengembalikan
        if (rayDirections == null || rayDirections.Length == 0)
            UpdateRayDirections();

        int index = GetClosestObstacleRayIndex();

        if (index >= 0 && index < rayDirections.Length)
            return rayDirections[index];

        return transform.forward; // Fallback
    }

    public float GetRayDistanceInDirection(Vector3 worldDirection)
    {
        // 🔴 PERBAIKAN: Pastikan origin up-to-date
        UpdateRayDirections();

        RaycastHit hit;
        if (Physics.Raycast(rayOrigin, worldDirection.normalized, out hit, rayLength,
            obstacleAndWallMask, queryTrigger))
        {
            return hit.distance;
        }

        return rayLength;
    }

    public bool HasObstacleInFront(float threshold = 0.3f)
    {
        // Safety check
        if (rayCount == 0 || rayDistances == null || rayDistances.Length == 0)
            return false;

        // Front direction (first ray should be forward)
        if (rayCount > 0)
        {
            // Index 0 adalah forward karena rayDirections dihitung dari transform.forward
            float worldDistance = rayDistances[0] * rayLength;
            return worldDistance < threshold;
        }

        return false;
    }

    public int GetRayCountInRange(float minDistance, float maxDistance)
    {
        // Safety check
        if (rayDistances == null || rayDistances.Length == 0)
            return 0;

        int count = 0;
        float min = minDistance / rayLength;
        float max = maxDistance / rayLength;

        foreach (float dist in rayDistances)
        {
            if (dist >= min && dist <= max)
                count++;
        }

        return count;
    }

    // ⬅️ DEBUG METHODS (for training analysis)
    public void LogRayDistances()
    {
        if (rayDistances == null || rayDistances.Length == 0)
        {
            Debug.Log("Ray distances array not initialized");
            return;
        }

        string log = "Ray Distances: ";
        int logCount = Mathf.Min(rayCount, 8); // Log 8 pertama saja
        for (int i = 0; i < logCount; i++)
        {
            if (i < rayDistances.Length)
                log += $"[{i}:{rayDistances[i]:F2}] ";
        }
        Debug.Log(log);
    }

    // ⬅️ UTILITY METHOD untuk training manager
    public void SetDebugRays(bool enabled)
    {
        showRays = enabled;
        if (!showRays)
        {
            Debug.Log("🔍 Ray debug visualization disabled");
        }
    }

    // ⬅️ OPTIONAL: SphereCast untuk deteksi lebih akurat
    public float GetSphereCastDistance(Vector3 direction, float radius = 0.2f)
    {
        UpdateRayDirections();

        RaycastHit hit;
        if (Physics.SphereCast(rayOrigin, radius, direction.normalized, out hit,
            rayLength, obstacleAndWallMask, queryTrigger))
        {
            return hit.distance;
        }

        return rayLength;
    }

    // ⬅️ EDITOR VISUALIZATION
    void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying || rayDirections == null)
            return;

        UpdateRayDirections(); // Update untuk preview di editor

        Gizmos.color = Color.cyan;
        Gizmos.DrawSphere(rayOrigin, 0.05f);

        // Draw all ray directions
        for (int i = 0; i < rayCount; i++)
        {
            // Safety check
            if (i >= rayDirections.Length) continue;

            // Gradient color based on distance
            float alpha = 0.3f;
            if (rayDistances != null && i < rayDistances.Length)
            {
                alpha = 0.1f + (rayDistances[i] * 0.3f); // Semakin jauh, semakin terang
            }

            Color rayColor = new Color(0, 1, 1, alpha);
            Gizmos.color = rayColor;
            Gizmos.DrawRay(rayOrigin, rayDirections[i] * 1.5f);
        }

        // Highlight forward direction
        Gizmos.color = Color.yellow;
        Gizmos.DrawRay(rayOrigin, transform.forward * 2f);

        // Draw ray origin offset helper
        Gizmos.color = Color.magenta;
        Vector3 worldOffset = transform.TransformPoint(rayOriginOffset);
        Gizmos.DrawLine(transform.position, worldOffset);
        Gizmos.DrawWireSphere(worldOffset, 0.03f);
    }

    void OnDrawGizmos()
    {
        if (!showRays || !Application.isPlaying)
            return;

        // Safety check
        if (rayDistances == null || rayDirections == null)
            return;

        // Visualize ray hits in real-time
        UpdateRayDirections();

        for (int i = 0; i < rayCount; i++)
        {
            // Safety check
            if (i >= rayDistances.Length || i >= rayDirections.Length)
                continue;

            float distance = rayDistances[i] * rayLength;
            Color rayColor = rayDistances[i] < 1f ? hitColor : missColor;

            Gizmos.color = rayColor;
            Gizmos.DrawRay(rayOrigin, rayDirections[i] * distance);
        }
    }

    // ⬅️ PUBLIC HELPER untuk mendapatkan ray origin world position
    public Vector3 GetRayOriginWorldPosition()
    {
        UpdateRayDirections();
        return rayOrigin;
    }

    // ⬅️ PUBLIC HELPER untuk mendapatkan semua ray directions
    public Vector3[] GetAllRayDirections()
    {
        UpdateRayDirections();
        return rayDirections;
    }

    // ⬅️ PUBLIC HELPER untuk reset sensor
    public void ResetSensor()
    {
        ValidateAndResizeArrays();
        InitializeRayDirections();

        // Reset distances
        for (int i = 0; i < rayCount; i++)
        {
            if (i < rayDistances.Length)
                rayDistances[i] = 1f;
        }
    }
}