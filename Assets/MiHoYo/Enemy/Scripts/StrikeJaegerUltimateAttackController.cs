using System;
using UnityEngine;

[RequireComponent(typeof(Animator))]
public class StrikeJaegerUltimateAttackController : MonoBehaviour
{
    [Serializable]
    public class UltimateSequenceStep
    {
        public string stepName = "Ultimate Step";
        [Min(1)] public int animatorPhaseValue = 1;
        public bool facePlayerOnEnter;
        public bool snapFacePlayerOnEnter = true;
        [Min(0f)] public float facePlayerDurationOnEnter;
        [Min(0f)] public float facePlayerTurnSpeed = 1440f;
        public bool facePlayerInLateUpdate;
        public bool fireDaggerOnEnter;
        public string daggerFirePointId;
        public bool hideMeshOnEnter;
        public bool showMeshOnEnter;
        public bool teleportBehindPlayerOnEnter;
        public bool teleportRelativeToPlayerOnEnter;
        public float teleportAngleFromPlayerForward = 120f;
        public bool teleportRelativeUsesSideOffset;
        [Min(0f)] public float maxStepDuration;
    }

    [Header("References")]
    [SerializeField] private StrikeJaegerUltimateChargeController chargeController;
    [SerializeField] private StrikeJaegerAIController aiController;
    [SerializeField] private CombatHealth health;
    [SerializeField] private Animator animator;
    [SerializeField] private EnemyCombatMemory combatMemory;
    [SerializeField] private EnemyHitReactionController hitReactionController;
    [SerializeField] private EnemyUltimateDaggerThrower ultimateDaggerThrower;
    [SerializeField] private StrikeJaegerUltimateRepositionController repositionController;

    [Header("Release")]
    [SerializeField] private bool autoTryReleaseWhenReady = true;
    [SerializeField] private float releaseCheckInterval = 0.35f;
    [SerializeField, Range(0f, 1f)] private float releaseChancePerCheck = 0.35f;
    [SerializeField] private float minReleaseDistance = 0f;
    [SerializeField] private float maxReleaseDistance = 12f;
    [SerializeField] private bool requireAIReadyState = true;
    [SerializeField] private bool consumeChargeOnUltimateStart = true;
    [SerializeField] private bool requireAnimatorStableBeforeRelease = true;
    [SerializeField] private float releaseDelayAfterPerfectDodge = 1f;
    [SerializeField] private float releaseDelayAfterInterrupted = 1f;
    [SerializeField] private float releaseDelayAfterHitReaction = 0.6f;

    [Header("Execution")]
    [SerializeField] private float ultimateFallbackDuration = 12f;
    [SerializeField] private bool autoAdvanceStepOnTimeout = true;
    [SerializeField] private float defaultStepTimeout = 3f;
    [SerializeField] private float aiReadyDelayAfterUltimate = 0.35f;
    [SerializeField] private int startingUltimatePhase = 1;
    [SerializeField] private bool showMeshWhenUltimateEnds = true;

    [Header("Parry Interrupt")]
    [SerializeField] private bool endUltimateOnSuccessfulParry = true;
    [SerializeField] private bool triggerStunOnParryInterrupt = true;
    [SerializeField] private string parryInterruptStunTriggerName = "Stun";
    [SerializeField] private float parryInterruptRecoveryDuration = 1.2f;

    [Header("Ultimate Sequence")]
    [SerializeField] private UltimateSequenceStep[] ultimateSequence = new UltimateSequenceStep[0];

    [Header("Animator Parameters")]
    [SerializeField] private string ultimateTriggerName = "UltimateAttack";
    [SerializeField] private string ultimateActiveBoolName = "UltimateActive";
    [SerializeField] private string ultimatePhaseIntName = "UltimatePhase";

    [Header("Debug")]
    [SerializeField] private bool logDebug;

    private const string UltimateRecoveryLockReason = "EnemyUltimateAttack";

    private bool _isExecutingUltimate;
    private bool _recoveryLockHeld;
    private float _releaseCheckTimer;
    private float _ultimateFallbackTimer;
    private float _facePlayerLockTimer;
    private float _facePlayerLockTurnSpeed;
    private bool _facePlayerLockLateUpdate;
    private int _currentUltimatePhase;
    private int _currentSequenceStepIndex = -1;
    private float _currentStepTimer;
    private float _ultimateReleaseBlockedUntil = -999f;

