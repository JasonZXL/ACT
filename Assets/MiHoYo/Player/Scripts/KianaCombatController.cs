using UnityEngine;
using UnityEngine.InputSystem;
using System;

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(Animator))]
[RequireComponent(typeof(PlayerBattleWillSystem))]
public class KianaCombatController : MonoBehaviour
{
    public static event Action<KianaCombatController> DodgeStarted;
    public static event Action<KianaCombatController> PerfectDodgeWindowOpened;
    public static event Action<KianaCombatController> PerfectDodgeWindowClosed;
    public static event Action<KianaCombatController> PerfectDodgeEffectAllowed;
    public static event Action<KianaCombatController> DodgeCounterDeclinedByMoveInput;
    public static event Action<KianaCombatController, string> BranchAttackScenarioEvent;

    private static KianaCombatController _activePerfectDodgeWindowOwner;
    private static KianaCombatController _activePerfectDodgeEffectOwner;

    public static KianaCombatController ActivePerfectDodgeWindowOwner => _activePerfectDodgeWindowOwner;
    public static KianaCombatController ActivePerfectDodgeEffectOwner => _activePerfectDodgeEffectOwner;

    public enum CombatMoveState
    {
        Idle = 0,
        Jog = 1,
        Boost = 2
    }

    public enum CombatMoveMode
    {
        Idle = 0,
        Move = 1
    }

    [Header("References")]
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private PlayerBattleWillSystem battleWillSystem;
    [SerializeField] private PlayerAttackTargetingSystem attackTargetingSystem;
    [SerializeField] private PlayerParryController parryController;
    [SerializeField] private KianaDodgeCooldown dodgeCooldown;
    [SerializeField] private PlayerBranchChargeVFXController branchChargeVfx;
    [SerializeField] private PlayerMovementLockController movementLockController;
    [SerializeField] private CombatMovementDebuffController movementDebuffController;

    [Header("Combat Movement")]
    [SerializeField] private float combatMoveSpeed = 4f;
    [SerializeField] private float combatBoostSpeed = 7f;
    [SerializeField] private float combatAttackMoveMultiplier = 0.35f;
    [SerializeField] private float dodgeDuration = 0.28f;
    [SerializeField] private float parryLockDuration = 0.25f;
    [SerializeField] private float minTurnSpeed = 360f;
    [SerializeField] private float maxTurnSpeed = 1080f;
    [SerializeField] private bool shouldFaceMoveDirection = true;
    [SerializeField] private bool dodgeInputEnabled;
    [SerializeField] private float moveInputGraceTime = 0.08f;

    [Header("Kiana Branch Attack")]
    [SerializeField] private float lightAttack03BranchHoldTime = 0.18f;
    [SerializeField] private float branchMinimumBattleWill = 100f;
    [SerializeField] private float branchChargeRate = 240f;
    [SerializeField] private float branchChargeDeadZone = 30f;
    [SerializeField] private float battleWillPerBranchLevel = 100f;
    [SerializeField] private int maxBranchAttackLevel = 2;
    [SerializeField] private bool spendBattleWillOnBranchRelease = true;
    [SerializeField] private bool useBranchSlowMotion = true;
    [SerializeField, Range(0.05f, 1f)] private float branchSlowMotionTimeScale = 0.25f;
    [SerializeField] private float branchSlowMotionMaxDuration = 2f;
    [SerializeField] private bool cancelBranchSlowMotionOnMoveInput = true;

    [Header("Attack Exit")]
    [SerializeField] private float attackExitFallbackNormalizedTime = 0.98f;

    [Header("Hit Reaction Root Motion")]
    [SerializeField] private bool enableEnemyHitReactionRootMotion = true;
    [SerializeField] private float hitLightRootMotionFallbackDuration = 0.45f;
    [SerializeField] private float hitHeavyRootMotionFallbackDuration = 0.9f;

    [Header("Perfect Dodge Counter")]
    [SerializeField] private bool enablePerfectDodgeCounterAttack = true;
    [SerializeField, Range(1f, 360f)] private float dodgeCounterAttackSearchAngle = 360f;
    [SerializeField] private bool enableDodgeCounterAttackLunge = true;
    [SerializeField] private float dodgeCounterAttackLungeSpeed = 12f;
    [SerializeField] private float dodgeCounterAttackStopDistance = 1.2f;
    [SerializeField] private float dodgeCounterAttackLungeMaxDuration = 0.28f;
    [SerializeField] private float dodgeCounterAttackMaxMoveDistance = 5f;
    [SerializeField] private float dodgeCounterAttackTransitionFallbackDelay = 0.6f;

    [Header("Parry Counter Attack Lunge")]
    [SerializeField] private bool allowParryCounterAttackToInterruptParryAction = true;
    [SerializeField] private bool enableParryCounterAttackLunge = true;
    [SerializeField, Range(1f, 360f)] private float parryCounterAttackSearchAngle = 360f;
    [SerializeField] private float parryCounterAttackLungeSpeed = 12f;
    [SerializeField] private float parryCounterAttackStopDistance = 1.2f;
    [SerializeField] private float parryCounterAttackLungeMaxDuration = 0.28f;
    [SerializeField] private float parryCounterAttackMaxMoveDistance = 5f;

    [Header("Dodge Counter Blink")]
    [SerializeField] private bool enableDodgeCounterBlink = true;
    [SerializeField] private Renderer[] dodgeCounterBlinkRenderers;
    [SerializeField] private float dodgeCounterBlinkDelay = 0f;
    [SerializeField] private float dodgeCounterHiddenDuration = 0.08f;

    [Header("Debug")]
    [SerializeField] private bool logLightAttackDebug;
    [SerializeField] private bool logLeftMouseInputDebug;
    [SerializeField] private bool logBranchAttackDebug;
    [SerializeField] private float branchAttackDebugInterval = 0.15f;
    [SerializeField] private bool logMovementDebug;
    [SerializeField] private float movementDebugInterval = 0.15f;

    private static readonly int HashHasMoveInput = Animator.StringToHash("HasMoveInput");
    private static readonly int HashMoveMode = Animator.StringToHash("MoveMode");
    private static readonly int HashCombatMoveMode = Animator.StringToHash("CombatMoveMode");
    private static readonly int HashCombatMoveMagnitude = Animator.StringToHash("CombatMoveMagnitude");
    private static readonly int HashFocusValue = Animator.StringToHash("FocusValue");
    private static readonly int HashCanExitAttack = Animator.StringToHash("CanExitAttack");
    private static readonly int HashCanExitHitReaction = Animator.StringToHash("CanExitHitReaction");
    private static readonly int HashLightAttackTrigger = Animator.StringToHash("LightAttack");
    private static readonly int HashLightComboNextTrigger = Animator.StringToHash("LightComboNext");
    private static readonly int HashDodgeCounterAttackTrigger = Animator.StringToHash("DodgeCounterAttack");
    private static readonly int HashLightBranchStartTrigger = Animator.StringToHash("LightBranchStart");
    private static readonly int HashBranchChargeAvailable = Animator.StringToHash("BranchChargeAvailable");
    private static readonly int HashBranchAttackLevel = Animator.StringToHash("BranchAttackLevel");
    private static readonly int HashBranchAttackReleaseTrigger = Animator.StringToHash("BranchAttackRelease");
    private static readonly int HashRunAttackTrigger = Animator.StringToHash("RunAttack");
    private static readonly int HashParryTrigger = Animator.StringToHash("Parry");
    private static readonly int HashParryCounterAttackTrigger = Animator.StringToHash("ParryCounterAttack");
    private static readonly int HashParryState = Animator.StringToHash("Parry");
    private static readonly int HashParryCounterAttackState = Animator.StringToHash("Parry_Counter_Attack");
    private static readonly int HashDodgeTrigger = Animator.StringToHash("Dodge");
    private static readonly int HashDodgeDirection = Animator.StringToHash("DodgeDirection");
    private static readonly int HashCanExitDodge = Animator.StringToHash("CanExitDodge");
    private static readonly int HashLightAttack01State = Animator.StringToHash("Light_Attack_01");
    private static readonly int HashLightAttack02State = Animator.StringToHash("Light_Attack_02");
    private static readonly int HashLightAttack03State = Animator.StringToHash("Light_Attack_03");
    private static readonly int HashLightAttack04State = Animator.StringToHash("Light_Attack_04");
    private static readonly int HashLightAttack05State = Animator.StringToHash("Light_Attack_05");
    private static readonly int HashDodgeCounterAttackState = Animator.StringToHash("Dodge_CounterAttack");
    private static readonly int HashKianaAttack3BranchState = Animator.StringToHash("Kiana_Attack_3_fenzhi");
    private static readonly int HashKianaAttack4BranchState = Animator.StringToHash("Kiana_Attack_4_fenzhi");
    private static readonly int HashKianaAttackQteState = Animator.StringToHash("Kiana_Attack_QTE");
    private static readonly int HashRunAttack01State = Animator.StringToHash("Run_Attack_01");
    private const string AttackStateTag = "Attack";
    private const int DodgeDirectionBack = 0;
    private const int DodgeDirectionForward = 1;

    private CharacterController _controller;
    private Animator _animator;
    private Vector2 _moveInput;
    private Vector3 _moveDirection;
    private Vector3 _lastPosition;
    private bool _hasMoveInput;
    private float _lastMoveInputTime = -999f;
    private CombatMoveState _moveState = CombatMoveState.Idle;
    private CombatMoveMode _moveMode = CombatMoveMode.Idle;

    private bool _leftMouseHeld;
    private bool _isBranchStartRequested;
    private bool _isBranchChargeWindowOpen;
    private bool _isBranchCharging;
    private bool _isBranchSlowMotionActive;
    private float _branchLeftMouseHoldTimer;
    private float _branchChargePoints;
    private float _branchSlowMotionTimer;
    private int _currentBranchAttackLevel;
    private int _lastLoggedBranchAttackLevel = -1;
    private float _timeScaleBeforeBranchSlowMotion = 1f;
    private float _fixedDeltaTimeBeforeBranchSlowMotion = 0.02f;
    private float _nextBranchHoldDebugTime;
    private float _nextBranchChargeDebugTime;
    private bool _queuedLightAttack;
    private bool _canLightAttackCancel;
    private bool _isAttackLocked;
    private bool _isAttackMovementLocked;
    private bool _isDodging;
    private bool _canDodgeAttackCancel;
    private bool _queuedDodgeRunAttack;
    private bool _queuedDodgeCounterAttack;
    private bool _queuedParryCounterAttack;
    private bool _dodgeCounterAttackReady;
    private bool _isHitReactionRootMotionActive;
    private bool _isHitReactionInputLocked;
    private int _dodgeCounterTriggerFrame = -1;
    private float _dodgeCounterTriggerUnscaledTime = -999f;
    private int _parryCounterTriggerFrame = -1;
    private bool _isDodgeCounterAttackLunging;
    private bool _isParryCounterAttackLunging;
    private float _dodgeTimer;
    private float _parryLockTimer;
    private float _hitReactionRootMotionTimer;
    private float _nextMovementDebugTime;
    private float _dodgeCounterAttackLungeTimer;
    private float _dodgeCounterAttackLungeMoved;
    private Transform _dodgeCounterAttackLungeTarget;
    private float _parryCounterAttackLungeTimer;
    private float _parryCounterAttackLungeMoved;
    private Transform _parryCounterAttackLungeTarget;
    private Coroutine _dodgeCounterBlinkRoutine;

    private void Awake()
    {
        _controller = GetComponent<CharacterController>();
        _animator = GetComponent<Animator>();
        if (battleWillSystem == null)
        {
            battleWillSystem = GetComponent<PlayerBattleWillSystem>();
        }

        if (attackTargetingSystem == null)
        {
            attackTargetingSystem = GetComponent<PlayerAttackTargetingSystem>();
        }

        if (parryController == null)
        {
            parryController = GetComponent<PlayerParryController>();
        }

        if (parryController == null)
        {
            parryController = gameObject.AddComponent<PlayerParryController>();
        }

        if (dodgeCooldown == null)
        {
            dodgeCooldown = GetComponent<KianaDodgeCooldown>();
        }

        if (branchChargeVfx == null)
        {
            branchChargeVfx = GetComponentInChildren<PlayerBranchChargeVFXController>(true);
        }

        if (movementLockController == null)
        {
            movementLockController = GetComponent<PlayerMovementLockController>();
        }

        if (movementDebuffController == null)
        {
            movementDebuffController = GetComponent<CombatMovementDebuffController>();
        }

        _lastPosition = transform.position;

        // 若游戏以战斗模式启动（startInCombatMode=true），Animator 会在同帧末触发
        // 进战动画的 AE_LockMovement，覆盖掉 ResetState() 的清锁效果，且由于状态机
        // 会中断该动画，AE_UnlockMovement 永远不会触发，导致移动锁永久残留。
        // 延后一帧再清锁，确保 Animator 初始化事件全部处理完毕后覆盖结果。
        // 若 startInCombatMode=false，本组件在 Start() 阶段会被 disable，协程自动停止。
        StartCoroutine(ResetMovementLockAfterFirstFrame());
    }

    private System.Collections.IEnumerator ResetMovementLockAfterFirstFrame()
    {
        yield return null;
        if (!IsAnimatorInParryActionState())
        {
            movementLockController?.ResetLock();
        }
    }

    private void OnEnable()
    {
        ResetState();
        SubscribeBattleWillSystem();
        RefreshBranchChargeVfxResourceReady(true);
        EnemyAttackWarningWindow.PerfectDodgeConfirmed += OnPerfectDodgeConfirmed;
    }

    private void OnDisable()
    {
        UnsubscribeBattleWillSystem();
        EnemyAttackWarningWindow.PerfectDodgeConfirmed -= OnPerfectDodgeConfirmed;
        AE_ClosePerfectDodgeWindow();
        EndBranchSlowMotion();
        StopParryCounterAttackLunge();
        RestoreDodgeCounterBlinkRenderers();
        branchChargeVfx?.HideImmediate();
    }

    private void SubscribeBattleWillSystem()
    {
        if (battleWillSystem == null)
        {
            battleWillSystem = GetComponent<PlayerBattleWillSystem>();
        }

        if (battleWillSystem != null)
        {
            battleWillSystem.BattleWillChanged -= OnBattleWillChanged;
            battleWillSystem.BattleWillChanged += OnBattleWillChanged;
        }
    }

