using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInputScenarioLogger : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private KianaCombatController kianaController;
    [SerializeField] private Animator animator;
    [SerializeField] private CharacterController characterController;
    [SerializeField] private PlayerBattleWillSystem battleWillSystem;
    [SerializeField] private PlayerPoiseController poiseController;
    [SerializeField] private CombatHealth health;
    [SerializeField] private PlayerParryController parryController;
    [SerializeField] private PlayerMovementLockController movementLockController;

    [Header("Logging")]
    [SerializeField] private bool logOnEnable = true;
    [SerializeField] private bool logPeriodicSnapshot = true;
    [SerializeField] private float periodicInterval = 1.0f;
    [SerializeField] private bool logInputChanges = true;
    [SerializeField] private bool logAnimatorStateChanges = true;
    [SerializeField] private bool logAnimatorParameterChanges = true;
    [SerializeField] private bool logKianaStateChanges = true;

    [Header("File Output")]
    [SerializeField] private bool writeToTextFile = true;
    [SerializeField] private bool mirrorToConsole;
    [SerializeField] private string logFolderName = "PlayerInputTestLogs";
    [SerializeField] private string fileNamePrefix = "PlayerInput";
    [SerializeField] private bool createNewFileOnEnable = true;
    [SerializeField] private bool flushFileEveryWrite = true;

    [Header("Debug Log Capture")]
    [SerializeField] private bool captureWatchedDebugLogs = true;
    [SerializeField] private string[] watchedDebugPrefixes =
    {
        "[KianaCombat][LeftMouse]",
        "[KianaCombat][BranchAttack]",
        "[PlayerCombat][LightAttack]",
        "[PlayerCombat][Movement]",
        "[PlayerCombat][DodgeCancel]",
        "[PlayerCombat][HeavyAttack]",
        "[CombatHealth]",
        "[PlayerParry]",
        "[EnemyParryWindow]",
        "[EnemyAttackWarning]",
        "[EnemyDaggerProjectile]",
        "[EnemyAttackHitbox]"
    };

    [Header("Snapshot Detail")]
    [SerializeField] private bool includeInput = true;
    [SerializeField] private bool includeAnimator = true;
    [SerializeField] private bool includeKianaInternals = true;
    [SerializeField] private bool includeResources = true;

    private const BindingFlags KianaFieldFlags = BindingFlags.Instance | BindingFlags.NonPublic;

    private static readonly string[] KianaSnapshotFieldNames =
    {
        "_moveState",
        "_moveMode",
        "_hasMoveInput",
        "_leftMouseHeld",
        "_queuedLightAttack",
        "_canLightAttackCancel",
        "_isAttackLocked",
        "_isAttackMovementLocked",
        "_isHitReactionRootMotionActive",
        "_isHitReactionInputLocked",
        "_isDodging",
        "_canDodgeAttackCancel",
        "_queuedDodgeRunAttack",
        "_queuedDodgeCounterAttack",
        "_queuedParryCounterAttack",
        "_dodgeCounterAttackReady",
        "_isDodgeCounterAttackLunging",
        "_isParryCounterAttackLunging",
        "_isBranchStartRequested",
        "_isBranchChargeWindowOpen",
        "_isBranchCharging",
        "_isBranchSlowMotionActive",
        "_currentBranchAttackLevel",
        "_branchChargePoints",
        "_branchLeftMouseHoldTimer",
        "_parryLockTimer"
    };

    private static readonly string[] KianaChangeFieldNames =
    {
        "_moveState",
        "_moveMode",
        "_hasMoveInput",
        "_leftMouseHeld",
        "_queuedLightAttack",
        "_canLightAttackCancel",
        "_isAttackLocked",
        "_isAttackMovementLocked",
        "_isHitReactionRootMotionActive",
        "_isHitReactionInputLocked",
        "_isDodging",
        "_canDodgeAttackCancel",
        "_queuedDodgeRunAttack",
        "_queuedDodgeCounterAttack",
        "_queuedParryCounterAttack",
        "_dodgeCounterAttackReady",
        "_isDodgeCounterAttackLunging",
        "_isParryCounterAttackLunging",
        "_isBranchStartRequested",
        "_isBranchChargeWindowOpen",
        "_isBranchCharging",
        "_isBranchSlowMotionActive",
        "_currentBranchAttackLevel"
    };

    private static readonly int HashCombatMode = Animator.StringToHash("CombatMode");
    private static readonly int HashHasMoveInput = Animator.StringToHash("HasMoveInput");
    private static readonly int HashMoveMode = Animator.StringToHash("MoveMode");
    private static readonly int HashCombatMoveMode = Animator.StringToHash("CombatMoveMode");
    private static readonly int HashCombatMoveMagnitude = Animator.StringToHash("CombatMoveMagnitude");
    private static readonly int HashCanExitAttack = Animator.StringToHash("CanExitAttack");
    private static readonly int HashCanExitHitReaction = Animator.StringToHash("CanExitHitReaction");
    private static readonly int HashBranchAttackLevel = Animator.StringToHash("BranchAttackLevel");
    private static readonly int HashFocusValue = Animator.StringToHash("FocusValue");

    private readonly StringBuilder _builder = new StringBuilder(2048);
    private FieldInfo[] _kianaSnapshotFields;
    private FieldInfo[] _kianaChangeFields;
    private StreamWriter _logWriter;
    private string _logFilePath;
    private float _nextPeriodicLogTime;
    private bool _hasSnapshot;

    private Vector2 _lastMoveInput;
    private bool _lastMovePressed;
    private bool _lastLeftMouseDown;
    private bool _lastRightMouseDown;
    private bool _lastSpaceDown;
    private bool _lastShiftDown;

    private int _lastStateHash;
    private int _lastNextStateHash;
    private bool _lastInTransition;
    private bool _lastCombatMode;
    private bool _lastHasMoveInputParam;
    private bool _lastCanExitAttack;
    private bool _lastCanExitHitReaction;
    private int _lastMoveMode;
    private int _lastCombatMoveMode;
    private int _lastBranchAttackLevel;
    private float _lastCombatMoveMagnitude;
    private float _lastFocusValue;
    private string _lastKianaChangeSignature;

    private void Awake()
    {
        ResolveReferences();
        CacheReflectionFields();
        CaptureSnapshotState();
    }

    private void OnEnable()
    {
        ResolveReferences();
        CacheReflectionFields();
        SubscribeUnityLogCapture();
        SubscribeScenarioEvents();
        PrepareFileOutput();
        CaptureSnapshotState();
        _nextPeriodicLogTime = Time.time + periodicInterval;

        if (logOnEnable)
        {
            LogSnapshot("LoggerEnabled");
        }
    }

    private void OnDisable()
    {
        UnsubscribeUnityLogCapture();
        UnsubscribeScenarioEvents();
        CloseFileOutput();
    }

    private void OnValidate()
    {
        periodicInterval = Mathf.Max(0.1f, periodicInterval);
        if (string.IsNullOrWhiteSpace(logFolderName))
        {
            logFolderName = "PlayerInputTestLogs";
        }

        if (string.IsNullOrWhiteSpace(fileNamePrefix))
        {
            fileNamePrefix = "PlayerInput";
        }
    }

    private void Update()
    {
        ResolveReferences();
        LogChangedInput();
        LogChangedAnimator();
        LogChangedKianaState();

        if (!logPeriodicSnapshot || Time.time < _nextPeriodicLogTime)
        {
            return;
        }

        _nextPeriodicLogTime = Time.time + periodicInterval;
        LogSnapshot("Periodic");
    }

    [ContextMenu("Log Snapshot Now")]
    public void LogSnapshotNow()
    {
        ResolveReferences();
        LogSnapshot("ManualSnapshot");
    }

    [ContextMenu("Mark Scenario Start")]
    public void MarkScenarioStart()
    {
        ResolveReferences();
        if (writeToTextFile && createNewFileOnEnable && _logWriter == null)
        {
            PrepareFileOutput();
        }

        _nextPeriodicLogTime = Time.time + periodicInterval;
        LogSnapshot("ScenarioStart");
    }

    [ContextMenu("Mark Scenario End")]
    public void MarkScenarioEnd()
    {
        ResolveReferences();
        LogSnapshot("ScenarioEnd");
    }

    private void LogChangedInput()
    {
        if (!logInputChanges)
        {
            return;
        }

        PlayerInputSnapshot input = ReadInputSnapshot();
        if (!_hasSnapshot)
        {
            CaptureInput(input);
            return;
        }

        if (input.LeftMousePressed)
        {
            LogSnapshot("Input:LeftMousePressed");
        }

        if (input.LeftMouseReleased)
        {
            LogSnapshot("Input:LeftMouseReleased");
        }

        if (input.RightMousePressed)
        {
            LogSnapshot("Input:RightMousePressed");
        }

        if (input.RightMouseReleased)
        {
            LogSnapshot("Input:RightMouseReleased");
        }

        if (input.SpacePressed)
        {
            LogSnapshot("Input:SpacePressed");
        }

        if (input.SpaceReleased)
        {
            LogSnapshot("Input:SpaceReleased");
        }

        if (input.ShiftDown != _lastShiftDown)
        {
            LogSnapshot($"Input:Shift:{_lastShiftDown}->{input.ShiftDown}");
        }

        bool moveChanged = input.MovePressed != _lastMovePressed ||
            (input.MoveInput - _lastMoveInput).sqrMagnitude > 0.001f;
        if (moveChanged)
        {
            LogSnapshot($"Input:Move:{FormatVector2(_lastMoveInput)}/{_lastMovePressed}->{FormatVector2(input.MoveInput)}/{input.MovePressed}");
        }
    }

    private void LogChangedAnimator()
    {
        if ((!logAnimatorStateChanges && !logAnimatorParameterChanges) || animator == null)
        {
            return;
        }

        AnimatorStateInfo currentInfo = animator.GetCurrentAnimatorStateInfo(0);
        bool inTransition = animator.IsInTransition(0);
        int nextHash = 0;
        if (inTransition)
        {
            nextHash = animator.GetNextAnimatorStateInfo(0).shortNameHash;
        }

        if (_hasSnapshot && logAnimatorStateChanges)
        {
            bool stateChanged = currentInfo.shortNameHash != _lastStateHash ||
                nextHash != _lastNextStateHash ||
                inTransition != _lastInTransition;
            if (stateChanged)
            {
                LogSnapshot($"AnimatorState:{_lastStateHash}/{_lastInTransition}/{_lastNextStateHash}->{currentInfo.shortNameHash}/{inTransition}/{nextHash}");
            }
        }

        if (!_hasSnapshot || !logAnimatorParameterChanges)
        {
            return;
        }

        bool combatMode = GetAnimatorBool(HashCombatMode, _lastCombatMode);
        bool hasMoveInput = GetAnimatorBool(HashHasMoveInput, _lastHasMoveInputParam);
        bool canExitAttack = GetAnimatorBool(HashCanExitAttack, _lastCanExitAttack);
        bool canExitHitReaction = GetAnimatorBool(HashCanExitHitReaction, _lastCanExitHitReaction);
        int moveMode = GetAnimatorInt(HashMoveMode, _lastMoveMode);
        int combatMoveMode = GetAnimatorInt(HashCombatMoveMode, _lastCombatMoveMode);
        int branchAttackLevel = GetAnimatorInt(HashBranchAttackLevel, _lastBranchAttackLevel);
        float combatMoveMagnitude = GetAnimatorFloat(HashCombatMoveMagnitude, _lastCombatMoveMagnitude);
        float focusValue = GetAnimatorFloat(HashFocusValue, _lastFocusValue);

        bool changed = combatMode != _lastCombatMode ||
            hasMoveInput != _lastHasMoveInputParam ||
            canExitAttack != _lastCanExitAttack ||
            canExitHitReaction != _lastCanExitHitReaction ||
            moveMode != _lastMoveMode ||
            combatMoveMode != _lastCombatMoveMode ||
            branchAttackLevel != _lastBranchAttackLevel ||
            !Approximately(combatMoveMagnitude, _lastCombatMoveMagnitude, 0.01f) ||
            !Approximately(focusValue, _lastFocusValue, 0.1f);
        if (changed)
        {
            LogSnapshot("AnimatorParamsChanged");
        }
    }

    private void LogChangedKianaState()
    {
        if (!logKianaStateChanges || kianaController == null || _kianaChangeFields == null)
        {
            return;
        }

        string signature = BuildKianaFieldSignature(_kianaChangeFields);
        if (_hasSnapshot && !string.Equals(signature, _lastKianaChangeSignature, StringComparison.Ordinal))
        {
            LogSnapshot("KianaStateChanged");
        }
    }

    private void LogSnapshot(string reason)
    {
        BuildSnapshot(reason);
        string message = _builder.ToString();
        if (mirrorToConsole)
        {
            Debug.Log(message, this);
        }

        WriteSnapshotToFile(message);
        CaptureSnapshotState();
    }

    private void BuildSnapshot(string reason)
    {
        _builder.Length = 0;
        _builder.Append("[PlayerInputScenario] reason=").Append(reason)
            .Append(" time=").Append(Time.time.ToString("F3"))
            .Append(" frame=").Append(Time.frameCount);

        if (includeInput)
        {
            AppendInputSnapshot();
        }

        if (includeAnimator)
        {
            AppendAnimatorSnapshot();
        }

        if (includeKianaInternals)
        {
            AppendKianaSnapshot();
        }

        if (includeResources)
        {
            AppendResourceSnapshot();
        }
    }

    private void AppendInputSnapshot()
    {
        PlayerInputSnapshot input = ReadInputSnapshot();
        _builder.Append(" | input move=").Append(FormatVector2(input.MoveInput))
            .Append(", movePressed=").Append(input.MovePressed)
            .Append(", leftDown=").Append(input.LeftMouseDown)
            .Append(", leftPressed=").Append(input.LeftMousePressed)
            .Append(", leftReleased=").Append(input.LeftMouseReleased)
            .Append(", rightDown=").Append(input.RightMouseDown)
            .Append(", rightPressed=").Append(input.RightMousePressed)
            .Append(", rightReleased=").Append(input.RightMouseReleased)
            .Append(", spaceDown=").Append(input.SpaceDown)
            .Append(", spacePressed=").Append(input.SpacePressed)
            .Append(", spaceReleased=").Append(input.SpaceReleased)
            .Append(", shiftDown=").Append(input.ShiftDown);
    }

    private void AppendAnimatorSnapshot()
    {
        if (animator == null)
        {
            _builder.Append(" | animator=null");
            return;
        }

        AnimatorStateInfo currentInfo = animator.GetCurrentAnimatorStateInfo(0);
        bool inTransition = animator.IsInTransition(0);
        AnimatorStateInfo nextInfo = default(AnimatorStateInfo);
        if (inTransition)
        {
            nextInfo = animator.GetNextAnimatorStateInfo(0);
        }

        _builder.Append(" | animator stateHash=").Append(currentInfo.shortNameHash)
            .Append(", norm=").Append(currentInfo.normalizedTime.ToString("F3"))
            .Append(", inTransition=").Append(inTransition)
            .Append(", nextStateHash=").Append(inTransition ? nextInfo.shortNameHash : 0)
            .Append(", nextNorm=").Append(inTransition ? nextInfo.normalizedTime.ToString("F3") : "0.000")
            .Append(", CombatMode=").Append(GetAnimatorBool(HashCombatMode, false))
            .Append(", HasMoveInput=").Append(GetAnimatorBool(HashHasMoveInput, false))
            .Append(", MoveMode=").Append(GetAnimatorInt(HashMoveMode, 0))
            .Append(", CombatMoveMode=").Append(GetAnimatorInt(HashCombatMoveMode, 0))
            .Append(", CombatMoveMagnitude=").Append(GetAnimatorFloat(HashCombatMoveMagnitude, 0f).ToString("F3"))
            .Append(", CanExitAttack=").Append(GetAnimatorBool(HashCanExitAttack, false))
            .Append(", CanExitHitReaction=").Append(GetAnimatorBool(HashCanExitHitReaction, true))
            .Append(", BranchAttackLevel=").Append(GetAnimatorInt(HashBranchAttackLevel, 0))
            .Append(", FocusValue=").Append(GetAnimatorFloat(HashFocusValue, 0f).ToString("F1"))
            .Append(", applyRootMotion=").Append(animator.applyRootMotion);
    }

    private void AppendKianaSnapshot()
    {
        if (kianaController == null)
        {
            _builder.Append(" | kiana=null");
            return;
        }

        _builder.Append(" | kiana ");
        _builder.Append(BuildKianaFieldSignature(_kianaSnapshotFields));
    }

    private void AppendResourceSnapshot()
    {
        _builder.Append(" | resources");
        if (kianaController != null)
        {
            _builder.Append(" battleWill=").Append(kianaController.GetCurrentBattleWill().ToString("F1"))
                .Append('/').Append(kianaController.GetMaxBattleWill().ToString("F1"))
                .Append(", previewCost=").Append(kianaController.GetPreviewBattleWillCost().ToString("F1"));
        }
        else if (battleWillSystem != null)
        {
            _builder.Append(" battleWill=").Append(battleWillSystem.CurrentBattleWill.ToString("F1"))
                .Append('/').Append(battleWillSystem.MaxBattleWill.ToString("F1"));
        }

        if (poiseController != null)
        {
            _builder.Append(", poise=").Append(poiseController.CurrentPoise.ToString("F1"))
                .Append('/').Append(poiseController.MaxPoise.ToString("F1"));
        }

        if (health != null)
        {
            _builder.Append(", health=").Append(health.CurrentHealth.ToString("F1"))
                .Append('/').Append(health.MaxHealth.ToString("F1"));
        }

        if (parryController != null)
        {
            _builder.Append(", parryWindow=").Append(parryController.ParryWindowOpen)
                .Append(", parryCooldown=").Append(parryController.ParryCooldownRemaining.ToString("F3"))
                .Append(", parryCounterPending=").Append(parryController.CounterPending)
                .Append(", parryCounterReady=").Append(parryController.CounterReady)
                .Append(", lastParried=").Append(parryController.LastParriedWindow != null ? parryController.LastParriedWindow.name : "None");
        }

        if (movementLockController != null)
        {
            _builder.Append(", movementLock=").Append(movementLockController.IsMovementLocked)
                .Append(", attackInputLock=").Append(movementLockController.IsAttackLocked);
        }

        if (characterController != null)
        {
            _builder.Append(", ccVelocity=").Append(FormatVector3(characterController.velocity));
        }
    }

    private void CaptureSnapshotState()
    {
        PlayerInputSnapshot input = ReadInputSnapshot();
        CaptureInput(input);

        if (animator != null)
        {
            AnimatorStateInfo currentInfo = animator.GetCurrentAnimatorStateInfo(0);
            _lastStateHash = currentInfo.shortNameHash;
            _lastInTransition = animator.IsInTransition(0);
            _lastNextStateHash = _lastInTransition ? animator.GetNextAnimatorStateInfo(0).shortNameHash : 0;
            _lastCombatMode = GetAnimatorBool(HashCombatMode, _lastCombatMode);
            _lastHasMoveInputParam = GetAnimatorBool(HashHasMoveInput, _lastHasMoveInputParam);
            _lastCanExitAttack = GetAnimatorBool(HashCanExitAttack, _lastCanExitAttack);
            _lastCanExitHitReaction = GetAnimatorBool(HashCanExitHitReaction, _lastCanExitHitReaction);
            _lastMoveMode = GetAnimatorInt(HashMoveMode, _lastMoveMode);
            _lastCombatMoveMode = GetAnimatorInt(HashCombatMoveMode, _lastCombatMoveMode);
            _lastBranchAttackLevel = GetAnimatorInt(HashBranchAttackLevel, _lastBranchAttackLevel);
            _lastCombatMoveMagnitude = GetAnimatorFloat(HashCombatMoveMagnitude, _lastCombatMoveMagnitude);
            _lastFocusValue = GetAnimatorFloat(HashFocusValue, _lastFocusValue);
        }

        _lastKianaChangeSignature = BuildKianaFieldSignature(_kianaChangeFields);
        _hasSnapshot = true;
    }

    private void CaptureInput(PlayerInputSnapshot input)
    {
        _lastMoveInput = input.MoveInput;
        _lastMovePressed = input.MovePressed;
        _lastLeftMouseDown = input.LeftMouseDown;
        _lastRightMouseDown = input.RightMouseDown;
        _lastSpaceDown = input.SpaceDown;
        _lastShiftDown = input.ShiftDown;
    }

    private PlayerInputSnapshot ReadInputSnapshot()
    {
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;

        bool w = keyboard != null && keyboard.wKey.isPressed;
        bool a = keyboard != null && keyboard.aKey.isPressed;
        bool s = keyboard != null && keyboard.sKey.isPressed;
        bool d = keyboard != null && keyboard.dKey.isPressed;
        Vector2 moveInput = new Vector2((d ? 1f : 0f) - (a ? 1f : 0f), (w ? 1f : 0f) - (s ? 1f : 0f));
        if (moveInput.sqrMagnitude > 1f)
        {
            moveInput.Normalize();
        }

        bool leftDown = mouse != null && mouse.leftButton.isPressed;
        bool rightDown = mouse != null && mouse.rightButton.isPressed;
        bool spaceDown = keyboard != null && keyboard.spaceKey.isPressed;
        bool shiftDown = keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);

        return new PlayerInputSnapshot
        {
            MoveInput = moveInput,
            MovePressed = moveInput.sqrMagnitude > 0.001f,
            LeftMouseDown = leftDown,
            LeftMousePressed = mouse != null && mouse.leftButton.wasPressedThisFrame,
            LeftMouseReleased = mouse != null && mouse.leftButton.wasReleasedThisFrame,
            RightMouseDown = rightDown,
            RightMousePressed = mouse != null && mouse.rightButton.wasPressedThisFrame,
            RightMouseReleased = mouse != null && mouse.rightButton.wasReleasedThisFrame,
            SpaceDown = spaceDown,
            SpacePressed = keyboard != null && keyboard.spaceKey.wasPressedThisFrame,
            SpaceReleased = keyboard != null && keyboard.spaceKey.wasReleasedThisFrame,
            ShiftDown = shiftDown
        };
    }

    private void ResolveReferences()
    {
        if (kianaController == null)
        {
            kianaController = GetComponent<KianaCombatController>();
        }

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        if (characterController == null)
        {
            characterController = GetComponent<CharacterController>();
        }

        if (battleWillSystem == null)
        {
            battleWillSystem = GetComponent<PlayerBattleWillSystem>();
        }

        if (poiseController == null)
        {
            poiseController = GetComponent<PlayerPoiseController>();
        }

        if (health == null)
        {
            health = GetComponent<CombatHealth>();
        }

        if (parryController == null)
        {
            parryController = GetComponent<PlayerParryController>();
        }

        if (movementLockController == null)
        {
            movementLockController = GetComponent<PlayerMovementLockController>();
        }
    }

    private void CacheReflectionFields()
    {
        if (_kianaSnapshotFields == null || _kianaSnapshotFields.Length == 0)
        {
            _kianaSnapshotFields = CacheFields(KianaSnapshotFieldNames);
        }

        if (_kianaChangeFields == null || _kianaChangeFields.Length == 0)
        {
            _kianaChangeFields = CacheFields(KianaChangeFieldNames);
        }
    }

    private FieldInfo[] CacheFields(string[] fieldNames)
    {
        FieldInfo[] fields = new FieldInfo[fieldNames.Length];
        Type type = typeof(KianaCombatController);
        for (int i = 0; i < fieldNames.Length; i++)
        {
            fields[i] = type.GetField(fieldNames[i], KianaFieldFlags);
        }

        return fields;
    }

    private string BuildKianaFieldSignature(FieldInfo[] fields)
    {
        if (kianaController == null || fields == null)
        {
            return "none";
        }

        StringBuilder temp = new StringBuilder(512);
        for (int i = 0; i < fields.Length; i++)
        {
            FieldInfo field = fields[i];
            if (field == null)
            {
                continue;
            }

            if (temp.Length > 0)
            {
                temp.Append(", ");
            }

            temp.Append(field.Name).Append('=').Append(FormatValue(field.GetValue(kianaController)));
        }

        return temp.Length > 0 ? temp.ToString() : "none";
    }

    private void SubscribeUnityLogCapture()
    {
        if (!captureWatchedDebugLogs)
        {
            return;
        }

        Application.logMessageReceived -= OnUnityLogMessageReceived;
        Application.logMessageReceived += OnUnityLogMessageReceived;
    }

    private void UnsubscribeUnityLogCapture()
    {
        Application.logMessageReceived -= OnUnityLogMessageReceived;
    }

    private void OnUnityLogMessageReceived(string condition, string stackTrace, LogType type)
    {
        if (!captureWatchedDebugLogs || !ShouldCaptureDebugLog(condition))
        {
            return;
        }

        string line = $"[CapturedPlayerDebug] type={type} time={Time.time:F3} frame={Time.frameCount} {condition}";
        WriteSnapshotToFile(line);
    }

    private void SubscribeScenarioEvents()
    {
        KianaCombatController.BranchAttackScenarioEvent -= OnKianaBranchAttackScenarioEvent;
        KianaCombatController.BranchAttackScenarioEvent += OnKianaBranchAttackScenarioEvent;

        PlayerParryController.ParryStarted -= OnPlayerParryStarted;
        PlayerParryController.ParryStarted += OnPlayerParryStarted;
        PlayerParryController.ParryWindowOpened -= OnPlayerParryWindowOpened;
        PlayerParryController.ParryWindowOpened += OnPlayerParryWindowOpened;
        PlayerParryController.ParryWindowClosed -= OnPlayerParryWindowClosed;
        PlayerParryController.ParryWindowClosed += OnPlayerParryWindowClosed;
        PlayerParryController.ParrySucceeded -= OnPlayerParrySucceeded;
        PlayerParryController.ParrySucceeded += OnPlayerParrySucceeded;
        PlayerParryController.ProjectileParried -= OnPlayerProjectileParried;
        PlayerParryController.ProjectileParried += OnPlayerProjectileParried;

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

    private void UnsubscribeScenarioEvents()
    {
        KianaCombatController.BranchAttackScenarioEvent -= OnKianaBranchAttackScenarioEvent;
        PlayerParryController.ParryStarted -= OnPlayerParryStarted;
        PlayerParryController.ParryWindowOpened -= OnPlayerParryWindowOpened;
        PlayerParryController.ParryWindowClosed -= OnPlayerParryWindowClosed;
        PlayerParryController.ParrySucceeded -= OnPlayerParrySucceeded;
        PlayerParryController.ProjectileParried -= OnPlayerProjectileParried;

        if (battleWillSystem != null)
        {
            battleWillSystem.BattleWillChanged -= OnBattleWillChanged;
        }
    }

    private void OnKianaBranchAttackScenarioEvent(KianaCombatController source, string message)
    {
        if (kianaController != null && source != kianaController)
        {
            return;
        }

        LogSnapshot($"KianaBranch:{message}");
    }

    private void OnBattleWillChanged(PlayerBattleWillSystem source, float previousValue, float currentValue)
    {
        if (battleWillSystem != null && source != battleWillSystem)
        {
            return;
        }

        LogSnapshot($"BattleWillChanged:{previousValue:F1}->{currentValue:F1}");
    }

    private void OnPlayerParryStarted(PlayerParryController source)
    {
        if (!IsThisPlayerParry(source))
        {
            return;
        }

        LogSnapshot("Parry:Started");
    }

    private void OnPlayerParryWindowOpened(PlayerParryController source)
    {
        if (!IsThisPlayerParry(source))
        {
            return;
        }

        LogSnapshot("Parry:WindowOpened");
    }

    private void OnPlayerParryWindowClosed(PlayerParryController source)
    {
        if (!IsThisPlayerParry(source))
        {
            return;
        }

        LogSnapshot("Parry:WindowClosed");
    }

    private void OnPlayerParrySucceeded(PlayerParryController source, EnemyParryWindow parriedWindow)
    {
        if (!IsThisPlayerParry(source))
        {
            return;
        }

        LogSnapshot($"Parry:Succeeded:{(parriedWindow != null ? parriedWindow.name : "None")}");
    }

    private void OnPlayerProjectileParried(PlayerParryController source, EnemyDaggerProjectile projectile)
    {
        if (!IsThisPlayerParry(source))
        {
            return;
        }

        LogSnapshot($"Parry:ProjectileParried:{(projectile != null ? projectile.name : "None")}");
    }

    private bool IsThisPlayerParry(PlayerParryController source)
    {
        if (source == null)
        {
            return false;
        }

        if (parryController == null)
        {
            parryController = GetComponent<PlayerParryController>();
        }

        return parryController == null || source == parryController;
    }

    private bool ShouldCaptureDebugLog(string condition)
    {
        if (string.IsNullOrEmpty(condition) || watchedDebugPrefixes == null)
        {
            return false;
        }

        for (int i = 0; i < watchedDebugPrefixes.Length; i++)
        {
            string prefix = watchedDebugPrefixes[i];
            if (!string.IsNullOrEmpty(prefix) && condition.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private void PrepareFileOutput()
    {
        if (!writeToTextFile || _logWriter != null)
        {
            return;
        }

        try
        {
            string folder = Path.Combine(Application.persistentDataPath, logFolderName);
            Directory.CreateDirectory(folder);
            string fileName = $"{fileNamePrefix}_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            _logFilePath = Path.Combine(folder, fileName);
            _logWriter = new StreamWriter(_logFilePath, false, Encoding.UTF8);
            _logWriter.WriteLine($"[PlayerInputScenario] file={_logFilePath}");
            _logWriter.WriteLine($"[PlayerInputScenario] scene={gameObject.scene.name}, object={name}");
            if (flushFileEveryWrite)
            {
                _logWriter.Flush();
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[PlayerInputScenario] Failed to create log file. {exception.Message}", this);
            CloseFileOutput();
        }
    }

    private void CloseFileOutput()
    {
        if (_logWriter == null)
        {
            return;
        }

        try
        {
            _logWriter.Flush();
            _logWriter.Dispose();
        }
        catch (Exception)
        {
            // ignored on shutdown
        }

        _logWriter = null;
    }

    private void WriteSnapshotToFile(string message)
    {
        if (!writeToTextFile)
        {
            return;
        }

        if (_logWriter == null)
        {
            PrepareFileOutput();
        }

        if (_logWriter == null)
        {
            return;
        }

        _logWriter.WriteLine(message);
        if (flushFileEveryWrite)
        {
            _logWriter.Flush();
        }
    }

    private bool GetAnimatorBool(int hash, bool fallback)
    {
        if (animator == null || !HasAnimatorParameter(hash, AnimatorControllerParameterType.Bool))
        {
            return fallback;
        }

        return animator.GetBool(hash);
    }

    private int GetAnimatorInt(int hash, int fallback)
    {
        if (animator == null || !HasAnimatorParameter(hash, AnimatorControllerParameterType.Int))
        {
            return fallback;
        }

        return animator.GetInteger(hash);
    }

    private float GetAnimatorFloat(int hash, float fallback)
    {
        if (animator == null || !HasAnimatorParameter(hash, AnimatorControllerParameterType.Float))
        {
            return fallback;
        }

        return animator.GetFloat(hash);
    }

    private bool HasAnimatorParameter(int hash, AnimatorControllerParameterType type)
    {
        if (animator == null)
        {
            return false;
        }

        AnimatorControllerParameter[] parameters = animator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            AnimatorControllerParameter parameter = parameters[i];
            if (parameter.nameHash == hash && parameter.type == type)
            {
                return true;
            }
        }

        return false;
    }

    private static bool Approximately(float a, float b, float tolerance)
    {
        return Mathf.Abs(a - b) <= tolerance;
    }

    private static string FormatVector2(Vector2 value)
    {
        return $"({value.x:F1},{value.y:F1})";
    }

    private static string FormatVector3(Vector3 value)
    {
        return $"({value.x:F2},{value.y:F2},{value.z:F2})";
    }

    private static string FormatValue(object value)
    {
        if (value == null)
        {
            return "null";
        }

        if (value is float floatValue)
        {
            return floatValue.ToString("F2");
        }

        if (value is Vector2 vector2)
        {
            return FormatVector2(vector2);
        }

        if (value is Vector3 vector3)
        {
            return FormatVector3(vector3);
        }

        return value.ToString();
    }

    private struct PlayerInputSnapshot
    {
        public Vector2 MoveInput;
        public bool MovePressed;
        public bool LeftMouseDown;
        public bool LeftMousePressed;
        public bool LeftMouseReleased;
        public bool RightMouseDown;
        public bool RightMousePressed;
        public bool RightMouseReleased;
        public bool SpaceDown;
        public bool SpacePressed;
        public bool SpaceReleased;
        public bool ShiftDown;
    }
}
