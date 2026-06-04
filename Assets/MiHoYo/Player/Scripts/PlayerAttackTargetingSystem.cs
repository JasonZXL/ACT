using UnityEngine;

public class PlayerAttackTargetingSystem : MonoBehaviour
{
    [Header("Search")]
    [SerializeField] private LayerMask targetLayers = ~0;
    [SerializeField] private float searchRadius = 6f;
    [SerializeField] private float searchAngle = 100f;
    [SerializeField] private float maxTargetHoldDistance = 8f;
    [SerializeField] private QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Collide;
    [SerializeField] private int maxSearchResults = 24;

    [Header("Facing")]
    [SerializeField] private float turnSpeed = 1080f;
    [SerializeField] private bool keepTargetOnlyWhileAttackLocked = true;

    [Header("Debug")]
    [SerializeField] private bool logTargetingDebug;
    [SerializeField] private bool drawSearchGizmos = true;

    private Collider[] _searchBuffer;
    private AttackTarget _currentTarget;

    public AttackTarget CurrentTarget => _currentTarget;
    public bool HasTarget => _currentTarget != null && _currentTarget.Targetable;

    private void Awake()
    {
        EnsureSearchBuffer();
    }

    private void OnValidate()
    {
        searchRadius = Mathf.Max(0.1f, searchRadius);
        maxTargetHoldDistance = Mathf.Max(searchRadius, maxTargetHoldDistance);
        searchAngle = Mathf.Clamp(searchAngle, 1f, 360f);
        turnSpeed = Mathf.Max(1f, turnSpeed);
        maxSearchResults = Mathf.Max(1, maxSearchResults);
    }

    public bool AcquireAttackTarget()
    {
        return AcquireAttackTarget(searchAngle);
    }

    public bool AcquireAttackTarget(float searchAngleOverride)
    {
        EnsureSearchBuffer();

        AttackTarget bestTarget = null;
        float bestScore = float.NegativeInfinity;
        Vector3 origin = transform.position;
        Vector3 forward = transform.forward;
        float effectiveSearchAngle = Mathf.Clamp(searchAngleOverride, 1f, 360f);
        float halfSearchAngle = effectiveSearchAngle * 0.5f;

        int hitCount = Physics.OverlapSphereNonAlloc(origin, searchRadius, _searchBuffer, targetLayers, triggerInteraction);
        for (int i = 0; i < hitCount; i++)
        {
            Collider candidateCollider = _searchBuffer[i];
            if (candidateCollider == null || candidateCollider.transform == transform || candidateCollider.transform.IsChildOf(transform))
            {
                continue;
            }

            AttackTarget candidate = candidateCollider.GetComponentInParent<AttackTarget>();
            if (candidate == null || !candidate.Targetable)
            {
                continue;
            }

            Vector3 toTarget = candidate.TargetPoint.position - origin;
            toTarget.y = 0f;
            float distance = toTarget.magnitude;
            if (distance <= 0.001f)
            {
                continue;
            }

            float angle = Vector3.Angle(forward, toTarget / distance);
            if (angle > halfSearchAngle)
            {
                continue;
            }

            float angleScore = 1f - angle / halfSearchAngle;
            float distanceScore = 1f - Mathf.Clamp01(distance / searchRadius);
            float score = angleScore * 0.7f + distanceScore * 0.3f;
            if (score > bestScore)
            {
                bestScore = score;
                bestTarget = candidate;
            }
        }

        _currentTarget = bestTarget;
        LogTargetingDebug(_currentTarget != null
            ? $"Attack target acquired: {_currentTarget.name}, angle={effectiveSearchAngle:F1}"
            : $"Attack target acquire failed. angle={effectiveSearchAngle:F1}");
        return _currentTarget != null;
    }

    public bool TrySetAttackTargetFromTransform(Transform targetTransform, bool snapToTarget = true)
    {
        if (targetTransform == null)
        {
            return false;
        }

        AttackTarget target = targetTransform.GetComponentInParent<AttackTarget>();
        if (target == null)
        {
            target = targetTransform.GetComponentInChildren<AttackTarget>();
        }

        if (target == null || !target.Targetable)
        {
            return false;
        }

        _currentTarget = target;
        if (snapToTarget)
        {
            SnapToCurrentTarget();
        }

        LogTargetingDebug($"Attack target set directly: {_currentTarget.name}");
        return true;
    }

    public void FaceCurrentTarget(float deltaTime)
    {
        if (!HasTarget)
        {
            _currentTarget = null;
            return;
        }

        Vector3 toTarget = _currentTarget.TargetPoint.position - transform.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude <= 0.001f)
        {
            return;
        }

        Quaternion targetRotation = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, turnSpeed * deltaTime);
    }

    public void SnapToCurrentTarget()
    {
        if (!HasTarget)
        {
            _currentTarget = null;
            return;
        }

        Vector3 toTarget = _currentTarget.TargetPoint.position - transform.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude <= 0.001f)
        {
            return;
        }

        transform.rotation = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
    }

    public void ClearAttackTarget()
    {
        if (_currentTarget != null)
        {
            LogTargetingDebug($"Attack target cleared: {_currentTarget.name}");
        }

        _currentTarget = null;
    }

    public void ClearAttackTargetIfInvalid(bool attackLocked)
    {
        if (_currentTarget == null)
        {
            return;
        }

        if (!HasTarget || Vector3.Distance(transform.position, _currentTarget.TargetPoint.position) > maxTargetHoldDistance)
        {
            ClearAttackTarget();
            return;
        }

        if (keepTargetOnlyWhileAttackLocked && !attackLocked)
        {
            ClearAttackTarget();
        }
    }

    private void EnsureSearchBuffer()
    {
        if (_searchBuffer == null || _searchBuffer.Length != maxSearchResults)
        {
            _searchBuffer = new Collider[maxSearchResults];
        }
    }

    private void LogTargetingDebug(string message)
    {
        if (!logTargetingDebug)
        {
            return;
        }

        Debug.Log($"[PlayerAttackTargeting] {message} time={Time.time:F3}", this);
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawSearchGizmos)
        {
            return;
        }

        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.2f);
        Gizmos.DrawSphere(transform.position, searchRadius);
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.85f);
        Gizmos.DrawWireSphere(transform.position, searchRadius);
    }
}