    public bool IsExecutingUltimate => _isExecutingUltimate;
    public int CurrentUltimatePhase => _currentUltimatePhase;
    public int CurrentSequenceStepIndex => _currentSequenceStepIndex;
    public string CurrentSequenceStepName => TryGetCurrentStep(out UltimateSequenceStep step)
        ? step.stepName
        : string.Empty;

    private void Awake()
    {
        ResolveReferences();
        _releaseCheckTimer = releaseCheckInterval;
    }

    private void OnEnable()
    {
        ResolveReferences();
        PlayerParryController.ParrySucceeded += OnPlayerParrySucceeded;
        SubscribeCombatMemory();
        SubscribeHitReaction();
    }

    private void OnDisable()
    {
        PlayerParryController.ParrySucceeded -= OnPlayerParrySucceeded;
        UnsubscribeCombatMemory();
        UnsubscribeHitReaction();
        if (_isExecutingUltimate)
        {
            FinishUltimate("ControllerDisabled");
        }
    }

    private void OnValidate()
    {
        releaseCheckInterval = Mathf.Max(0.05f, releaseCheckInterval);
        minReleaseDistance = Mathf.Max(0f, minReleaseDistance);
        maxReleaseDistance = Mathf.Max(minReleaseDistance, maxReleaseDistance);
        releaseDelayAfterPerfectDodge = Mathf.Max(0f, releaseDelayAfterPerfectDodge);
        releaseDelayAfterInterrupted = Mathf.Max(0f, releaseDelayAfterInterrupted);
        releaseDelayAfterHitReaction = Mathf.Max(0f, releaseDelayAfterHitReaction);
        ultimateFallbackDuration = Mathf.Max(0.1f, ultimateFallbackDuration);
        defaultStepTimeout = Mathf.Max(0.1f, defaultStepTimeout);
        aiReadyDelayAfterUltimate = Mathf.Max(0f, aiReadyDelayAfterUltimate);
        startingUltimatePhase = Mathf.Max(1, startingUltimatePhase);
        parryInterruptRecoveryDuration = Mathf.Max(0f, parryInterruptRecoveryDuration);

        if (ultimateSequence == null)
        {
            return;
        }

        for (int i = 0; i < ultimateSequence.Length; i++)
        {
            if (ultimateSequence[i] == null)
            {
                continue;
            }

            ultimateSequence[i].animatorPhaseValue = Mathf.Max(1, ultimateSequence[i].animatorPhaseValue);
            ultimateSequence[i].maxStepDuration = Mathf.Max(0f, ultimateSequence[i].maxStepDuration);
            ultimateSequence[i].teleportAngleFromPlayerForward = Mathf.Repeat(ultimateSequence[i].teleportAngleFromPlayerForward + 180f, 360f) - 180f;
        }
    }

    private void Update()
    {
        if (_isExecutingUltimate)
        {
            UpdateUltimateFallback();
            UpdateUltimateStepTimeout();
            if (!_facePlayerLockLateUpdate)
            {
                UpdateUltimateFacingLock(Time.deltaTime);
            }
            return;
        }

        if (!autoTryReleaseWhenReady)
        {
            return;
        }

        _releaseCheckTimer -= Time.deltaTime;
        if (_releaseCheckTimer > 0f)
        {
            return;
        }

        _releaseCheckTimer = releaseCheckInterval;
        TryAutoReleaseUltimate();
    }

    private void LateUpdate()
    {
        if (!_isExecutingUltimate || !_facePlayerLockLateUpdate)
        {
            return;
        }

        UpdateUltimateFacingLock(Time.deltaTime);
    }

    public bool TryStartUltimate()
    {
        return TryStartUltimate("DirectRequest");
    }

    public void AE_AdvanceUltimatePhase()
    {
        if (!_isExecutingUltimate)
        {
            LogDebug("AE_AdvanceUltimatePhase ignored: ultimate is not executing.");
            return;
        }

        AdvanceUltimateSequence("AE_AdvanceUltimatePhase");
    }