    private void UnsubscribeBattleWillSystem()
    {
        if (battleWillSystem != null)
        {
            battleWillSystem.BattleWillChanged -= OnBattleWillChanged;
        }
    }

    private void OnBattleWillChanged(PlayerBattleWillSystem source, float previousValue, float currentValue)
    {
        RefreshBranchChargeVfxResourceReady();
    }

    private void RefreshBranchChargeVfxResourceReady(bool forceVisualState = false)
    {
        if (branchChargeVfx == null)
        {
            return;
        }

        bool canReleaseBranchAttack = GetCurrentBattleWill() >= branchMinimumBattleWill;
        branchChargeVfx.SetResourceReady(canReleaseBranchAttack, forceVisualState);
    }

    private void Update()
    {
        ResolveCameraReference();
        ReadMovementInput();
        UpdateMoveDirection();
        FaceMoveDirection();
        ReadCombatInput();
        ProcessQueuedDodgeCounterAttack();
        ProcessQueuedParryCounterAttack();
        ProcessQueuedLightAttack();
        FaceAttackTarget();
        UpdateActionTimers();
        UpdateAttackExitFallback();
        UpdateAttackMovementLockSafety();
        UpdateExternalMovementLockSafety();
        UpdateDodgeCounterTransitionFallback();
        UpdateParryCounterLockFallback();
        UpdateHitReactionRootMotionFallback();
        UpdateDodgeCounterAttackLunge();
        UpdateParryCounterAttackLunge();
        MoveCharacter();
        UpdateAnimatorParameters();
        LogMovementDebug();
    }

    public void ResetState()
    {
        _moveInput = Vector2.zero;
        _moveDirection = Vector3.zero;
        _hasMoveInput = false;
        _lastMoveInputTime = -999f;
        _moveState = CombatMoveState.Idle;
        _moveMode = CombatMoveMode.Idle;
        _leftMouseHeld = false;
        _isBranchStartRequested = false;
        _isBranchChargeWindowOpen = false;
        _isBranchCharging = false;
        _isBranchSlowMotionActive = false;
        _branchLeftMouseHoldTimer = 0f;
        _branchChargePoints = 0f;
        _branchSlowMotionTimer = 0f;
        _currentBranchAttackLevel = 0;
        _lastLoggedBranchAttackLevel = -1;
        _nextBranchHoldDebugTime = 0f;
        _nextBranchChargeDebugTime = 0f;
        _queuedLightAttack = false;
        _canLightAttackCancel = false;
        _isAttackLocked = false;
        _isAttackMovementLocked = false;
        _isDodging = false;
        _canDodgeAttackCancel = false;
        _queuedDodgeRunAttack = false;
        _queuedDodgeCounterAttack = false;
        _queuedParryCounterAttack = false;
        _dodgeCounterAttackReady = false;
        _isHitReactionRootMotionActive = false;
        _isHitReactionInputLocked = false;
        _dodgeCounterTriggerFrame = -1;
        _dodgeCounterTriggerUnscaledTime = -999f;
        _parryCounterTriggerFrame = -1;
        StopDodgeCounterAttackLunge();
        StopParryCounterAttackLunge();
        RestoreDodgeCounterBlinkRenderers();
        _dodgeTimer = 0f;
        _parryLockTimer = 0f;
        _hitReactionRootMotionTimer = 0f;
        _lastPosition = transform.position;
        _nextMovementDebugTime = 0f;
        AE_ClosePerfectDodgeWindow();
        movementLockController?.ResetLock();
        ClearAttackTarget();
        branchChargeVfx?.HideImmediate();

        if (_animator != null)
        {
            _animator.SetBool(HashCanExitAttack, false);
            SafeSetBool(HashCanExitHitReaction, true);
            SafeSetBool(HashBranchChargeAvailable, false);
            SafeSetBool(HashCanExitDodge, false);
            SafeSetInteger(HashBranchAttackLevel, 0);
            SafeSetInteger(HashDodgeDirection, DodgeDirectionBack);
        }
    }

    public float GetCurrentFocus()
    {
        return GetCurrentBattleWill();
    }

    public void GainFocus(float amount)
    {
        GainBattleWill(amount);
    }

    public float GetCurrentBattleWill()
    {
        return battleWillSystem != null ? battleWillSystem.CurrentBattleWill : 0f;
    }

    public float GetMaxBattleWill()
    {
        return battleWillSystem != null ? battleWillSystem.MaxBattleWill : 0f;
    }

    public float GetBattleWillNormalized()
    {
        return battleWillSystem != null ? battleWillSystem.NormalizedBattleWill : 0f;
    }

    public void GainBattleWill(float amount)
    {
        if (battleWillSystem != null)
        {
            battleWillSystem.GainBattleWill(amount);
        }
    }

    public bool TrySpendBattleWill(float amount)
    {
        if (amount <= 0f)
        {
            return true;
        }

        return battleWillSystem != null && battleWillSystem.TrySpendBattleWill(amount);
    }

    public void FillBattleWill()
    {
        if (battleWillSystem != null)
        {
            battleWillSystem.FillBattleWill();
        }
    }

    public float GetPreviewBattleWillCost()
    {
        if (!_isBranchCharging)
        {
            return 0f;
        }

        return Mathf.Clamp(_branchChargePoints, 0f, GetCurrentBattleWill());
    }

