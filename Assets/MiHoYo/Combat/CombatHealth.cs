using System;
using UnityEngine;

public class CombatHealth : MonoBehaviour, IPlayerAttackReceiver, IEnemyAttackReceiver
{
    public static event Action<CombatHealth, EnemyAttackHitData, float> EnemyDamageApplied;
    public static event Action<CombatHealth, EnemyAttackHitData, float> EnemyDamageRolledBack;

    [Header("Health")]
    [SerializeField] private float maxHealth = 300f;
    [SerializeField] private bool destroyOnDeath;
    [SerializeField] private bool waitForDeathAnimationEvent = true;

    [Header("Poise")]
    [SerializeField] private float maxPoise = 100f;
    [SerializeField] private float poiseRecoverDelay = 1.2f;
    [SerializeField] private float poiseRecoverSpeed = 35f;

    [Header("Animator")]
    [SerializeField] private Animator animator;
    [SerializeField] private string hitLightTriggerName = "HitLight";
    [SerializeField] private string hitHeavyTriggerName = "HitHeavy";
    [SerializeField] private string stunTriggerName = "Stun";
    [SerializeField] private string dieTriggerName = "Die";
    [SerializeField] private string deadBoolName = "Dead";

    [Header("Perfect Dodge Rollback")]
    [SerializeField] private bool rollbackRecentEnemyHitOnPerfectDodge = true;
    [SerializeField] private float perfectDodgeRollbackWindow = 0.35f;
    [SerializeField] private bool resetHitReactionTriggersOnRollback = true;

    [Header("Debug")]
    [SerializeField] private bool logDebug;

    private float _currentHealth;
    private float _currentPoise;
    private float _poiseRecoverTimer;
    private bool _isDead;
    private PlayerPoiseController _playerPoiseController;
    private EnemyAttackHitData _lastEnemyHitData;
    private float _lastEnemyHitTime = -999f;
    private float _lastEnemyAppliedHealthDamage;
    private float _lastEnemyAppliedPoiseDamage;
    private bool _lastEnemyHitReactionPlayed;
    private bool _lastEnemyHitRolledBack;

    public event Action<CombatHealth> Died;

    public float CurrentHealth => _currentHealth;
    public float MaxHealth => maxHealth;
    public float HealthNormalized => maxHealth <= 0f ? 0f : Mathf.Clamp01(_currentHealth / maxHealth);
    public float CurrentPoise => _currentPoise;
    public bool IsDead => _isDead;

    private void Awake()
    {
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        _playerPoiseController = GetComponent<PlayerPoiseController>();
        _currentHealth = maxHealth;
        _currentPoise = maxPoise;
    }

    private void OnValidate()
    {
        maxHealth = Mathf.Max(1f, maxHealth);
        maxPoise = Mathf.Max(1f, maxPoise);
        poiseRecoverDelay = Mathf.Max(0f, poiseRecoverDelay);
        poiseRecoverSpeed = Mathf.Max(0f, poiseRecoverSpeed);
        perfectDodgeRollbackWindow = Mathf.Max(0f, perfectDodgeRollbackWindow);
    }

    private void OnEnable()
    {
        EnemyAttackWarningWindow.PerfectDodgeConfirmed += OnPerfectDodgeConfirmed;
    }

    private void OnDisable()
    {
        EnemyAttackWarningWindow.PerfectDodgeConfirmed -= OnPerfectDodgeConfirmed;
    }

    private void Update()
    {
        if (_isDead || _currentPoise >= maxPoise)
        {
            return;
        }

        if (_poiseRecoverTimer > 0f)
        {
            _poiseRecoverTimer -= Time.deltaTime;
            return;
        }

        _currentPoise = Mathf.Min(maxPoise, _currentPoise + poiseRecoverSpeed * Time.deltaTime);
    }

    public void ReceivePlayerAttackHit(PlayerAttackHitData hitData)
    {
        if (hitData == null)
        {
            return;
        }

        bool enemyHitReactionHandlesFeedback = GetComponent<EnemyHitReactionController>() != null;
        ApplyDamage(
            hitData.Damage,
            hitData.PoiseDamage,
            hitData.AttackId,
            hitData.Attacker,
            !enemyHitReactionHandlesFeedback);
        SendMessage("OnCombatHealthPlayerHitReceived", hitData, SendMessageOptions.DontRequireReceiver);
    }

