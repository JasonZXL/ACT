using UnityEngine;

public class EnemyHitReactionController : MonoBehaviour
{
    public event System.Action<EnemyHitReactionController, HitReactionType, bool> HitReactionStarted;

    [System.Serializable]
    public class AttackToughness
    {
        public int attackIndex = 1;
        public float toughness = 12f;
    }

    public enum HitReactionType
    {
        None = 0,
        LowFront = 1,
        LowBack = 2,
        HighFront = 3,
        HighBack = 4
    }

    public enum PlayerHitFeedbackType
    {
        None = 0,
        HitStay = 1,
        HitLow = 2,
        HitHigh = 3
    }

    [System.Serializable]
    public class AttackFeedbackRule
    {
        public string attackId = "";
        public PlayerHitFeedbackType feedback = PlayerHitFeedbackType.HitStay;
        public bool matchByContains = true;
    }

    [Header("References")]
    [SerializeField] private Animator animator;
    [SerializeField] private StrikeJaegerAIController aiController;
    [SerializeField] private CombatHealth combatHealth;
    [SerializeField] private EnemyHealth enemyHealth;
    [SerializeField] private WitchTimeController witchTimeController;
    [SerializeField] private EnemyStaggerController staggerController;

    [Header("Toughness")]
    [SerializeField] private float defaultActionToughness = 12f;
    [SerializeField] private float idleToughness = 10f;
    [SerializeField] private float nonAttackLowReactionPoiseThreshold = 8f;
    [SerializeField] private float nonAttackHighReactionPoiseThreshold = 25f;
    [SerializeField, Range(0.001f, 1f)] private float witchTimeToughnessMultiplier = 0.01f;
    [SerializeField] private AttackToughness[] attackToughness =
    {
        new AttackToughness { attackIndex = 1, toughness = 10f },
        new AttackToughness { attackIndex = 2, toughness = 16f },
        new AttackToughness { attackIndex = 3, toughness = 12f },
        new AttackToughness { attackIndex = 4, toughness = 14f },
        new AttackToughness { attackIndex = 5, toughness = 22f },
        new AttackToughness { attackIndex = 6, toughness = 28f }
    };

    [Header("Player Hit Feedback Rules")]
    [SerializeField] private bool useAttackFeedbackRules = true;
    [SerializeField] private PlayerHitFeedbackType defaultFeedback = PlayerHitFeedbackType.HitStay;
    [SerializeField] private AttackFeedbackRule[] attackFeedbackRules =
    {
        new AttackFeedbackRule { attackId = "Dodge_CounterAttack", feedback = PlayerHitFeedbackType.HitLow },
        new AttackFeedbackRule { attackId = "Parry_CounterAttack", feedback = PlayerHitFeedbackType.HitLow },
        new AttackFeedbackRule { attackId = "Attack03_02", feedback = PlayerHitFeedbackType.HitHigh },
        new AttackFeedbackRule { attackId = "Attack03_03", feedback = PlayerHitFeedbackType.HitHigh }
    };

    [Header("Forced High Reactions")]
    [SerializeField] private string[] forcedHighReactionAttackIds =
    {
        "Attack03_02",
        "Attack03_03",
        "Kiana_Attack03_02",
        "Kiana_Attack03_03"
    };
    [SerializeField] private bool matchForcedHighAttackIdByContains = true;

    [Header("Reaction Interrupts")]
    [SerializeField] private bool allowReactionInterrupts = true;
    [SerializeField] private float minimumPoiseToInterruptCurrentReaction = 8f;
    [SerializeField] private float reactionArmorDuration = 0.35f;
    [SerializeField] private bool forcedHighBypassesReactionArmor = true;
    [SerializeField] private bool witchTimeBypassesReactionArmor = true;

    [Header("Timing")]
    [SerializeField] private float hitReactionFallbackDuration = 1.2f;
    [SerializeField] private float idleLowHitReactionFallbackDuration = 0.35f;
    [SerializeField] private float readyDelayAfterHitReaction = 0.15f;

    [Header("Animator Parameters")]
    [SerializeField] private string hitReactionTriggerName = "HitReaction";
    [SerializeField] private string hitReactionTypeParameterName = "HitReactionType";
    [SerializeField] private string hitReactingBoolName = "HitReacting";

