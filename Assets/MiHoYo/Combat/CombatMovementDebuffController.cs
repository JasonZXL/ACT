using System;
using UnityEngine;

public class CombatMovementDebuffController : MonoBehaviour
{
    [Serializable]
    public class MoveSlowRule
    {
        public string attackId = "Enemy_Ultimate_Dagger";
        [Range(0f, 1f)] public float speedMultiplier = 0.55f;
        [Min(0f)] public float duration = 1.5f;
    }

    [Header("References")]
    [SerializeField] private CombatHealth combatHealth;

    [Header("Move Slow Rules")]
    [SerializeField] private MoveSlowRule[] moveSlowRules =
    {
        new MoveSlowRule { attackId = "Enemy_Ultimate_Dagger_Back", speedMultiplier = 0.55f, duration = 1.5f },
        new MoveSlowRule { attackId = "Enemy_Ultimate_Dagger_Right", speedMultiplier = 0.55f, duration = 1.5f },
        new MoveSlowRule { attackId = "Enemy_Ultimate_Dagger_Left", speedMultiplier = 0.55f, duration = 1.5f }
    };

    [Header("Debug")]
    [SerializeField] private bool logDebug;

    private float _moveSpeedMultiplier = 1f;
    private float _moveSlowTimer;
    private string _activeSlowAttackId;

    public float MoveSpeedMultiplier => _moveSlowTimer > 0f ? _moveSpeedMultiplier : 1f;
    public bool HasMoveSlow => _moveSlowTimer > 0f;
    public float MoveSlowTimeRemaining => Mathf.Max(0f, _moveSlowTimer);
    public string ActiveSlowAttackId => _activeSlowAttackId;

    private void Awake()
    {
        if (combatHealth == null)
        {
            combatHealth = GetComponent<CombatHealth>();
        }
    }

    private void OnEnable()
    {
        CombatHealth.EnemyDamageApplied += OnEnemyDamageApplied;
    }

    private void OnDisable()
    {
        CombatHealth.EnemyDamageApplied -= OnEnemyDamageApplied;
    }

    private void OnValidate()
    {
        if (moveSlowRules == null)
        {
            return;
        }

        for (int i = 0; i < moveSlowRules.Length; i++)
        {
            MoveSlowRule rule = moveSlowRules[i];
            if (rule == null)
            {
                continue;
            }

            rule.speedMultiplier = Mathf.Clamp01(rule.speedMultiplier);
            rule.duration = Mathf.Max(0f, rule.duration);
        }
    }

    private void Update()
    {
        if (_moveSlowTimer <= 0f)
        {
            return;
        }

        _moveSlowTimer -= Time.deltaTime;
        if (_moveSlowTimer > 0f)
        {
            return;
        }

        ClearMoveSlow("TimerExpired");
    }

    public void ApplyMoveSlow(float speedMultiplier, float duration, string sourceAttackId = "")
    {
        speedMultiplier = Mathf.Clamp01(speedMultiplier);
        duration = Mathf.Max(0f, duration);
        if (duration <= 0f)
        {
            return;
        }

        if (_moveSlowTimer > 0f)
        {
            _moveSpeedMultiplier = Mathf.Min(_moveSpeedMultiplier, speedMultiplier);
            _moveSlowTimer = Mathf.Max(_moveSlowTimer, duration);
        }
        else
        {
            _moveSpeedMultiplier = speedMultiplier;
            _moveSlowTimer = duration;
        }

        _activeSlowAttackId = sourceAttackId;
        LogDebug($"Move slow applied. attackId={sourceAttackId}, multiplier={_moveSpeedMultiplier:F2}, duration={_moveSlowTimer:F2}");
    }

    public void ClearMoveSlow(string reason = "Manual")
    {
        if (_moveSlowTimer <= 0f && Mathf.Approximately(_moveSpeedMultiplier, 1f))
        {
            return;
        }

        LogDebug($"Move slow cleared. reason={reason}, previousAttackId={_activeSlowAttackId}");
        _moveSpeedMultiplier = 1f;
        _moveSlowTimer = 0f;
        _activeSlowAttackId = string.Empty;
    }

    private void OnEnemyDamageApplied(CombatHealth targetHealth, EnemyAttackHitData hitData, float appliedHealthDamage)
    {
        if (targetHealth == null || hitData == null || appliedHealthDamage <= 0f)
        {
            return;
        }

        if (combatHealth != null && targetHealth != combatHealth)
        {
            return;
        }

        MoveSlowRule rule = FindMoveSlowRule(hitData.AttackId);
        if (rule == null)
        {
            return;
        }

        ApplyMoveSlow(rule.speedMultiplier, rule.duration, hitData.AttackId);
    }

    private MoveSlowRule FindMoveSlowRule(string attackId)
    {
        if (string.IsNullOrEmpty(attackId) || moveSlowRules == null)
        {
            return null;
        }

        for (int i = 0; i < moveSlowRules.Length; i++)
        {
            MoveSlowRule rule = moveSlowRules[i];
            if (rule != null && string.Equals(rule.attackId, attackId, StringComparison.Ordinal))
            {
                return rule;
            }
        }

        return null;
    }

    private void LogDebug(string message)
    {
        if (!logDebug)
        {
            return;
        }

        Debug.Log($"[CombatMovementDebuff] {message} time={Time.time:F3}", this);
    }
}