    public void ReceiveEnemyAttackHit(EnemyAttackHitData hitData)
    {
        if (hitData == null)
        {
            return;
        }

        bool playedHitReaction = TakeEnemyDamage(hitData);
        if (!_isDead && playedHitReaction)
        {
            SendMessage("OnCombatHealthEnemyHitReceived", hitData, SendMessageOptions.DontRequireReceiver);
        }
    }

    public void TakeDamage(float damage)
    {
        TakeDamage(damage, 0f, string.Empty, null);
    }

    public void ApplyParryBreak(GameObject parrySource)
    {
        if (_isDead)
        {
            return;
        }

        LogDebug($"Parry break applied. source={(parrySource != null ? parrySource.name : "None")}");

        EnemyHitReactionController hitReactionController = GetComponent<EnemyHitReactionController>();
        if (hitReactionController != null)
        {
            hitReactionController.ApplyParryBreak(parrySource);
            return;
        }

        SetAnimatorTrigger(hitLightTriggerName);
    }

    public void TakeDamage(float damage, float poiseDamage, string attackId, GameObject attacker)
    {
        ApplyDamage(damage, poiseDamage, attackId, attacker, true);
    }

    private void ApplyDamage(float damage, float poiseDamage, string attackId, GameObject attacker, bool triggerHitReaction)
    {
        if (_isDead)
        {
            return;
        }

        _currentHealth = Mathf.Max(0f, _currentHealth - Mathf.Max(0f, damage));
        _currentPoise = Mathf.Max(0f, _currentPoise - Mathf.Max(0f, poiseDamage));
        _poiseRecoverTimer = poiseRecoverDelay;

        LogDebug($"Damage received. attacker={(attacker != null ? attacker.name : "None")}, attackId={attackId}, damage={damage:F1}, poiseDamage={poiseDamage:F1}, health={_currentHealth:F1}/{maxHealth:F1}, poise={_currentPoise:F1}/{maxPoise:F1}");

        if (_currentHealth <= 0f)
        {
            Die();
            return;
        }

        if (_currentPoise <= 0f)
        {
            _currentPoise = maxPoise;
            if (triggerHitReaction)
            {
                SetAnimatorTrigger(stunTriggerName);
            }
            return;
        }

        if (triggerHitReaction)
        {
            SetAnimatorTrigger(poiseDamage > 0f ? hitHeavyTriggerName : hitLightTriggerName);
        }
    }

