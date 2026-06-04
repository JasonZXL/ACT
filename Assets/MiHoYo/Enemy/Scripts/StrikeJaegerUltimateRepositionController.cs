using UnityEngine;

public class StrikeJaegerUltimateRepositionController : MonoBehaviour
{
    [Header("Visibility")]
    [SerializeField] private Renderer[] meshRenderers;

    [Header("Target")]
    [SerializeField] private Transform playerTarget;
    [SerializeField] private string playerTag = "Player";

    [Header("Teleport Behind Player")]
    [SerializeField] private float behindDistance = 2.2f;
    [SerializeField] private float sideOffset;
    [SerializeField] private bool keepCurrentY = true;
    [SerializeField] private bool facePlayerAfterTeleport = true;
    [SerializeField] private bool useGroundProbe;
    [SerializeField] private LayerMask groundLayers = ~0;
    [SerializeField] private float groundProbeHeight = 3f;
    [SerializeField] private float groundProbeDistance = 8f;

    [Header("Safe Teleport")]
    [SerializeField] private bool useSafeTeleport = true;
    [SerializeField, Range(0.1f, 1f)] private float minimumDistanceRatio = 1f;
    [SerializeField] private bool requireGroundForSafeTeleport = true;
    [SerializeField] private bool stayInPlaceWhenNoSafePoint = true;
    [SerializeField] private float landingCheckRadius = 0.45f;
    [SerializeField] private LayerMask blockingLayers = 0;
    [SerializeField] private QueryTriggerInteraction blockingTriggerInteraction = QueryTriggerInteraction.Ignore;
    [SerializeField] private float[] fallbackAnglesFromPlayerForward =
    {
        120f,
        -120f,
        60f,
        -60f
    };

    [Header("Character Controller")]
    [SerializeField] private CharacterController characterController;
    [SerializeField] private bool temporarilyDisableCharacterController = true;

    [Header("Debug")]
    [SerializeField] private bool logDebug;

    private void Awake()
    {
        ResolveReferences();
    }

    private void Reset()
    {
        ResolveReferences();
        meshRenderers = GetComponentsInChildren<Renderer>(true);
    }

    private void OnValidate()
    {
        behindDistance = Mathf.Max(0f, behindDistance);
        groundProbeHeight = Mathf.Max(0.01f, groundProbeHeight);
        groundProbeDistance = Mathf.Max(0.01f, groundProbeDistance);
        landingCheckRadius = Mathf.Max(0f, landingCheckRadius);
    }

    public void AE_UltimateHideMesh()
    {
        HideMesh();
    }

    public void AE_UltimateShowMesh()
    {
        ShowMesh();
    }

    public void AE_UltimateTeleportBehindPlayer()
    {
        TeleportBehindPlayer();
    }

    public void AE_UltimateTeleportRelativeToPlayerForward(float angleFromPlayerForward)
    {
        TeleportRelativeToPlayerForward(angleFromPlayerForward);
    }

    public void AE_UltimateVanishAndReposition()
    {
        HideMesh();
        TeleportBehindPlayer();
    }

    public void HideMesh()
    {
        SetMeshVisible(false);
    }

    public void ShowMesh()
    {
        SetMeshVisible(true);
    }

    public bool TeleportBehindPlayer()
    {
        return TeleportRelativeToPlayerForward(180f, true);
    }

    public bool TeleportRelativeToPlayerForward(float angleFromPlayerForward)
    {
        return TeleportRelativeToPlayerForward(angleFromPlayerForward, false);
    }