    [Header("Hit Stay Feedback")]
    [SerializeField] private string hitStayTriggerName = "HitStay";
    [SerializeField] private bool triggerHitStayOnUnbrokenHits = true;
    [SerializeField] private bool triggerHitStayWhileStaggered = true;
    [SerializeField] private bool triggerHitStayWhileHitReacting;
    [SerializeField] private float hitStayTriggerCooldown = 0.08f;

    [Header("Debug")]
    [SerializeField] private bool logDebug;

    private float _reactionFallbackTimer;
    private float _reactionArmorTimer;
    private float _hitStayCooldownTimer;
    private bool _hitReacting;
    private bool _currentReactionFromIdle;
    private HitReactionType _currentReactionType = HitReactionType.None;

    public bool IsHitReacting => _hitReacting;
    public HitReactionType CurrentReactionType => _currentReactionType;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnValidate()
    {
        defaultActionToughness = Mathf.Max(0f, defaultActionToughness);
        idleToughness = Mathf.Max(0f, idleToughness);
        nonAttackLowReactionPoiseThreshold = Mathf.Max(0f, nonAttackLowReactionPoiseThreshold);
        nonAttackHighReactionPoiseThreshold = Mathf.Max(nonAttackLowReactionPoiseThreshold, nonAttackHighReactionPoiseThreshold);
        witchTimeToughnessMultiplier = Mathf.Clamp(witchTimeToughnessMultiplier, 0.001f, 1f);
        minimumPoiseToInterruptCurrentReaction = Mathf.Max(0f, minimumPoiseToInterruptCurrentReaction);
        reactionArmorDuration = Mathf.Max(0f, reactionArmorDuration);
        hitReactionFallbackDuration = Mathf.Max(0.1f, hitReactionFallbackDuration);
        idleLowHitReactionFallbackDuration = Mathf.Max(0.05f, idleLowHitReactionFallbackDuration);
        readyDelayAfterHitReaction = Mathf.Max(0f, readyDelayAfterHitReaction);
        hitStayTriggerCooldown = Mathf.Max(0f, hitStayTriggerCooldown);

        if (attackToughness == null)
        {
            return;
        }

        for (int i = 0; i < attackToughness.Length; i++)
        {
            if (attackToughness[i] == null)
            {
                continue;
            }

            attackToughness[i].attackIndex = Mathf.Max(1, attackToughness[i].attackIndex);
            attackToughness[i].toughness = Mathf.Max(0f, attackToughness[i].toughness);
        }
    }

    private void Update()
    {
        if (_reactionArmorTimer > 0f)
        {
            _reactionArmorTimer = Mathf.Max(0f, _reactionArmorTimer - Time.deltaTime);
        }

        if (_hitStayCooldownTimer > 0f)
        {
            _hitStayCooldownTimer = Mathf.Max(0f, _hitStayCooldownTimer - Time.deltaTime);
        }

        if (!_hitReacting)
        {
            return;
        }

        _reactionFallbackTimer -= Time.deltaTime;
        if (_reactionFallbackTimer <= 0f)
        {
            FinishHitReaction("Fallback");
        }
    }

    public void OnCombatHealthPlayerHitReceived(PlayerAttackHitData hitData)
    {
        ReceivePlayerHitForReaction(hitData);
    }

