using System;
using System.Collections.Generic;
using UnityEngine;

public class EnemyAttackHitboxController : MonoBehaviour
{
    public static event Action<EnemyAttackHitData> EnemyAttackHitConfirmed;
    public static event Action<GameObject, string> EnemyAttackHitWindowStarted;

    public static void PublishEnemyAttackHit(EnemyAttackHitData hitData)
    {
        EnemyAttackHitConfirmed?.Invoke(hitData);
    }

    public enum HitboxShape
    {
        Box = 0,
        Sphere = 1
    }

    public enum HitboxSpaceMode
    {
        FollowOrigin = 0,
        LockOriginOnHitStart = 1,
        WorldAligned = 2,
        FollowPositionLockRotationOnHitStart = 3
    }

    [Serializable]
    public class AttackHitDefinition
    {
        public string attackId = "Enemy_Attack_01";
        public bool showGizmo = true;
        public HitboxShape shape = HitboxShape.Box;
        public Transform origin;
        public HitboxSpaceMode spaceMode = HitboxSpaceMode.FollowOrigin;
        public Vector3 centerOffset = new Vector3(0f, 1f, 1.5f);
        public Vector3 boxHalfExtents = new Vector3(0.8f, 0.8f, 0.9f);
        public float sphereRadius = 1f;
        public Vector3 rotationOffset;
        public float damage = 20f;
        public float poiseDamage = 10f;
        public float knockbackForce;
        [Tooltip("Keep this off for normal attacks. Multi-hit animations should use multiple AE_EnemyAttackHitStart/AE_EnemyAttackHitEnd windows instead.")]
        public bool allowRepeatedHits;
        public float repeatedHitInterval = 0.25f;
    }

    [Serializable]
    public class PlayerHitReactionSequenceRule
    {
        public string attackId = "";
        public bool matchByContains = true;
        public float comboResetDelay = 1.2f;
        public EnemyHitReactionType[] hitReactionSequence =
        {
            EnemyHitReactionType.Light
        };

        [NonSerialized] public int CurrentHitStep;
        [NonSerialized] public float LastHitStepTime = -999f;
    }

    [Header("Hit Definitions")]
    [SerializeField] private AttackHitDefinition defaultHitDefinition = new AttackHitDefinition();
    [SerializeField] private AttackHitDefinition[] attackHitDefinitions;

    [Header("Detection")]
    [SerializeField] private LayerMask targetLayers = ~0;
    [SerializeField] private QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Collide;
    [SerializeField] private int maxHits = 16;

    [Header("Perfect Dodge")]
    [SerializeField] private EnemyAttackWarningWindow attackWarningWindow;
    [SerializeField] private bool ignorePerfectDodgedTargets = true;

    [Header("Parry")]
    [SerializeField] private EnemyParryWindow parryWindow;
    [SerializeField] private bool ignoreParriedTargets = true;

    [Header("Player Hit Reaction")]
    [SerializeField] private bool useWarningTypeForPlayerHitReaction = true;
    [SerializeField] private float warningTypeMemoryDuration = 3f;
    [SerializeField] private EnemyHitReactionType defaultPlayerHitReaction = EnemyHitReactionType.Heavy;
    [SerializeField] private PlayerHitReactionSequenceRule[] playerHitReactionSequenceRules;

    [Header("Debug")]
    [SerializeField] private bool logHitDebug;
    [SerializeField] private bool logRejectedHits;
    [SerializeField] private bool drawHitboxGizmos = true;
    [SerializeField] private Color inactiveGizmoColor = new Color(1f, 0.25f, 0.1f, 0.25f);
    [SerializeField] private Color activeGizmoColor = new Color(1f, 0f, 0f, 0.35f);

    private readonly HashSet<int> _hitTargets = new HashSet<int>();
    private readonly Dictionary<int, float> _nextRepeatedHitTimes = new Dictionary<int, float>();
    private Collider[] _hitBuffer;
    private AttackHitDefinition _activeDefinition;
    private string _activeAttackId;
    private Vector3 _lockedOriginPosition;
    private Quaternion _lockedOriginRotation;
    private bool _hitWindowOpen;

