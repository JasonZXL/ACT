using System;
using System.Collections.Generic;
using UnityEngine;

public class PlayerAttackHitboxController : MonoBehaviour
{
    public static event Action<PlayerAttackHitData> PlayerAttackHitConfirmed;

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

    [System.Serializable]
    public class AttackHitDefinition
    {
        public string attackId = "Light_Attack_01";
        public bool showGizmo = true;
        public HitboxShape shape = HitboxShape.Box;
        public Transform origin;
        public HitboxSpaceMode spaceMode = HitboxSpaceMode.FollowOrigin;
        public Vector3 centerOffset = new Vector3(0f, 1f, 1f);
        public Vector3 boxHalfExtents = new Vector3(0.6f, 0.6f, 0.8f);
        public float sphereRadius = 0.8f;
        public Vector3 rotationOffset;
        public float damage = 10f;
        public float poiseDamage;
        public float knockbackForce;
        [Tooltip("Keep this off for normal weapon attacks. Multi-hit attack animations should use multiple AE_AttackHitStart/AE_AttackHitEnd windows instead.")]
        public bool allowRepeatedHits;
        [Tooltip("Only used when Allow Repeated Hits is enabled. Controls how often the same target can be hit inside one open hit window.")]
        public float repeatedHitInterval = 0.25f;
    }

    [System.Serializable]
    public class EnemyHitFeedbackSequenceRule
    {
        public string attackId = "Kiana_Attack_QTE";
        public bool matchByContains = true;
        public float comboResetDelay = 1.2f;
        public EnemyHitReactionController.PlayerHitFeedbackType[] hitFeedbackSequence =
        {
            EnemyHitReactionController.PlayerHitFeedbackType.HitStay,
            EnemyHitReactionController.PlayerHitFeedbackType.HitStay,
            EnemyHitReactionController.PlayerHitFeedbackType.HitStay,
            EnemyHitReactionController.PlayerHitFeedbackType.HitHigh
        };

        [NonSerialized] public int CurrentHitStep;
        [NonSerialized] public float LastHitStepTime = -999f;
    }

    [System.Serializable]
    public class AttackDamageStep
    {
        public float damage = 10f;
        public float poiseDamage;
    }

    [System.Serializable]
    public class AttackDamageSequenceRule
    {
        public string attackId = "Kiana_Attack_QTE";
        public bool matchByContains = true;
        public float comboResetDelay = 1.2f;
        public AttackDamageStep[] hitSequence =
        {
            new AttackDamageStep(),
            new AttackDamageStep(),
            new AttackDamageStep(),
            new AttackDamageStep()
        };

        [NonSerialized] public int CurrentHitStep;
        [NonSerialized] public float LastHitStepTime = -999f;
    }

    [Header("Hit Definitions")]
    [SerializeField] private AttackHitDefinition defaultHitDefinition = new AttackHitDefinition();
    [SerializeField] private AttackHitDefinition[] attackHitDefinitions;

    [Header("Enemy Hit Feedback Sequence")]
    [SerializeField] private EnemyHitFeedbackSequenceRule[] enemyHitFeedbackSequenceRules =
    {
        new EnemyHitFeedbackSequenceRule()
    };

    [Header("Attack Damage Sequence")]
    [SerializeField] private AttackDamageSequenceRule[] attackDamageSequenceRules =
    {
        new AttackDamageSequenceRule()
    };

    [Header("Detection")]
    [SerializeField] private LayerMask targetLayers = ~0;
    [SerializeField] private QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Collide;
    [SerializeField] private int maxHits = 16;