    public void ReceivePlayerHitForReaction(PlayerAttackHitData hitData)
    {
        if (hitData == null || IsDead())
        {
            return;
        }

        bool forcedHigh = IsForcedHighReactionAttack(hitData.AttackId);
        if (useAttackFeedbackRules)
        {
            HandleAttackFeedbackRule(hitData, forcedHigh);
            return;
        }

        bool witchTimeActive = IsWitchTimeActive();
        bool actingAttack = aiController != null && aiController.IsActing && aiController.CurrentActionIsAttack;
        bool interruptedAttack = actingAttack;
        float effectiveToughness = ResolveCurrentEffectiveToughness(witchTimeActive);
        LogDebug(
            $"Hit received. attackId={hitData.AttackId}, damage={hitData.Damage:F1}, poise={hitData.PoiseDamage:F1}, attacker={(hitData.Attacker != null ? hitData.Attacker.name : "None")}, aiState={(aiController != null ? aiController.State.ToString() : "None")}, action={(aiController != null ? aiController.CurrentActionName : "None")}, attackIndex={(aiController != null ? aiController.CurrentAttackIndex : 0)}, actingAttack={actingAttack}, forcedHigh={forcedHigh}, witchTime={witchTimeActive}, toughnessContext={ResolveCurrentToughnessContext()}, effectiveToughness={effectiveToughness:F1}");

        if (staggerController != null && staggerController.IsStaggered)
        {
            if (triggerHitStayWhileStaggered)
            {
                TryTriggerHitStay(hitData, "Staggered");
            }

            LogDebug($"Hit reaction ignored while staggered. attackId={hitData.AttackId}, poise={hitData.PoiseDamage:F1}, staggerTimer={staggerController.StaggerTimer:F2}");
            return;
        }

        if (_hitReacting && (!allowReactionInterrupts || hitData.PoiseDamage < minimumPoiseToInterruptCurrentReaction))
        {
            if (triggerHitStayWhileHitReacting)
            {
                TryTriggerHitStay(hitData, "HitReacting");
            }

            LogDebug(
                $"Hit reaction ignored while reacting. attackId={hitData.AttackId}, poise={hitData.PoiseDamage:F1}, allowReactionInterrupts={allowReactionInterrupts}, minInterruptPoise={minimumPoiseToInterruptCurrentReaction:F1}, currentReaction={_currentReactionType}");
            return;
        }

        if (_reactionArmorTimer > 0f
            && !(forcedHighBypassesReactionArmor && forcedHigh)
            && !(witchTimeBypassesReactionArmor && witchTimeActive))
        {
            LogDebug(
                $"Hit ignored by reaction armor. attackId={hitData.AttackId}, poise={hitData.PoiseDamage:F1}, armorRemaining={_reactionArmorTimer:F2}, forcedHigh={forcedHigh}, witchTime={witchTimeActive}");
            return;
        }

        HitReactionType reactionType = ResolveReactionType(hitData, forcedHigh, witchTimeActive, out bool fromIdle);
        if (reactionType == HitReactionType.None)
        {
            if (triggerHitStayOnUnbrokenHits)
            {
                TryTriggerHitStay(hitData, "Unbroken");
            }

            LogDebug(
                $"Hit did not break toughness. attackId={hitData.AttackId}, poise={hitData.PoiseDamage:F1}, toughnessContext={ResolveCurrentToughnessContext()}, effectiveToughness={effectiveToughness:F1}, currentAttack={(aiController != null ? aiController.CurrentAttackIndex : 0)}, idleProtected={IsIdleProtected()}, actingAttack={actingAttack}, forcedHigh={forcedHigh}, witchTime={witchTimeActive}");
            return;
        }

        LogDebug(
            $"Hit breaks toughness. attackId={hitData.AttackId}, reaction={reactionType}, poise={hitData.PoiseDamage:F1}, toughnessContext={ResolveCurrentToughnessContext()}, effectiveToughness={effectiveToughness:F1}, fromIdle={fromIdle}, interruptedAttack={interruptedAttack}");
        StartHitReaction(reactionType, hitData, fromIdle, interruptedAttack);
    }