    private void Awake()
    {
        if (attackWarningWindow == null)
        {
            attackWarningWindow = GetComponent<EnemyAttackWarningWindow>();
        }

        if (parryWindow == null)
        {
            parryWindow = GetComponent<EnemyParryWindow>();
        }

        EnsureHitBuffer();
    }

    private void OnValidate()
    {
        maxHits = Mathf.Max(1, maxHits);
        ValidateDefinition(defaultHitDefinition);
        ValidatePlayerHitReactionSequenceRules();

        if (attackHitDefinitions == null)
        {
            return;
        }

        for (int i = 0; i < attackHitDefinitions.Length; i++)
        {
            ValidateDefinition(attackHitDefinitions[i]);
        }

    }

    private void Update()
    {
        if (!_hitWindowOpen || _activeDefinition == null)
        {
            return;
        }

        EvaluateActiveHitbox();
    }

    public void AE_EnemyAttackHitStart(string attackId)
    {
        OpenHitWindow(attackId);
    }

    public void AE_EnemyAttackHitEnd()
    {
        CloseHitWindow();
    }

    public void AE_EnemyAttackHitOnce(string attackId)
    {
        AttackHitDefinition definition = GetDefinition(attackId);
        if (definition == null)
        {
            LogHitDebug($"Enemy attack hit once ignored: no definition found. attackId={attackId}");
            return;
        }

        _activeAttackId = attackId;
        _activeDefinition = definition;
        CacheLockedOrigin(definition);
        _hitTargets.Clear();
        _nextRepeatedHitTimes.Clear();
        AdvancePlayerHitReactionSequence(attackId);
        EnemyAttackHitWindowStarted?.Invoke(gameObject, attackId);
        EvaluateActiveHitbox();
        LogHitDebug($"Enemy attack hit once evaluated. attackId={attackId}");
    }

    public void AE_AttackHitStart(string attackId)
    {
        AE_EnemyAttackHitStart(attackId);
    }

    public void AE_AttackHitEnd()
    {
        AE_EnemyAttackHitEnd();
    }

    public void AE_ResetPlayerHitReactionSequence(string attackId)
    {
        PlayerHitReactionSequenceRule rule = FindPlayerHitReactionSequenceRule(attackId);
        if (rule == null)
        {
            LogHitDebug($"Player hit reaction sequence reset skipped: no rule found. attackId={attackId}");
            return;
        }

        ResetPlayerHitReactionSequence(rule, $"AnimationEvent:{attackId}");
    }

    public void AE_ResetAllPlayerHitReactionSequences()
    {
        if (playerHitReactionSequenceRules == null)
        {
            return;
        }

        for (int i = 0; i < playerHitReactionSequenceRules.Length; i++)
        {
            ResetPlayerHitReactionSequence(playerHitReactionSequenceRules[i], "AnimationEvent:All");
        }
    }

    public void OpenHitWindow(string attackId)
    {
        AttackHitDefinition definition = GetDefinition(attackId);
        if (definition == null)
        {
            LogHitDebug($"Enemy hit window open ignored: no definition found. attackId={attackId}");
            return;
        }

        _activeAttackId = attackId;
        _activeDefinition = definition;
        _hitWindowOpen = true;
        CacheLockedOrigin(definition);
        _hitTargets.Clear();
        _nextRepeatedHitTimes.Clear();
        AdvancePlayerHitReactionSequence(attackId);
        EnemyAttackHitWindowStarted?.Invoke(gameObject, attackId);
        EvaluateActiveHitbox();
        LogHitDebug($"Enemy hit window opened. attackId={attackId}");
    }