    public bool TeleportRelativeToPlayerForward(float angleFromPlayerForward, bool applySideOffset)
    {
        Transform target = ResolvePlayerTarget();
        if (target == null)
        {
            LogDebug("Teleport ignored: player target missing.");
            return false;
        }

        Vector3 targetForward = target.forward;
        targetForward.y = 0f;
        if (targetForward.sqrMagnitude <= 0.001f)
        {
            targetForward = transform.forward;
            targetForward.y = 0f;
        }

        if (targetForward.sqrMagnitude <= 0.001f)
        {
            targetForward = Vector3.forward;
        }

        targetForward.Normalize();
        Vector3 targetRight = target.right;
        targetRight.y = 0f;
        if (targetRight.sqrMagnitude > 0.001f)
        {
            targetRight.Normalize();
        }

        Vector3 destination;
        string destinationReason;
        if (useSafeTeleport)
        {
            destination = ResolveSafeTeleportDestination(
                target,
                targetForward,
                targetRight,
                angleFromPlayerForward,
                applySideOffset,
                out destinationReason);
        }
        else
        {
            destination = BuildTeleportDestination(
                target,
                targetForward,
                targetRight,
                angleFromPlayerForward,
                applySideOffset,
                out destinationReason);
        }

        MoveTo(destination);

        if (facePlayerAfterTeleport)
        {
            FacePlayer();
        }

        LogDebug($"Teleported near player. target={target.name}, destination={FormatVector(destination)}, reason={destinationReason}, angle={angleFromPlayerForward:F1}, distance={behindDistance:F2}, side={sideOffset:F2}");
        return true;
    }

    public void FacePlayer()
    {
        Transform target = ResolvePlayerTarget();
        if (target == null)
        {
            LogDebug("FacePlayer ignored: player target missing.");
            return;
        }

        Vector3 toTarget = target.position - transform.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude <= 0.001f)
        {
            return;
        }