    private void HandleAttackFeedbackRule(PlayerAttackHitData hitData, bool forcedHigh)
    {
        if (staggerController != null && staggerController.IsStaggered)
        {
            if (triggerHitStayWhileStaggered)
            {
                TryTriggerHitStay(hitData, "Staggered");
            }

            LogDebug($"Hit feedback ignored while staggered. attackId={hitData.AttackId}, poise={hitData.PoiseDamage:F1}, staggerTimer={staggerController.StaggerTimer:F2}");
            return;
        }

        PlayerHitFeedbackType feedback = hitData.HasFeedbackOverride && hitData.FeedbackOverride != PlayerHitFeedbackType.None
            ? hitData.FeedbackOverride
            : ResolveAttackFeedback(hitData.AttackId, forcedHigh);
        LogDebug($"Hit feedback resolved. attackId={hitData.AttackId}, feedback={feedback}, override={hitData.HasFeedbackOverride}, poise={hitData.PoiseDamage:F1}");

        switch (feedback)
        {
            case PlayerHitFeedbackType.HitStay:
                TryTriggerHitStay(hitData, "AttackFeedbackRule");
                return;
            case PlayerHitFeedbackType.HitLow:
                StartHitReaction(
                    IsHitFromFront(hitData) ? HitReactionType.LowFront : HitReactionType.LowBack,
                    hitData,
                    false,
                    aiController != null && aiController.IsActing && aiController.CurrentActionIsAttack);
                return;
            case PlayerHitFeedbackType.HitHigh:
                StartHitReaction(
                    IsHitFromFront(hitData) ? HitReactionType.HighFront : HitReactionType.HighBack,
                    hitData,
                    false,
                    aiController != null && aiController.IsActing && aiController.CurrentActionIsAttack);
                return;
        }
    }

    private PlayerHitFeedbackType ResolveAttackFeedback(string attackId, bool forcedHigh)
    {
        if (forcedHigh)
        {
            return PlayerHitFeedbackType.HitHigh;
        }

        if (!string.IsNullOrEmpty(attackId) && attackFeedbackRules != null)
        {
            for (int i = 0; i < attackFeedbackRules.Length; i++)
            {
                AttackFeedbackRule rule = attackFeedbackRules[i];
                if (rule == null || string.IsNullOrEmpty(rule.attackId))
                {
                    continue;
                }

                if (rule.matchByContains)
                {
                    if (attackId.IndexOf(rule.attackId, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return rule.feedback;
                    }
                }
                else if (string.Equals(attackId, rule.attackId, System.StringComparison.OrdinalIgnoreCase))
                {
                    return rule.feedback;
                }
            }
        }

        return defaultFeedback;
    }

    public void ApplyParryBreak(GameObject parrySource)
    {
        if (IsDead())
        {
            return;
        }

        ResolveReferences();

        if (staggerController != null && staggerController.IsStaggered)
        {
            LogDebug("Parry break ignored while staggered.");
            return;
        }

        Vector3 sourcePosition = parrySource != null ? parrySource.transform.position : transform.position + transform.forward;
        Vector3 direction = transform.position - sourcePosition;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.001f)
        {
            direction = -transform.forward;
        }

        PlayerAttackHitData parryHitData = new PlayerAttackHitData(
            parrySource,
            null,
            "ParryBreak",
            0f,
            Mathf.Max(nonAttackLowReactionPoiseThreshold, defaultActionToughness),
            0f,
            transform.position,
            direction.normalized);

        HitReactionType reactionType = IsHitFromFront(parryHitData) ? HitReactionType.LowFront : HitReactionType.LowBack;
        LogDebug($"Parry break forces low hit reaction. reaction={reactionType}, source={(parrySource != null ? parrySource.name : "None")}");
        StartHitReaction(reactionType, parryHitData, false, true);
    }

    public void AE_EnemyHitReactionFinished()
    {
        FinishHitReaction("AE_EnemyHitReactionFinished");
    }

    public void CancelActiveHitReactionForStagger()
    {
        ResolveReferences();

        _hitReacting = false;
        _currentReactionFromIdle = false;
        _currentReactionType = HitReactionType.None;
        _reactionFallbackTimer = 0f;
        _reactionArmorTimer = 0f;
        SetAnimatorBool(hitReactingBoolName, false);
        ResetAnimatorTrigger(hitReactionTriggerName);
        LogDebug("Hit reaction cancelled for stagger.");
    }

    private void StartHitReaction(HitReactionType reactionType, PlayerAttackHitData hitData, bool fromIdle, bool interruptedAttack)
    {
        ResolveReferences();

        ResetAnimatorTrigger(hitStayTriggerName);
        _currentReactionType = reactionType;
        _currentReactionFromIdle = fromIdle;
        _hitReacting = true;
        _reactionFallbackTimer = ResolveReactionFallbackDuration(reactionType, fromIdle);

        aiController?.BeginExternalRecovery(_reactionFallbackTimer);
        SetAnimatorInteger(hitReactionTypeParameterName, (int)reactionType);
        SetAnimatorBool(hitReactingBoolName, true);
        SetAnimatorTrigger(hitReactionTriggerName);

        LogDebug(
            $"Hit reaction started. type={reactionType}, fromIdle={fromIdle}, interruptedAttack={interruptedAttack}, fallback={_reactionFallbackTimer:F2}, attackId={hitData.AttackId}, poise={hitData.PoiseDamage:F1}, attacker={(hitData.Attacker != null ? hitData.Attacker.name : "None")}");
        HitReactionStarted?.Invoke(this, reactionType, interruptedAttack);
    }