    public void CloseHitWindow()
    {
        if (!_hitWindowOpen)
        {
            return;
        }

        LogHitDebug($"Enemy hit window closed. attackId={_activeAttackId}, hitCount={_hitTargets.Count}");
        parryWindow?.ClearParriedState($"EnemyHitWindowClosed:{_activeAttackId}");
        _hitWindowOpen = false;
        _activeAttackId = string.Empty;
        _activeDefinition = null;
        _hitTargets.Clear();
        _nextRepeatedHitTimes.Clear();
    }

    private void EvaluateActiveHitbox()
    {
        EnsureHitBuffer();

        Transform origin = GetOrigin(_activeDefinition);
        Vector3 center = GetWorldCenter(origin, _activeDefinition);
        Quaternion rotation = GetWorldRotation(origin, _activeDefinition);

        int hitCount = _activeDefinition.shape == HitboxShape.Sphere
            ? Physics.OverlapSphereNonAlloc(center, _activeDefinition.sphereRadius, _hitBuffer, targetLayers, triggerInteraction)
            : Physics.OverlapBoxNonAlloc(center, _activeDefinition.boxHalfExtents, _hitBuffer, rotation, targetLayers, triggerInteraction);

        for (int i = 0; i < hitCount; i++)
        {
            Collider hitCollider = _hitBuffer[i];
            if (hitCollider == null || hitCollider.transform == transform || hitCollider.transform.IsChildOf(transform))
            {
                continue;
            }

            TryRegisterHit(hitCollider, center);
        }
    }

    private void TryRegisterHit(Collider hitCollider, Vector3 hitboxCenter)
    {
        if (ignorePerfectDodgedTargets &&
            attackWarningWindow != null &&
            attackWarningWindow.IsPerfectDodgedBy(hitCollider.transform))
        {
            LogRejectedHit(hitCollider, "target already perfect-dodged this enemy attack");
            return;
        }

        if (ignoreParriedTargets &&
            parryWindow != null &&
            parryWindow.IsParriedBy(hitCollider.transform))
        {
            LogRejectedHit(hitCollider, "target already parried this enemy attack");
            return;
        }

        int targetId = GetTargetId(hitCollider);
        if (!_activeDefinition.allowRepeatedHits && _hitTargets.Contains(targetId))
        {
            LogRejectedHit(hitCollider, "target already hit in this window");
            return;
        }

        if (_activeDefinition.allowRepeatedHits &&
            _nextRepeatedHitTimes.TryGetValue(targetId, out float nextHitTime) &&
            Time.time < nextHitTime)
        {
            LogRejectedHit(hitCollider, $"repeated hit cooldown active until {nextHitTime:F3}");
            return;
        }

        _hitTargets.Add(targetId);
        if (_activeDefinition.allowRepeatedHits)
        {
            _nextRepeatedHitTimes[targetId] = Time.time + _activeDefinition.repeatedHitInterval;
        }

        Vector3 closestPoint = hitCollider.ClosestPoint(hitboxCenter);
        Vector3 direction = closestPoint - transform.position;
        if (direction.sqrMagnitude <= 0.0001f)
        {
            direction = transform.forward;
        }

        EnemyAttackHitData hitData = new EnemyAttackHitData(
            gameObject,
            hitCollider,
            _activeAttackId,
            _activeDefinition.damage,
            _activeDefinition.poiseDamage,
            _activeDefinition.knockbackForce,
            closestPoint,
            direction.normalized,
            ResolvePlayerHitReactionType(_activeAttackId));

        DeliverHit(hitCollider, hitData);
        EnemyAttackHitConfirmed?.Invoke(hitData);
        LogHitDebug($"Enemy hit registered. attackId={_activeAttackId}, target={hitCollider.name}, damage={_activeDefinition.damage:F1}");
    }

    private void DeliverHit(Collider hitCollider, EnemyAttackHitData hitData)
    {
        IEnemyAttackReceiver receiver = FindReceiver(hitCollider);
        if (receiver != null)
        {
            receiver.ReceiveEnemyAttackHit(hitData);
            return;
        }

        hitCollider.SendMessageUpwards("ReceiveEnemyAttackHit", hitData, SendMessageOptions.DontRequireReceiver);
        hitCollider.SendMessageUpwards("TakeEnemyAttackHit", hitData, SendMessageOptions.DontRequireReceiver);
        hitCollider.SendMessageUpwards("TakeDamage", hitData.Damage, SendMessageOptions.DontRequireReceiver);
    }