        transform.rotation = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
        LogDebug($"Faced player. target={target.name}, direction={FormatVector(toTarget.normalized)}");
    }

    private void ResolveReferences()
    {
        if (characterController == null)
        {
            characterController = GetComponent<CharacterController>();
        }

        ResolvePlayerTarget();
    }

    private Transform ResolvePlayerTarget()
    {
        if (playerTarget != null)
        {
            return playerTarget;
        }

        if (!string.IsNullOrEmpty(playerTag))
        {
            GameObject playerObject = GameObject.FindGameObjectWithTag(playerTag);
            if (playerObject != null)
            {
                playerTarget = playerObject.transform;
            }
        }

        return playerTarget;
    }

    private void MoveTo(Vector3 destination)
    {
        bool shouldDisableController = temporarilyDisableCharacterController &&
                                       characterController != null &&
                                       characterController.enabled;

        if (shouldDisableController)
        {
            characterController.enabled = false;
        }

        transform.position = destination;

        if (shouldDisableController)
        {
            characterController.enabled = true;
        }
    }

    private Vector3 ResolveSafeTeleportDestination(
        Transform target,
        Vector3 targetForward,
        Vector3 targetRight,
        float primaryAngleFromPlayerForward,
        bool primaryApplySideOffset,
        out string reason)
    {
        if (TryBuildValidTeleportDestination(
                target,
                targetForward,
                targetRight,
                primaryAngleFromPlayerForward,
                primaryApplySideOffset,
                $"PrimaryAngle:{primaryAngleFromPlayerForward:F0}",
                out Vector3 primaryDestination,
                out reason))
        {
            return primaryDestination;
        }

        if (fallbackAnglesFromPlayerForward != null)
        {
            for (int i = 0; i < fallbackAnglesFromPlayerForward.Length; i++)
            {
                float angle = fallbackAnglesFromPlayerForward[i];
                bool applySideOffset = Mathf.Abs(Mathf.DeltaAngle(angle, 180f)) <= 0.01f;
                string label = $"FallbackAngle:{angle:F0}";
                if (TryBuildValidTeleportDestination(target, targetForward, targetRight, angle, applySideOffset, label, out Vector3 fallbackDestination, out reason))
                {
                    return fallbackDestination;
                }
            }
        }

        if (stayInPlaceWhenNoSafePoint)
        {
            reason = "NoSafePoint:StayInPlace";
            LogDebug("Safe teleport failed to find a valid destination. Staying in place.");
            return transform.position;
        }

        Vector3 unsafeDestination = BuildTeleportDestination(
            target,
            targetForward,
            targetRight,
            primaryAngleFromPlayerForward,
            primaryApplySideOffset,
            out reason);
        reason = $"UnsafeFallback:{reason}";
        return unsafeDestination;
    }

    private bool TryBuildValidTeleportDestination(
        Transform target,
        Vector3 targetForward,
        Vector3 targetRight,
        float angleFromPlayerForward,
        bool applySideOffset,
        string label,
        out Vector3 destination,
        out string reason)
    {
        destination = BuildTeleportDestination(target, targetForward, targetRight, angleFromPlayerForward, applySideOffset, out string buildReason);
        float horizontalDistance = HorizontalDistance(target.position, destination);
        float minimumDistance = behindDistance * minimumDistanceRatio;
        if (horizontalDistance + 0.001f < minimumDistance)
        {
            reason = $"{label}:RejectedDistance:{horizontalDistance:F2}<{minimumDistance:F2}";
            LogDebug($"Safe teleport candidate rejected. reason={reason}, destination={FormatVector(destination)}");
            return false;
        }

        Vector3 groundedPosition = destination;
        if (requireGroundForSafeTeleport && !TryProjectToGround(destination, out groundedPosition))
        {
            reason = $"{label}:RejectedNoGround";
            LogDebug($"Safe teleport candidate rejected. reason={reason}, destination={FormatVector(destination)}");
            return false;
        }

        if (requireGroundForSafeTeleport)
        {
            destination = groundedPosition;
        }

        if (landingCheckRadius > 0f && IsLandingBlocked(destination))
        {
            reason = $"{label}:RejectedBlocked";
            LogDebug($"Safe teleport candidate rejected. reason={reason}, destination={FormatVector(destination)}");
            return false;
        }

        reason = $"{label}:Accepted:{buildReason}, dist={horizontalDistance:F2}";
        return true;
    }

    private Vector3 BuildTeleportDestination(
        Transform target,
        Vector3 targetForward,
        Vector3 targetRight,
        float angleFromPlayerForward,
        bool applySideOffset,
        out string reason)
    {
        Vector3 directionFromPlayer = Quaternion.AngleAxis(angleFromPlayerForward, Vector3.up) * targetForward;
        directionFromPlayer.y = 0f;
        if (directionFromPlayer.sqrMagnitude <= 0.001f)
        {
            directionFromPlayer = -targetForward;
        }

        directionFromPlayer.Normalize();
        Vector3 destination = target.position + directionFromPlayer * behindDistance;
        if (applySideOffset)
        {
            destination += targetRight * sideOffset;
        }

        if (keepCurrentY)
        {
            destination.y = transform.position.y;
            reason = $"angle={angleFromPlayerForward:F0}, keepY";
        }
        else if (useGroundProbe && TryProjectToGround(destination, out Vector3 groundedPosition))
        {
            destination = groundedPosition;
            reason = $"angle={angleFromPlayerForward:F0}, grounded";
        }
        else
        {
            reason = $"angle={angleFromPlayerForward:F0}, rawY";
        }

        return destination;
    }

    private bool IsLandingBlocked(Vector3 destination)
    {
        Vector3 checkCenter = destination + Vector3.up * Mathf.Max(landingCheckRadius, 0.05f);
        Collider[] hits = Physics.OverlapSphere(checkCenter, landingCheckRadius, blockingLayers, blockingTriggerInteraction);
        for (int i = 0; i < hits.Length; i++)
        {
            Collider hit = hits[i];
            if (hit == null || hit.transform == transform || hit.transform.IsChildOf(transform))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private bool TryProjectToGround(Vector3 sourcePosition, out Vector3 groundedPosition)
    {
        Vector3 rayStart = sourcePosition + Vector3.up * groundProbeHeight;
        if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, groundProbeDistance, groundLayers, QueryTriggerInteraction.Ignore))
        {
            groundedPosition = hit.point;
            return true;
        }

        groundedPosition = sourcePosition;
        return false;
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    private void SetMeshVisible(bool visible)
    {
        if (meshRenderers == null)
        {
            return;
        }

        for (int i = 0; i < meshRenderers.Length; i++)
        {
            Renderer targetRenderer = meshRenderers[i];
            if (targetRenderer == null)
            {
                continue;
            }

            targetRenderer.enabled = visible;
        }

        LogDebug($"Mesh visibility changed. visible={visible}");
    }

    private void LogDebug(string message)
    {
        if (!logDebug)
        {
            return;
        }

        Debug.Log($"[StrikeJaegerUltimateReposition] {message} time={Time.time:F3}", this);
    }

    private static string FormatVector(Vector3 value)
    {
        return $"({value.x:F3},{value.y:F3},{value.z:F3})";
    }
}
