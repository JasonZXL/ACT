using UnityEngine;

public class EnemyStaggerController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private EnemyHitReactionController hitReactionController;
    [SerializeField] private StrikeJaegerAIController aiController;
    [SerializeField] private Animator animator;

    [Header("Stagger Value")]
    [SerializeField] private int requiredStaggerPoints = 4;
    [SerializeField] private float staggerValuePerPoint = 100f;
    [SerializeField] private bool usePlayerAttackPoiseDamage = true;

    [Header("Timing")]
    [SerializeField] private float staggerDuration = 5f;
    [SerializeField] private float readyDelayAfterStagger = 0.25f;

    [Header("Animator Parameters")]
    [SerializeField] private string debuffStartTriggerName = "DebuffStart";
    [SerializeField] private string debuffedBoolName = "Debuffed";
    [SerializeField] private string debuffEndTriggerName = "DebuffEnd";
    [SerializeField] private string hitReactionTriggerToResetOnStagger = "HitReaction";

    [Header("Debug")]
    [SerializeField] private bool logDebug;

    private float _currentStaggerValue;
    private float _staggerTimer;
    private bool _staggered;

    public int RequiredStaggerPoints => requiredStaggerPoints;
    public float StaggerValuePerPoint => staggerValuePerPoint;
    public float RequiredStaggerValue => Mathf.Max(1f, requiredStaggerPoints * staggerValuePerPoint);
    public float CurrentStaggerValue => _currentStaggerValue;
    public float RemainingStaggerValue => Mathf.Max(0f, RequiredStaggerValue - _currentStaggerValue);
    public int CurrentStaggerPoints => staggerValuePerPoint <= 0f ? 0 : Mathf.FloorToInt(_currentStaggerValue / staggerValuePerPoint);
    public int RemainingStaggerPoints => Mathf.CeilToInt(RemainingStaggerValue / Mathf.Max(1f, staggerValuePerPoint));
    public bool IsStaggered => _staggered;
    public float StaggerDuration => staggerDuration;
    public float StaggerTimer => _staggerTimer;
    public float StaggerFillNormalized => !_staggered || staggerDuration <= 0f
        ? 0f
        : Mathf.Clamp01(1f - _staggerTimer / staggerDuration);
    public float BreakRemainingNormalized => RequiredStaggerValue <= 0f
        ? 0f
        : Mathf.Clamp01(RemainingStaggerValue / RequiredStaggerValue);

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        PlayerAttackHitboxController.PlayerAttackHitConfirmed += OnPlayerAttackHitConfirmed;
    }

    private void OnDisable()
    {
        PlayerAttackHitboxController.PlayerAttackHitConfirmed -= OnPlayerAttackHitConfirmed;
    }

    private void OnValidate()
    {
        requiredStaggerPoints = Mathf.Max(1, requiredStaggerPoints);
        staggerValuePerPoint = Mathf.Max(1f, staggerValuePerPoint);
        staggerDuration = Mathf.Max(0.1f, staggerDuration);
        readyDelayAfterStagger = Mathf.Max(0f, readyDelayAfterStagger);
    }

    private void Update()
    {
        if (!_staggered)
        {
            return;
        }

        _staggerTimer = Mathf.Max(0f, _staggerTimer - Time.deltaTime);
        if (_staggerTimer <= 0f)
        {
            EndStagger("Timer");
        }
    }

    public void ResetStagger()
    {
        _currentStaggerValue = 0f;
        _staggerTimer = 0f;
        _staggered = false;
        SetAnimatorBool(debuffedBoolName, false);
        LogDebug("Stagger reset.");
    }

    public void ForceStartStagger()
    {
        StartStagger("ForceStartStagger");
    }

    public void ForceEndStagger()
    {
        EndStagger("ForceEndStagger");
    }

    private void OnPlayerAttackHitConfirmed(PlayerAttackHitData hitData)
    {
        if (!usePlayerAttackPoiseDamage || hitData == null || hitData.HitCollider == null)
        {
            return;
        }

        if (!IsHitOnThisEnemy(hitData.HitCollider))
        {
            return;
        }

        AddStaggerValue(hitData.PoiseDamage, hitData.AttackId);
    }

    public void AddStaggerValue(float amount, string reason = "")
    {
        if (amount <= 0f)
        {
            return;
        }

        LogDebug($"Stagger value requested. reason={reason}, amount={amount:F1}, staggered={_staggered}, current={_currentStaggerValue:F1}/{RequiredStaggerValue:F1}");

        if (_staggered)
        {
            LogDebug($"Stagger value ignored. reason=AlreadyStaggered, timer={_staggerTimer:F2}");
            return;
        }

        float previousValue = _currentStaggerValue;
        _currentStaggerValue = Mathf.Clamp(_currentStaggerValue + amount, 0f, RequiredStaggerValue);
        LogDebug($"Stagger value gained. reason={reason}, amount={amount:F1}, previous={previousValue:F1}/{RequiredStaggerValue:F1}, current={_currentStaggerValue:F1}/{RequiredStaggerValue:F1}");

        if (_currentStaggerValue >= RequiredStaggerValue)
        {
            StartStagger($"ValueFull:{reason}");
        }
    }

    private bool IsHitOnThisEnemy(Collider hitCollider)
    {
        if (hitCollider == null)
        {
            return false;
        }

        Transform hitTransform = hitCollider.transform;
        return hitTransform == transform ||
            hitTransform.IsChildOf(transform) ||
            transform.IsChildOf(hitTransform);
    }

    private void StartStagger(string reason)
    {
        ResolveReferences();

        _currentStaggerValue = RequiredStaggerValue;
        _staggered = true;
        _staggerTimer = staggerDuration;

        aiController?.AddExternalRecoveryLock("EnemyStagger");
        aiController?.BeginExternalRecovery(staggerDuration + readyDelayAfterStagger);
        hitReactionController?.CancelActiveHitReactionForStagger();
        ResetAnimatorTrigger(hitReactionTriggerToResetOnStagger);
        SetAnimatorBool(debuffedBoolName, true);
        SetAnimatorTrigger(debuffStartTriggerName);
        LogDebug($"Stagger started. reason={reason}, duration={staggerDuration:F2}");
    }

    private void EndStagger(string reason)
    {
        if (!_staggered)
        {
            return;
        }

        _staggered = false;
        _currentStaggerValue = 0f;
        _staggerTimer = 0f;
        SetAnimatorBool(debuffedBoolName, false);
        SetAnimatorTrigger(debuffEndTriggerName);
        aiController?.RemoveExternalRecoveryLock("EnemyStagger");
        aiController?.EndExternalRecovery(readyDelayAfterStagger);
        LogDebug($"Stagger ended. reason={reason}, readyDelay={readyDelayAfterStagger:F2}");
    }

    private void ResolveReferences()
    {
        if (hitReactionController == null)
        {
            hitReactionController = GetComponent<EnemyHitReactionController>();
        }

        if (aiController == null)
        {
            aiController = GetComponent<StrikeJaegerAIController>();
        }

        if (animator == null)
        {
            animator = GetComponent<Animator>();
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

        Debug.Log($"[EnemyStagger] {message} time={Time.time:F3}", this);
    }
}
