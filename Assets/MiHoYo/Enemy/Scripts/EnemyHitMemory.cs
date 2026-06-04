using System;
using UnityEngine;

public class EnemyHitMemory : MonoBehaviour
{
    public event Action<EnemyHitMemory> HitMemoryChanged;

    public enum HitImpact
    {
        None = 0,
        Micro = 1,
        Small = 2,
        Heavy = 3
    }

    public enum HitDirection
    {
        Unknown = 0,
        Front = 1,
        Back = 2,
        Side = 3
    }

    [Header("References")]
    [SerializeField] private EnemyHitReactionController hitReactionController;

    [Header("Impact Thresholds")]
    [SerializeField] private float smallHitPoiseThreshold = 10f;
    [SerializeField] private float heavyHitPoiseThreshold = 25f;
    [SerializeField] private bool lowReactionCountsAsSmall = true;
    [SerializeField] private bool forcedHighReactionCountsAsHeavy = true;

    [Header("Recent Hit Windows")]
    [SerializeField] private float recentHitWindow = 2.5f;
    [SerializeField] private float recentHeavyHitWindow = 4f;
    [SerializeField] private int maxStoredHits = 32;

    [Header("Debug")]
    [SerializeField] private bool logDebug;

    private HitEntry[] _hitEntries;
    private int _nextHitIndex;
    private int _storedHitCount;
    private HitImpact _lastHitImpact = HitImpact.None;
    private HitDirection _lastHitDirection = HitDirection.Unknown;
    private EnemyHitReactionController.HitReactionType _lastHitReactType = EnemyHitReactionController.HitReactionType.None;
    private string _lastAttackId = string.Empty;
    private float _lastHitTime = -999f;
    private float _lastHeavyHitTime = -999f;
    private bool _lastHitInterruptedAttack;

    public HitImpact LastHitImpact => _lastHitImpact;
    public HitDirection LastHitDirection => _lastHitDirection;
    public EnemyHitReactionController.HitReactionType LastHitReactType => _lastHitReactType;
    public string LastAttackId => _lastAttackId;
    public float LastHitTime => _lastHitTime;
    public bool LastHitInterruptedAttack => _lastHitInterruptedAttack;
    public int RecentHitCount => CountRecentHits(HitImpact.None);
    public int RecentLightHitCount => CountRecentHits(HitImpact.Micro);
    public int RecentSmallHitCount => CountRecentHits(HitImpact.Small);
    public int RecentHeavyHitCount => CountRecentHits(HitImpact.Heavy);
    public bool WasRecentlyLaunchedOrHeavyHit => Time.time - _lastHeavyHitTime <= recentHeavyHitWindow;
    public bool WasRecentlyHit => Time.time - _lastHitTime <= recentHitWindow;

    private struct HitEntry
    {
        public float time;
        public HitImpact impact;
        public HitDirection direction;
        public string attackId;
    }

    private void Awake()
    {
        ResolveReferences();
        AllocateHitEntries();
    }

    private void OnEnable()
    {
        ResolveReferences();
        AllocateHitEntries();

        PlayerAttackHitboxController.PlayerAttackHitConfirmed += OnPlayerAttackHitConfirmed;
        if (hitReactionController != null)
        {
            hitReactionController.HitReactionStarted += OnHitReactionStarted;
        }
    }

    private void OnDisable()
    {
        PlayerAttackHitboxController.PlayerAttackHitConfirmed -= OnPlayerAttackHitConfirmed;
        if (hitReactionController != null)
        {
            hitReactionController.HitReactionStarted -= OnHitReactionStarted;
        }
    }

    private void OnValidate()
    {
        smallHitPoiseThreshold = Mathf.Max(0f, smallHitPoiseThreshold);
        heavyHitPoiseThreshold = Mathf.Max(smallHitPoiseThreshold, heavyHitPoiseThreshold);
        recentHitWindow = Mathf.Max(0.05f, recentHitWindow);
        recentHeavyHitWindow = Mathf.Max(recentHitWindow, recentHeavyHitWindow);
        maxStoredHits = Mathf.Max(4, maxStoredHits);
    }

    public void ResetMemory()
    {
        _nextHitIndex = 0;
        _storedHitCount = 0;
        _lastHitImpact = HitImpact.None;
        _lastHitDirection = HitDirection.Unknown;
        _lastHitReactType = EnemyHitReactionController.HitReactionType.None;
        _lastAttackId = string.Empty;
        _lastHitTime = -999f;
        _lastHeavyHitTime = -999f;
        _lastHitInterruptedAttack = false;
        AllocateHitEntries();
        HitMemoryChanged?.Invoke(this);
        LogDebug("Hit memory reset.");
    }

    private void OnPlayerAttackHitConfirmed(PlayerAttackHitData hitData)
    {
        if (hitData == null || hitData.HitCollider == null)
        {
            return;
        }

        Transform hitTransform = hitData.HitCollider.transform;
        if (hitTransform != transform && !hitTransform.IsChildOf(transform))
        {
            return;
        }

        HitImpact impact = ResolveImpact(hitData.PoiseDamage);
        HitDirection direction = ResolveDirection(hitData);
        RecordHit(impact, direction, hitData.AttackId);
    }