    private void FinishHitReaction(string reason)
    {
        if (!_hitReacting)
        {
            return;
        }

        _hitReacting = false;
        _currentReactionFromIdle = false;
        _reactionFallbackTimer = 0f;
        _reactionArmorTimer = reactionArmorDuration;
        SetAnimatorBool(hitReactingBoolName, false);
        aiController?.EndExternalRecovery(readyDelayAfterHitReaction);
        LogDebug($"Hit reaction finished. reason={reason}, type={_currentReactionType}, armor={_reactionArmorTimer:F2}");
    }

    private HitReactionType ResolveReactionType(
        PlayerAttackHitData hitData,
        bool forcedHigh,
        bool witchTimeActive,
        out bool fromIdle)
    {
        fromIdle = false;
        bool front = IsHitFromFront(hitData);
        if (forcedHigh)
        {
            return front ? HitReactionType.HighFront : HitReactionType.HighBack;
        }

        bool protectedByAttackToughness = aiController != null && aiController.IsActing && aiController.CurrentActionIsAttack;
        if (protectedByAttackToughness)
        {
            return CanBreakCurrentAttack(hitData, witchTimeActive)
                ? front ? HitReactionType.LowFront : HitReactionType.LowBack
                : HitReactionType.None;
        }

        if (IsIdleProtected())
        {
            fromIdle = true;
            float toughness = witchTimeActive ? idleToughness * witchTimeToughnessMultiplier : idleToughness;
            return hitData.PoiseDamage >= toughness
                ? front ? HitReactionType.LowFront : HitReactionType.LowBack
                : HitReactionType.None;
        }

        if (hitData.PoiseDamage >= nonAttackHighReactionPoiseThreshold)
        {
            return front ? HitReactionType.HighFront : HitReactionType.HighBack;
        }

        if (hitData.PoiseDamage >= nonAttackLowReactionPoiseThreshold)
        {
            return front ? HitReactionType.LowFront : HitReactionType.LowBack;
        }

        return HitReactionType.None;
    }

    private bool CanBreakCurrentAttack(PlayerAttackHitData hitData, bool witchTimeActive)
    {
        if (witchTimeActive && (hitData.Damage > 0f || hitData.PoiseDamage > 0f))
        {
            return true;
        }

        float toughness = GetCurrentActionToughness();
        if (witchTimeActive)
        {
            toughness *= witchTimeToughnessMultiplier;
        }

        return hitData.PoiseDamage >= toughness;
    }

    private float ResolveCurrentEffectiveToughness(bool witchTimeActive)
    {
        float toughness;
        if (aiController != null && aiController.IsActing && aiController.CurrentActionIsAttack)
        {
            toughness = GetCurrentActionToughness();
        }
        else if (IsIdleProtected())
        {
            toughness = idleToughness;
        }
        else
        {
            toughness = nonAttackLowReactionPoiseThreshold;
        }

        return witchTimeActive ? toughness * witchTimeToughnessMultiplier : toughness;
    }

    private string ResolveCurrentToughnessContext()
    {
        if (aiController != null && aiController.IsActing && aiController.CurrentActionIsAttack)
        {
            return $"Attack{aiController.CurrentAttackIndex}";
        }

        return IsIdleProtected() ? "Idle" : "NonAttack";
    }

    private bool IsIdleProtected()
    {
        return aiController == null || aiController.State == StrikeJaegerAIController.AIActionState.Ready;
    }

    private float ResolveReactionFallbackDuration(HitReactionType reactionType, bool fromIdle)
    {
        if (fromIdle && (reactionType == HitReactionType.LowFront || reactionType == HitReactionType.LowBack))
        {
            return idleLowHitReactionFallbackDuration;
        }

        return hitReactionFallbackDuration;
    }