    private bool TakeEnemyDamage(EnemyAttackHitData hitData)
    {
        if (_isDead)
        {
            return false;
        }

        float damage = hitData.Damage;
        float poiseDamage = hitData.PoiseDamage;
        float healthBeforeDamage = _currentHealth;
        float poiseBeforeDamage = _currentPoise;
        _currentHealth = Mathf.Max(0f, _currentHealth - Mathf.Max(0f, damage));
        bool usePlayerPoise = _playerPoiseController != null;
        bool playHitReaction = true;
        if (usePlayerPoise)
        {
            playHitReaction = _playerPoiseController.ShouldPlayHitReaction(hitData);
        }
        else
        {
            _currentPoise = Mathf.Max(0f, _currentPoise - Mathf.Max(0f, poiseDamage));
            _poiseRecoverTimer = poiseRecoverDelay;
        }

        float appliedHealthDamage = Mathf.Max(0f, healthBeforeDamage - _currentHealth);
        float appliedPoiseDamage = Mathf.Max(0f, poiseBeforeDamage - _currentPoise);
        RememberEnemyHitForRollback(hitData, appliedHealthDamage, appliedPoiseDamage, playHitReaction);

        LogDebug($"Enemy damage received. attacker={(hitData.Attacker != null ? hitData.Attacker.name : "None")}, attackId={hitData.AttackId}, reaction={hitData.HitReactionType}, damage={damage:F1}, poiseDamage={poiseDamage:F1}, health={_currentHealth:F1}/{maxHealth:F1}, poise={_currentPoise:F1}/{maxPoise:F1}, playerPoise={(usePlayerPoise ? _playerPoiseController.CurrentPoise.ToString("F1") : "None")}, playHitReaction={playHitReaction}");

        if (appliedHealthDamage > 0f)
        {
            EnemyDamageApplied?.Invoke(this, hitData, appliedHealthDamage);
            SendMessage(
                "OnCombatHealthEnemyDamageApplied",
                new EnemyDamageAppliedMessage(this, hitData, appliedHealthDamage),
                SendMessageOptions.DontRequireReceiver);
            LogDebug($"Enemy damage confirmed. attackId={hitData.AttackId}, appliedHealthDamage={appliedHealthDamage:F1}");
        }

        if (_currentHealth <= 0f)
        {
            Die();
            return false;
        }

        if (usePlayerPoise)
        {
            if (playHitReaction)
            {
                SetAnimatorTrigger(hitData.HitReactionType == EnemyHitReactionType.Heavy
                    ? hitHeavyTriggerName
                    : hitLightTriggerName);
            }

            return playHitReaction;
        }

        if (_currentPoise <= 0f)
        {
            _currentPoise = maxPoise;
            SetAnimatorTrigger(stunTriggerName);
            return true;
        }

        SetAnimatorTrigger(hitData.HitReactionType == EnemyHitReactionType.Heavy
            ? hitHeavyTriggerName
            : hitLightTriggerName);
        return true;
    }

    private void RememberEnemyHitForRollback(
        EnemyAttackHitData hitData,
        float appliedHealthDamage,
        float appliedPoiseDamage,
        bool playedHitReaction)
    {
        _lastEnemyHitData = hitData;
        _lastEnemyHitTime = Time.time;
        _lastEnemyAppliedHealthDamage = appliedHealthDamage;
        _lastEnemyAppliedPoiseDamage = appliedPoiseDamage;
        _lastEnemyHitReactionPlayed = playedHitReaction;
        _lastEnemyHitRolledBack = false;
    }

    private void OnPerfectDodgeConfirmed(EnemyAttackWarningWindow warningWindow, KianaCombatController dodger)
    {
        if (!rollbackRecentEnemyHitOnPerfectDodge ||
            warningWindow == null ||
            dodger == null ||
            _lastEnemyHitData == null ||
            _lastEnemyHitRolledBack ||
            _isDead)
        {
            return;
        }

        if (!IsOwnDodger(dodger) || !IsRecentEnemyHitFromWarning(warningWindow))
        {
            return;
        }

        RollbackLastEnemyHit("PerfectDodgeConfirmed", warningWindow);
    }

    private bool IsOwnDodger(KianaCombatController dodger)
    {
        Transform dodgerTransform = dodger.transform;
        return dodger.gameObject == gameObject ||
            dodgerTransform.IsChildOf(transform) ||
            transform.IsChildOf(dodgerTransform);
    }

    private bool IsRecentEnemyHitFromWarning(EnemyAttackWarningWindow warningWindow)
    {
        if (Time.time - _lastEnemyHitTime > perfectDodgeRollbackWindow)
        {
            return false;
        }

        GameObject attacker = _lastEnemyHitData.Attacker;
        if (attacker == null)
        {
            return false;
        }

        Transform attackerTransform = attacker.transform;
        Transform warningTransform = warningWindow.transform;
        return attackerTransform == warningTransform ||
            attackerTransform.IsChildOf(warningTransform) ||
            warningTransform.IsChildOf(attackerTransform);
    }

