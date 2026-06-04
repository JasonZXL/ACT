using UnityEngine;

public class EnemyPressureModel : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private StrikeJaegerAIController aiController;
    [SerializeField] private EnemyCombatMemory combatMemory;

    [Header("Limits")]
    [SerializeField] private float maxValue = 100f;
    [SerializeField] private float memoryDecaySpeed = 8f;

    [Header("Enemy Attack Result")]
    [SerializeField] private float hitPressureGain = 18f;
    [SerializeField] private float hitThreatLoss = 10f;
    [SerializeField] private float hitAggressionGain = 10f;
    [SerializeField] private float hitBurstGain = 8f;
    [SerializeField] private float comboPressureBonus = 8f;
    [SerializeField] private float whiffPressureLoss = 10f;
    [SerializeField] private float whiffThreatGain = 6f;
    [SerializeField] private float whiffRetreatGain = 8f;
    [SerializeField] private float consecutiveWhiffExtraRetreat = 5f;
    [SerializeField] private float perfectDodgedPressureLoss = 25f;
    [SerializeField] private float perfectDodgedThreatGain = 20f;
    [SerializeField] private float perfectDodgedRetreatGain = 25f;
    [SerializeField] private float interruptedThreatGain = 18f;
    [SerializeField] private float interruptedRetreatGain = 18f;

    [Header("Player Hit Enemy")]
    [SerializeField] private float microHitThreatGain = 6f;
    [SerializeField] private float smallHitThreatGain = 12f;
    [SerializeField] private float heavyHitThreatGain = 25f;
    [SerializeField] private float playerHitPressureLoss = 12f;
    [SerializeField] private float playerHitRetreatGain = 10f;
    [SerializeField] private float smallHitPoiseThreshold = 10f;
    [SerializeField] private float heavyHitPoiseThreshold = 25f;
    [SerializeField] private bool markHeavyPlayerHitAsInterrupted = true;

    [Header("Live Context")]
    [SerializeField] private float playerDodgeCooldownPressure = 10f;
    [SerializeField] private float playerDodgeCooldownAggression = 15f;
    [SerializeField] private float playerDodgeCooldownBurst = 15f;
    [SerializeField] private float closePlayerAttackThreat = 12f;
    [SerializeField] private float closePlayerAttackRetreat = 15f;
    [SerializeField] private float pointBlankThreat = 5f;
    [SerializeField] private float pointBlankRetreat = 8f;
    [SerializeField] private float runningApproachAggression = 5f;
    [SerializeField] private float runningApproachBurst = 8f;

    [Header("Debug")]
    [SerializeField] private bool logDebug;
    [SerializeField] private bool logTickDebug;
    [SerializeField] private float tickDebugInterval = 0.5f;

    private float _pressureMemory;
    private float _threatMemory;
    private float _aggressionMemory;
    private float _retreatMemory;
    private float _burstMemory;
    private float _livePressure;
    private float _liveThreat;
    private float _liveAggression;
    private float _liveRetreat;
    private float _liveBurst;
    private float _nextTickDebugTime;

    public float PressureLevel => ClampValue(_pressureMemory + _livePressure);
    public float ThreatenedLevel => ClampValue(_threatMemory + _liveThreat);
    public float AggressionLevel => ClampValue(_aggressionMemory + _liveAggression);
    public float RetreatDesire => ClampValue(_retreatMemory + _liveRetreat);
    public float BurstDesire => ClampValue(_burstMemory + _liveBurst);

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();

        if (combatMemory != null)
        {
            combatMemory.AttackResultRecorded += OnAttackResultRecorded;
        }

        PlayerAttackHitboxController.PlayerAttackHitConfirmed += OnPlayerAttackHitConfirmed;
    }

    private void OnDisable()
    {
        if (combatMemory != null)
        {
            combatMemory.AttackResultRecorded -= OnAttackResultRecorded;
        }

        PlayerAttackHitboxController.PlayerAttackHitConfirmed -= OnPlayerAttackHitConfirmed;
    }

    private void OnValidate()
    {
        maxValue = Mathf.Max(1f, maxValue);
        memoryDecaySpeed = Mathf.Max(0f, memoryDecaySpeed);
        smallHitPoiseThreshold = Mathf.Max(0f, smallHitPoiseThreshold);
        heavyHitPoiseThreshold = Mathf.Max(smallHitPoiseThreshold, heavyHitPoiseThreshold);
        tickDebugInterval = Mathf.Max(0.05f, tickDebugInterval);
    }

    private void Update()
    {
        DecayMemoryValues();
        UpdateLiveContext();
        LogTickDebug();
    }

    public void ResetPressure()
    {
        _pressureMemory = 0f;
        _threatMemory = 0f;
        _aggressionMemory = 0f;
        _retreatMemory = 0f;
        _burstMemory = 0f;
        _livePressure = 0f;
        _liveThreat = 0f;
        _liveAggression = 0f;
        _liveRetreat = 0f;
        _liveBurst = 0f;
        LogDebugMessage("Pressure reset.");
    }

    private void ResolveReferences()
    {
        if (aiController == null)
        {
            aiController = GetComponent<StrikeJaegerAIController>();
        }

        if (combatMemory == null)
        {
            combatMemory = GetComponent<EnemyCombatMemory>();
        }
    }

    private void OnAttackResultRecorded(EnemyCombatMemory memory, EnemyCombatMemory.AttackResult result)
    {
        if (memory != combatMemory)
        {
            return;
        }

        switch (result)
        {
            case EnemyCombatMemory.AttackResult.Hit:
                AddPressure(hitPressureGain + Mathf.Max(0, combatMemory.ComboSuccessCount - 1) * comboPressureBonus);
                AddThreat(-hitThreatLoss);
                AddAggression(hitAggressionGain);
                AddBurst(hitBurstGain);
                AddRetreat(-hitThreatLoss);
                break;
            case EnemyCombatMemory.AttackResult.Whiff:
                AddPressure(-whiffPressureLoss);
                AddThreat(whiffThreatGain);
                AddRetreat(whiffRetreatGain + Mathf.Max(0, combatMemory.ConsecutiveWhiffCount - 1) * consecutiveWhiffExtraRetreat);
                AddAggression(-whiffPressureLoss);
                break;
            case EnemyCombatMemory.AttackResult.PerfectDodged:
                AddPressure(-perfectDodgedPressureLoss);
                AddThreat(perfectDodgedThreatGain);
                AddRetreat(perfectDodgedRetreatGain);
                AddAggression(-perfectDodgedPressureLoss);
                AddBurst(-perfectDodgedPressureLoss);
                break;
            case EnemyCombatMemory.AttackResult.Interrupted:
                AddPressure(-playerHitPressureLoss);
                AddThreat(interruptedThreatGain);
                AddRetreat(interruptedRetreatGain);
                AddAggression(-playerHitPressureLoss);
                break;
        }

        LogDebugMessage($"Attack result applied. result={result}, pressure={PressureLevel:F1}, threat={ThreatenedLevel:F1}, aggression={AggressionLevel:F1}, retreat={RetreatDesire:F1}, burst={BurstDesire:F1}");
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

        float threatGain = ResolvePlayerHitThreatGain(hitData);
        AddThreat(threatGain);
        AddPressure(-playerHitPressureLoss);
        AddRetreat(playerHitRetreatGain + threatGain * 0.35f);
        AddAggression(-threatGain * 0.5f);
        AddBurst(-threatGain * 0.35f);

        if (markHeavyPlayerHitAsInterrupted && hitData.PoiseDamage >= heavyHitPoiseThreshold)
        {
            combatMemory?.NotifyInterrupted();
        }

        LogDebugMessage($"Player hit applied. attackId={hitData.AttackId}, damage={hitData.Damage:F1}, poise={hitData.PoiseDamage:F1}, threatGain={threatGain:F1}");
    }

    private float ResolvePlayerHitThreatGain(PlayerAttackHitData hitData)
    {
        if (hitData.PoiseDamage >= heavyHitPoiseThreshold)
        {
            return heavyHitThreatGain;
        }

        if (hitData.PoiseDamage >= smallHitPoiseThreshold)
        {
            return smallHitThreatGain;
        }

        return microHitThreatGain;
    }

    private void DecayMemoryValues()
    {
        if (memoryDecaySpeed <= 0f)
        {
            return;
        }

        float step = memoryDecaySpeed * Time.deltaTime;
        _pressureMemory = Mathf.MoveTowards(_pressureMemory, 0f, step);
        _threatMemory = Mathf.MoveTowards(_threatMemory, 0f, step);
        _aggressionMemory = Mathf.MoveTowards(_aggressionMemory, 0f, step);
        _retreatMemory = Mathf.MoveTowards(_retreatMemory, 0f, step);
        _burstMemory = Mathf.MoveTowards(_burstMemory, 0f, step);
    }

    private void UpdateLiveContext()
    {
        _livePressure = 0f;
        _liveThreat = 0f;
        _liveAggression = 0f;
        _liveRetreat = 0f;
        _liveBurst = 0f;

        if (aiController == null || !aiController.HasPlayerTarget)
        {
            return;
        }

        if (!aiController.PlayerDodgeReady)
        {
            _livePressure += playerDodgeCooldownPressure;
            _liveAggression += playerDodgeCooldownAggression;
            _liveBurst += playerDodgeCooldownBurst;
        }

        bool close = aiController.CurrentDistanceBand == StrikeJaegerAIController.DistanceBand.Close ||
            aiController.CurrentDistanceBand == StrikeJaegerAIController.DistanceBand.PointBlank;

        if (aiController.PlayerIsAttacking && close)
        {
            _liveThreat += closePlayerAttackThreat;
            _liveRetreat += closePlayerAttackRetreat;
        }

        if (aiController.CurrentDistanceBand == StrikeJaegerAIController.DistanceBand.PointBlank)
        {
            _liveThreat += pointBlankThreat;
            _liveRetreat += pointBlankRetreat;
        }

        bool midOrFar = aiController.CurrentDistanceBand == StrikeJaegerAIController.DistanceBand.Mid ||
            aiController.CurrentDistanceBand == StrikeJaegerAIController.DistanceBand.Far;

        if (aiController.PlayerIsRunning && midOrFar)
        {
            _liveAggression += runningApproachAggression;
            _liveBurst += runningApproachBurst;
        }
    }

    private void AddPressure(float value)
    {
        _pressureMemory = ClampValue(_pressureMemory + value);
    }

    private void AddThreat(float value)
    {
        _threatMemory = ClampValue(_threatMemory + value);
    }

    private void AddAggression(float value)
    {
        _aggressionMemory = ClampValue(_aggressionMemory + value);
    }

    private void AddRetreat(float value)
    {
        _retreatMemory = ClampValue(_retreatMemory + value);
    }

    private void AddBurst(float value)
    {
        _burstMemory = ClampValue(_burstMemory + value);
    }

    private float ClampValue(float value)
    {
        return Mathf.Clamp(value, 0f, maxValue);
    }

    private void LogTickDebug()
    {
        if (!logTickDebug || Time.time < _nextTickDebugTime)
        {
            return;
        }

        _nextTickDebugTime = Time.time + tickDebugInterval;
        LogDebugMessage($"Tick pressure={PressureLevel:F1}, threat={ThreatenedLevel:F1}, aggression={AggressionLevel:F1}, retreat={RetreatDesire:F1}, burst={BurstDesire:F1}");
    }

    private void LogDebugMessage(string message)
    {
        if (!logDebug && !logTickDebug)
        {
            return;
        }

        Debug.Log($"[EnemyPressureModel] {message} time={Time.time:F3}", this);
    }
}