    private void OnHitReactionStarted(
        EnemyHitReactionController source,
        EnemyHitReactionController.HitReactionType reactionType,
        bool interruptedAttack)
    {
        if (source != hitReactionController)
        {
            return;
        }

        _lastHitReactType = reactionType;
        _lastHitInterruptedAttack = interruptedAttack;

        HitDirection reactionDirection = ResolveReactionDirection(reactionType);
        if (reactionDirection != HitDirection.Unknown)
        {
            _lastHitDirection = reactionDirection;
        }

        if (forcedHighReactionCountsAsHeavy && IsHighReaction(reactionType))
        {
            _lastHitImpact = HitImpact.Heavy;
            _lastHeavyHitTime = Time.time;
            PromoteLatestHit(HitImpact.Heavy);
        }
        else if (lowReactionCountsAsSmall && IsLowReaction(reactionType) && _lastHitImpact == HitImpact.Micro)
        {
            _lastHitImpact = HitImpact.Small;
            PromoteLatestHit(HitImpact.Small);
        }

        HitMemoryChanged?.Invoke(this);
        LogDebug($"Hit reaction remembered. reaction={reactionType}, interrupted={interruptedAttack}, impact={_lastHitImpact}, direction={_lastHitDirection}");
    }

    private void RecordHit(HitImpact impact, HitDirection direction, string attackId)
    {
        AllocateHitEntries();

        float now = Time.time;
        _lastHitImpact = impact;
        _lastHitDirection = direction;
        _lastAttackId = attackId;
        _lastHitTime = now;
        _lastHitReactType = EnemyHitReactionController.HitReactionType.None;
        _lastHitInterruptedAttack = false;

        if (impact == HitImpact.Heavy)
        {
            _lastHeavyHitTime = now;
        }

        _hitEntries[_nextHitIndex] = new HitEntry
        {
            time = now,
            impact = impact,
            direction = direction,
            attackId = attackId
        };

        _nextHitIndex = (_nextHitIndex + 1) % _hitEntries.Length;
        _storedHitCount = Mathf.Min(_storedHitCount + 1, _hitEntries.Length);
        HitMemoryChanged?.Invoke(this);
        LogDebug($"Player hit remembered. attackId={attackId}, impact={impact}, direction={direction}, recent={RecentHitCount}, micro={RecentLightHitCount}, small={RecentSmallHitCount}, heavy={RecentHeavyHitCount}");
    }

    private int CountRecentHits(HitImpact impactFilter)
    {
        if (_hitEntries == null || _storedHitCount <= 0)
        {
            return 0;
        }

        float minTime = Time.time - recentHitWindow;
        int count = 0;
        for (int i = 0; i < _storedHitCount; i++)
        {
            HitEntry entry = _hitEntries[i];
            if (entry.time < minTime)
            {
                continue;
            }

            if (impactFilter != HitImpact.None && entry.impact != impactFilter)
            {
                continue;
            }

            count++;
        }

        return count;
    }

    private HitImpact ResolveImpact(float poiseDamage)
    {
        if (poiseDamage >= heavyHitPoiseThreshold)
        {
            return HitImpact.Heavy;
        }

        if (poiseDamage >= smallHitPoiseThreshold)
        {
            return HitImpact.Small;
        }

        return HitImpact.Micro;
    }

    private HitDirection ResolveDirection(PlayerAttackHitData hitData)
    {
        Vector3 sourceDirection = Vector3.zero;
        if (hitData.Attacker != null)
        {
            sourceDirection = hitData.Attacker.transform.position - transform.position;
        }
        else if (hitData.Direction.sqrMagnitude > 0.001f)
        {
            sourceDirection = -hitData.Direction;
        }

        sourceDirection.y = 0f;
        if (sourceDirection.sqrMagnitude <= 0.001f)
        {
            return HitDirection.Unknown;
        }

        Vector3 direction = sourceDirection.normalized;
        float frontDot = Vector3.Dot(transform.forward, direction);
        if (frontDot >= 0.45f)
        {
            return HitDirection.Front;
        }

        if (frontDot <= -0.45f)
        {
            return HitDirection.Back;
        }

        return HitDirection.Side;
    }

    private HitDirection ResolveReactionDirection(EnemyHitReactionController.HitReactionType reactionType)
    {
        switch (reactionType)
        {
            case EnemyHitReactionController.HitReactionType.LowFront:
            case EnemyHitReactionController.HitReactionType.HighFront:
                return HitDirection.Front;
            case EnemyHitReactionController.HitReactionType.LowBack:
            case EnemyHitReactionController.HitReactionType.HighBack:
                return HitDirection.Back;
            default:
                return HitDirection.Unknown;
        }
    }

    private bool IsHighReaction(EnemyHitReactionController.HitReactionType reactionType)
    {
        return reactionType == EnemyHitReactionController.HitReactionType.HighFront ||
            reactionType == EnemyHitReactionController.HitReactionType.HighBack;
    }

    private bool IsLowReaction(EnemyHitReactionController.HitReactionType reactionType)
    {
        return reactionType == EnemyHitReactionController.HitReactionType.LowFront ||
            reactionType == EnemyHitReactionController.HitReactionType.LowBack;
    }

    private void PromoteLatestHit(HitImpact impact)
    {
        if (_hitEntries == null || _storedHitCount <= 0)
        {
            return;
        }

        int latestIndex = (_nextHitIndex - 1 + _hitEntries.Length) % _hitEntries.Length;
        HitEntry latest = _hitEntries[latestIndex];
        if (Time.time - latest.time > 0.15f)
        {
            return;
        }

        latest.impact = impact;
        _hitEntries[latestIndex] = latest;
    }

    private void ResolveReferences()
    {
        if (hitReactionController == null)
        {
            hitReactionController = GetComponent<EnemyHitReactionController>();
        }
    }

    private void AllocateHitEntries()
    {
        if (_hitEntries != null && _hitEntries.Length == maxStoredHits)
        {
            return;
        }

        _hitEntries = new HitEntry[maxStoredHits];
        _nextHitIndex = 0;
        _storedHitCount = 0;
    }

    private void LogDebug(string message)
    {
        if (!logDebug)
        {
            return;
        }

        Debug.Log($"[EnemyHitMemory] {message} time={Time.time:F3}", this);
    }
}