    public void AE_SetUltimatePhase(int phaseIndex)
    {
        if (!_isExecutingUltimate)
        {
            LogDebug($"AE_SetUltimatePhase ignored: ultimate is not executing. requestedPhase={phaseIndex}");
            return;
        }

        SetUltimatePhase(Mathf.Max(1, phaseIndex), "AE_SetUltimatePhase");
    }

    public void AE_EndUltimateAttack()
    {
        FinishUltimate("AE_EndUltimateAttack");
    }

    public void AE_UltimateFireDagger(string firePointId)
    {
        if (!_isExecutingUltimate)
        {
            LogDebug($"AE_UltimateFireDagger ignored: ultimate is not executing. firePoint={firePointId}");
            return;
        }

        if (ultimateDaggerThrower == null)
        {
            LogDebug($"AE_UltimateFireDagger ignored: thrower missing. firePoint={firePointId}");
            return;
        }

        ultimateDaggerThrower.FireUltimateDagger(firePointId);
    }

    public void AE_UltimateFireCurrentStepDagger()
    {
        if (!TryGetCurrentStep(out UltimateSequenceStep step))
        {
            LogDebug("AE_UltimateFireCurrentStepDagger ignored: current step missing.");
            return;
        }

        AE_UltimateFireDagger(step.daggerFirePointId);
    }

    public void AE_UltimateHideMesh()
    {
        if (repositionController == null)
        {
            LogDebug("AE_UltimateHideMesh ignored: reposition controller missing.");
            return;
        }

        repositionController.HideMesh();
        LogDebug("AE_UltimateHideMesh executed.");
    }

    public void AE_UltimateShowMesh()
    {
        if (repositionController == null)
        {
            LogDebug("AE_UltimateShowMesh ignored: reposition controller missing.");
            return;
        }

        repositionController.ShowMesh();
        LogDebug("AE_UltimateShowMesh executed.");
    }

    public void AE_UltimateFacePlayer()
    {
        if (!_isExecutingUltimate)
        {
            LogDebug("AE_UltimateFacePlayer ignored: ultimate is not executing.");
            return;
        }

        FacePlayerForUltimate(true);
        LogDebug("AE_UltimateFacePlayer executed.");
    }

    public void AE_UltimateTeleportBehindPlayer()
    {
        if (!_isExecutingUltimate)
        {
            LogDebug("AE_UltimateTeleportBehindPlayer ignored: ultimate is not executing.");
            return;
        }

        if (repositionController == null)
        {
            LogDebug("AE_UltimateTeleportBehindPlayer ignored: reposition controller missing.");
            return;
        }

        repositionController.TeleportBehindPlayer();
        FacePlayerForUltimate(true);
        _facePlayerLockTimer = Mathf.Max(_facePlayerLockTimer, 0.15f);
    }

    public void AE_UltimateVanishAndReposition()
    {
        if (!_isExecutingUltimate)
        {
            LogDebug("AE_UltimateVanishAndReposition ignored: ultimate is not executing.");
            return;
        }

        if (repositionController == null)
        {
            LogDebug("AE_UltimateVanishAndReposition ignored: reposition controller missing.");
            return;
        }

        repositionController.HideMesh();
        repositionController.TeleportBehindPlayer();
        FacePlayerForUltimate(true);
        _facePlayerLockTimer = Mathf.Max(_facePlayerLockTimer, 0.15f);
    }

    public void ForceEndUltimate()
    {
        FinishUltimate("ForceEndUltimate");
    }

    private void OnPlayerParrySucceeded(PlayerParryController parrier, EnemyParryWindow parriedWindow)
    {
        if (!endUltimateOnSuccessfulParry || !_isExecutingUltimate || parriedWindow == null)
        {
            return;
        }

        if (!IsParryWindowOwnedByThisEnemy(parriedWindow))
        {
            return;
        }

        FinishUltimate("ParryInterrupt");

        if (triggerStunOnParryInterrupt)
        {
            SetAnimatorTrigger(parryInterruptStunTriggerName);
        }

        if (parryInterruptRecoveryDuration > 0f)
        {
            aiController?.BeginExternalRecovery(parryInterruptRecoveryDuration);
        }

        LogDebug(parrier != null
            ? $"Ultimate interrupted by parry. parrier={parrier.name}, stunTrigger={parryInterruptStunTriggerName}, recovery={parryInterruptRecoveryDuration:F2}"
            : $"Ultimate interrupted by parry. stunTrigger={parryInterruptStunTriggerName}, recovery={parryInterruptRecoveryDuration:F2}");
    }