    private IEnemyAttackReceiver FindReceiver(Collider hitCollider)
    {
        MonoBehaviour[] behaviours = hitCollider.GetComponentsInParent<MonoBehaviour>();
        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] is IEnemyAttackReceiver receiver)
            {
                return receiver;
            }
        }

        return null;
    }

    private AttackHitDefinition GetDefinition(string attackId)
    {
        if (!string.IsNullOrEmpty(attackId) && attackHitDefinitions != null)
        {
            for (int i = 0; i < attackHitDefinitions.Length; i++)
            {
                AttackHitDefinition definition = attackHitDefinitions[i];
                if (definition != null && definition.attackId == attackId)
                {
                    return definition;
                }
            }
        }

        return defaultHitDefinition;
    }

    private Transform GetOrigin(AttackHitDefinition definition)
    {
        return definition != null && definition.origin != null ? definition.origin : transform;
    }

    private Vector3 GetWorldCenter(Transform origin, AttackHitDefinition definition)
    {
        if (definition.spaceMode == HitboxSpaceMode.LockOriginOnHitStart && _activeDefinition == definition)
        {
            return _lockedOriginPosition + _lockedOriginRotation * definition.centerOffset;
        }

        if (definition.spaceMode == HitboxSpaceMode.FollowPositionLockRotationOnHitStart && _activeDefinition == definition)
        {
            return origin.position + _lockedOriginRotation * definition.centerOffset;
        }

        if (definition.spaceMode == HitboxSpaceMode.WorldAligned)
        {
            return origin.position + definition.centerOffset;
        }

        return origin.TransformPoint(definition.centerOffset);
    }

    private Quaternion GetWorldRotation(Transform origin, AttackHitDefinition definition)
    {
        if ((definition.spaceMode == HitboxSpaceMode.LockOriginOnHitStart ||
                definition.spaceMode == HitboxSpaceMode.FollowPositionLockRotationOnHitStart) &&
            _activeDefinition == definition)
        {
            return _lockedOriginRotation * Quaternion.Euler(definition.rotationOffset);
        }

        if (definition.spaceMode == HitboxSpaceMode.WorldAligned)
        {
            return Quaternion.Euler(definition.rotationOffset);
        }

        return origin.rotation * Quaternion.Euler(definition.rotationOffset);
    }

    private void CacheLockedOrigin(AttackHitDefinition definition)
    {
        Transform origin = GetOrigin(definition);
        _lockedOriginPosition = origin.position;
        _lockedOriginRotation = origin.rotation;
    }

    private int GetTargetId(Collider hitCollider)
    {
        if (hitCollider.attachedRigidbody != null)
        {
            return hitCollider.attachedRigidbody.GetInstanceID();
        }

        return hitCollider.transform.root.GetInstanceID();
    }

    private void EnsureHitBuffer()
    {
        if (_hitBuffer == null || _hitBuffer.Length != maxHits)
        {
            _hitBuffer = new Collider[maxHits];
        }
    }

    private void ValidateDefinition(AttackHitDefinition definition)
    {
        if (definition == null)
        {
            return;
        }

        definition.sphereRadius = Mathf.Max(0.01f, definition.sphereRadius);
        definition.boxHalfExtents = Vector3.Max(definition.boxHalfExtents, Vector3.one * 0.01f);
        definition.repeatedHitInterval = Mathf.Max(0.01f, definition.repeatedHitInterval);
    }

    private void ValidatePlayerHitReactionSequenceRules()
    {
        if (playerHitReactionSequenceRules == null)
        {
            return;
        }

        for (int i = 0; i < playerHitReactionSequenceRules.Length; i++)
        {
            PlayerHitReactionSequenceRule rule = playerHitReactionSequenceRules[i];
            if (rule == null)
            {
                continue;
            }

            rule.comboResetDelay = Mathf.Max(0.05f, rule.comboResetDelay);
        }
    }

    private void AdvancePlayerHitReactionSequence(string attackId)
    {
        PlayerHitReactionSequenceRule rule = FindPlayerHitReactionSequenceRule(attackId);
        if (rule == null)
        {
            return;
        }

        float elapsed = Time.time - rule.LastHitStepTime;
        if (rule.CurrentHitStep > 0 && elapsed > rule.comboResetDelay)
        {
            ResetPlayerHitReactionSequence(rule, $"WindowTimeout:{attackId}, elapsed={elapsed:F3}, delay={rule.comboResetDelay:F3}");
        }

        rule.CurrentHitStep++;
        rule.LastHitStepTime = Time.time;
        LogHitDebug($"Player hit reaction sequence advanced. attackId={attackId}, hitStep={rule.CurrentHitStep}, elapsed={elapsed:F3}, resetDelay={rule.comboResetDelay:F3}");
    }

    private EnemyHitReactionType ResolvePlayerHitReactionType(string attackId)
    {
        PlayerHitReactionSequenceRule sequenceRule = FindPlayerHitReactionSequenceRule(attackId);
        if (sequenceRule != null && TryResolveSequenceHitReaction(sequenceRule, out EnemyHitReactionType sequenceReaction))
        {
            LogHitDebug($"Player hit reaction resolved by sequence. attackId={attackId}, hitStep={sequenceRule.CurrentHitStep}, reaction={sequenceReaction}");
            return sequenceReaction;
        }

        if (!useWarningTypeForPlayerHitReaction)
        {
            return defaultPlayerHitReaction;
        }

        float now = Time.time;
        bool parryWarningRecentlyOpened = parryWindow != null &&
            now - parryWindow.LastWindowOpenTime <= warningTypeMemoryDuration;
        bool dodgeWarningRecentlyOpened = attackWarningWindow != null &&
            now - attackWarningWindow.LastWindowOpenTime <= warningTypeMemoryDuration;

        if (parryWarningRecentlyOpened &&
            (!dodgeWarningRecentlyOpened ||
                parryWindow.LastWindowOpenTime >= attackWarningWindow.LastWindowOpenTime - 0.05f))
        {
            return EnemyHitReactionType.Light;
        }

        if (dodgeWarningRecentlyOpened)
        {
            return EnemyHitReactionType.Heavy;
        }

        return defaultPlayerHitReaction;
    }

    private bool TryResolveSequenceHitReaction(PlayerHitReactionSequenceRule rule, out EnemyHitReactionType reaction)
    {
        reaction = defaultPlayerHitReaction;
        if (rule == null || rule.hitReactionSequence == null || rule.hitReactionSequence.Length == 0)
        {
            return false;
        }

        int sequenceIndex = Mathf.Clamp(rule.CurrentHitStep - 1, 0, rule.hitReactionSequence.Length - 1);
        reaction = rule.hitReactionSequence[sequenceIndex];
        return true;
    }

    private PlayerHitReactionSequenceRule FindPlayerHitReactionSequenceRule(string attackId)
    {
        if (string.IsNullOrEmpty(attackId) || playerHitReactionSequenceRules == null)
        {
            return null;
        }

        for (int i = 0; i < playerHitReactionSequenceRules.Length; i++)
        {
            PlayerHitReactionSequenceRule rule = playerHitReactionSequenceRules[i];
            if (rule == null || string.IsNullOrEmpty(rule.attackId))
            {
                continue;
            }

            if (rule.matchByContains)
            {
                if (attackId.IndexOf(rule.attackId, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return rule;
                }
            }
            else if (string.Equals(attackId, rule.attackId, StringComparison.OrdinalIgnoreCase))
            {
                return rule;
            }
        }

        return null;
    }

    private void ResetPlayerHitReactionSequence(PlayerHitReactionSequenceRule rule, string reason)
    {
        if (rule == null)
        {
            return;
        }

        rule.CurrentHitStep = 0;
        rule.LastHitStepTime = -999f;
        LogHitDebug($"Player hit reaction sequence reset. attackId={rule.attackId}, reason={reason}");
    }

    private void LogRejectedHit(Collider hitCollider, string reason)
    {
        if (!logHitDebug || !logRejectedHits)
        {
            return;
        }

        Debug.Log(
            $"[EnemyAttackHitbox] Hit rejected. attackId={_activeAttackId}, target={hitCollider.name}, reason={reason}, time={Time.time:F3}",
            this);
    }

    private void LogHitDebug(string message)
    {
        if (!logHitDebug)
        {
            return;
        }

        Debug.Log($"[EnemyAttackHitbox] {message} time={Time.time:F3}", this);
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawHitboxGizmos)
        {
            return;
        }

        if (_hitWindowOpen && _activeDefinition != null)
        {
            DrawDefinitionGizmo(_activeDefinition, activeGizmoColor);
            return;
        }

        if (attackHitDefinitions == null || attackHitDefinitions.Length == 0)
        {
            DrawDefinitionGizmo(defaultHitDefinition, inactiveGizmoColor);
            return;
        }

        for (int i = 0; i < attackHitDefinitions.Length; i++)
        {
            DrawDefinitionGizmo(attackHitDefinitions[i], inactiveGizmoColor);
        }
    }

    private void DrawDefinitionGizmo(AttackHitDefinition definition, Color color)
    {
        if (definition == null || !definition.showGizmo)
        {
            return;
        }

        Transform origin = GetOrigin(definition);
        Vector3 center = GetWorldCenter(origin, definition);
        Quaternion rotation = GetWorldRotation(origin, definition);

        Color previousColor = Gizmos.color;
        Matrix4x4 previousMatrix = Gizmos.matrix;
        Gizmos.color = color;

        if (definition.shape == HitboxShape.Sphere)
        {
            Gizmos.DrawSphere(center, definition.sphereRadius);
            Gizmos.color = new Color(color.r, color.g, color.b, 1f);
            Gizmos.DrawWireSphere(center, definition.sphereRadius);
        }
        else
        {
            Gizmos.matrix = Matrix4x4.TRS(center, rotation, Vector3.one);
            Gizmos.DrawCube(Vector3.zero, definition.boxHalfExtents * 2f);
            Gizmos.color = new Color(color.r, color.g, color.b, 1f);
            Gizmos.DrawWireCube(Vector3.zero, definition.boxHalfExtents * 2f);
        }

        Gizmos.matrix = previousMatrix;
        Gizmos.color = previousColor;
    }
}

public enum EnemyHitReactionType
{
    Light = 0,
    Heavy = 1
}

public class EnemyAttackHitData
{
    public EnemyAttackHitData(
        GameObject attacker,
        Collider hitCollider,
        string attackId,
        float damage,
        float poiseDamage,
        float knockbackForce,
        Vector3 point,
        Vector3 direction,
        EnemyHitReactionType hitReactionType = EnemyHitReactionType.Heavy)
    {
        Attacker = attacker;
        HitCollider = hitCollider;
        AttackId = attackId;
        Damage = damage;
        PoiseDamage = poiseDamage;
        KnockbackForce = knockbackForce;
        Point = point;
        Direction = direction;
        HitReactionType = hitReactionType;
    }

    public GameObject Attacker { get; }
    public Collider HitCollider { get; }
    public string AttackId { get; }
    public float Damage { get; }
    public float PoiseDamage { get; }
    public float KnockbackForce { get; }
    public Vector3 Point { get; }
    public Vector3 Direction { get; }
    public EnemyHitReactionType HitReactionType { get; }
}

public interface IEnemyAttackReceiver
{
    void ReceiveEnemyAttackHit(EnemyAttackHitData hitData);
}