    [Header("Debug")]
    [SerializeField] private bool logHitDebug;
    [SerializeField] private bool logRejectedHits;
    [SerializeField] private bool drawHitboxGizmos = true;
    [SerializeField] private Color inactiveGizmoColor = new Color(1f, 0.85f, 0.1f, 0.25f);
    [SerializeField] private Color activeGizmoColor = new Color(1f, 0.1f, 0.05f, 0.35f);

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
        EnsureHitBuffer();
    }

    private void OnDisable()
    {
        // 确保组件被禁用时（玩家死亡、场景切换等）不遗留打开的命中窗口，
        // 否则对象重新启用后第一帧会立刻对周围所有目标执行一次幽灵攻击。
        CloseHitWindow();
    }

    private void OnValidate()
    {
        maxHits = Mathf.Max(1, maxHits);
        if (defaultHitDefinition != null)
        {
            defaultHitDefinition.sphereRadius = Mathf.Max(0.01f, defaultHitDefinition.sphereRadius);
            defaultHitDefinition.boxHalfExtents = Vector3.Max(defaultHitDefinition.boxHalfExtents, Vector3.one * 0.01f);
        }

        ValidateEnemyHitFeedbackSequenceRules();
        ValidateAttackDamageSequenceRules();

        if (attackHitDefinitions == null)
        {
            return;
        }

        for (int i = 0; i < attackHitDefinitions.Length; i++)
        {
            AttackHitDefinition definition = attackHitDefinitions[i];
            if (definition == null)
            {
                continue;
            }

            definition.sphereRadius = Mathf.Max(0.01f, definition.sphereRadius);
            definition.boxHalfExtents = Vector3.Max(definition.boxHalfExtents, Vector3.one * 0.01f);
            definition.repeatedHitInterval = Mathf.Max(0.01f, definition.repeatedHitInterval);
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

    public void AE_AttackHitStart(string attackId)
    {
        OpenHitWindow(attackId);
    }

    public void AE_AttackHitEnd()
    {
        CloseHitWindow();
    }

    public void AE_AttackHitOnce(string attackId)
    {
        AttackHitDefinition definition = GetDefinition(attackId);
        if (definition == null)
        {
            LogHitDebug($"Attack hit once ignored: no definition found. attackId={attackId}");
            return;
        }

        _activeAttackId = attackId;
        _activeDefinition = definition;
        CacheLockedOrigin(definition);
        _hitTargets.Clear();
        _nextRepeatedHitTimes.Clear();
        AdvanceEnemyHitFeedbackSequence(attackId);
        AdvanceAttackDamageSequence(attackId);
        EvaluateActiveHitbox();
        LogHitDebug($"Attack hit once evaluated. attackId={attackId}");
    }

    public void AE_ResetEnemyHitFeedbackSequence(string attackId)
    {
        EnemyHitFeedbackSequenceRule rule = FindEnemyHitFeedbackSequenceRule(attackId);
        if (rule == null)
        {
            LogHitDebug($"Enemy hit feedback sequence reset skipped: no rule found. attackId={attackId}");
            return;
        }

        ResetEnemyHitFeedbackSequence(rule, $"AnimationEvent:{attackId}");
    }

    public void AE_ResetAllEnemyHitFeedbackSequences()
    {
        if (enemyHitFeedbackSequenceRules == null)
        {
            return;
        }

        for (int i = 0; i < enemyHitFeedbackSequenceRules.Length; i++)
        {
            ResetEnemyHitFeedbackSequence(enemyHitFeedbackSequenceRules[i], "AnimationEvent:All");
        }
    }

    public void AE_ResetAttackDamageSequence(string attackId)
    {
        AttackDamageSequenceRule rule = FindAttackDamageSequenceRule(attackId);
        if (rule == null)
        {
            LogHitDebug($"Attack damage sequence reset skipped: no rule found. attackId={attackId}");
            return;
        }

        ResetAttackDamageSequence(rule, $"AnimationEvent:{attackId}");
    }

    public void AE_ResetAllAttackDamageSequences()
    {
        if (attackDamageSequenceRules == null)
        {
            return;
        }

        ResetAllAttackDamageSequences("AnimationEvent:All");
    }

    public void AE_QteAttackDamageSequenceStart()
    {
        AE_ResetAttackDamageSequence("Kiana_Attack_QTE");
    }

    public void AE_QteAttackDamageSequenceEnd()
    {
        AE_ResetAttackDamageSequence("Kiana_Attack_QTE");
    }

    public void OpenHitWindow(string attackId)
    {
        AttackHitDefinition definition = GetDefinition(attackId);
        if (definition == null)
        {
            LogHitDebug($"Hit window open ignored: no definition found. attackId={attackId}");
            return;
        }

        _activeAttackId = attackId;
        _activeDefinition = definition;
        _hitWindowOpen = true;
        CacheLockedOrigin(definition);
        _hitTargets.Clear();
        _nextRepeatedHitTimes.Clear();
        AdvanceEnemyHitFeedbackSequence(attackId);
        AdvanceAttackDamageSequence(attackId);
        EvaluateActiveHitbox();
        LogHitDebug($"Hit window opened. attackId={attackId}");
    }

    public void CloseHitWindow()
    {
        if (!_hitWindowOpen)
        {
            return;
        }

        LogHitDebug($"Hit window closed. attackId={_activeAttackId}, hitCount={_hitTargets.Count}");
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

        bool hasFeedbackOverride = ResolveEnemyHitFeedbackOverride(
            _activeAttackId,
            out EnemyHitReactionController.PlayerHitFeedbackType feedbackOverride);
        ResolveAttackDamageValues(
            _activeAttackId,
            _activeDefinition,
            out float damage,
            out float poiseDamage,
            out int attackIndex);

        PlayerAttackHitData hitData = new PlayerAttackHitData(
            gameObject,
            hitCollider,
            _activeAttackId,
            damage,
            poiseDamage,
            _activeDefinition.knockbackForce,
            closestPoint,
            direction.normalized,
            hasFeedbackOverride,
            feedbackOverride,
            attackIndex);

        DeliverHit(hitCollider, hitData);
        PlayerAttackHitConfirmed?.Invoke(hitData);
        LogHitDebug($"Hit registered. attackId={_activeAttackId}, attackIndex={attackIndex}, target={hitCollider.name}, damage={damage:F1}, poise={poiseDamage:F1}, feedbackOverride={(hitData.HasFeedbackOverride ? hitData.FeedbackOverride.ToString() : "None")}");
    }

    private void LogRejectedHit(Collider hitCollider, string reason)
    {
        if (!logHitDebug || !logRejectedHits)
        {
            return;
        }

        Debug.Log(
            $"[PlayerAttackHitbox] Hit rejected. attackId={_activeAttackId}, target={hitCollider.name}, reason={reason}, time={Time.time:F3}",
            this);
    }

    private void DeliverHit(Collider hitCollider, PlayerAttackHitData hitData)
    {
        IPlayerAttackReceiver receiver = FindReceiver(hitCollider);
        if (receiver != null)
        {
            receiver.ReceivePlayerAttackHit(hitData);
            return;
        }

        hitCollider.SendMessageUpwards("ReceivePlayerAttackHit", hitData, SendMessageOptions.DontRequireReceiver);
        hitCollider.SendMessageUpwards("TakePlayerAttackHit", hitData, SendMessageOptions.DontRequireReceiver);
        hitCollider.SendMessageUpwards("TakeDamage", hitData.Damage, SendMessageOptions.DontRequireReceiver);
    }

    private IPlayerAttackReceiver FindReceiver(Collider hitCollider)
    {
        MonoBehaviour[] behaviours = hitCollider.GetComponentsInParent<MonoBehaviour>();
        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] is IPlayerAttackReceiver receiver)
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

    private void ValidateEnemyHitFeedbackSequenceRules()
    {
        if (enemyHitFeedbackSequenceRules == null)
        {
            return;
        }

        for (int i = 0; i < enemyHitFeedbackSequenceRules.Length; i++)
        {
            EnemyHitFeedbackSequenceRule rule = enemyHitFeedbackSequenceRules[i];
            if (rule == null)
            {
                continue;
            }

            rule.comboResetDelay = Mathf.Max(0.05f, rule.comboResetDelay);
        }
    }

    private void ValidateAttackDamageSequenceRules()
    {
        if (attackDamageSequenceRules == null)
        {
            return;
        }

        for (int i = 0; i < attackDamageSequenceRules.Length; i++)
        {
            AttackDamageSequenceRule rule = attackDamageSequenceRules[i];
            if (rule == null)
            {
                continue;
            }

            rule.comboResetDelay = Mathf.Max(0.05f, rule.comboResetDelay);
        }
    }

    private void AdvanceEnemyHitFeedbackSequence(string attackId)
    {
        EnemyHitFeedbackSequenceRule rule = FindEnemyHitFeedbackSequenceRule(attackId);
        if (rule == null)
        {
            return;
        }

        float elapsed = Time.time - rule.LastHitStepTime;
        if (rule.CurrentHitStep > 0 && elapsed > rule.comboResetDelay)
        {
            ResetEnemyHitFeedbackSequence(rule, $"WindowTimeout:{attackId}, elapsed={elapsed:F3}, delay={rule.comboResetDelay:F3}");
        }

        rule.CurrentHitStep++;
        rule.LastHitStepTime = Time.time;
        LogHitDebug($"Enemy hit feedback sequence advanced. attackId={attackId}, hitStep={rule.CurrentHitStep}, elapsed={elapsed:F3}, resetDelay={rule.comboResetDelay:F3}");
    }

    private void AdvanceAttackDamageSequence(string attackId)
    {
        AttackDamageSequenceRule rule = FindAttackDamageSequenceRule(attackId);
        if (rule == null)
        {
            return;
        }

        float elapsed = Time.time - rule.LastHitStepTime;
        if (rule.CurrentHitStep > 0 && elapsed > rule.comboResetDelay)
        {
            ResetAttackDamageSequence(rule, $"WindowTimeout:{attackId}, elapsed={elapsed:F3}, delay={rule.comboResetDelay:F3}");
        }
        else if (rule.hitSequence != null &&
            rule.hitSequence.Length > 0 &&
            rule.CurrentHitStep >= rule.hitSequence.Length)
        {
            ResetAttackDamageSequence(rule, $"SequenceComplete:{attackId}, step={rule.CurrentHitStep}, length={rule.hitSequence.Length}");
        }

        rule.CurrentHitStep++;
        rule.LastHitStepTime = Time.time;
        LogHitDebug($"Attack damage sequence advanced. attackId={attackId}, attackIndex={rule.CurrentHitStep}, elapsed={elapsed:F3}, resetDelay={rule.comboResetDelay:F3}");
    }

    private bool ResolveEnemyHitFeedbackOverride(string attackId, out EnemyHitReactionController.PlayerHitFeedbackType feedback)
    {
        feedback = EnemyHitReactionController.PlayerHitFeedbackType.None;
        EnemyHitFeedbackSequenceRule rule = FindEnemyHitFeedbackSequenceRule(attackId);
        if (rule == null || rule.hitFeedbackSequence == null || rule.hitFeedbackSequence.Length == 0)
        {
            return false;
        }

        int sequenceIndex = Mathf.Clamp(rule.CurrentHitStep - 1, 0, rule.hitFeedbackSequence.Length - 1);
        feedback = rule.hitFeedbackSequence[sequenceIndex];
        return true;
    }

    private void ResolveAttackDamageValues(
        string attackId,
        AttackHitDefinition definition,
        out float damage,
        out float poiseDamage,
        out int attackIndex)
    {
        damage = definition != null ? definition.damage : 0f;
        poiseDamage = definition != null ? definition.poiseDamage : 0f;
        attackIndex = 0;

        AttackDamageSequenceRule rule = FindAttackDamageSequenceRule(attackId);
        if (rule == null || rule.hitSequence == null || rule.hitSequence.Length == 0)
        {
            return;
        }

        attackIndex = Mathf.Max(1, rule.CurrentHitStep);
        int sequenceIndex = Mathf.Clamp(attackIndex - 1, 0, rule.hitSequence.Length - 1);
        AttackDamageStep step = rule.hitSequence[sequenceIndex];
        if (step == null)
        {
            return;
        }

        damage = step.damage;
        poiseDamage = step.poiseDamage;
    }

    private EnemyHitFeedbackSequenceRule FindEnemyHitFeedbackSequenceRule(string attackId)
    {
        if (string.IsNullOrEmpty(attackId) || enemyHitFeedbackSequenceRules == null)
        {
            return null;
        }

        for (int i = 0; i < enemyHitFeedbackSequenceRules.Length; i++)
        {
            EnemyHitFeedbackSequenceRule rule = enemyHitFeedbackSequenceRules[i];
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

    private AttackDamageSequenceRule FindAttackDamageSequenceRule(string attackId)
    {
        if (string.IsNullOrEmpty(attackId) || attackDamageSequenceRules == null)
        {
            return null;
        }

        for (int i = 0; i < attackDamageSequenceRules.Length; i++)
        {
            AttackDamageSequenceRule rule = attackDamageSequenceRules[i];
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

    private void ResetEnemyHitFeedbackSequence(EnemyHitFeedbackSequenceRule rule, string reason)
    {
        if (rule == null)
        {
            return;
        }

        rule.CurrentHitStep = 0;
        rule.LastHitStepTime = -999f;
        LogHitDebug($"Enemy hit feedback sequence reset. attackId={rule.attackId}, reason={reason}");
    }

    private void ResetAttackDamageSequence(AttackDamageSequenceRule rule, string reason)
    {
        if (rule == null)
        {
            return;
        }

        rule.CurrentHitStep = 0;
        rule.LastHitStepTime = -999f;
        LogHitDebug($"Attack damage sequence reset. attackId={rule.attackId}, reason={reason}");
    }

    private void ResetAllAttackDamageSequences(string reason)
    {
        if (attackDamageSequenceRules == null)
        {
            return;
        }

        for (int i = 0; i < attackDamageSequenceRules.Length; i++)
        {
            ResetAttackDamageSequence(attackDamageSequenceRules[i], reason);
        }
    }

    private void LogHitDebug(string message)
    {
        if (!logHitDebug)
        {
            return;
        }

        Debug.Log($"[PlayerAttackHitbox] {message} time={Time.time:F3}", this);
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

public class PlayerAttackHitData
{
    public PlayerAttackHitData(
        GameObject attacker,
        Collider hitCollider,
        string attackId,
        float damage,
        float poiseDamage,
        float knockbackForce,
        Vector3 point,
        Vector3 direction,
        bool hasFeedbackOverride = false,
        EnemyHitReactionController.PlayerHitFeedbackType feedbackOverride = EnemyHitReactionController.PlayerHitFeedbackType.None,
        int attackIndex = 0)
    {
        Attacker = attacker;
        HitCollider = hitCollider;
        AttackId = attackId;
        Damage = damage;
        PoiseDamage = poiseDamage;
        KnockbackForce = knockbackForce;
        Point = point;
        Direction = direction;
        HasFeedbackOverride = hasFeedbackOverride;
        FeedbackOverride = feedbackOverride;
        AttackIndex = attackIndex;
    }

    public GameObject Attacker { get; }
    public Collider HitCollider { get; }
    public string AttackId { get; }
    public float Damage { get; }
    public float PoiseDamage { get; }
    public float KnockbackForce { get; }
    public Vector3 Point { get; }
    public Vector3 Direction { get; }
    public bool HasFeedbackOverride { get; }
    public EnemyHitReactionController.PlayerHitFeedbackType FeedbackOverride { get; }
    public int AttackIndex { get; }
}

public interface IPlayerAttackReceiver
{
    void ReceivePlayerAttackHit(PlayerAttackHitData hitData);
}
