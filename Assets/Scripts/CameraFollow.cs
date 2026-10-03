using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [Header("Target")]
    public Transform droneTarget;

    [Header("Camera Position Settings")]
    public float cameraHeight = 15f;          // ⬅️ Tinggi kamera dari ground
    public float cameraDistance = 12f;        // ⬅️ Jarak horizontal dari target
    public float heightDamping = 2f;          // ⬅️ Smoothing untuk perubahan tinggi
    public float distanceDamping = 3f;        // ⬅️ Smoothing untuk perubahan jarak

    [Header("Camera Rotation Settings")]
    public float fixedPitchAngle = 60f;       // ⬅️ Sudut kamera menghadap ke bawah (derajat)
    public bool followRotation = false;       // ⬅️ Kalau true, kamera ikut rotasi drone
    public float rotationDamping = 5f;        // ⬅️ Smoothing untuk rotasi

    [Header("Edge Scrolling/Boundaries")]
    public bool useBoundaries = false;
    public float boundaryPadding = 2f;        // ⬅️ Jarak dari target sebelum kamera bergerak
    public float maxBoundaryDistance = 10f;   // ⬅️ Jarak maksimal kamera bisa bergerak dari target

    [Header("Zoom Settings")]
    public float minHeight = 5f;
    public float maxHeight = 30f;
    public float minDistance = 5f;
    public float maxDistance = 20f;
    public float zoomSpeed = 10f;

    private Vector3 currentVelocity;
    private float currentHeight;
    private float currentDistance;

    void Start()
    {
        // Inisialisasi posisi kamera
        currentHeight = cameraHeight;
        currentDistance = cameraDistance;

        // Set initial rotation (selalu melihat ke bawah)
        transform.rotation = Quaternion.Euler(fixedPitchAngle, 0f, 0f);
    }

    void LateUpdate()
    {
        if (droneTarget == null) return;

        HandleZoom();
        UpdateCameraPosition();

        if (useBoundaries)
        {
            ApplyBoundaries();
        }
    }

    void UpdateCameraPosition()
    {
        // Target position = posisi drone di ground level
        Vector3 targetPosition = droneTarget.position;
        targetPosition.y = 0; // Gunakan ground level sebagai reference

        // Hitung posisi kamera berdasarkan sudut tetap
        float targetHeight = targetPosition.y + currentHeight;
        float targetDistance = currentDistance;

        // Smooth damping untuk height dan distance
        float newHeight = Mathf.Lerp(transform.position.y, targetHeight, heightDamping * Time.deltaTime);
        float newDistance = Mathf.Lerp(Mathf.Abs(transform.position.x - targetPosition.x), targetDistance, distanceDamping * Time.deltaTime);

        // Hitung offset berdasarkan sudut kamera
        Vector3 offset = new Vector3(0, 0, -newDistance);

        // Rotasi offset berdasarkan yaw drone (jika followRotation true)
        if (followRotation)
        {
            offset = Quaternion.Euler(0, droneTarget.eulerAngles.y, 0) * offset;
        }

        // Terapkan offset ke target position
        Vector3 desiredPosition = targetPosition + offset;
        desiredPosition.y = newHeight;

        // Smooth movement
        transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref currentVelocity, 0.1f);

        // Atur rotasi kamera
        UpdateCameraRotation();
    }

    void UpdateCameraRotation()
    {
        if (followRotation)
        {
            // Kamera mengikuti rotasi drone
            Quaternion targetRotation = Quaternion.Euler(fixedPitchAngle, droneTarget.eulerAngles.y, 0);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationDamping * Time.deltaTime);
        }
        else
        {
            // Kamera tetap menghadap ke target dari atas
            Vector3 lookAtPoint = droneTarget.position;
            lookAtPoint.y += 2f; // Sedikit di atas drone

            transform.rotation = Quaternion.Euler(fixedPitchAngle, 0f, 0f);

            // Atau alternatif: selalu melihat ke drone
            // transform.LookAt(lookAtPoint);
        }
    }

    void HandleZoom()
    {
        // Input scroll wheel
        float scroll = Input.GetAxis("Mouse ScrollWheel");

        if (scroll != 0)
        {
            // Zoom dengan mengubah height dan distance secara proporsional
            cameraHeight -= scroll * zoomSpeed;
            cameraDistance -= scroll * zoomSpeed * 0.8f; // Distance berubah lebih lambat

            // Clamp values
            cameraHeight = Mathf.Clamp(cameraHeight, minHeight, maxHeight);
            cameraDistance = Mathf.Clamp(cameraDistance, minDistance, maxDistance);

            // Update current values
            currentHeight = cameraHeight;
            currentDistance = cameraDistance;
        }
    }

    void ApplyBoundaries()
    {
        Vector3 targetPos = droneTarget.position;
        Vector3 cameraPos = transform.position;

        // Hitung jarak horizontal dari drone
        Vector3 horizontalDifference = cameraPos - targetPos;
        horizontalDifference.y = 0;

        float currentDistanceFromTarget = horizontalDifference.magnitude;

        // Jika kamera terlalu jauh dari target
        if (currentDistanceFromTarget > maxBoundaryDistance)
        {
            Vector3 directionToTarget = (targetPos - cameraPos).normalized;
            directionToTarget.y = 0;

            Vector3 newPosition = targetPos + directionToTarget * maxBoundaryDistance;
            newPosition.y = cameraPos.y;

            transform.position = newPosition;
        }

        // Jika drone bergerak mendekati edge
        Vector3 droneToCamera = cameraPos - targetPos;
        droneToCamera.y = 0;

        if (droneToCamera.magnitude < boundaryPadding)
        {
            // Dorong kamera sedikit menjauh
            Vector3 newPos = targetPos + droneToCamera.normalized * boundaryPadding;
            newPos.y = cameraPos.y;
            transform.position = newPos;
        }
    }

    // ⬅️ PUBLIC METHODS UNTUK DYNAMIC CONTROL
    public void SetCameraHeight(float height)
    {
        cameraHeight = Mathf.Clamp(height, minHeight, maxHeight);
        currentHeight = cameraHeight;
    }

    public void SetCameraDistance(float distance)
    {
        cameraDistance = Mathf.Clamp(distance, minDistance, maxDistance);
        currentDistance = cameraDistance;
    }

    public void SetPitchAngle(float angle)
    {
        fixedPitchAngle = Mathf.Clamp(angle, 10f, 89f); // 10-89 derajat
    }

    public void ToggleFollowRotation()
    {
        followRotation = !followRotation;
    }

    public void ResetToDefaults()
    {
        cameraHeight = 15f;
        cameraDistance = 12f;
        fixedPitchAngle = 60f;
        followRotation = false;

        currentHeight = cameraHeight;
        currentDistance = cameraDistance;
    }

    // ⬅️ DEBUG VISUALIZATION
    void OnDrawGizmosSelected()
    {
        if (droneTarget == null) return;

        // Draw camera view frustum
        Camera cam = GetComponent<Camera>();
        if (cam != null)
        {
            Gizmos.color = Color.yellow;

            Vector3[] frustumCorners = new Vector3[4];
            cam.CalculateFrustumCorners(new Rect(0, 0, 1, 1), cam.farClipPlane, Camera.MonoOrStereoscopicEye.Mono, frustumCorners);

            for (int i = 0; i < 4; i++)
            {
                Vector3 worldCorner = transform.TransformVector(frustumCorners[i]);
                Gizmos.DrawLine(transform.position, transform.position + worldCorner);
            }
        }

        // Draw boundary zone
        if (useBoundaries)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(droneTarget.position, maxBoundaryDistance);

            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(droneTarget.position, boundaryPadding);
        }

        // Draw line to target
        Gizmos.color = Color.green;
        Gizmos.DrawLine(transform.position, droneTarget.position);
    }
}