    private bool IsParryWindowOwnedByThisEnemy(EnemyParryWindow parriedWindow)
    {
        Transform parryTransform = parriedWindow.transform;
        return parryTransform == transform ||
               parryTransform.IsChildOf(transform) ||
               transform.IsChildOf(parryTransform);
    }

    private void ResolveReferences()
    {
        if (chargeController == null)
        {
            chargeController = GetComponent<StrikeJaegerUltimateChargeController>();
        }

        if (aiController == null)
        {
            aiController = GetComponent<StrikeJaegerAIController>();
        }

        if (health == null)
        {
            health = GetComponent<CombatHealth>();
        }

        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }

        if (combatMemory == null)
        {
            combatMemory = GetComponent<EnemyCombatMemory>();
        }

        if (hitReactionController == null)
        {
            hitReactionController = GetComponent<EnemyHitReactionController>();
        }

        if (ultimateDaggerThrower == null)
        {
            ultimateDaggerThrower = GetComponent<EnemyUltimateDaggerThrower>();
        }

        if (repositionController == null)
        {
            repositionController = GetComponent<StrikeJaegerUltimateRepositionController>();
        }

    }

    private void TryAutoReleaseUltimate()
    {
        if (chargeController == null || !chargeController.UltimateReady)
        {
            return;
        }

        if (!CanReleaseUltimate(out string rejectionReason))
        {
            LogDebug($"Auto release rejected. reason={rejectionReason}");
            return;
        }

        if (UnityEngine.Random.value > releaseChancePerCheck)
        {
            LogDebug($"Auto release roll failed. chance={releaseChancePerCheck:F2}");
            return;
        }

        TryStartUltimate("AutoRelease");
    }

    private void SubscribeCombatMemory()
    {
        if (combatMemory == null)
        {
            return;
        }

        combatMemory.AttackResultRecorded -= OnAttackResultRecorded;
        combatMemory.AttackResultRecorded += OnAttackResultRecorded;
    }

    private void UnsubscribeCombatMemory()
    {
        if (combatMemory == null)
        {
            return;
        }

        combatMemory.AttackResultRecorded -= OnAttackResultRecorded;
    }

    private void SubscribeHitReaction()
    {
        if (hitReactionController == null)
        {
            return;
        }

        hitReactionController.HitReactionStarted -= OnHitReactionStarted;
        hitReactionController.HitReactionStarted += OnHitReactionStarted;
    }

    private void UnsubscribeHitReaction()
    {
        if (hitReactionController == null)
        {
            return;
        }

        hitReactionController.HitReactionStarted -= OnHitReactionStarted;
    }

    private void OnAttackResultRecorded(EnemyCombatMemory memory, EnemyCombatMemory.AttackResult result)
    {
        if (memory != combatMemory)
        {
            return;
        }

        switch (result)
        {
            case EnemyCombatMemory.AttackResult.PerfectDodged:
                BlockUltimateRelease(releaseDelayAfterPerfectDodge, "PerfectDodged");
                break;
            case EnemyCombatMemory.AttackResult.Interrupted:
                BlockUltimateRelease(releaseDelayAfterInterrupted, "Interrupted");
                break;
        }
    }

    private void OnHitReactionStarted(EnemyHitReactionController controller, EnemyHitReactionController.HitReactionType reactionType, bool interruptedAttack)
    {
        if (controller != hitReactionController)
        {
            return;
        }

        BlockUltimateRelease(releaseDelayAfterHitReaction, $"HitReaction:{reactionType}");
    }

    private void BlockUltimateRelease(float duration, string reason)
    {
        if (duration <= 0f)
        {
            return;
        }

        float blockedUntil = Time.time + duration;
        if (blockedUntil <= _ultimateReleaseBlockedUntil)
        {
            return;
        }

        _ultimateReleaseBlockedUntil = blockedUntil;
        _releaseCheckTimer = Mathf.Max(_releaseCheckTimer, duration);
        LogDebug($"Ultimate release blocked. reason={reason}, duration={duration:F2}, until={_ultimateReleaseBlockedUntil:F2}");
    }

    private bool TryStartUltimate(string reason)
    {
        ResolveReferences();

        if (!CanReleaseUltimate(out string rejectionReason))
        {
            LogDebug($"Ultimate start rejected. reason={reason}, detail={rejectionReason}");
            return false;
        }

        if (consumeChargeOnUltimateStart &&
            chargeController != null &&
            !chargeController.ConsumeReadyCharge())
        {
            LogDebug($"Ultimate start rejected. reason={reason}, detail=ChargeConsumeFailed");
            return false;
        }

        _isExecutingUltimate = true;
        _ultimateFallbackTimer = ultimateFallbackDuration;
        _currentSequenceStepIndex = -1;
        _currentUltimatePhase = Mathf.Max(1, startingUltimatePhase);
        _releaseCheckTimer = releaseCheckInterval;

        if (aiController != null)
        {
            aiController.AddExternalRecoveryLock(UltimateRecoveryLockReason);
            _recoveryLockHeld = true;
            aiController.BeginExternalRecovery(ultimateFallbackDuration);
        }

        SetAnimatorBool(ultimateActiveBoolName, true);
        EnterFirstUltimateStep();
        SetAnimatorTrigger(ultimateTriggerName);

        LogDebug(
            $"Ultimate started. reason={reason}, phase={_currentUltimatePhase}, sequenceStep={_currentSequenceStepIndex}, stepName={CurrentSequenceStepName}, fallback={_ultimateFallbackTimer:F2}, distance={GetCurrentPlayerDistance():F2}");
        return true;
    }

    private bool CanReleaseUltimate(out string rejectionReason)
    {
        ResolveReferences();

        if (_isExecutingUltimate)
        {
            rejectionReason = "AlreadyExecuting";
            return false;
        }

        if (health != null && health.IsDead)
        {
            rejectionReason = "EnemyDead";
            return false;
        }

        if (chargeController == null)
        {
            rejectionReason = "ChargeControllerMissing";
            return false;
        }

        if (!chargeController.UltimateReady)
        {
            rejectionReason = "UltimateNotReady";
            return false;
        }

        if (aiController != null)
        {
            if (!aiController.HasPlayerTarget)
            {
                rejectionReason = "PlayerTargetMissing";
                return false;
            }

            if (requireAIReadyState && !aiController.CanThink)
            {
                rejectionReason = $"AIStateNotReady:{aiController.State}";
                return false;
            }
        }

        if (Time.time < _ultimateReleaseBlockedUntil)
        {
            rejectionReason = $"ReleaseBlocked:{_ultimateReleaseBlockedUntil - Time.time:F2}";
            return false;
        }

        if (requireAnimatorStableBeforeRelease && animator != null && animator.IsInTransition(0))
        {
            rejectionReason = "AnimatorInTransition";
            return false;
        }

        float playerDistance = GetCurrentPlayerDistance();
        if (playerDistance < minReleaseDistance || playerDistance > maxReleaseDistance)
        {
            rejectionReason = $"DistanceOutOfRange:{playerDistance:F2}";
            return false;
        }

        rejectionReason = string.Empty;
        return true;
    }

    private float GetCurrentPlayerDistance()
    {
        return aiController != null && aiController.HasPlayerTarget
            ? aiController.PlayerDistance
            : 0f;
    }

    private void UpdateUltimateFallback()
    {
        _ultimateFallbackTimer -= Time.deltaTime;
        if (_ultimateFallbackTimer > 0f)
        {
            return;
        }

        FinishUltimate("FallbackTimeout");
    }

    private void UpdateUltimateStepTimeout()
    {
        if (!autoAdvanceStepOnTimeout || _currentSequenceStepIndex < 0)
        {
            return;
        }

        _currentStepTimer -= Time.deltaTime;
        if (_currentStepTimer > 0f)
        {
            return;
        }

        if (HasNextSequenceStep())
        {
            LogDebug($"Ultimate step timeout. stepIndex={_currentSequenceStepIndex}, stepName={CurrentSequenceStepName}. Advancing sequence.");
            AdvanceUltimateSequence("StepTimeout");
            return;
        }

        LogDebug($"Ultimate final step timeout. stepIndex={_currentSequenceStepIndex}, stepName={CurrentSequenceStepName}. Finishing ultimate.");
        FinishUltimate("StepTimeout");
    }

    private bool HasNextSequenceStep()
    {
        return ultimateSequence != null &&
               ultimateSequence.Length > 0 &&
               _currentSequenceStepIndex + 1 >= 0 &&
               _currentSequenceStepIndex + 1 < ultimateSequence.Length;
    }

    private float ResolveStepTimeout(UltimateSequenceStep step)
    {
        if (step != null && step.maxStepDuration > 0f)
        {
            return step.maxStepDuration;
        }

        return Mathf.Max(0.1f, defaultStepTimeout);
    }

    private void FinishUltimate(string reason)
    {
        if (!_isExecutingUltimate)
        {
            return;
        }

        _isExecutingUltimate = false;
        _ultimateFallbackTimer = 0f;
        _currentStepTimer = 0f;
        _facePlayerLockTimer = 0f;
        _facePlayerLockTurnSpeed = 0f;
        _facePlayerLockLateUpdate = false;
        _currentSequenceStepIndex = -1;

        if (showMeshWhenUltimateEnds)
        {
            repositionController?.ShowMesh();
        }

        SetAnimatorBool(ultimateActiveBoolName, false);

        if (_recoveryLockHeld && aiController != null)
        {
            aiController.RemoveExternalRecoveryLock(UltimateRecoveryLockReason);
            _recoveryLockHeld = false;
        }

        aiController?.EndExternalRecovery(aiReadyDelayAfterUltimate);
        LogDebug($"Ultimate finished. reason={reason}, readyDelay={aiReadyDelayAfterUltimate:F2}");
    }

    private void SetUltimatePhase(int phaseIndex, string reason)
    {
        _currentUltimatePhase = Mathf.Max(1, phaseIndex);
        SetAnimatorInt(ultimatePhaseIntName, _currentUltimatePhase);
        LogDebug($"Ultimate phase changed. reason={reason}, phase={_currentUltimatePhase}");
    }

    private void EnterFirstUltimateStep()
    {
        if (ultimateSequence != null && ultimateSequence.Length > 0)
        {
            _currentSequenceStepIndex = 0;
            ApplySequenceStep(ultimateSequence[0], "UltimateStart");
            return;
        }

        SetUltimatePhase(_currentUltimatePhase, "UltimateStartFallbackPhase");
    }

    private void AdvanceUltimateSequence(string reason)
    {
        if (ultimateSequence == null || ultimateSequence.Length == 0)
        {
            SetUltimatePhase(_currentUltimatePhase + 1, reason);
            return;
        }

        int nextStepIndex = _currentSequenceStepIndex + 1;
        if (nextStepIndex < 0 || nextStepIndex >= ultimateSequence.Length)
        {
            LogDebug($"Ultimate sequence advance ignored: no next step. reason={reason}, currentStep={_currentSequenceStepIndex}");
            return;
        }

        _currentSequenceStepIndex = nextStepIndex;
        ApplySequenceStep(ultimateSequence[_currentSequenceStepIndex], reason);
    }

    private void ApplySequenceStep(UltimateSequenceStep step, string reason)
    {
        if (step == null)
        {
            LogDebug($"Ultimate sequence step missing. reason={reason}, stepIndex={_currentSequenceStepIndex}");
            return;
        }

        SetUltimatePhase(Mathf.Max(1, step.animatorPhaseValue), reason);
        _currentStepTimer = ResolveStepTimeout(step);
        ApplyStepEnterActions(step, reason);
        LogDebug(
            $"Ultimate sequence step entered. reason={reason}, stepIndex={_currentSequenceStepIndex}, stepName={step.stepName}, animatorPhase={step.animatorPhaseValue}, timeout={_currentStepTimer:F2}");
    }

    private void ApplyStepEnterActions(UltimateSequenceStep step, string reason)
    {
        if (step.facePlayerOnEnter)
        {
            FacePlayerForUltimate(step.snapFacePlayerOnEnter);
            BeginFacePlayerLock(step.facePlayerDurationOnEnter, step.facePlayerTurnSpeed, step.facePlayerInLateUpdate);
        }

        if (step.hideMeshOnEnter)
        {
            repositionController?.HideMesh();
        }

        if (step.teleportBehindPlayerOnEnter)
        {
            repositionController?.TeleportBehindPlayer();
            FacePlayerForUltimate(true);
            _facePlayerLockTimer = Mathf.Max(_facePlayerLockTimer, 0.15f);
        }

        if (step.teleportRelativeToPlayerOnEnter)
        {
            repositionController?.TeleportRelativeToPlayerForward(
                step.teleportAngleFromPlayerForward,
                step.teleportRelativeUsesSideOffset);
            FacePlayerForUltimate(true);
            _facePlayerLockTimer = Mathf.Max(_facePlayerLockTimer, 0.15f);
        }

        if (step.showMeshOnEnter)
        {
            repositionController?.ShowMesh();
        }

        if (!step.fireDaggerOnEnter)
        {
            return;
        }

        if (ultimateDaggerThrower == null)
        {
            LogDebug($"Step dagger fire skipped: thrower missing. reason={reason}, step={step.stepName}, firePoint={step.daggerFirePointId}");
            return;
        }

        ultimateDaggerThrower.FireUltimateDagger(step.daggerFirePointId);
    }

    private void BeginFacePlayerLock(float duration, float turnSpeed, bool lateUpdate)
    {
        if (duration <= 0f)
        {
            return;
        }

        if (duration >= _facePlayerLockTimer)
        {
            _facePlayerLockTimer = duration;
            _facePlayerLockTurnSpeed = turnSpeed;
            _facePlayerLockLateUpdate = lateUpdate;
            return;
        }

        _facePlayerLockTurnSpeed = Mathf.Max(_facePlayerLockTurnSpeed, turnSpeed);
        _facePlayerLockLateUpdate = _facePlayerLockLateUpdate || lateUpdate;
    }

    private void UpdateUltimateFacingLock(float deltaTime)
    {
        if (_facePlayerLockTimer <= 0f)
        {
            return;
        }

        _facePlayerLockTimer -= deltaTime;
        FacePlayerForUltimate(_facePlayerLockTurnSpeed, deltaTime);
        if (_facePlayerLockTimer <= 0f)
        {
            _facePlayerLockTurnSpeed = 0f;
            _facePlayerLockLateUpdate = false;
        }
    }

    private void FacePlayerForUltimate(bool snap)
    {
        if (aiController != null)
        {
            aiController.FacePlayerForExternalAction(snap);
            return;
        }

        repositionController?.FacePlayer();
    }

    private void FacePlayerForUltimate(float turnSpeed, float deltaTime)
    {
        if (aiController != null)
        {
            aiController.FacePlayerForExternalAction(turnSpeed, deltaTime);
            return;
        }

        repositionController?.FacePlayer();
    }

    private bool TryGetCurrentStep(out UltimateSequenceStep step)
    {
        step = null;
        if (ultimateSequence == null ||
            _currentSequenceStepIndex < 0 ||
            _currentSequenceStepIndex >= ultimateSequence.Length)
        {
            return false;
        }

        step = ultimateSequence[_currentSequenceStepIndex];
        return step != null;
    }

    private void SetAnimatorTrigger(string triggerName)
    {
        if (!HasAnimatorParameter(triggerName, AnimatorControllerParameterType.Trigger))
        {
            LogDebug($"Animator trigger skipped. parameter={triggerName}, expected=Trigger");
            return;
        }

        animator.SetTrigger(triggerName);
    }

    private void SetAnimatorBool(string boolName, bool value)
    {
        if (!HasAnimatorParameter(boolName, AnimatorControllerParameterType.Bool))
        {
            LogDebug($"Animator bool skipped. parameter={boolName}, expected=Bool");
            return;
        }

        animator.SetBool(boolName, value);
    }

    private void SetAnimatorInt(string intName, int value)
    {
        if (!HasAnimatorParameter(intName, AnimatorControllerParameterType.Int))
        {
            LogDebug($"Animator int skipped. parameter={intName}, expected=Int");
            return;
        }

        animator.SetInteger(intName, value);
    }

    private bool HasAnimatorParameter(string parameterName, AnimatorControllerParameterType parameterType)
    {
        if (animator == null || string.IsNullOrEmpty(parameterName))
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

        Debug.Log($"[StrikeJaegerUltimateAttack] {message} time={Time.time:F3}", this);
    }
}