    private float GetCurrentActionToughness()
    {
        int attackIndex = aiController != null ? aiController.CurrentAttackIndex : 0;
        if (attackIndex <= 0 || attackToughness == null)
        {
            return defaultActionToughness;
        }

        for (int i = 0; i < attackToughness.Length; i++)
        {
            AttackToughness entry = attackToughness[i];
            if (entry != null && entry.attackIndex == attackIndex)
            {
                return entry.toughness;
            }
        }

        return defaultActionToughness;
    }

    private bool IsForcedHighReactionAttack(string attackId)
    {
        if (string.IsNullOrEmpty(attackId) || forcedHighReactionAttackIds == null)
        {
            return false;
        }

        for (int i = 0; i < forcedHighReactionAttackIds.Length; i++)
        {
            string forcedId = forcedHighReactionAttackIds[i];
            if (string.IsNullOrEmpty(forcedId))
            {
                continue;
            }

            if (matchForcedHighAttackIdByContains)
            {
                if (attackId.IndexOf(forcedId, System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            else if (string.Equals(attackId, forcedId, System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void TryTriggerHitStay(PlayerAttackHitData hitData, string reason)
    {
        ResolveReferences();

        if (_hitStayCooldownTimer > 0f)
        {
            return;
        }

        if (animator == null || string.IsNullOrEmpty(hitStayTriggerName))
        {
            return;
        }

        if (!HasAnimatorParameter(hitStayTriggerName, AnimatorControllerParameterType.Trigger))
        {
            LogDebug($"HitStay skipped. Missing trigger={hitStayTriggerName}");
            return;
        }

        _hitStayCooldownTimer = hitStayTriggerCooldown;
        animator.SetTrigger(hitStayTriggerName);
        LogDebug($"HitStay triggered. reason={reason}, attackId={hitData.AttackId}, damage={hitData.Damage:F1}, poise={hitData.PoiseDamage:F1}");
    }

    private bool IsWitchTimeActive()
    {
        return witchTimeController != null && witchTimeController.Active;
    }

    private bool IsHitFromFront(PlayerAttackHitData hitData)
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
            return true;
        }

        return Vector3.Dot(transform.forward, sourceDirection.normalized) >= 0f;
    }

    private bool IsDead()
    {
        if (combatHealth != null)
        {
            return combatHealth.IsDead;
        }

        return enemyHealth != null && enemyHealth.IsDead;
    }

    private void ResolveReferences()
    {
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        if (aiController == null)
        {
            aiController = GetComponent<StrikeJaegerAIController>();
        }

        if (combatHealth == null)
        {
            combatHealth = GetComponent<CombatHealth>();
        }

        if (enemyHealth == null)
        {
            enemyHealth = GetComponent<EnemyHealth>();
        }

        if (witchTimeController == null)
        {
            witchTimeController = GetComponent<WitchTimeController>();
        }

        if (staggerController == null)
        {
            staggerController = GetComponent<EnemyStaggerController>();
        }
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

    private void SetAnimatorInteger(string parameterName, int value)
    {
        if (animator == null || string.IsNullOrEmpty(parameterName))
        {
            return;
        }

        if (!HasAnimatorParameter(parameterName, AnimatorControllerParameterType.Int))
        {
            LogDebug($"Animator int skipped. Missing parameter={parameterName}");
            return;
        }

        animator.SetInteger(parameterName, value);
    }

    private void SetAnimatorBool(string parameterName, bool value)
    {
        if (animator == null || string.IsNullOrEmpty(parameterName))
        {
            return;
        }

        if (!HasAnimatorParameter(parameterName, AnimatorControllerParameterType.Bool))
        {
            LogDebug($"Animator bool skipped. Missing parameter={parameterName}");
            return;
        }

        animator.SetBool(parameterName, value);
    }

    private bool HasAnimatorParameter(string parameterName, AnimatorControllerParameterType parameterType)
    {
        if (animator == null)
        {
            return false;
        }

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

        Debug.Log($"[EnemyHitReaction] {message} time={Time.time:F3}", this);
    }
}