    private void ResolveCameraReference()
    {
        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }
    }

    private void ReadMovementInput()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            _moveInput = Vector2.zero;
            _hasMoveInput = false;
            _moveState = CombatMoveState.Idle;
            _moveMode = CombatMoveMode.Idle;
            return;
        }

        float x = 0f;
        float y = 0f;

        if (keyboard.aKey.isPressed && !keyboard.dKey.isPressed)
        {
            x = -1f;
        }
        else if (keyboard.dKey.isPressed && !keyboard.aKey.isPressed)
        {
            x = 1f;
        }

        if (keyboard.wKey.isPressed && !keyboard.sKey.isPressed)
        {
            y = 1f;
        }
        else if (keyboard.sKey.isPressed && !keyboard.wKey.isPressed)
        {
            y = -1f;
        }

        _moveInput = new Vector2(x, y);
        if (_moveInput.sqrMagnitude > 1f)
        {
            _moveInput.Normalize();
        }

        bool hasRawMoveInput = _moveInput.sqrMagnitude > 0.001f;
        bool movePressedThisFrame =
            keyboard.wKey.wasPressedThisFrame ||
            keyboard.aKey.wasPressedThisFrame ||
            keyboard.sKey.wasPressedThisFrame ||
            keyboard.dKey.wasPressedThisFrame;
        if (hasRawMoveInput)
        {
            _lastMoveInputTime = Time.time;
        }

        bool shiftPressed = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
        _hasMoveInput = hasRawMoveInput || Time.time - _lastMoveInputTime <= moveInputGraceTime;
        _moveState = !_hasMoveInput ? CombatMoveState.Idle : shiftPressed ? CombatMoveState.Jog : CombatMoveState.Boost;
        _moveMode = _hasMoveInput ? CombatMoveMode.Move : CombatMoveMode.Idle;

        TryCancelBranchSlowMotionByMoveInput(movePressedThisFrame, hasRawMoveInput);
        TryDeclineDodgeCounterByMoveInput(movePressedThisFrame);
    }

    private void TryCancelBranchSlowMotionByMoveInput(bool movePressedThisFrame, bool hasRawMoveInput)
    {
        if (!cancelBranchSlowMotionOnMoveInput || !_isBranchSlowMotionActive || !movePressedThisFrame || !hasRawMoveInput)
        {
            return;
        }

        CancelBranchChargeWindowByMoveInput();
    }

    private void TryDeclineDodgeCounterByMoveInput(bool movePressedThisFrame)
    {
        if (!movePressedThisFrame || !_dodgeCounterAttackReady || _queuedDodgeCounterAttack || _isDodgeCounterAttackLunging)
        {
            return;
        }

        Mouse mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame)
        {
            return;
        }

        _dodgeCounterAttackReady = false;
        _queuedDodgeCounterAttack = false;
        SafeResetTrigger(HashDodgeCounterAttackTrigger);
        DodgeCounterDeclinedByMoveInput?.Invoke(this);
        LogDodgeCancelDebug("Dodge counter attack declined by move input. Witch time fade-out requested.");
    }

    private void ReadCombatInput()
    {
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;
        if (keyboard == null || mouse == null)
        {
            return;
        }

        if (keyboard.spaceKey.wasPressedThisFrame)
        {
            TryStartCombatDodge();
        }

        if (mouse.rightButton.wasPressedThisFrame)
        {
            // 统一走带状态清理的弹刀路径，与旧 TryStartParry() 行为一致：
            // 1. 闪避中或弹刀冷却期内不允许再次弹刀
            // 2. 清除所有攻击状态（防止 _queuedLightAttack 等残留）
            // 3. 设置 _parryLockTimer 防止弹刀后立即攻击/闪避
            if (!_isDodging && _parryLockTimer <= 0f)
            {
                if (parryController != null && !parryController.CanStartParry)
                {
                    LogDodgeCancelDebug($"Right mouse parry rejected before cancel. parryCooldown={parryController.ParryCooldownRemaining:F3}");
                    return;
                }

                CancelAttackForDefensiveAction();
                SafeResetTrigger(HashParryCounterAttackTrigger);
                if (parryController != null)
                {
                    if (parryController.TryStartParry())
                    {
                        _parryLockTimer = parryLockDuration;
                        movementLockController?.AE_LockMovement();
                        LogDodgeCancelDebug("Right mouse parry started. Movement locked immediately by input path.");
                    }
                }
                else
                {
                    _parryLockTimer = parryLockDuration;
                    _animator.ResetTrigger(HashDodgeTrigger);
                    _animator.SetTrigger(HashParryTrigger);
                    movementLockController?.AE_LockMovement();
                    LogDodgeCancelDebug("Right mouse parry started without PlayerParryController. Movement locked immediately by input path.");
                }
            }
            else
            {
                LogDodgeCancelDebug($"Right mouse parry rejected. dodging={_isDodging}, parryLock={_parryLockTimer:F3}");
            }
        }

        if (mouse.leftButton.wasPressedThisFrame)
        {
            bool attackBusyAtPress = IsAttackInputBusy();
            _leftMouseHeld = true;

            LogLeftMouseInputDebug($"Left mouse pressed. attackBusyAtPress={attackBusyAtPress}.");
            LogLightAttackDebug("Left mouse pressed.");
            LogDodgeCancelDebug("Left mouse pressed.");
            LogBranchAttackDebug($"Left mouse pressed. attackBusyAtPress={attackBusyAtPress}.");

            if (TryStartParryCounterAttack())
            {
                LogLeftMouseInputDebug("Left mouse press consumed by ParryCounterAttack.");
                return;
            }

            if (attackBusyAtPress)
            {
                if (_isBranchChargeWindowOpen)
                {
                    LogLeftMouseInputDebug("Left mouse press consumed by branch charge window.");
                    LogBranchAttackDebug("Left mouse press accepted inside branch charge window. Starting branch charge.");
                    StartBranchCharge();
                }
                else if (!IsAnimatorInLightAttack03State())
                {
                    LogLeftMouseInputDebug("Left mouse press routed to light attack queue.");
                    TryQueueLightAttack();
                }
                else
                {
                    LogLeftMouseInputDebug("Left mouse press held for Light_Attack_03 branch threshold check.");
                    LogBranchAttackDebug("Left mouse press during Light_Attack_03. Waiting for hold threshold.");
                }
            }
            else
            {
                LogLeftMouseInputDebug("Left mouse press stored. Release will resolve attack start.");
            }
        }

        UpdateKianaBranchAttackInput();

        if (mouse.leftButton.wasReleasedThisFrame)
        {
            LogLeftMouseInputDebug("Left mouse released.");
            LogLightAttackDebug("Left mouse released.");
            LogDodgeCancelDebug("Left mouse released.");
            LogBranchAttackDebug("Left mouse released.");
            ResolveLeftMouseRelease();
        }
    }

    private void ResolveLeftMouseRelease()
    {
        LogLeftMouseInputDebug("ResolveLeftMouseRelease called.");
        LogDodgeCancelDebug("ResolveLeftMouseRelease called.");
        if (!_leftMouseHeld)
        {
            LogLeftMouseInputDebug("ResolveLeftMouseRelease ignored: left mouse was not held by controller.");
            LogDodgeCancelDebug("ResolveLeftMouseRelease ignored: left mouse was not held by controller.");
            return;
        }

        _leftMouseHeld = false;
        LogLeftMouseInputDebug("ResolveLeftMouseRelease accepted: leftHeld cleared.");
        LogDodgeCancelDebug("ResolveLeftMouseRelease accepted: leftHeld cleared, resolving attack.");
        LogBranchAttackDebug("ResolveLeftMouseRelease accepted: leftHeld cleared.");

        if (_isBranchCharging)
        {
            LogLeftMouseInputDebug("Left mouse release consumed by branch release.");
            LogBranchAttackDebug("Left mouse released while branch charging. Releasing branch attack.");
            ReleaseBranchAttack();
            return;
        }

        if (_dodgeCounterAttackReady && _isDodging && !_canDodgeAttackCancel)
        {
            _queuedDodgeCounterAttack = true;
            LogLeftMouseInputDebug("Left mouse release queued DodgeCounterAttack: dodge cancel window not open yet.");
            LogDodgeCancelDebug("Dodge counter attack queued: cancel window is not open yet.");
            return;
        }

        if (_isDodging && !_canDodgeAttackCancel && CanStartRunAttackFromCurrentInput())
        {
            _queuedDodgeRunAttack = true;
            LogLeftMouseInputDebug("Left mouse release queued DodgeRunAttack: dodge cancel window not open yet.");
            LogDodgeCancelDebug("Dodge run attack queued: cancel window is not open yet.");
            return;
        }

        if (IsAttackInputBusy())
        {
            if (_canLightAttackCancel && IsAnimatorInLightAttack03State() && !_isBranchStartRequested)
            {
                LogLeftMouseInputDebug("Left mouse release consumed by normal next light attack from Light_Attack_03.");
                LogLightAttackDebug("Light_Attack_03 short release inside cancel window. Starting normal next light attack.");
                TryStartNextLightAttack();
            }
            else if (_canLightAttackCancel)
            {
                LogLeftMouseInputDebug("Left mouse release ignored: light combo cancel window requires press or hold.");
                LogLightAttackDebug("Left mouse release ignored for light combo: attack cancel window requires press or hold.");
            }
            else
            {
                LogLeftMouseInputDebug("Left mouse release routed to queue because attack input is busy.");
                TryQueueLightAttack();
            }
        }
        else
        {
            // 若 TryStartDodgeCounterAttack 因 Animator 过渡而推迟（返回 false 但设置了队列），
            // 不应继续尝试 TryStartRunAttack / TryStartLightAttack，
            // 避免 _queuedLightAttack 与 _queuedDodgeCounterAttack 同时为 true，
            // 导致 ProcessQueuedLightAttack 优先执行并触发 Attack01。
            bool counterQueued = _queuedDodgeCounterAttack;
            bool counterStarted = TryStartDodgeCounterAttack();
            LogLeftMouseInputDebug($"Left mouse release free-state resolution. counterQueuedBefore={counterQueued}, counterStarted={counterStarted}.");
            if (!counterStarted && !counterQueued && !TryStartRunAttack())
            {
                LogLeftMouseInputDebug("Left mouse release routed to TryStartLightAttack after counter/run checks.");
                TryStartLightAttack();
            }
        }

    }

    private void UpdateMoveDirection()
    {
        if (!_hasMoveInput)
        {
            _moveDirection = Vector3.zero;
            return;
        }

        if (cameraTransform == null)
        {
            _moveDirection = new Vector3(_moveInput.x, 0f, _moveInput.y).normalized;
            return;
        }

        Vector3 forward = cameraTransform.forward;
        Vector3 right = cameraTransform.right;
        forward.y = 0f;
        right.y = 0f;
        forward.Normalize();
        right.Normalize();

        Vector3 worldDirection = forward * _moveInput.y + right * _moveInput.x;
        _moveDirection = worldDirection.sqrMagnitude > 0.001f ? worldDirection.normalized : Vector3.zero;
    }

    private void UpdateActionTimers()
    {
        if (_isDodging)
        {
            _dodgeTimer -= Time.deltaTime;
            if (_dodgeTimer <= 0f)
            {
                _isDodging = false;
                _canDodgeAttackCancel = false;
                _queuedDodgeRunAttack = false;
            }
        }
        else
        {
            _dodgeTimer = 0f;
        }

        if (_parryLockTimer > 0f)
        {
            _parryLockTimer -= Time.deltaTime;
        }

        if (_isBranchSlowMotionActive && branchSlowMotionMaxDuration > 0f)
        {
            _branchSlowMotionTimer -= Time.unscaledDeltaTime;
            if (_branchSlowMotionTimer <= 0f)
            {
                LogBranchAttackDebug("Branch slow motion max duration reached.");
                EndBranchSlowMotion();
            }
        }
    }

    private void MoveCharacter()
    {
        float speed = _moveState switch
        {
            CombatMoveState.Jog => combatMoveSpeed,
            CombatMoveState.Boost => combatBoostSpeed,
            _ => 0f
        };

        if (_isDodging || ShouldLockScriptMovementForRootMotion())
        {
            speed = 0f;
        }
        else if (movementDebuffController != null)
        {
            speed *= movementDebuffController.MoveSpeedMultiplier;
        }

        Vector3 horizontalVelocity = _moveDirection * speed;
        _controller.Move(horizontalVelocity * Time.deltaTime);
    }

    private void OnAnimatorMove()
    {
        if (_animator == null || !ShouldApplyRootMotion())
        {
            return;
        }

        Vector3 delta = _animator.deltaPosition;
        _controller.Move(delta);
        transform.rotation *= _animator.deltaRotation;

        if (!logMovementDebug)
        {
            return;
        }

        if (Time.time < _nextMovementDebugTime)
        {
            return;
        }

        Debug.Log(
            $"[PlayerCombat][Movement] OnAnimatorMove " +
            $"time={Time.time:F3}, applyRootMotion={_animator.applyRootMotion}, " +
            $"locked={_isAttackLocked}, moveLocked={_isAttackMovementLocked}, hitRootMotion={_isHitReactionRootMotionActive}, hitLocked={_isHitReactionInputLocked}, dodging={_isDodging}, " +
            $"delta=({delta.x:F4},{delta.y:F4},{delta.z:F4}), " +
            $"deltaMag={delta.magnitude:F4}, rootRotDelta={_animator.deltaRotation.eulerAngles}",
            this);
    }

    private bool ShouldLockScriptMovementForRootMotion()
    {
        bool externalLock = movementLockController != null && movementLockController.IsMovementLocked;
        return _isDodging ||
            _isAttackMovementLocked ||
            _isBranchChargeWindowOpen ||
            _isBranchCharging ||
            _isHitReactionRootMotionActive ||
            _isHitReactionInputLocked ||
            externalLock ||
            ShouldLockParryActionMovement();
    }

    private bool ShouldLockParryActionMovement()
    {
        return IsAnimatorInParryActionState() && !_animator.GetBool(HashCanExitAttack);
    }

    private bool ShouldApplyRootMotion()
    {
        return _isDodging || _isAttackMovementLocked || _isHitReactionRootMotionActive;
    }

    private void AcquireAttackTarget()
    {
        if (attackTargetingSystem == null)
        {
            return;
        }

        attackTargetingSystem.AcquireAttackTarget();
    }

    private void AcquireDodgeCounterAttackTarget()
    {
        if (attackTargetingSystem == null)
        {
            return;
        }

        if (attackTargetingSystem.AcquireAttackTarget(dodgeCounterAttackSearchAngle))
        {
            attackTargetingSystem.SnapToCurrentTarget();
        }
    }

    private void AcquireParryCounterAttackTarget()
    {
        if (attackTargetingSystem == null)
        {
            return;
        }

        EnemyParryWindow parriedWindow = parryController != null ? parryController.LastParriedWindow : null;
        if (parriedWindow != null && attackTargetingSystem.TrySetAttackTargetFromTransform(parriedWindow.transform))
        {
            return;
        }

        if (attackTargetingSystem.AcquireAttackTarget(parryCounterAttackSearchAngle))
        {
            attackTargetingSystem.SnapToCurrentTarget();
        }
    }

    private void StartDodgeCounterAttackLunge()
    {
        if (!enableDodgeCounterAttackLunge || attackTargetingSystem == null || !attackTargetingSystem.HasTarget)
        {
            StopDodgeCounterAttackLunge();
            return;
        }

        _dodgeCounterAttackLungeTarget = attackTargetingSystem.CurrentTarget.TargetPoint;
        _dodgeCounterAttackLungeTimer = Mathf.Max(0.01f, dodgeCounterAttackLungeMaxDuration);
        _dodgeCounterAttackLungeMoved = 0f;
        StopParryCounterAttackLunge();
        _isDodgeCounterAttackLunging = true;
        LogDodgeCancelDebug($"Dodge counter attack lunge started. target={attackTargetingSystem.CurrentTarget.name}.");
    }

    private void UpdateDodgeCounterAttackLunge()
    {
        if (!_isDodgeCounterAttackLunging)
        {
            return;
        }

        if (_controller == null || _dodgeCounterAttackLungeTarget == null || !_isAttackMovementLocked)
        {
            StopDodgeCounterAttackLunge();
            return;
        }

        Vector3 toTarget = _dodgeCounterAttackLungeTarget.position - transform.position;
        toTarget.y = 0f;
        float distance = toTarget.magnitude;
        if (distance <= dodgeCounterAttackStopDistance || distance <= 0.001f)
        {
            StopDodgeCounterAttackLunge();
            return;
        }

        float deltaTime = Time.deltaTime;
        _dodgeCounterAttackLungeTimer -= deltaTime;

        float remainingDistance = Mathf.Max(0f, distance - dodgeCounterAttackStopDistance);
        float step = dodgeCounterAttackLungeSpeed * deltaTime;
        if (dodgeCounterAttackMaxMoveDistance > 0f)
        {
            step = Mathf.Min(step, Mathf.Max(0f, dodgeCounterAttackMaxMoveDistance - _dodgeCounterAttackLungeMoved));
        }

        step = Mathf.Min(step, remainingDistance);
        if (step <= 0.001f || _dodgeCounterAttackLungeTimer <= 0f)
        {
            StopDodgeCounterAttackLunge();
            return;
        }

        _controller.Move(toTarget.normalized * step);
        _dodgeCounterAttackLungeMoved += step;
    }

    private void StopDodgeCounterAttackLunge()
    {
        if (!_isDodgeCounterAttackLunging)
        {
            return;
        }

        _isDodgeCounterAttackLunging = false;
        _dodgeCounterAttackLungeTarget = null;
        _dodgeCounterAttackLungeTimer = 0f;
        _dodgeCounterAttackLungeMoved = 0f;
        LogDodgeCancelDebug("Dodge counter attack lunge stopped.");
    }

    private void StartParryCounterAttackLunge()
    {
        if (!enableParryCounterAttackLunge || attackTargetingSystem == null || !attackTargetingSystem.HasTarget)
        {
            StopParryCounterAttackLunge();
            return;
        }

        _parryCounterAttackLungeTarget = attackTargetingSystem.CurrentTarget.TargetPoint;
        _parryCounterAttackLungeTimer = Mathf.Max(0.01f, parryCounterAttackLungeMaxDuration);
        _parryCounterAttackLungeMoved = 0f;
        StopDodgeCounterAttackLunge();
        _isParryCounterAttackLunging = true;
        LogLightAttackDebug($"Parry counter attack lunge started. target={attackTargetingSystem.CurrentTarget.name}.");
    }

    private void UpdateParryCounterAttackLunge()
    {
        if (!_isParryCounterAttackLunging)
        {
            return;
        }

        if (_controller == null || _parryCounterAttackLungeTarget == null || !_isAttackMovementLocked)
        {
            StopParryCounterAttackLunge();
            return;
        }

        Vector3 toTarget = _parryCounterAttackLungeTarget.position - transform.position;
        toTarget.y = 0f;
        float distance = toTarget.magnitude;
        if (distance <= parryCounterAttackStopDistance || distance <= 0.001f)
        {
            StopParryCounterAttackLunge();
            return;
        }

        float deltaTime = Time.deltaTime;
        _parryCounterAttackLungeTimer -= deltaTime;

        float remainingDistance = Mathf.Max(0f, distance - parryCounterAttackStopDistance);
        float step = parryCounterAttackLungeSpeed * deltaTime;
        if (parryCounterAttackMaxMoveDistance > 0f)
        {
            step = Mathf.Min(step, Mathf.Max(0f, parryCounterAttackMaxMoveDistance - _parryCounterAttackLungeMoved));
        }

        step = Mathf.Min(step, remainingDistance);
        if (step <= 0.001f || _parryCounterAttackLungeTimer <= 0f)
        {
            StopParryCounterAttackLunge();
            return;
        }

        _controller.Move(toTarget.normalized * step);
        _parryCounterAttackLungeMoved += step;
    }

    private void StopParryCounterAttackLunge()
    {
        if (!_isParryCounterAttackLunging)
        {
            return;
        }

        _isParryCounterAttackLunging = false;
        _parryCounterAttackLungeTarget = null;
        _parryCounterAttackLungeTimer = 0f;
        _parryCounterAttackLungeMoved = 0f;
        LogLightAttackDebug("Parry counter attack lunge stopped.");
    }

    private void PlayDodgeCounterBlink()
    {
        if (!enableDodgeCounterBlink || dodgeCounterBlinkRenderers == null || dodgeCounterBlinkRenderers.Length == 0)
        {
            return;
        }

        RestoreDodgeCounterBlinkRenderers();
        _dodgeCounterBlinkRoutine = StartCoroutine(DodgeCounterBlinkRoutine());
    }

    private System.Collections.IEnumerator DodgeCounterBlinkRoutine()
    {
        float delay = Mathf.Max(0f, dodgeCounterBlinkDelay);
        if (delay > 0f)
        {
            yield return new WaitForSecondsRealtime(delay);
        }

        SetDodgeCounterBlinkRenderersVisible(false);

        float hiddenDuration = Mathf.Max(0f, dodgeCounterHiddenDuration);
        if (hiddenDuration > 0f)
        {
            yield return new WaitForSecondsRealtime(hiddenDuration);
        }

        SetDodgeCounterBlinkRenderersVisible(true);
        _dodgeCounterBlinkRoutine = null;
    }

    private void RestoreDodgeCounterBlinkRenderers()
    {
        if (_dodgeCounterBlinkRoutine != null)
        {
            StopCoroutine(_dodgeCounterBlinkRoutine);
            _dodgeCounterBlinkRoutine = null;
        }

        SetDodgeCounterBlinkRenderersVisible(true);
    }

    private void SetDodgeCounterBlinkRenderersVisible(bool visible)
    {
        if (dodgeCounterBlinkRenderers == null)
        {
            return;
        }

        for (int i = 0; i < dodgeCounterBlinkRenderers.Length; i++)
        {
            Renderer targetRenderer = dodgeCounterBlinkRenderers[i];
            if (targetRenderer == null)
            {
                continue;
            }

            targetRenderer.enabled = visible;
        }
    }

    private void FaceAttackTarget()
    {
        if (attackTargetingSystem == null)
        {
            return;
        }

        bool attackLocked = _isAttackLocked || _isAttackMovementLocked || _isBranchChargeWindowOpen || _isBranchCharging || IsAnimatorInAttackState();
        attackTargetingSystem.ClearAttackTargetIfInvalid(attackLocked);
        if (!attackLocked)
        {
            return;
        }

        attackTargetingSystem.FaceCurrentTarget(Time.deltaTime);
    }

    private void ClearAttackTarget()
    {
        StopDodgeCounterAttackLunge();
        StopParryCounterAttackLunge();

        if (attackTargetingSystem == null)
        {
            return;
        }

        attackTargetingSystem.ClearAttackTarget();
    }

    private bool IsAnimatorInAttackState()
    {
        if (_animator == null)
        {
            return false;
        }

        AnimatorStateInfo current = _animator.GetCurrentAnimatorStateInfo(0);
        if (current.IsTag(AttackStateTag))
        {
            return true;
        }

        if (!_animator.IsInTransition(0))
        {
            return false;
        }

        AnimatorStateInfo next = _animator.GetNextAnimatorStateInfo(0);
        return next.IsTag(AttackStateTag);
    }

    private bool IsAnimatorInParryActionState()
    {
        if (_animator == null)
        {
            return false;
        }

        AnimatorStateInfo current = _animator.GetCurrentAnimatorStateInfo(0);
        if (current.shortNameHash == HashParryState || current.shortNameHash == HashParryCounterAttackState)
        {
            return true;
        }

        if (!_animator.IsInTransition(0))
        {
            return false;
        }

        AnimatorStateInfo next = _animator.GetNextAnimatorStateInfo(0);
        return next.shortNameHash == HashParryState || next.shortNameHash == HashParryCounterAttackState;
    }

    private bool IsAnimatorInLightAttack03State()
    {
        if (_animator == null)
        {
            return false;
        }

        AnimatorStateInfo current = _animator.GetCurrentAnimatorStateInfo(0);
        if (current.shortNameHash == HashLightAttack03State)
        {
            return true;
        }

        if (!_animator.IsInTransition(0))
        {
            return false;
        }

        AnimatorStateInfo next = _animator.GetNextAnimatorStateInfo(0);
        return next.shortNameHash == HashLightAttack03State;
    }

    private bool IsAnimatorInBranchFinisherState()
    {
        if (_animator == null)
        {
            return false;
        }

        AnimatorStateInfo current = _animator.GetCurrentAnimatorStateInfo(0);
        if (IsBranchFinisherState(current))
        {
            return true;
        }

        if (!_animator.IsInTransition(0))
        {
            return false;
        }

        AnimatorStateInfo next = _animator.GetNextAnimatorStateInfo(0);
        return IsBranchFinisherState(next);
    }

    private bool IsBranchFinisherState(AnimatorStateInfo stateInfo)
    {
        return stateInfo.shortNameHash == HashKianaAttack4BranchState ||
            stateInfo.shortNameHash == HashKianaAttackQteState;
    }

    private bool IsAttackInputBusy()
    {
        bool externalAttackLock = movementLockController != null && movementLockController.IsAttackLocked;
        return _isAttackLocked || _isHitReactionInputLocked || _isBranchChargeWindowOpen || _isBranchCharging || IsAnimatorInAttackState() || externalAttackLock;
    }

    private bool CanContinueLightComboFromCurrentState()
    {
        if (_animator == null)
        {
            return false;
        }

        AnimatorStateInfo current = _animator.GetCurrentAnimatorStateInfo(0);
        if (CanLightComboStateContinue(current))
        {
            return true;
        }

        if (!_animator.IsInTransition(0))
        {
            return false;
        }

        AnimatorStateInfo next = _animator.GetNextAnimatorStateInfo(0);
        return CanLightComboStateContinue(next);
    }

    private bool CanLightComboStateContinue(AnimatorStateInfo stateInfo)
    {
        return stateInfo.shortNameHash == HashLightAttack01State ||
            stateInfo.shortNameHash == HashLightAttack02State ||
            stateInfo.shortNameHash == HashLightAttack03State ||
            stateInfo.shortNameHash == HashLightAttack04State ||
            stateInfo.shortNameHash == HashLightAttack05State ||
            stateInfo.shortNameHash == HashDodgeCounterAttackState ||
            stateInfo.shortNameHash == HashRunAttack01State;
    }

    private void UpdateAttackExitFallback()
    {
        if (_animator == null || attackExitFallbackNormalizedTime <= 0f || _animator.GetBool(HashCanExitAttack))
        {
            return;
        }

        AnimatorStateInfo current = _animator.GetCurrentAnimatorStateInfo(0);
        if (!current.IsTag(AttackStateTag) || _animator.IsInTransition(0))
        {
            return;
        }

        if (current.normalizedTime < attackExitFallbackNormalizedTime)
        {
            return;
        }

        _isAttackMovementLocked = false;
        _animator.SetBool(HashCanExitAttack, true);
        LogLightAttackDebug("Attack exit fallback set CanExitAttack true and unlocked movement.");
    }

    /// <summary>
    /// 攻击移动锁安全兜底：若 _isAttackMovementLocked 在 Animator 已离开攻击状态后仍为 true，
    /// 强制清除，防止 Root Motion 被持续应用到 Idle/Run 等非攻击动画，
    /// 导致角色在非攻击状态下被 Root Motion 拖着滑动。
    ///
    /// 触发条件：攻击动画被非战斗输入打断（如剧情过渡、外部状态机切换），
    /// 导致 AE_UnlockAttackMovement 事件来不及触发。
    /// 注意：不干预闪避状态（_isDodging=true 时 Root Motion 由闪避逻辑控制）。
    /// </summary>
    private void UpdateAttackMovementLockSafety()
    {
        if (!_isAttackMovementLocked || _isDodging)
        {
            return;
        }

        // 若仍在攻击状态或正在向攻击状态过渡，Root Motion 应继续应用，不干预
        if (IsAnimatorInAttackState() || _animator.IsInTransition(0))
        {
            return;
        }

        // _isAttackLocked=true 说明攻击动作刚刚在本帧启动（所有攻击启动函数均同帧
        // 同时设置这两个标志并发出 Animator trigger）。Animator 在同一帧内不处理
        // trigger，因此 IsAnimatorInAttackState() 此时仍为 false，不可误判为残留锁。
        // 必须等 _isAttackLocked 由 AE_AllowAttackCancel_start 或 AE_AllowAttackExit
        // 清除后，再判断移动锁是否还在攻击状态外残留。
        if (_isAttackLocked)
        {
            return;
        }

        // 到达这里说明：攻击输入锁已由动画事件正常清除（动作已过 cancel 窗口或结束），
        // 但 _isAttackMovementLocked 仍为 true，且 Animator 已不在攻击状态。
        // 典型原因：AE_UnlockAttackMovement 事件被漏放，或动画被意外打断导致事件未触发。
        // 强制清除，防止 Root Motion 被持续应用到 Idle/Run 等非攻击动画。
        _isAttackMovementLocked = false;
        LogLightAttackDebug("UpdateAttackMovementLockSafety: cleared stale attack movement lock. Animator left attack state without AE_UnlockAttackMovement.");
    }

    private void UpdateExternalMovementLockSafety()
    {
        if (movementLockController == null || !movementLockController.IsMovementLocked)
        {
            return;
        }

        if (_isDodging || _isHitReactionRootMotionActive || IsAnimatorInAttackState() || IsAnimatorInParryActionState())
        {
            return;
        }

        movementLockController.AE_UnlockMovement();
        LogLightAttackDebug("UpdateExternalMovementLockSafety: cleared stale external movement lock after Animator left attack/parry state.");
    }

    private void UpdateHitReactionRootMotionFallback()
    {
        if (!_isHitReactionRootMotionActive)
        {
            return;
        }

        _hitReactionRootMotionTimer -= Time.deltaTime;
        if (_hitReactionRootMotionTimer > 0f)
        {
            return;
        }

        _isHitReactionRootMotionActive = false;
        _hitReactionRootMotionTimer = 0f;
        LogLightAttackDebug("UpdateHitReactionRootMotionFallback: hit reaction root motion ended by fallback timer.");
    }

    private void FaceMoveDirection()
    {
        if (_isDodging)
        {
            return;
        }

        Vector3 facingDirection = _moveDirection;
        if (!shouldFaceMoveDirection || facingDirection.sqrMagnitude <= 0.001f)
        {
            return;
        }

        float targetYaw = Mathf.Atan2(facingDirection.x, facingDirection.z) * Mathf.Rad2Deg;
        float currentYaw = transform.eulerAngles.y;
        float angleDelta = Mathf.Abs(Mathf.DeltaAngle(currentYaw, targetYaw));
        float turnSpeed = Mathf.Lerp(minTurnSpeed, maxTurnSpeed, angleDelta / 180f);
        float nextYaw = Mathf.MoveTowardsAngle(
            currentYaw,
            targetYaw,
            turnSpeed * Time.deltaTime);

        transform.rotation = Quaternion.Euler(0f, nextYaw, 0f);
    }

    private void UpdateAnimatorParameters()
    {
        _animator.SetBool(HashHasMoveInput, _hasMoveInput);
        _animator.SetInteger(HashMoveMode, (int)_moveState);
        _animator.SetInteger(HashCombatMoveMode, (int)_moveMode);
        _animator.SetFloat(HashCombatMoveMagnitude, _hasMoveInput ? _moveInput.magnitude : 0f);
        _animator.SetFloat(HashFocusValue, GetCurrentBattleWill());
    }

    private void TryStartCombatDodge()
    {
        if (!dodgeInputEnabled)
        {
            return;
        }

        if (dodgeCooldown != null && !dodgeCooldown.CanDodge)
        {
            LogDodgeCancelDebug($"TryStartCombatDodge rejected: dodge charges={dodgeCooldown.CurrentCharges}/{dodgeCooldown.MaxCharges}, nextChargeIn={dodgeCooldown.CooldownRemaining:F2}s.");
            return;
        }

        if (_isDodging || _parryLockTimer > 0f)
        {
            return;
        }

        CancelAttackForDefensiveAction();
        bool hasDirectionalDodgeInput = _moveInput.sqrMagnitude > 0.001f && _moveDirection.sqrMagnitude > 0.001f;
        int dodgeDirection = hasDirectionalDodgeInput ? DodgeDirectionForward : DodgeDirectionBack;
        SafeSetInteger(HashDodgeDirection, dodgeDirection);

        if (hasDirectionalDodgeInput)
        {
            float targetYaw = Mathf.Atan2(_moveDirection.x, _moveDirection.z) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, targetYaw, 0f);
        }
        _isDodging = true;
        _canDodgeAttackCancel = false;
        _queuedDodgeRunAttack = false;
        _queuedDodgeCounterAttack = false;
        _queuedParryCounterAttack = false;
        _dodgeTimer = dodgeDuration;
        SafeSetBool(HashCanExitDodge, false);
        _animator.ResetTrigger(HashParryTrigger);
        _animator.SetTrigger(HashDodgeTrigger);
        dodgeCooldown?.StartCooldown();
        DodgeStarted?.Invoke(this);
        LogDodgeCancelDebug($"Dodge trigger set and charge consumed. direction={(hasDirectionalDodgeInput ? "Forward" : "Back")}, charges={dodgeCooldown?.CurrentCharges}/{dodgeCooldown?.MaxCharges}, nextChargeIn={dodgeCooldown?.CooldownRemaining:F2}s.");
    }

    private void TryStartParry()
    {
        if (_isDodging || _parryLockTimer > 0f || (parryController != null && !parryController.CanStartParry))
        {
            return;
        }

        CancelAttackForDefensiveAction();
        if (parryController != null)
        {
            if (parryController.TryStartParry())
            {
                _parryLockTimer = parryLockDuration;
            }
        }
        else
        {
            _parryLockTimer = parryLockDuration;
            _animator.ResetTrigger(HashDodgeTrigger);
            _animator.SetTrigger(HashParryTrigger);
        }
    }

    private void CancelAttackForDefensiveAction()
    {
        _queuedLightAttack = false;
        _canLightAttackCancel = false;
        _isAttackLocked = false;
        _isAttackMovementLocked = false;
        _canDodgeAttackCancel = false;
        _queuedDodgeRunAttack = false;
        _queuedDodgeCounterAttack = false;
        _queuedParryCounterAttack = false;
        _dodgeCounterAttackReady = false;
        _parryCounterTriggerFrame = -1;
        SafeSetBool(HashCanExitDodge, false);
        _leftMouseHeld = false;
        ClearBranchAttackState(true);
        // 被命中 / 开始新的闪避或弹反时，必须关闭 perfect dodge 窗口，
        // 否则 _activePerfectDodgeWindowOwner（静态字段）会一直有效，
        // 之后每次敌人开 warning window 都会被误判为完美闪避，
        // 造成玩家没有闪避也能触发 DodgeCounterAttack。
        AE_ClosePerfectDodgeWindow();
        _animator.SetBool(HashCanExitAttack, false);
        _animator.ResetTrigger(HashLightAttackTrigger);
        _animator.ResetTrigger(HashLightComboNextTrigger);
        _animator.ResetTrigger(HashRunAttackTrigger);
        SafeResetTrigger(HashParryCounterAttackTrigger);
        SafeResetTrigger(HashDodgeCounterAttackTrigger);
        SafeResetTrigger(HashLightBranchStartTrigger);
        SafeResetTrigger(HashBranchAttackReleaseTrigger);
        ClearAttackTarget();
        // 攻击被防御动作中断时，同步重置外部 MovementLockController。
        // 若弹刀/闪避动画在 AE_UnlockMovement / AE_UnlockAttack 之前被打断，
        // 动画事件的 Unlock 调用将永远不会执行，导致移动/攻击 lock 旗标残留，
        // 下一次攻击或移动将被错误地锁定。
        movementLockController?.ResetLock();
    }

    public void OnCombatHealthEnemyHitReceived(EnemyAttackHitData hitData)
    {
        if (!enableEnemyHitReactionRootMotion || hitData == null)
        {
            return;
        }

        CancelAttackForDefensiveAction();
        _isDodging = false;
        _canDodgeAttackCancel = false;
        _isHitReactionRootMotionActive = true;
        _isHitReactionInputLocked = true;
        _hitReactionRootMotionTimer = hitData.HitReactionType == EnemyHitReactionType.Heavy
            ? hitHeavyRootMotionFallbackDuration
            : hitLightRootMotionFallbackDuration;
        SafeSetBool(HashCanExitHitReaction, false);
        movementLockController?.AE_LockMovement();
        movementLockController?.AE_LockAttack();
        ClearAttackTarget();
        LogLightAttackDebug($"Enemy hit reaction root motion started. reaction={hitData.HitReactionType}, fallback={_hitReactionRootMotionTimer:F2}");
    }

    public void OnCombatHealthEnemyHitRolledBack(EnemyAttackHitData hitData)
    {
        _isHitReactionRootMotionActive = false;
        _isHitReactionInputLocked = false;
        _hitReactionRootMotionTimer = 0f;
        SafeSetBool(HashCanExitHitReaction, true);
        movementLockController?.AE_UnlockMovement();
        movementLockController?.AE_UnlockAttack();
        LogLightAttackDebug(hitData != null
            ? $"Enemy hit rollback received. Cleared pending hit reaction root motion. attackId={hitData.AttackId}"
            : "Enemy hit rollback received. Cleared pending hit reaction root motion.");
    }

    public void AE_StartHitReaction()
    {
        _isHitReactionInputLocked = true;
        SafeSetBool(HashCanExitHitReaction, false);
        movementLockController?.AE_LockMovement();
        movementLockController?.AE_LockAttack();
        LogLightAttackDebug("AE_StartHitReaction called. CanExitHitReaction set false, movement/attack locked.");
    }

    public void AE_AllowHitReactionExit()
    {
        _isHitReactionInputLocked = false;
        SafeSetBool(HashCanExitHitReaction, true);
        movementLockController?.AE_UnlockMovement();
        movementLockController?.AE_UnlockAttack();
        LogLightAttackDebug("AE_AllowHitReactionExit called. CanExitHitReaction set true, movement/attack unlocked.");
    }

    public void AE_StartHitReactionRootMotion()
    {
        if (!enableEnemyHitReactionRootMotion)
        {
            return;
        }

        _isHitReactionRootMotionActive = true;
        _hitReactionRootMotionTimer = Mathf.Max(_hitReactionRootMotionTimer, hitHeavyRootMotionFallbackDuration);
        LogLightAttackDebug("AE_StartHitReactionRootMotion called.");
    }

    public void AE_EndHitReactionRootMotion()
    {
        _isHitReactionRootMotionActive = false;
        _hitReactionRootMotionTimer = 0f;
        LogLightAttackDebug("AE_EndHitReactionRootMotion called.");
    }

    private void OnPerfectDodgeConfirmed(EnemyAttackWarningWindow warningWindow, KianaCombatController dodger)
    {
        if (!enablePerfectDodgeCounterAttack || dodger != this)
        {
            return;
        }

        _dodgeCounterAttackReady = true;
        _queuedDodgeCounterAttack = false;
        LogDodgeCancelDebug(warningWindow != null
            ? $"Perfect dodge confirmed. Dodge counter attack readied. warning={warningWindow.name}."
            : "Perfect dodge confirmed. Dodge counter attack readied.");
    }

    private bool TryStartDodgeCounterAttack()
    {
        LogDodgeCancelDebug("TryStartDodgeCounterAttack called.");

        if (!enablePerfectDodgeCounterAttack || !_dodgeCounterAttackReady)
        {
            LogDodgeCancelDebug("TryStartDodgeCounterAttack rejected: no ready counter.");
            return false;
        }

        bool cancelingDodge = _canDodgeAttackCancel;
        if (IsAttackInputBusy() || (_isDodging && !cancelingDodge) || _parryLockTimer > 0f)
        {
            LogDodgeCancelDebug("TryStartDodgeCounterAttack rejected: attack busy, dodge cancel not open, or parry lock.");
            return false;
        }

        if (_animator.IsInTransition(0))
        {
            _queuedDodgeCounterAttack = true;
            LogDodgeCancelDebug("TryStartDodgeCounterAttack deferred: animator in active transition.");
            return false;
        }

        _dodgeCounterAttackReady = false;
        _queuedDodgeCounterAttack = false;
        _queuedDodgeRunAttack = false;
        _isAttackLocked = true;
        _isAttackMovementLocked = true;
        _isDodging = false;
        _canDodgeAttackCancel = false;
        _queuedLightAttack = false;
        _canLightAttackCancel = false;
        // 闪避反击动画会打断闪避动画，导致 AE_CloseDodgeAttackCancel 永远不会触发，
        // _activePerfectDodgeWindowOwner / _activePerfectDodgeEffectOwner 将滞留为 this。
        // 若不在此处主动清理，敌人下次开 AttackWarningWindow 时 OpenWindow() 会直接将
        // ActivePerfectDodgeWindowOwner 视为有效，误判为完美闪避并触发女巫时间。
        AE_ClosePerfectDodgeWindow();
        AcquireDodgeCounterAttackTarget();
        StartDodgeCounterAttackLunge();
        PlayDodgeCounterBlink();
        _animator.SetBool(HashCanExitAttack, false);
        _animator.ResetTrigger(HashLightAttackTrigger);
        _animator.ResetTrigger(HashLightComboNextTrigger);
        _animator.ResetTrigger(HashRunAttackTrigger);
        SafeSetTrigger(HashDodgeCounterAttackTrigger);
        _dodgeCounterTriggerFrame = Time.frameCount;
        _dodgeCounterTriggerUnscaledTime = Time.unscaledTime;
        LogDodgeCancelDebug("DodgeCounterAttack trigger requested.");
        return true;
    }

    private bool TryStartParryCounterAttack()
    {
        if (parryController == null || !parryController.CounterReady)
        {
            if (parryController != null && parryController.CounterPending)
            {
                _queuedParryCounterAttack = true;
                LogLightAttackDebug("Parry counter attack queued: counter window pending.");
                return true;
            }

            return false;
        }

        if (!HasAnimatorParameter(HashParryCounterAttackTrigger))
        {
            LogLightAttackDebug("Parry counter attack rejected: missing Animator trigger ParryCounterAttack.");
            return false;
        }

        bool canInterruptParryAction = allowParryCounterAttackToInterruptParryAction && IsAnimatorInParryActionState();
        if ((IsAttackInputBusy() && !canInterruptParryAction) || _isDodging || _parryLockTimer > 0f)
        {
            _queuedParryCounterAttack = true;
            LogLightAttackDebug("Parry counter attack queued: attack busy, dodging, or parry lock.");
            return true;
        }

        if (_animator.IsInTransition(0) && !canInterruptParryAction)
        {
            _queuedParryCounterAttack = true;
            LogLightAttackDebug("Parry counter attack queued: animator in active transition.");
            return true;
        }

        if (!parryController.TryConsumeCounter())
        {
            return false;
        }

        _isAttackLocked = true;
        _isAttackMovementLocked = true;
        _queuedLightAttack = false;
        _canLightAttackCancel = false;
        _leftMouseHeld = false;
        _queuedDodgeRunAttack = false;
        _queuedDodgeCounterAttack = false;
        _queuedParryCounterAttack = false;
        AcquireParryCounterAttackTarget();
        StartParryCounterAttackLunge();
        _animator.SetBool(HashCanExitAttack, false);
        _animator.ResetTrigger(HashLightAttackTrigger);
        _animator.ResetTrigger(HashLightComboNextTrigger);
        _animator.ResetTrigger(HashRunAttackTrigger);
        _animator.ResetTrigger(HashParryTrigger);
        SafeResetTrigger(HashDodgeCounterAttackTrigger);
        _parryCounterTriggerFrame = Time.frameCount;
        SafeSetTrigger(HashParryCounterAttackTrigger);
        LogLightAttackDebug("ParryCounterAttack trigger requested.");
        return true;
    }

    private bool TryStartLightAttack()
    {
        LogLightAttackDebug("TryStartLightAttack called.");

        if (_isDodging || _parryLockTimer > 0f)
        {
            LogLightAttackDebug("TryStartLightAttack rejected: dodging or parry lock.");
            return false;
        }

        // 外部攻击锁（如出生动作）期间完全屏蔽攻击，不进入缓冲队列
        if (movementLockController != null && movementLockController.IsAttackLocked)
        {
            LogLightAttackDebug("TryStartLightAttack rejected: external attack lock active.");
            return false;
        }

        if (IsAttackInputBusy())
        {
            LogLightAttackDebug("TryStartLightAttack converted to queue: attack input is busy.");
            TryQueueLightAttack();
            return false;
        }

        // 若 Animator 已处于活跃过渡，AnyState 过渡无法中断它；
        // 发送 LightAttack trigger 会被消耗但不触发状态切换，_isAttackLocked 将永久残留。
        // 降级为队列，等过渡完成后由 ProcessQueuedLightAttack 自动重试。
        if (_animator.IsInTransition(0))
        {
            LogLightAttackDebug("TryStartLightAttack deferred: animator in active transition. Queuing for retry.");
            _queuedLightAttack = true;
            return false;
        }

        _isAttackLocked = true;
        _isAttackMovementLocked = true;
        _queuedLightAttack = false;
        _canLightAttackCancel = false;
        AcquireAttackTarget();
        _animator.SetBool(HashCanExitAttack, false);
        _animator.ResetTrigger(HashLightComboNextTrigger);
        _animator.SetTrigger(HashLightAttackTrigger);
        LogLightAttackDebug("LightAttack trigger set.");
        return true;
    }

    private bool TryQueueLightAttack()
    {
        LogLightAttackDebug("TryQueueLightAttack called.");

        if (_isDodging || _parryLockTimer > 0f)
        {
            LogLightAttackDebug("TryQueueLightAttack rejected: dodging or parry lock.");
            return false;
        }

        if (!CanContinueLightComboFromCurrentState())
        {
            _queuedLightAttack = false;
            _animator.ResetTrigger(HashLightComboNextTrigger);
            LogLightAttackDebug("TryQueueLightAttack rejected: current attack has no next light combo.");
            return false;
        }

        _queuedLightAttack = true;
        if (_canLightAttackCancel)
        {
            LogLightAttackDebug("Light attack input received inside cancel window. Starting next light attack.");
            return TryStartNextLightAttack();
        }

        LogLightAttackDebug("Light attack queued.");
        return true;
    }

    private void UpdateKianaBranchAttackInput()
    {
        if (_isDodging || _parryLockTimer > 0f)
        {
            LogBranchAttackDebugThrottled(ref _nextBranchHoldDebugTime, "Branch input update skipped: dodging or parry lock.");
            return;
        }

        if (_isBranchCharging)
        {
            UpdateBranchCharge();
            return;
        }

        if (!_leftMouseHeld || _isBranchStartRequested || !IsAnimatorInLightAttack03State())
        {
            if (_branchLeftMouseHoldTimer > 0f)
            {
                LogBranchAttackDebug($"Light_Attack_03 branch hold timer reset. leftHeld={_leftMouseHeld}, startRequested={_isBranchStartRequested}, inLightAttack03={IsAnimatorInLightAttack03State()}.");
            }

            _branchLeftMouseHoldTimer = 0f;
            return;
        }

        _branchLeftMouseHoldTimer += Time.deltaTime;
        LogBranchAttackDebugThrottled(
            ref _nextBranchHoldDebugTime,
            $"Light_Attack_03 branch hold ticking. hold={_branchLeftMouseHoldTimer:F3}/{lightAttack03BranchHoldTime:F3}, cancelWindow={_canLightAttackCancel}.");

        if (_canLightAttackCancel && _branchLeftMouseHoldTimer >= lightAttack03BranchHoldTime)
        {
            TryStartLightAttack03Branch();
        }
    }

    private bool TryStartLightAttack03Branch()
    {
        if (_isBranchStartRequested || !IsAnimatorInLightAttack03State())
        {
            LogBranchAttackDebug($"TryStartLightAttack03Branch rejected. startRequested={_isBranchStartRequested}, inLightAttack03={IsAnimatorInLightAttack03State()}.");
            ReportBranchAttackScenarioEvent("TryStartLightAttack03BranchRejected");
            return false;
        }

        LogBranchAttackDebug("TryStartLightAttack03Branch accepted. Locking attack and setting LightBranchStart.");
        ReportBranchAttackScenarioEvent("TryStartLightAttack03BranchAccepted");
        _isBranchStartRequested = true;
        _queuedLightAttack = false;
        _canLightAttackCancel = false;
        _isAttackLocked = true;
        _isAttackMovementLocked = true;
        AcquireAttackTarget();
        _animator.SetBool(HashCanExitAttack, false);
        _animator.ResetTrigger(HashLightComboNextTrigger);
        SafeSetTrigger(HashLightBranchStartTrigger);
        LogBranchAttackDebug($"LightBranchStart trigger requested. canExitAttack={_animator.GetBool(HashCanExitAttack)}.");
        ReportBranchAttackScenarioEvent("LightBranchStartTriggerRequested");
        return true;
    }

    private void StartBranchCharge()
    {
        if (!_isBranchChargeWindowOpen || _isBranchCharging)
        {
            LogBranchAttackDebug($"StartBranchCharge rejected. windowOpen={_isBranchChargeWindowOpen}, charging={_isBranchCharging}.");
            ReportBranchAttackScenarioEvent("StartBranchChargeRejected");
            return;
        }

        _isBranchCharging = true;
        _branchChargePoints = 0f;
        _currentBranchAttackLevel = 0;
        _lastLoggedBranchAttackLevel = -1;
        _nextBranchChargeDebugTime = 0f;
        branchChargeVfx?.StartCharging();
        branchChargeVfx?.SetChargeLevel(0);
        LogBranchAttackDebug("Branch charge started by second left mouse hold. BranchAttackLevel reset to 0.");
        ReportBranchAttackScenarioEvent("StartBranchChargeAccepted");
    }

    private void UpdateBranchCharge()
    {
        _branchChargePoints = Mathf.Clamp(_branchChargePoints + branchChargeRate * Time.unscaledDeltaTime, 0f, GetMaxBattleWill());
        int branchLevel = ResolveAvailableBranchTier(_branchChargePoints);
        _currentBranchAttackLevel = branchLevel;
        LogBranchAttackDebugThrottled(
            ref _nextBranchChargeDebugTime,
            $"Branch charge ticking. charge={_branchChargePoints:F1}, level={_currentBranchAttackLevel}, battleWill={GetCurrentBattleWill():F1}/{GetMaxBattleWill():F1}, previewCost={GetPreviewBattleWillCost():F1}.");

        if (_currentBranchAttackLevel != _lastLoggedBranchAttackLevel)
        {
            _lastLoggedBranchAttackLevel = _currentBranchAttackLevel;
            SafeSetInteger(HashBranchAttackLevel, _currentBranchAttackLevel);
            branchChargeVfx?.SetChargeLevel(_currentBranchAttackLevel);
            LogBranchAttackDebug($"Branch charge level changed. BranchAttackLevel={_currentBranchAttackLevel}.");
            ReportBranchAttackScenarioEvent($"BranchChargeLevelChanged:{_currentBranchAttackLevel}");
        }
    }

    private void ReleaseBranchAttack()
    {
        int finalTier = Mathf.Max(0, _currentBranchAttackLevel);
        float spendAmount = finalTier * battleWillPerBranchLevel;
        LogBranchAttackDebug($"ReleaseBranchAttack called. finalTier={finalTier}, spend={spendAmount:F1}.");
        ReportBranchAttackScenarioEvent($"ReleaseBranchAttackCalled:tier={finalTier},spend={spendAmount:F1}");
        _leftMouseHeld = false;

        if (finalTier <= 0)
        {
            ClearBranchAttackState(false);
            branchChargeVfx?.CancelCharging();
            SafeSetBool(HashBranchChargeAvailable, false);
            _animator.SetBool(HashCanExitAttack, true);
            LogBranchAttackDebug("ReleaseBranchAttack cancelled: finalTier <= 0. CanExitAttack set true.");
            ReportBranchAttackScenarioEvent("ReleaseBranchAttackCancelled:TierZero");
            return;
        }

        if (spendBattleWillOnBranchRelease)
        {
            LogBranchAttackDebug($"Spending battle will for branch release. amount={spendAmount:F1}, before={GetCurrentBattleWill():F1}.");
            ReportBranchAttackScenarioEvent($"BranchSpendBattleWillBefore:amount={spendAmount:F1},current={GetCurrentBattleWill():F1}");
            SpendBattleWill(spendAmount);
            LogBranchAttackDebug($"Battle will spent for branch release. after={GetCurrentBattleWill():F1}.");
            ReportBranchAttackScenarioEvent($"BranchSpendBattleWillAfter:current={GetCurrentBattleWill():F1}");
        }

        _isAttackLocked = true;
        _isAttackMovementLocked = true;
        _animator.SetBool(HashCanExitAttack, false);
        _isBranchChargeWindowOpen = false;
        _isBranchCharging = false;
        EndBranchSlowMotion();
        SafeSetInteger(HashBranchAttackLevel, finalTier);
        SafeSetBool(HashBranchChargeAvailable, false);
        SafeSetTrigger(HashBranchAttackReleaseTrigger);
        branchChargeVfx?.StopCharging(true);
        RefreshBranchChargeVfxResourceReady(true);
        LogBranchAttackDebug($"BranchAttackRelease trigger requested. finalTier={finalTier}, CanExitAttack reset false.");
        ReportBranchAttackScenarioEvent($"BranchAttackReleaseTriggerRequested:tier={finalTier}");
    }

    private int ResolveDesiredBranchTier(float chargePoints)
    {
        if (battleWillPerBranchLevel <= 0f || chargePoints <= branchChargeDeadZone)
        {
            return 0;
        }

        int level = Mathf.FloorToInt(chargePoints / battleWillPerBranchLevel) + 1;
        return Mathf.Clamp(level, 1, Mathf.Max(1, maxBranchAttackLevel));
    }

    private int ResolveAvailableBranchTier(float chargePoints)
    {
        int desiredTier = ResolveDesiredBranchTier(chargePoints);
        int availableTier = battleWillPerBranchLevel <= 0f ? 0 : Mathf.FloorToInt(GetCurrentBattleWill() / battleWillPerBranchLevel);
        return Mathf.Clamp(Mathf.Min(desiredTier, availableTier), 0, Mathf.Max(1, maxBranchAttackLevel));
    }

    private void ClearBranchAttackState(bool resetAnimatorParameters)
    {
        _isBranchStartRequested = false;
        _isBranchChargeWindowOpen = false;
        _isBranchCharging = false;
        _branchLeftMouseHoldTimer = 0f;
        _branchChargePoints = 0f;
        _currentBranchAttackLevel = 0;
        _lastLoggedBranchAttackLevel = -1;
        _nextBranchHoldDebugTime = 0f;
        _nextBranchChargeDebugTime = 0f;
        EndBranchSlowMotion();

        if (resetAnimatorParameters)
        {
            SafeSetBool(HashBranchChargeAvailable, false);
            SafeSetInteger(HashBranchAttackLevel, 0);
            SafeResetTrigger(HashLightBranchStartTrigger);
            SafeResetTrigger(HashBranchAttackReleaseTrigger);
        }

        RefreshBranchChargeVfxResourceReady(true);
        LogBranchAttackDebug($"Branch attack state cleared. resetAnimatorParameters={resetAnimatorParameters}.");
        ReportBranchAttackScenarioEvent($"BranchAttackStateCleared:resetAnimatorParameters={resetAnimatorParameters}");
    }

    private void CancelBranchChargeWindowByMoveInput()
    {
        LogBranchAttackDebug("Branch slow motion cancelled by move input.");
        ReportBranchAttackScenarioEvent("BranchSlowMotionCancelledByMoveInput");

        ClearBranchAttackState(false);
        SafeSetBool(HashBranchChargeAvailable, false);
        SafeSetInteger(HashBranchAttackLevel, 0);
        _animator.SetBool(HashCanExitAttack, true);
        branchChargeVfx?.CancelCharging();
        RefreshBranchChargeVfxResourceReady(true);
    }

    private void ClearBranchAttackReleaseStateIfNeeded(string source)
    {
        bool hasBranchLevel = _currentBranchAttackLevel > 0 || GetAnimatorInteger(HashBranchAttackLevel) > 0;
        if (!hasBranchLevel)
        {
            return;
        }

        if (!IsAnimatorInBranchFinisherState())
        {
            LogBranchAttackDebug($"{source}: branch level clear skipped because current state is not branch finisher yet.");
            return;
        }

        _currentBranchAttackLevel = 0;
        _lastLoggedBranchAttackLevel = -1;
        _branchChargePoints = 0f;
        _isBranchStartRequested = false;
        _isBranchChargeWindowOpen = false;
        _isBranchCharging = false;
        SafeSetBool(HashBranchChargeAvailable, false);
        SafeSetInteger(HashBranchAttackLevel, 0);
        SafeResetTrigger(HashBranchAttackReleaseTrigger);
        RefreshBranchChargeVfxResourceReady(true);
        LogBranchAttackDebug($"{source}: branch finisher reached exit phase. BranchAttackLevel cleared.");
        ReportBranchAttackScenarioEvent($"BranchFinisherExitClear:{source}");
    }

    private void BeginBranchSlowMotion()
    {
        if (!useBranchSlowMotion || _isBranchSlowMotionActive)
        {
            return;
        }

        _timeScaleBeforeBranchSlowMotion = Time.timeScale;
        _fixedDeltaTimeBeforeBranchSlowMotion = Time.fixedDeltaTime;
        Time.timeScale = branchSlowMotionTimeScale;
        Time.fixedDeltaTime = _fixedDeltaTimeBeforeBranchSlowMotion * branchSlowMotionTimeScale;
        _branchSlowMotionTimer = branchSlowMotionMaxDuration;
        _isBranchSlowMotionActive = true;
        LogBranchAttackDebug("Branch slow motion started.");
    }

    private void EndBranchSlowMotion()
    {
        if (!_isBranchSlowMotionActive)
        {
            return;
        }

        float restoreScale = _timeScaleBeforeBranchSlowMotion <= 0f ? 1f : _timeScaleBeforeBranchSlowMotion;
        Time.timeScale = restoreScale;
        Time.fixedDeltaTime = _fixedDeltaTimeBeforeBranchSlowMotion;
        _isBranchSlowMotionActive = false;
        _branchSlowMotionTimer = 0f;
        LogBranchAttackDebug("Branch slow motion ended.");
    }

    private bool TryStartRunAttack()
    {
        LogLightAttackDebug("TryStartRunAttack called.");
        LogDodgeCancelDebug("TryStartRunAttack called.");

        bool cancelingDodge = _canDodgeAttackCancel;
        LogDodgeCancelDebug($"TryStartRunAttack state check. cancelingDodge={cancelingDodge}.");
        if (IsAttackInputBusy() || (_isDodging && !cancelingDodge) || _parryLockTimer > 0f)
        {
            LogLightAttackDebug("TryStartRunAttack rejected: attack, dodge, or parry lock.");
            LogDodgeCancelDebug("TryStartRunAttack rejected: attack busy, dodge cancel not open, or parry lock.");
            return false;
        }

        if (!CanStartRunAttackFromCurrentInput())
        {
            LogLightAttackDebug("TryStartRunAttack rejected: not boosting.");
            LogDodgeCancelDebug("TryStartRunAttack rejected: not boosting.");
            return false;
        }

        _isAttackLocked = true;
        _isAttackMovementLocked = true;
        _isDodging = false;
        _canDodgeAttackCancel = false;
        _queuedDodgeRunAttack = false;
        _queuedDodgeCounterAttack = false;
        _queuedParryCounterAttack = false;
        _queuedLightAttack = false;
        _canLightAttackCancel = false;
        AcquireAttackTarget();
        _animator.SetBool(HashCanExitAttack, false);
        _animator.ResetTrigger(HashLightComboNextTrigger);
        _animator.SetTrigger(HashRunAttackTrigger);
        LogLightAttackDebug("RunAttack trigger set.");
        LogDodgeCancelDebug("RunAttack trigger set successfully.");
        return true;
    }

    private bool CanStartRunAttackFromCurrentInput()
    {
        // 同时要求有实际的移动方向，防止 moveInputGraceTime 宽容期内
        // WASD 已松开但 _hasMoveInput 仍为 true 时触发 Run Attack，
        // 导致 _moveDirection=zero、角色原地打出跑步攻击的异常表现。
        return _hasMoveInput && _moveState == CombatMoveState.Boost && _moveDirection.sqrMagnitude > 0.001f;
    }

    private void SpendBattleWill(float amount)
    {
        TrySpendBattleWill(amount);
    }

    public void AE_AllowAttackCancel_start()
    {
        OpenAttackCancelWindow("AE_AllowAttackCancel_start");
    }

    public void AE_AllowAttackCancel_end()
    {
        CloseAttackCancelWindow("AE_AllowAttackCancel_end");
    }

    private void OpenAttackCancelWindow(string eventName)
    {
        LogLightAttackDebug($"{eventName} called.");
        _isAttackLocked = false;
        _canLightAttackCancel = true;

        bool hadQueuedLightAttack = _queuedLightAttack;
        bool hasLiveLightAttackInput = IsLightAttackInputPressedNow();
        _queuedLightAttack = false;

        if (hasLiveLightAttackInput)
        {
            if (IsAnimatorInLightAttack03State())
            {
                if (_branchLeftMouseHoldTimer >= lightAttack03BranchHoldTime)
                {
                    LogBranchAttackDebug($"{eventName}: Light_Attack_03 hold threshold already reached. Starting branch.");
                    TryStartLightAttack03Branch();
                    return;
                }

                LogBranchAttackDebug($"{eventName}: Light_Attack_03 live input held. Waiting for branch hold threshold.");
                return;
            }

            if (!CanContinueLightComboFromCurrentState())
            {
                _animator.ResetTrigger(HashLightComboNextTrigger);
                LogLightAttackDebug("Live light attack input ignored: current attack has no next light combo.");
                return;
            }

            LogLightAttackDebug($"Live light attack input found at cancel point. Starting next light attack. hadQueued={hadQueuedLightAttack}.");
            TryStartNextLightAttack();
            return;
        }

        LogLightAttackDebug($"Attack cancel window opened. Waiting for live light attack input. hadQueued={hadQueuedLightAttack}.");
    }

    private void CloseAttackCancelWindow(string eventName)
    {
        _canLightAttackCancel = false;
        _queuedLightAttack = false;
        LogLightAttackDebug($"{eventName} called. Attack cancel window closed.");
    }

    private bool IsLightAttackInputPressedNow()
    {
        Mouse mouse = Mouse.current;
        return mouse != null && mouse.leftButton.isPressed;
    }

    private bool HasAnimatorParameter(int parameterHash)
    {
        if (_animator == null)
        {
            return false;
        }

        foreach (AnimatorControllerParameter parameter in _animator.parameters)
        {
            if (parameter.nameHash == parameterHash)
            {
                return true;
            }
        }

        return false;
    }

    private void SafeSetBool(int parameterHash, bool value)
    {
        if (HasAnimatorParameter(parameterHash))
        {
            _animator.SetBool(parameterHash, value);
            LogBranchAttackDebug($"Animator bool set. parameter={GetAnimatorParameterName(parameterHash)}, value={value}.");
        }
        else
        {
            LogBranchAttackDebug($"Animator bool parameter missing. hash={parameterHash}, value={value}.");
        }
    }

    private void SafeSetInteger(int parameterHash, int value)
    {
        if (HasAnimatorParameter(parameterHash))
        {
            _animator.SetInteger(parameterHash, value);
            LogBranchAttackDebug($"Animator int set. parameter={GetAnimatorParameterName(parameterHash)}, value={value}.");
        }
        else
        {
            LogBranchAttackDebug($"Animator int parameter missing. hash={parameterHash}, value={value}.");
        }
    }

    private int GetAnimatorInteger(int parameterHash)
    {
        if (HasAnimatorParameter(parameterHash))
        {
            return _animator.GetInteger(parameterHash);
        }

        return 0;
    }

    private void SafeSetTrigger(int parameterHash)
    {
        if (HasAnimatorParameter(parameterHash))
        {
            _animator.SetTrigger(parameterHash);
            LogBranchAttackDebug($"Animator trigger set. parameter={GetAnimatorParameterName(parameterHash)}.");
        }
        else
        {
            LogBranchAttackDebug($"Animator parameter missing for trigger hash={parameterHash}.");
        }
    }

    private void SafeResetTrigger(int parameterHash)
    {
        if (HasAnimatorParameter(parameterHash))
        {
            _animator.ResetTrigger(parameterHash);
            LogBranchAttackDebug($"Animator trigger reset. parameter={GetAnimatorParameterName(parameterHash)}.");
        }
    }

    private string GetAnimatorParameterName(int parameterHash)
    {
        if (_animator == null)
        {
            return parameterHash.ToString();
        }

        foreach (AnimatorControllerParameter parameter in _animator.parameters)
        {
            if (parameter.nameHash == parameterHash)
            {
                return parameter.name;
            }
        }

        return parameterHash.ToString();
    }

    public void AE_AllowAttackExit()
    {
        // 若 Animator 正在向攻击状态过渡（如连招内 RunAttack_01→02、LightAttack_04→05），
        // 当前 AE_AllowAttackExit 属于源状态的事件，不应在此时将 CanExitAttack 设为 true。
        // 否则目标攻击状态一进入就会因 CanExitAttack=true 且 hasMoveInput=true 而立刻退出，
        // 表现为攻击动作刚抬起手就切回了跑步。
        // 目标攻击状态会在自身对应的 AE_AllowAttackExit 时机正确设置该标志。
        if (_animator.IsInTransition(0))
        {
            AnimatorStateInfo next = _animator.GetNextAnimatorStateInfo(0);
            if (next.IsTag(AttackStateTag))
            {
                LogLightAttackDebug("AE_AllowAttackExit skipped: transitioning to attack state. CanExitAttack will be set by the next attack's own event.");
                return;
            }
        }

        _isAttackLocked = false;
        _canLightAttackCancel = false;
        _queuedLightAttack = false;
        _animator.ResetTrigger(HashLightComboNextTrigger);
        ClearBranchAttackReleaseStateIfNeeded("AE_AllowAttackExit");
        _animator.SetBool(HashCanExitAttack, true);
        LogLightAttackDebug("AE_AllowAttackExit called. CanExitAttack set true.");
    }

    public void AE_StartBranchChargeWindow()
    {
        LogBranchAttackDebug("AE_StartBranchChargeWindow called.");
        ReportBranchAttackScenarioEvent("AE_StartBranchChargeWindow");
        _isBranchStartRequested = true;
        _leftMouseHeld = false;
        _branchChargePoints = 0f;
        _currentBranchAttackLevel = 0;
        _lastLoggedBranchAttackLevel = -1;
        SafeSetInteger(HashBranchAttackLevel, 0);

        if (GetCurrentBattleWill() < branchMinimumBattleWill)
        {
            LogBranchAttackDebug($"Branch charge skipped: battle will below minimum. current={GetCurrentBattleWill():F1}, required={branchMinimumBattleWill:F1}.");
            ReportBranchAttackScenarioEvent($"BranchChargeUnavailable:current={GetCurrentBattleWill():F1},required={branchMinimumBattleWill:F1}");
            ClearBranchAttackState(false);
            branchChargeVfx?.CancelCharging();
            SafeSetBool(HashBranchChargeAvailable, false);
            _isAttackLocked = false;
            _isAttackMovementLocked = false;
            _animator.SetBool(HashCanExitAttack, true);
            LogBranchAttackDebug("Branch charge unavailable. CanExitAttack set true, attack/movement locks cleared.");
            return;
        }

        _isBranchChargeWindowOpen = true;
        _isBranchCharging = false;
        _isAttackLocked = false;
        SafeSetBool(HashBranchChargeAvailable, true);
        branchChargeVfx?.ShowChargeAvailable();
        BeginBranchSlowMotion();
        LogBranchAttackDebug("Branch charge window opened. Waiting for second left mouse hold.");
        ReportBranchAttackScenarioEvent("BranchChargeWindowOpened");
    }

    public void AE_EndBranchChargeWindow()
    {
        LogBranchAttackDebug("AE_EndBranchChargeWindow called.");
        ReportBranchAttackScenarioEvent("AE_EndBranchChargeWindow");
        if (_isBranchCharging)
        {
            LogBranchAttackDebug("AE_EndBranchChargeWindow found active branch charge. Releasing branch attack from animation event.");
            ReportBranchAttackScenarioEvent("AE_EndBranchChargeWindowReleasingActiveCharge");
            ReleaseBranchAttack();
            return;
        }

        if (!_isBranchChargeWindowOpen)
        {
            LogBranchAttackDebug("AE_EndBranchChargeWindow ignored: branch window is already closed, likely after branch release.");
            ReportBranchAttackScenarioEvent("AE_EndBranchChargeWindowIgnored:WindowAlreadyClosed");
            return;
        }

        ClearBranchAttackState(false);
        SafeSetBool(HashBranchChargeAvailable, false);
        _animator.SetBool(HashCanExitAttack, true);
        LogBranchAttackDebug("Branch charge window ended without active charge. CanExitAttack set true.");
        ReportBranchAttackScenarioEvent("BranchChargeWindowEndedWithoutCharge");
    }

    public void AE_UnlockAttackMovement()
    {
        // 若 Animator 正在向攻击状态过渡（如连招内 02→03、03→04），
        // _isAttackMovementLocked 已由 TryStartNextLightAttack 为下一段攻击设为 true。
        // 此时不清除该标志，确保下一段攻击的 root motion 位移能正常应用。
        if (_animator.IsInTransition(0))
        {
            AnimatorStateInfo next = _animator.GetNextAnimatorStateInfo(0);
            if (next.IsTag(AttackStateTag))
            {
                LogLightAttackDebug("AE_UnlockAttackMovement skipped: transitioning to attack state. Keeping movement locked for next attack.");
                return;
            }
        }

        _isAttackMovementLocked = false;
        ClearBranchAttackReleaseStateIfNeeded("AE_UnlockAttackMovement");
        ClearAttackTarget();
        LogLightAttackDebug("AE_UnlockAttackMovement called. Attack movement unlocked.");
    }

    public void AE_AllowDodgeAttackCancel()
    {
        if (!_isDodging)
        {
            return;
        }

        _canDodgeAttackCancel = true;
        _isDodging = false;
        _dodgeTimer = 0f;
        LogLightAttackDebug("AE_AllowDodgeAttackCancel called. Dodge can cancel into run attack.");
        LogDodgeCancelDebug("AE_AllowDodgeAttackCancel called. Dodge movement lock released and can cancel into run attack.");

        if (_queuedDodgeRunAttack)
        {
            if (_dodgeCounterAttackReady)
            {
                _queuedDodgeCounterAttack = true;
                _queuedDodgeRunAttack = false;
                LogDodgeCancelDebug("Queued dodge run attack converted to dodge counter attack because perfect dodge counter is ready.");
            }
            else
            {
                LogDodgeCancelDebug("Queued dodge run attack found. Trying run attack now.");
                TryStartRunAttack();
            }
        }

        if (_queuedDodgeCounterAttack)
        {
            LogDodgeCancelDebug("Queued dodge counter attack found. Trying counter attack now.");
            TryStartDodgeCounterAttack();
        }
    }

    public void AE_AllowDodgeMoveExit()
    {
        SafeSetBool(HashCanExitDodge, true);
        LogDodgeCancelDebug("AE_AllowDodgeMoveExit called. CanExitDodge set true.");
    }

    public void AE_CloseDodgeMoveExit()
    {
        SafeSetBool(HashCanExitDodge, false);
        LogDodgeCancelDebug("AE_CloseDodgeMoveExit called. CanExitDodge set false.");
    }

    public void AE_OpenPerfectDodgeWindow()
    {
        _activePerfectDodgeWindowOwner = this;
        PerfectDodgeWindowOpened?.Invoke(this);
        LogDodgeCancelDebug("AE_OpenPerfectDodgeWindow called. Perfect dodge window opened.");
    }

    public void AE_ClosePerfectDodgeWindow()
    {
        if (_activePerfectDodgeWindowOwner == this)
        {
            _activePerfectDodgeWindowOwner = null;
        }

        if (_activePerfectDodgeEffectOwner == this)
        {
            _activePerfectDodgeEffectOwner = null;
        }

        PerfectDodgeWindowClosed?.Invoke(this);
        LogDodgeCancelDebug("AE_ClosePerfectDodgeWindow called. Perfect dodge window closed.");
    }

    public void AE_AllowPerfectDodgeEffect()
    {
        _activePerfectDodgeEffectOwner = this;
        PerfectDodgeEffectAllowed?.Invoke(this);
        LogDodgeCancelDebug("AE_AllowPerfectDodgeEffect called. Perfect dodge effect can start.");
    }

    public void AE_CloseDodgeAttackCancel()
    {
        AE_ClosePerfectDodgeWindow();
        _canDodgeAttackCancel = false;
        _queuedDodgeRunAttack = false;
        _queuedDodgeCounterAttack = false;
        SafeSetBool(HashCanExitDodge, false);

        // 闪避取消窗口关闭意味着闪避动作的有效输入期已结束。
        // 若反击机会此时还未被消费（如玩家碰了方向键过渡到了 walk/run_combat），
        // 清除反击就绪标志和 Animator trigger，防止后续普通攻击输入时意外触发反击。
        if (_dodgeCounterAttackReady)
        {
            _dodgeCounterAttackReady = false;
            SafeResetTrigger(HashDodgeCounterAttackTrigger);
            LogDodgeCancelDebug("Dodge counter attack ready cleared on cancel window close: counter attack window expired.");
        }

        LogLightAttackDebug("AE_CloseDodgeAttackCancel called. Dodge attack cancel closed.");
        LogDodgeCancelDebug("AE_CloseDodgeAttackCancel called. Dodge attack cancel closed.");
    }

    private bool TryStartNextLightAttack()
    {
        if (_isDodging || _parryLockTimer > 0f)
        {
            LogLightAttackDebug("TryStartNextLightAttack rejected: dodging or parry lock.");
            return false;
        }

        if (!CanContinueLightComboFromCurrentState())
        {
            _isAttackLocked = false;
            _queuedLightAttack = false;
            _animator.ResetTrigger(HashLightComboNextTrigger);
            LogLightAttackDebug("TryStartNextLightAttack rejected: current attack has no next light combo.");
            return false;
        }

        // 仅当过渡目标不是攻击状态时才延迟（如 Light_Attack_05 → Walk_Combat）。
        // 若目标仍是攻击状态（如连招内 03→04 过渡），则正常执行，确保 _isAttackMovementLocked
        // 得到正确设置，否则攻击动画的 root motion 位移将无法应用。
        if (_animator.IsInTransition(0))
        {
            AnimatorStateInfo next = _animator.GetNextAnimatorStateInfo(0);
            if (!next.IsTag(AttackStateTag))
            {
                LogLightAttackDebug("TryStartNextLightAttack deferred: transitioning to non-attack state. Queuing for retry.");
                _queuedLightAttack = true;
                return false;
            }
        }

        _isAttackLocked = true;
        _isAttackMovementLocked = true;
        _queuedLightAttack = false;
        _canLightAttackCancel = false;
        AcquireAttackTarget();
        _animator.SetBool(HashCanExitAttack, false);
        _animator.ResetTrigger(HashLightAttackTrigger);
        _animator.SetTrigger(HashLightComboNextTrigger);
        LogLightAttackDebug("LightComboNext trigger set.");
        return true;
    }

    public void AE_ForceResetAttackState()
    {
        LogLightAttackDebug("AE_ForceResetAttackState called.");
        _queuedLightAttack = false;
        _canLightAttackCancel = false;
        _isAttackLocked = false;
        _isAttackMovementLocked = false;
        _canDodgeAttackCancel = false;
        _queuedDodgeRunAttack = false;
        _queuedDodgeCounterAttack = false;
        _queuedParryCounterAttack = false;
        _dodgeCounterAttackReady = false;
        _isHitReactionRootMotionActive = false;
        _isHitReactionInputLocked = false;
        _parryCounterTriggerFrame = -1;
        _hitReactionRootMotionTimer = 0f;
        AE_ClosePerfectDodgeWindow();
        ClearBranchAttackState(true);
        ClearAttackTarget();
        _animator.ResetTrigger(HashLightAttackTrigger);
        _animator.ResetTrigger(HashLightComboNextTrigger);
        _animator.ResetTrigger(HashRunAttackTrigger);
        SafeResetTrigger(HashParryCounterAttackTrigger);
        SafeResetTrigger(HashDodgeCounterAttackTrigger);
        _dodgeCounterTriggerFrame = -1;
        _dodgeCounterTriggerUnscaledTime = -999f;
        _animator.SetBool(HashCanExitAttack, false);
    }

    /// <summary>
    /// 动画事件：取消当前进行中的攻击状态。
    /// 用于受击动画开始时，确保被打断的攻击动画残留的 _isAttackLocked /
    /// _isAttackMovementLocked 等标志得到清除，防止受击结束后人物进入永久锁死状态。
    /// 请在受击动画的第一帧添加此事件。
    /// </summary>
    public void AE_CancelCurrentAttack()
    {
        LogLightAttackDebug("AE_CancelCurrentAttack called. Clearing interrupted attack state.");
        CancelAttackForDefensiveAction();
    }

    /// <summary>
    /// 每帧检查是否有因 Animator 过渡而被延迟的闪避反击队列需要重试。
    /// 若 <see cref="_queuedDodgeCounterAttack"/> 为 true 但 TryStartDodgeCounterAttack 之前因
    /// 过渡未结束而返回 false，本方法会在过渡结束后自动重新发起反击，避免退化为普通
    /// Attack01。执行成功后还会清除可能被错误设置的轻攻击队列，防止反击结束后出现残留攻击。
    /// </summary>
    private void ProcessQueuedDodgeCounterAttack()
    {
        if (!_queuedDodgeCounterAttack || !_dodgeCounterAttackReady)
        {
            return;
        }

        if (IsAttackInputBusy() || (_isDodging && !_canDodgeAttackCancel) || _parryLockTimer > 0f)
        {
            return;
        }

        if (_animator.IsInTransition(0))
        {
            return;
        }

        LogDodgeCancelDebug("ProcessQueuedDodgeCounterAttack: executing deferred counter attack after transition ended.");
        _queuedLightAttack = false;
        TryStartDodgeCounterAttack();
    }

    private void ProcessQueuedParryCounterAttack()
    {
        if (!_queuedParryCounterAttack)
        {
            return;
        }

        if (parryController == null || (!parryController.CounterPending && !parryController.CounterReady))
        {
            _queuedParryCounterAttack = false;
            LogLightAttackDebug("Queued parry counter attack cleared: counter window expired.");
            return;
        }

        bool canInterruptParryAction = allowParryCounterAttackToInterruptParryAction && IsAnimatorInParryActionState();
        if (!parryController.CounterReady ||
            (IsAttackInputBusy() && !canInterruptParryAction) ||
            _isDodging ||
            _parryLockTimer > 0f ||
            (_animator.IsInTransition(0) && !canInterruptParryAction))
        {
            return;
        }

        LogLightAttackDebug("Queued parry counter attack executing.");
        TryStartParryCounterAttack();
    }

    /// <summary>
    /// 弹刀反击锁死兜底：若 ParryCounterAttack trigger 成功发出后 Animator 未能触发过渡
    /// （最常见原因：Animator 过渡勾选了 Has Exit Time，导致 trigger 在未达到 Exit Time
    /// 时被静默消费，不触发任何状态切换），在弹刀反击窗口关闭后自动清除残留的攻击锁，
    /// 防止玩家因 _isAttackLocked 永久为 true 而丧失后续所有左键输入能力。
    /// </summary>
    private void UpdateDodgeCounterTransitionFallback()
    {
        if (_dodgeCounterTriggerFrame < 0 || _animator == null)
        {
            return;
        }

        if (Time.frameCount <= _dodgeCounterTriggerFrame)
        {
            return;
        }

        AnimatorStateInfo current = _animator.GetCurrentAnimatorStateInfo(0);
        if (current.shortNameHash == HashDodgeCounterAttackState)
        {
            _dodgeCounterTriggerFrame = -1;
            _dodgeCounterTriggerUnscaledTime = -999f;
            return;
        }

        if (Time.unscaledTime - _dodgeCounterTriggerUnscaledTime < dodgeCounterAttackTransitionFallbackDelay)
        {
            return;
        }

        bool transitioningToDodgeCounter = _animator.IsInTransition(0) &&
            _animator.GetNextAnimatorStateInfo(0).shortNameHash == HashDodgeCounterAttackState;
        if (transitioningToDodgeCounter)
        {
            if (_animator.speed <= 0.01f)
            {
                _animator.speed = 1f;
            }

            _animator.Play("Dodge_CounterAttack", 0, 0f);
            _dodgeCounterTriggerFrame = -1;
            _dodgeCounterTriggerUnscaledTime = -999f;
            LogDodgeCancelDebug("UpdateDodgeCounterTransitionFallback: forced Dodge_CounterAttack because Animator transition was stuck.");
            return;
        }

        _isAttackLocked = false;
        _isAttackMovementLocked = false;
        _queuedDodgeCounterAttack = false;
        _dodgeCounterAttackReady = false;
        StopDodgeCounterAttackLunge();
        SafeResetTrigger(HashDodgeCounterAttackTrigger);
        _dodgeCounterTriggerFrame = -1;
        _dodgeCounterTriggerUnscaledTime = -999f;
        LogDodgeCancelDebug("UpdateDodgeCounterTransitionFallback: cleared stale counter attack lock because Animator did not enter Dodge_CounterAttack.");
    }

    /// <summary>
    /// 弹刀反击锁死兜底：若 ParryCounterAttack trigger 成功发出后 Animator 未能触发过渡
    /// （最常见原因：Animator 过渡勾选了 Has Exit Time，导致 trigger 在未达到 Exit Time
    /// 时被静默消费，不触发任何状态切换），在弹刀反击窗口关闭后自动清除残留的攻击锁，
    /// 防止玩家因 _isAttackLocked 永久为 true 而丧失后续所有左键输入能力。
    /// </summary>
    private void UpdateParryCounterLockFallback()
    {
        if (!_isAttackLocked)
        {
            return;
        }

        // 若 trigger 尚未发出（帧号无效），不干预
        if (_parryCounterTriggerFrame < 0)
        {
            return;
        }

        // 若人物确实在攻击状态或处于过渡中，说明 trigger 正常生效，不干预
        // 同时清除帧号记录，避免后续误判
        if (IsAnimatorInAttackState() || _animator.IsInTransition(0))
        {
            _parryCounterTriggerFrame = -1;
            return;
        }

        // 若弹刀反击窗口仍处于 pending 或 ready 状态，说明 trigger 尚未发出，不干预
        if (parryController != null && (parryController.CounterPending || parryController.CounterReady))
        {
            return;
        }

        // Animator 在同一帧内不会处理 trigger——必须至少等一帧后再判断。
        // 否则 TryStartLightAttack / TryStartParryCounterAttack 刚设置锁并发出 trigger，
        // 本方法在同一个 Update 调用中就因 IsAnimatorInAttackState()=false 而误清除锁。
        if (Time.frameCount <= _parryCounterTriggerFrame)
        {
            return;
        }

        // 到达这里说明：trigger 已发出至少一帧，但 Animator 仍未进入攻击状态且无过渡，
        // 且弹刀窗口已关闭。trigger 被静默消费导致永久锁死，强制清除。
        _isAttackLocked = false;
        _isAttackMovementLocked = false;
        StopParryCounterAttackLunge();
        _parryCounterTriggerFrame = -1;
        LogLightAttackDebug("UpdateParryCounterLockFallback: cleared stale attack lock. Likely cause: ParryCounterAttack trigger silently consumed (Has Exit Time on Animator transition).");
    }

    /// <summary>
    /// 每帧检查是否有因活跃过渡被延迟的攻击队列需要重试。
    /// 当 Animator 过渡完全结束、攻击输入不繁忙时，自动消费队列并发起新攻击，
    /// 确保玩家在过渡边缘时序窗口内的输入不会丢失或造成永久锁死。
    /// </summary>
    private void ProcessQueuedLightAttack()
    {
        if (!_queuedLightAttack || IsAttackInputBusy() || _isDodging || _parryLockTimer > 0f)
        {
            return;
        }

        if (_animator.IsInTransition(0))
        {
            return;
        }

        _queuedLightAttack = false;
        LogLightAttackDebug("ProcessQueuedLightAttack: deferred attack executing after transition ended.");
        TryStartLightAttack();
    }

    private void LogBranchAttackDebug(string message)
    {
        if (!logBranchAttackDebug)
        {
            return;
        }

        AnimatorStateInfo current = _animator.GetCurrentAnimatorStateInfo(0);
        AnimatorStateInfo next = _animator.GetNextAnimatorStateInfo(0);
        bool inTransition = _animator.IsInTransition(0);
        int desiredTier = ResolveDesiredBranchTier(_branchChargePoints);
        int availableTier = battleWillPerBranchLevel <= 0f ? 0 : Mathf.FloorToInt(GetCurrentBattleWill() / battleWillPerBranchLevel);

        Debug.Log(
            $"[KianaCombat][BranchAttack] {message} " +
            $"time={Time.time:F3}, unscaledTime={Time.unscaledTime:F3}, " +
            $"leftHeld={_leftMouseHeld}, holdTimer={_branchLeftMouseHoldTimer:F3}, startRequested={_isBranchStartRequested}, " +
            $"windowOpen={_isBranchChargeWindowOpen}, charging={_isBranchCharging}, slowMotion={_isBranchSlowMotionActive}, " +
            $"branchCharge={_branchChargePoints:F1}, desiredTier={desiredTier}, availableTier={availableTier}, currentLevel={_currentBranchAttackLevel}, " +
            $"battleWill={GetCurrentBattleWill():F1}/{GetMaxBattleWill():F1}, previewCost={GetPreviewBattleWillCost():F1}, " +
            $"locked={_isAttackLocked}, moveLocked={_isAttackMovementLocked}, queuedLight={_queuedLightAttack}, cancelWindow={_canLightAttackCancel}, " +
            $"canExitAttack={_animator.GetBool(HashCanExitAttack)}, hasMoveInput={_hasMoveInput}, moveMode={_moveMode}, moveState={_moveState}, " +
            $"animStateHash={current.fullPathHash}, animShortHash={current.shortNameHash}, animNorm={current.normalizedTime:F3}, " +
            $"inTransition={inTransition}, nextStateHash={next.fullPathHash}, nextShortHash={next.shortNameHash}, nextNorm={next.normalizedTime:F3}",
            this);
    }

    private void LogBranchAttackDebugThrottled(ref float nextLogTime, string message)
    {
        if (!logBranchAttackDebug)
        {
            return;
        }

        if (Time.unscaledTime < nextLogTime)
        {
            return;
        }

        nextLogTime = Time.unscaledTime + Mathf.Max(0.02f, branchAttackDebugInterval);
        LogBranchAttackDebug(message);
    }

    private void ReportBranchAttackScenarioEvent(string message)
    {
        BranchAttackScenarioEvent?.Invoke(this, message);
    }

    private void LogLightAttackDebug(string message)
    {
        if (!logLightAttackDebug)
        {
            return;
        }

        AnimatorStateInfo current = _animator.GetCurrentAnimatorStateInfo(0);
        AnimatorStateInfo next = _animator.GetNextAnimatorStateInfo(0);
        bool inTransition = _animator.IsInTransition(0);

        Debug.Log(
            $"[PlayerCombat][LightAttack] {message} " +
            $"time={Time.time:F3}, " +
            $"locked={_isAttackLocked}, moveLocked={_isAttackMovementLocked}, queued={_queuedLightAttack}, cancelWindow={_canLightAttackCancel}, " +
            $"leftHeld={_leftMouseHeld}, " +
            $"canExitAttack={_animator.GetBool(HashCanExitAttack)}, " +
            $"hasMoveInput={_hasMoveInput}, moveMode={_moveMode}, moveState={_moveState}, " +
            $"animStateHash={current.fullPathHash}, animNorm={current.normalizedTime:F3}, " +
            $"inTransition={inTransition}, nextStateHash={next.fullPathHash}, nextNorm={next.normalizedTime:F3}",
            this);
    }

    private void LogLeftMouseInputDebug(string message)
    {
        if (!logLeftMouseInputDebug)
        {
            return;
        }

        AnimatorStateInfo current = _animator.GetCurrentAnimatorStateInfo(0);
        AnimatorStateInfo next = _animator.GetNextAnimatorStateInfo(0);
        bool inTransition = _animator.IsInTransition(0);
        Mouse mouse = Mouse.current;

        Debug.Log(
            $"[KianaCombat][LeftMouse] {message} " +
            $"time={Time.time:F3}, unscaledTime={Time.unscaledTime:F3}, frame={Time.frameCount}, " +
            $"mouseDown={(mouse != null && mouse.leftButton.isPressed)}, " +
            $"mousePressedThisFrame={(mouse != null && mouse.leftButton.wasPressedThisFrame)}, " +
            $"mouseReleasedThisFrame={(mouse != null && mouse.leftButton.wasReleasedThisFrame)}, " +
            $"leftHeld={_leftMouseHeld}, " +
            $"attackBusy={IsAttackInputBusy()}, locked={_isAttackLocked}, moveLocked={_isAttackMovementLocked}, " +
            $"queuedLight={_queuedLightAttack}, cancelWindow={_canLightAttackCancel}, " +
            $"dodging={_isDodging}, dodgeCancel={_canDodgeAttackCancel}, dodgeCounterReady={_dodgeCounterAttackReady}, " +
            $"queuedDodgeCounter={_queuedDodgeCounterAttack}, queuedDodgeRun={_queuedDodgeRunAttack}, " +
            $"parryLock={_parryLockTimer:F3}, queuedParryCounter={_queuedParryCounterAttack}, " +
            $"branchStartRequested={_isBranchStartRequested}, branchWindow={_isBranchChargeWindowOpen}, branchCharging={_isBranchCharging}, " +
            $"hasMoveInput={_hasMoveInput}, moveInput=({_moveInput.x:F2},{_moveInput.y:F2}), moveState={_moveState}, moveMode={_moveMode}, " +
            $"canExitAttack={_animator.GetBool(HashCanExitAttack)}, " +
            $"animStateHash={current.fullPathHash}, animShortHash={current.shortNameHash}, animNorm={current.normalizedTime:F3}, " +
            $"inTransition={inTransition}, nextStateHash={next.fullPathHash}, nextShortHash={next.shortNameHash}, nextNorm={next.normalizedTime:F3}",
            this);
    }

    private void LogDodgeCancelDebug(string message)
    {
        if (!logLightAttackDebug)
        {
            return;
        }

        AnimatorStateInfo current = _animator.GetCurrentAnimatorStateInfo(0);
        AnimatorStateInfo next = _animator.GetNextAnimatorStateInfo(0);
        bool inTransition = _animator.IsInTransition(0);

        Debug.Log(
            $"[PlayerCombat][DodgeCancel] {message} " +
            $"time={Time.time:F3}, " +
            $"dodging={_isDodging}, dodgeTimer={_dodgeTimer:F3}, canDodgeAttackCancel={_canDodgeAttackCancel}, " +
            $"queuedDodgeRunAttack={_queuedDodgeRunAttack}, leftHeld={_leftMouseHeld}, " +
            $"attackLocked={_isAttackLocked}, attackMoveLocked={_isAttackMovementLocked}, queuedLight={_queuedLightAttack}, attackCancelWindow={_canLightAttackCancel}, " +
            $"parryLock={_parryLockTimer:F3}, " +
            $"hasMoveInput={_hasMoveInput}, moveInput=({_moveInput.x:F2},{_moveInput.y:F2}), " +
            $"moveMode={_moveMode}, moveState={_moveState}, " +
            $"isAttackBusy={IsAttackInputBusy()}, canExitAttack={_animator.GetBool(HashCanExitAttack)}, " +
            $"animStateHash={current.fullPathHash}, animNorm={current.normalizedTime:F3}, " +
            $"inTransition={inTransition}, nextStateHash={next.fullPathHash}, nextNorm={next.normalizedTime:F3}",
            this);
    }

    private void LogMovementDebug()
    {
        if (!logMovementDebug)
        {
            _lastPosition = transform.position;
            return;
        }

        bool attackTagged = IsAnimatorInAttackState();
        if (!_isAttackLocked && !_isAttackMovementLocked && !_isDodging && !attackTagged)
        {
            _lastPosition = transform.position;
            return;
        }

        if (Time.time < _nextMovementDebugTime)
        {
            return;
        }

        _nextMovementDebugTime = Time.time + movementDebugInterval;

        Vector3 currentPosition = transform.position;
        Vector3 frameDelta = currentPosition - _lastPosition;
        _lastPosition = currentPosition;

        AnimatorStateInfo current = _animator.GetCurrentAnimatorStateInfo(0);
        bool rootMotionMoveLocked = ShouldLockScriptMovementForRootMotion();
        bool externalMovementLocked = movementLockController != null && movementLockController.IsMovementLocked;
        bool externalAttackLocked = movementLockController != null && movementLockController.IsAttackLocked;
        bool parryActionMoveBlocked = ShouldLockParryActionMovement();

        Debug.Log(
            $"[PlayerCombat][Movement] UpdateEnd " +
            $"time={Time.time:F3}, locked={_isAttackLocked}, moveLocked={_isAttackMovementLocked}, hitRootMotion={_isHitReactionRootMotionActive}, hitLocked={_isHitReactionInputLocked}, dodging={_isDodging}, " +
            $"externalMoveLocked={externalMovementLocked}, externalAttackLocked={externalAttackLocked}, parryActionMoveBlocked={parryActionMoveBlocked}, attackTagged={attackTagged}, rootMotionMoveLocked={rootMotionMoveLocked}, " +
            $"canExitAttack={_animator.GetBool(HashCanExitAttack)}, " +
            $"hasMoveInput={_hasMoveInput}, moveInput=({_moveInput.x:F2},{_moveInput.y:F2}), " +
            $"moveDir=({_moveDirection.x:F3},{_moveDirection.y:F3},{_moveDirection.z:F3}), " +
            $"moveState={_moveState}, moveMode={_moveMode}, " +
            $"controllerVelocity=({_controller.velocity.x:F3},{_controller.velocity.y:F3},{_controller.velocity.z:F3}), " +
            $"frameDelta=({frameDelta.x:F4},{frameDelta.y:F4},{frameDelta.z:F4}), " +
            $"frameDeltaMag={frameDelta.magnitude:F4}, " +
            $"applyRootMotion={_animator.applyRootMotion}, " +
            $"animStateHash={current.fullPathHash}, animNorm={current.normalizedTime:F3}, " +
            $"inTransition={_animator.IsInTransition(0)}",
            this);
    }

}