    private void RollbackLastEnemyHit(string reason, EnemyAttackWarningWindow warningWindow)
    {
        _lastEnemyHitRolledBack = true;

        if (_lastEnemyAppliedHealthDamage > 0f)
        {
            _currentHealth = Mathf.Min(maxHealth, _currentHealth + _lastEnemyAppliedHealthDamage);
        }

        if (_playerPoiseController == null && _lastEnemyAppliedPoiseDamage > 0f)
        {
            _currentPoise = Mathf.Min(maxPoise, _currentPoise + _lastEnemyAppliedPoiseDamage);
        }

        if (resetHitReactionTriggersOnRollback)
        {
            ResetAnimatorTrigger(hitLightTriggerName);
            ResetAnimatorTrigger(hitHeavyTriggerName);
            ResetAnimatorTrigger(stunTriggerName);
        }

        SendMessage("OnCombatHealthEnemyHitRolledBack", _lastEnemyHitData, SendMessageOptions.DontRequireReceiver);
        if (_lastEnemyAppliedHealthDamage > 0f)
        {
            EnemyDamageRolledBack?.Invoke(this, _lastEnemyHitData, _lastEnemyAppliedHealthDamage);
        }

        LogDebug(
            $"Enemy hit rolled back. reason={reason}, warning={warningWindow.name}, attackId={_lastEnemyHitData.AttackId}, " +
            $"healthRestored={_lastEnemyAppliedHealthDamage:F1}, poiseRestored={_lastEnemyAppliedPoiseDamage:F1}, " +
            $"playedHitReaction={_lastEnemyHitReactionPlayed}, health={_currentHealth:F1}/{maxHealth:F1}");
    }

    public void ResetHealth()
    {
        _isDead = false;
        _currentHealth = maxHealth;
        _currentPoise = maxPoise;
        _poiseRecoverTimer = 0f;
        _playerPoiseController?.ResetPoise();
        SetAnimatorBool(deadBoolName, false);
    }

    private void Die()
    {
        _isDead = true;
        SetAnimatorBool(deadBoolName, true);
        SetAnimatorTrigger(dieTriggerName);
        LogDebug("Died.");
        Died?.Invoke(this);
        SendMessage("OnCombatHealthDied", this, SendMessageOptions.DontRequireReceiver);

        if (destroyOnDeath && !waitForDeathAnimationEvent)
        {
            Destroy(gameObject);
        }
    }

    public void AE_DestroyAfterDeathAnimation()
    {
        if (!_isDead || !destroyOnDeath)
        {
            return;
        }

        LogDebug("Death animation finished. Destroying owner.");
        Destroy(gameObject);
    }

    private void SetAnimatorTrigger(string triggerName)
    {
        if (animator == null || string.IsNullOrEmpty(triggerName))
        {
            return;
        }

        if (!HasAnimatorParameter(triggerName, AnimatorControllerParameterType.Trigger))
        {
            LogDebug($"Animator trigger skipped. Missing parameter={triggerName}");
            return;
        }

        animator.SetTrigger(triggerName);
    }

    private void ResetAnimatorTrigger(string triggerName)
    {
        if (animator == null || string.IsNullOrEmpty(triggerName))
        {
            return;
        }

        if (!HasAnimatorParameter(triggerName, AnimatorControllerParameterType.Trigger))
        {
            return;
        }

        animator.ResetTrigger(triggerName);
    }

    private void SetAnimatorBool(string boolName, bool value)
    {
        if (animator == null || string.IsNullOrEmpty(boolName))
        {
            return;
        }

        if (!HasAnimatorParameter(boolName, AnimatorControllerParameterType.Bool))
        {
            LogDebug($"Animator bool skipped. Missing parameter={boolName}");
            return;
        }

        animator.SetBool(boolName, value);
    }

    private bool HasAnimatorParameter(string parameterName, AnimatorControllerParameterType parameterType)
    {
        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.type == parameterType && parameter.name == parameterName)
            {
                return true;
            }
        }

        return false;
    }

    private void LogDebug(string message)
    {
        if (!logDebug)
        {
            return;
        }

        Debug.Log($"[CombatHealth] {message} time={Time.time:F3}", this);
    }
}

public sealed class EnemyDamageAppliedMessage
{
    public EnemyDamageAppliedMessage(CombatHealth targetHealth, EnemyAttackHitData hitData, float appliedHealthDamage)
    {
        TargetHealth = targetHealth;
        HitData = hitData;
        AppliedHealthDamage = appliedHealthDamage;
    }

    public CombatHealth TargetHealth { get; }
    public EnemyAttackHitData HitData { get; }
    public float AppliedHealthDamage { get; }
}
