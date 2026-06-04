using UnityEngine;

[RequireComponent(typeof(Animator))]
public class StrikeJaegerAIController : MonoBehaviour
{
    [System.Serializable]
    public class AttackCooldown
    {
        public int attackIndex = 1;
        public float cooldown = 1.5f;
        public int otherAttackUsesBeforeReuse = 1;
        public bool startReady = true;
        public bool allowDesireReuseRelief;
        public DesireGate reuseReliefDesire = DesireGate.Retreat;
        [Range(0f, 1f)] public float reuseReliefThresholdPercent = 0.9f;
        public int reuseReliefAmount = 1;
        public float reuseReliefCooldown = 1.5f;

        [System.NonSerialized] public float remaining;
        [System.NonSerialized] public int otherAttackUsesRemaining;
        [System.NonSerialized] public float reuseReliefTimer;
    }

    [System.Serializable]
    public class AttackProfile
    {
        public int attackIndex = 1;
        public AIIntent intent = AIIntent.Probe;
        public float minUsableDistance = 0f;
        public float maxUsableDistance = 3f;
        public float idealDistance = 1.5f;
        public float distanceTolerance = 2f;
        public float baseScore = 1f;
        public bool preferWhenPlayerDodgeOnCooldown;
        public bool preferWhenPlayerRunning;
        public bool preferWhenPlayerAttacking;
        public float pressureWeight;
        public float threatWeight;
        public float aggressionWeight;
        public float retreatWeight;
        public float burstWeight;
    }

    [System.Serializable]
    public class AttackComboProfile
    {
        public string comboName = "Attack Combo";
        [Tooltip("The attack state that can branch into this combo when AE_TryEnemyAttackCombo is called.")]
        public int sourceAttackIndex = 1;
        public ComboTargetType targetType = ComboTargetType.Attack;
        [Tooltip("Combo selection index to write into AttackComboIndex. For Attack targets this also remains the target AttackIndex, preserving existing combo profiles.")]
        public int comboIndex = 1;
        [Tooltip("Only used when Target Type is Movement.")]
        public AIMoveMode targetMoveMode = AIMoveMode.Dodge;
        [Tooltip("Only used when Target Type is Movement.")]
        public AIMoveDirection targetMoveDirection = AIMoveDirection.Back;
        [Range(0f, 1f)] public float comboChance = 0.35f;
        public AIIntent intent = AIIntent.Pressure;
        public float minUsableDistance = 0f;
        public float maxUsableDistance = 4f;
        public float idealDistance = 1.5f;
        public float distanceTolerance = 2f;
        public float baseScore = 1f;
        public float comboCooldown = 4f;
        public bool startReady = true;
        public bool preferWhenPlayerDodgeOnCooldown;
        public bool preferWhenPlayerRunning;
        public bool preferWhenPlayerAttacking;
        public float pressureWeight;
        public float threatWeight;
        public float aggressionWeight;
        public float retreatWeight;
        public float burstWeight;

        [System.NonSerialized] public float remaining;
    }

    public enum ComboTargetType
    {
        Attack = 0,
        Movement = 1
    }

    [System.Serializable]
    public class MovementScoreSnapshot
    {
        public float distance;
        public DistanceBand distanceBand;
        public bool playerAttacking;
        public bool playerRunning;
        public bool playerDodgeReady;
        public bool recentlyPunished;
        public bool recentlyWhiffed;
        public float pressure;
        public float threat;
        public float aggression;
        public float retreat;
        public float burst;
        public float runForwardScore;
        public float walkBackScore;
        public float strafeScore;
        public float dodgeBackScore;
        public float dodgeSideScore;
        public AIMoveMode selectedMode;
        public AIMoveDirection selectedDirection;
        public float selectedScore;
    }

    public enum DistanceBand
    {
        Far = 0,
        Mid = 1,
        Close = 2,
        PointBlank = 3
    }

    public enum AngleBand
    {
        Front = 0,
        Side = 1,
        Back = 2
    }

    public enum AIActionState
    {
        Disabled = 0,
        Ready = 1,
        Acting = 2,
        Recovering = 3
    }

    public enum AIMoveMode
    {
        Idle = 0,
        Walk = 1,
        Run = 2,
        Dodge = 3
    }

    public enum AIMoveDirection
    {
        Forward = 0,
        Back = 1,
        Left = 2,
        Right = 3
    }

    public enum AIIntent
    {
        Observe = 0,
        Probe = 1,
        Pressure = 2,
        Pounce = 3,
        Burst = 4,
        Disengage = 5,
        Punish = 6,
        Reset = 7
    }

    public enum DesireGate
    {
        Pressure = 0,
        Threat = 1,
        Aggression = 2,
        Retreat = 3,
        Burst = 4
    }

    [System.Serializable]
    public class HealthPhaseInfluence
    {
        public string phaseName = "Phase";
        [Range(0f, 1f)] public float minHealthPercent;
        [Range(0f, 1f)] public float maxHealthPercent = 1f;

        [Header("Desire Add")]
        public float pressureAdd;
        public float threatAdd;
        public float aggressionAdd;
        public float retreatAdd;
        public float burstAdd;

        [Header("Attack Score")]
        public float attackScoreMultiplier = 1f;
        public float lightAttackMultiplier = 1f;
        public float heavyAttackMultiplier = 1f;
        public float daggerAttackMultiplier = 1f;
        public float retreatAttackMultiplier = 1f;

        [Header("Movement Score")]
        public float runForwardMultiplier = 1f;
        public float walkBackMultiplier = 1f;
        public float strafeMultiplier = 1f;
        public float dodgeBackMultiplier = 1f;
        public float dodgeSideMultiplier = 1f;

        [Header("Combo Score")]
        public float attackComboMultiplier = 1f;
        public float movementComboMultiplier = 1f;

        [Header("Pulse")]
        public bool usePulse;
        public float pulseInterval = 4f;
        public float attackPulseAmount = 0.15f;
        public float movementPulseAmount = 0.15f;
    }

    [Header("References")]
    [SerializeField] private Animator animator;
    [SerializeField] private CombatHealth health;
    [SerializeField] private EnemyCombatMemory combatMemory;
    [SerializeField] private EnemyHitMemory hitMemory;
    [SerializeField] private EnemyPressureModel pressureModel;
    [SerializeField] private EnemyHitReactionController hitReactionController;
    [SerializeField] private Transform playerTarget;
    [SerializeField] private CharacterController playerController;
    [SerializeField] private Animator playerAnimator;
    [SerializeField] private KianaDodgeCooldown playerDodgeCooldown;
    [SerializeField] private string playerTag = "Player";

    [Header("Perception")]
    [SerializeField] private float pointBlankDistance = 2f;
    [SerializeField] private float closeDistance = 5f;
    [SerializeField] private float midDistance = 14f;
    [SerializeField] private float playerMovingSpeedThreshold = 0.15f;
    [SerializeField] private float playerRunningSpeedThreshold = 4.5f;
    [SerializeField] private float recentDodgeMemoryTime = 0.35f;
    [SerializeField] private string playerAttackStateTag = "Attack";

    [Header("Facing")]
    [SerializeField] private bool facePlayerContinuously = true;
    [SerializeField] private bool facePlayerWhileActing = true;
    [Tooltip("仅在 Acting 状态为攻击动作时生效。若为 false，移动类动作（闪避、走路等）执行期间不追踪玩家朝向，" +
             "防止 root motion 位移因持续旋转变成围绕玩家的圆弧运动。")]
    [SerializeField] private bool facePlayerWhileMovementActing;
    [SerializeField] private bool facePlayerWhileRecovering;
    [Tooltip("若为 false，受击动作（Hit_Low/High_F/B）期间停止追踪玩家朝向。" +
             "受击的 External Recovery 可能比动画先结束导致状态回 Ready，此守卫独立于 AI 状态机时序。")]
    [SerializeField] private bool facePlayerWhileHitReacting;
    [SerializeField] private float continuousTurnSpeed = 540f;
    [SerializeField] private bool facePlayerBeforeAttack = true;
    [SerializeField] private bool snapFacingBeforeAttack;
    [SerializeField] private float preAttackTurnSpeed = 720f;
    [SerializeField] private float facingModelYawOffset;

    [Header("Prototype Loop")]
    [SerializeField] private bool aiEnabled = true;
    [SerializeField] private bool usePrototypeAttackLoop;
    [SerializeField] private int prototypeAttackIndex = 1;
    [SerializeField] private float firstActionDelay = 1.5f;
    [SerializeField] private float actionCooldown = 0.85f;
    [SerializeField] private float actionFallbackDuration = 3f;

    [Header("Attack Cooldowns")]
    [SerializeField] private bool useAttackCooldowns = true;
    [SerializeField] private AttackCooldown[] attackCooldowns =
    {
        new AttackCooldown { attackIndex = 1, cooldown = 0.8f, otherAttackUsesBeforeReuse = 1 },
        new AttackCooldown { attackIndex = 2, cooldown = 4.6f, otherAttackUsesBeforeReuse = 2 },
        new AttackCooldown { attackIndex = 3, cooldown = 1f, otherAttackUsesBeforeReuse = 1 },
        new AttackCooldown { attackIndex = 4, cooldown = 4.2f, otherAttackUsesBeforeReuse = 2 },
        new AttackCooldown { attackIndex = 5, cooldown = 3.8f, otherAttackUsesBeforeReuse = 1 },
        new AttackCooldown { attackIndex = 6, cooldown = 5f, otherAttackUsesBeforeReuse = 1 }
    };

    [Header("Attack Selection")]
    [SerializeField] private bool useAttackSelection = true;
    [SerializeField] private AttackProfile[] attackProfiles =
    {
        new AttackProfile { attackIndex = 1, intent = AIIntent.Probe, minUsableDistance = 0f, maxUsableDistance = 3.4f, idealDistance = 1.4f, distanceTolerance = 2.2f, baseScore = 1.15f, pressureWeight = 0.004f, aggressionWeight = 0.004f },
        new AttackProfile { attackIndex = 2, intent = AIIntent.Reset, minUsableDistance = 0f, maxUsableDistance = 4.5f, idealDistance = 2.4f, distanceTolerance = 2.2f, baseScore = 0.45f, preferWhenPlayerAttacking = true, threatWeight = 0.004f, retreatWeight = 0.006f, aggressionWeight = -0.01f, burstWeight = -0.008f },
        new AttackProfile { attackIndex = 3, intent = AIIntent.Pressure, minUsableDistance = 0f, maxUsableDistance = 3.6f, idealDistance = 1.4f, distanceTolerance = 2.3f, baseScore = 1.25f, pressureWeight = 0.012f, aggressionWeight = 0.014f, threatWeight = -0.003f, retreatWeight = -0.008f },
        new AttackProfile { attackIndex = 4, intent = AIIntent.Disengage, minUsableDistance = 0f, maxUsableDistance = 4.8f, idealDistance = 2.4f, distanceTolerance = 2.2f, baseScore = 0.5f, preferWhenPlayerAttacking = true, threatWeight = 0.005f, retreatWeight = 0.007f, aggressionWeight = -0.008f },
        new AttackProfile { attackIndex = 5, intent = AIIntent.Burst, minUsableDistance = 2.2f, maxUsableDistance = 11f, idealDistance = 6f, distanceTolerance = 5.5f, baseScore = 1.45f, preferWhenPlayerDodgeOnCooldown = true, pressureWeight = 0.01f, aggressionWeight = 0.012f, burstWeight = 0.02f, threatWeight = -0.006f, retreatWeight = -0.01f },
        new AttackProfile { attackIndex = 6, intent = AIIntent.Pounce, minUsableDistance = 10f, maxUsableDistance = 24f, idealDistance = 18f, distanceTolerance = 8f, baseScore = 1.55f, preferWhenPlayerRunning = true, preferWhenPlayerDodgeOnCooldown = true, aggressionWeight = 0.012f, burstWeight = 0.022f, threatWeight = -0.012f, retreatWeight = -0.014f }
    };
    [SerializeField] private bool allowIntentFallback = true;
    [SerializeField] private float dodgeCooldownPreferenceBonus = 0.35f;
    [SerializeField] private float runningPreferenceBonus = 0.25f;
    [SerializeField] private float attackingPreferenceBonus = 0.3f;
    [SerializeField] private float intentMismatchPenalty = 0.35f;

    [Header("Attack Combos")]
    [SerializeField] private bool useAttackCombos = true;
    [SerializeField] private AttackComboProfile[] attackComboProfiles = new AttackComboProfile[0];

    [Header("Pressure Intent")]
    [SerializeField] private float highThreatIntentThreshold = 72f;
    [SerializeField] private float retreatIntentThreshold = 72f;
    [SerializeField] private float burstIntentThreshold = 42f;
    [SerializeField] private float aggressionIntentThreshold = 34f;

    [Header("Health Phase Influence")]
    [SerializeField] private bool useHealthPhaseInfluence = true;
    [SerializeField] private HealthPhaseInfluence[] healthPhaseInfluences =
    {
        new HealthPhaseInfluence
        {
            phaseName = "High HP Pressure",
            minHealthPercent = 0.7f,
            maxHealthPercent = 1f,
            pressureAdd = 8f,
            aggressionAdd = 10f,
            burstAdd = 6f,
            retreatAdd = -6f,
            attackScoreMultiplier = 1.15f,
            heavyAttackMultiplier = 1.2f,
            retreatAttackMultiplier = 0.8f,
            walkBackMultiplier = 0.8f,
            dodgeBackMultiplier = 0.85f,
            dodgeSideMultiplier = 0.9f
        },
        new HealthPhaseInfluence
        {
            phaseName = "Mid HP Unstable",
            minHealthPercent = 0.35f,
            maxHealthPercent = 0.7f,
            usePulse = true,
            pulseInterval = 4f,
            attackPulseAmount = 0.18f,
            movementPulseAmount = 0.18f
        },
        new HealthPhaseInfluence
        {
            phaseName = "Low HP Skirmish",
            minHealthPercent = 0.15f,
            maxHealthPercent = 0.35f,
            retreatAdd = 10f,
            threatAdd = 6f,
            lightAttackMultiplier = 1.15f,
            daggerAttackMultiplier = 1.3f,
            retreatAttackMultiplier = 1.25f,
            heavyAttackMultiplier = 0.85f,
            walkBackMultiplier = 1.2f,
            strafeMultiplier = 1.15f,
            dodgeBackMultiplier = 1.25f,
            dodgeSideMultiplier = 1.25f,
            movementComboMultiplier = 1.25f
        },
        new HealthPhaseInfluence
        {
            phaseName = "Desperate Last Stand",
            minHealthPercent = 0f,
            maxHealthPercent = 0.15f,
            pressureAdd = 12f,
            threatAdd = 10f,
            aggressionAdd = 12f,
            retreatAdd = 12f,
            burstAdd = 12f,
            attackScoreMultiplier = 1.25f,
            lightAttackMultiplier = 1.2f,
            heavyAttackMultiplier = 1.2f,
            daggerAttackMultiplier = 1.25f,
            retreatAttackMultiplier = 1.2f,
            runForwardMultiplier = 1.15f,
            walkBackMultiplier = 1.15f,
            strafeMultiplier = 1.15f,
            dodgeBackMultiplier = 0.9f,
            dodgeSideMultiplier = 0.9f,
            attackComboMultiplier = 1.2f,
            movementComboMultiplier = 1.1f
        }
    };

    [Header("Hit Memory Influence")]
    [SerializeField] private bool useHitMemoryDecisionInfluence = true;
    [SerializeField] private int recentHitPressureCount = 3;
    [SerializeField] private float heavyHitRetreatBonus = 1.25f;
    [SerializeField] private float smallHitDisengageBonus = 0.55f;
    [SerializeField] private float microHitCounterBonus = 0.45f;
    [SerializeField] private float repeatedHitRetreatBonus = 0.85f;
    [SerializeField] private float sideOrBackHitDisengageBonus = 0.45f;
    [SerializeField] private float interruptedAttackDisengageBonus = 0.75f;
    [SerializeField] private float heavyHitAggressiveAttackPenalty = 0.75f;
    [SerializeField] private float heavyHitResetAttackBonus = 0.8f;
    [SerializeField] private float microHitPressureAttackBonus = 0.45f;

    [Header("Movement Selection")]
    [SerializeField] private bool useMovementSelection = true;
    [SerializeField] private bool movementCanCompeteWithAttacks = true;
    [SerializeField] private float movementAttackCompetitionMultiplier = 1.15f;
    [SerializeField] private float minimumCompetingMovementScore = 0.8f;
    [SerializeField] private float movementActionCooldown = 0.35f;
    [SerializeField] private float movementFallbackDuration = 1.2f;
    [SerializeField] private float approachDistance = 9.5f;
    [SerializeField] private float retreatDistance = 2.4f;
    [SerializeField] private float strafeDistance = 6f;
    [SerializeField] private float strafeSwitchInterval = 1.15f;
    [SerializeField] private float closeDodgeDistance = 2.1f;
    [SerializeField] private float aggressiveApproachDistance = 5.8f;
    [SerializeField] private float recentResultMovementMemoryTime = 3f;
    [SerializeField] private float movementScoreRandomness = 0.15f;

    [Header("Action Balance")]
    [SerializeField] private bool useActionBalance = true;
    [Tooltip("正值代表最近偏进攻，负值代表最近偏移动。")]
    [SerializeField] private float maxActionBalance = 3f;
    [SerializeField] private float attackBalanceGain = 1f;
    [SerializeField] private float movementBalanceGain = 1f;
    [SerializeField] private float actionBalanceDecayPerSecond = 0.45f;
    [SerializeField] private float actionBalanceScoreShift = 0.22f;

    [Header("Animator Parameters")]
    [SerializeField] private string attackTriggerName = "Attack";
    [SerializeField] private string attackComboTriggerName = "AttackCombo";
    [SerializeField] private string attackComboIndexParameterName = "AttackComboIndex";
    [SerializeField] private string attackIndexParameterName = "AttackIndex";
    [SerializeField] private string actingBoolName = "Attacking";
    [SerializeField] private string moveTriggerName = "Move";
    [SerializeField] private string moveModeParameterName = "MoveMode";
    [SerializeField] private string moveDirectionParameterName = "MoveDirection";
    [SerializeField] private string movingBoolName = "Moving";

    [Header("Debug")]
    [SerializeField] private bool logDebug;
    [SerializeField] private bool logPerceptionDebug;
    [SerializeField] private bool logCooldownDebug;
    [SerializeField] private bool logSelectionDebug;
    [SerializeField] private bool logAttackComboDebug;
    [SerializeField] private float perceptionDebugInterval = 0.25f;
    [SerializeField] private MovementScoreSnapshot lastMovementScore = new MovementScoreSnapshot();

    private AIActionState _state = AIActionState.Ready;
    private DistanceBand _distanceBand = DistanceBand.Far;
    private AngleBand _angleBand = AngleBand.Front;
    private float _thinkTimer;
    private float _actionFallbackTimer;
    private int _externalRecoveryLockCount;
    private float _recentPlayerDodgeTimer;
    private float _nextPerceptionDebugTime;
    private float _movementCooldownTimer;
    private float _strafeSwitchTimer;
    private float _actionBalance;
    private float _playerDistance;
    private float _playerFacingAngle;
    private int _currentAttackIndex;
    private AIMoveMode _currentMoveMode = AIMoveMode.Idle;
    private AIMoveDirection _currentMoveDirection = AIMoveDirection.Forward;
    private string _currentActionName;
    private AIIntent _currentIntent = AIIntent.Observe;
    private AttackComboProfile _activeCombo;
    private HealthPhaseInfluence _currentHealthPhase;
    private float _healthPhasePulseTimer;
    private int _healthPhasePulseSign = 1;
    private bool _currentActionIsAttack;
    private bool _strafeRightNext = true;
    private bool _hasPlayerTarget;
    private bool _playerIsMoving;
    private bool _playerIsRunning;
    private bool _playerIsAttacking;
    private bool _playerIsDodging;
    private bool _playerDodgeReady;

    public AIActionState State => _state;
    public DistanceBand CurrentDistanceBand => _distanceBand;
    public AngleBand CurrentAngleBand => _angleBand;
    public bool CanThink => aiEnabled && _state == AIActionState.Ready && !IsDead();
    public bool IsActing => _state == AIActionState.Acting;
    public bool CurrentActionIsAttack => _currentActionIsAttack;
    public bool HasPlayerTarget => _hasPlayerTarget;
    public bool PlayerIsMoving => _playerIsMoving;
    public bool PlayerIsRunning => _playerIsRunning;
    public bool PlayerIsAttacking => _playerIsAttacking;
    public bool PlayerIsDodging => _playerIsDodging;
    public bool PlayerDodgeReady => _playerDodgeReady;
    public float PlayerDistance => _playerDistance;
    public float PlayerFacingAngle => _playerFacingAngle;
    public int CurrentAttackIndex => _currentAttackIndex;
    public AIMoveMode CurrentMoveMode => _currentMoveMode;
    public AIMoveDirection CurrentMoveDirection => _currentMoveDirection;
    public string CurrentActionName => _currentActionName;
    public AIIntent CurrentIntent => _currentIntent;
    public string CurrentHealthPhaseName => _currentHealthPhase != null ? _currentHealthPhase.phaseName : "None";
    public MovementScoreSnapshot LastMovementScore => lastMovementScore;
    public EnemyHitMemory HitMemory => hitMemory;
    public float ActionBalance => _actionBalance;
    public int AttackCooldownCount => attackCooldowns != null ? attackCooldowns.Length : 0;

    public bool TryGetAttackCooldownDebugInfo(
        int index,
        out int attackIndex,
        out float remaining,
        out int otherUsesRemaining,
        out float cooldown)
    {
        attackIndex = 0;
        remaining = 0f;
        otherUsesRemaining = 0;
        cooldown = 0f;

        if (attackCooldowns == null || index < 0 || index >= attackCooldowns.Length || attackCooldowns[index] == null)
        {
            return false;
        }

        AttackCooldown entry = attackCooldowns[index];
        attackIndex = entry.attackIndex;
        remaining = entry.remaining;
        otherUsesRemaining = entry.otherAttackUsesRemaining;
        cooldown = entry.cooldown;
        return true;
    }

    [ContextMenu("Test Movement/Run Forward")]
    private void TestRunForward()
    {
        TestRequestMovement(AIMoveMode.Run, AIMoveDirection.Forward);
    }

    [ContextMenu("Test Movement/Walk Back")]
    private void TestWalkBack()
    {
        TestRequestMovement(AIMoveMode.Walk, AIMoveDirection.Back);
    }

    [ContextMenu("Test Movement/Walk Left")]
    private void TestWalkLeft()
    {
        TestRequestMovement(AIMoveMode.Walk, AIMoveDirection.Left);
    }

    [ContextMenu("Test Movement/Walk Right")]
    private void TestWalkRight()
    {
        TestRequestMovement(AIMoveMode.Walk, AIMoveDirection.Right);
    }

    [ContextMenu("Test Movement/Dodge Back")]
    private void TestDodgeBack()
    {
        TestRequestMovement(AIMoveMode.Dodge, AIMoveDirection.Back);
    }

    [ContextMenu("Test Movement/Dodge Left")]
    private void TestDodgeLeft()
    {
        TestRequestMovement(AIMoveMode.Dodge, AIMoveDirection.Left);
    }

    [ContextMenu("Test Movement/Dodge Right")]
    private void TestDodgeRight()
    {
        TestRequestMovement(AIMoveMode.Dodge, AIMoveDirection.Right);
    }

    [ContextMenu("Apply Default StrikeJaeger Attack Tuning")]
    private void ApplyDefaultStrikeJaegerAttackTuning()
    {
        attackCooldowns = new[]
        {
            new AttackCooldown { attackIndex = 1, cooldown = 0.8f, otherAttackUsesBeforeReuse = 1 },
            new AttackCooldown { attackIndex = 2, cooldown = 4.6f, otherAttackUsesBeforeReuse = 2 },
            new AttackCooldown { attackIndex = 3, cooldown = 1f, otherAttackUsesBeforeReuse = 1 },
            new AttackCooldown { attackIndex = 4, cooldown = 4.2f, otherAttackUsesBeforeReuse = 2 },
            new AttackCooldown { attackIndex = 5, cooldown = 3.8f, otherAttackUsesBeforeReuse = 1 },
            new AttackCooldown { attackIndex = 6, cooldown = 5f, otherAttackUsesBeforeReuse = 1 }
        };

        attackProfiles = new[]
        {
            new AttackProfile { attackIndex = 1, intent = AIIntent.Probe, minUsableDistance = 0f, maxUsableDistance = 3.4f, idealDistance = 1.4f, distanceTolerance = 2.2f, baseScore = 1.15f, pressureWeight = 0.004f, aggressionWeight = 0.004f },
            new AttackProfile { attackIndex = 2, intent = AIIntent.Reset, minUsableDistance = 0f, maxUsableDistance = 4.5f, idealDistance = 2.4f, distanceTolerance = 2.2f, baseScore = 0.45f, preferWhenPlayerAttacking = true, threatWeight = 0.004f, retreatWeight = 0.006f, aggressionWeight = -0.01f, burstWeight = -0.008f },
            new AttackProfile { attackIndex = 3, intent = AIIntent.Pressure, minUsableDistance = 0f, maxUsableDistance = 3.6f, idealDistance = 1.4f, distanceTolerance = 2.3f, baseScore = 1.25f, pressureWeight = 0.012f, aggressionWeight = 0.014f, threatWeight = -0.003f, retreatWeight = -0.008f },
            new AttackProfile { attackIndex = 4, intent = AIIntent.Disengage, minUsableDistance = 0f, maxUsableDistance = 4.8f, idealDistance = 2.4f, distanceTolerance = 2.2f, baseScore = 0.5f, preferWhenPlayerAttacking = true, threatWeight = 0.005f, retreatWeight = 0.007f, aggressionWeight = -0.008f },
            new AttackProfile { attackIndex = 5, intent = AIIntent.Burst, minUsableDistance = 2.2f, maxUsableDistance = 11f, idealDistance = 6f, distanceTolerance = 5.5f, baseScore = 1.45f, preferWhenPlayerDodgeOnCooldown = true, pressureWeight = 0.01f, aggressionWeight = 0.012f, burstWeight = 0.02f, threatWeight = -0.006f, retreatWeight = -0.01f },
            new AttackProfile { attackIndex = 6, intent = AIIntent.Pounce, minUsableDistance = 10f, maxUsableDistance = 24f, idealDistance = 18f, distanceTolerance = 8f, baseScore = 1.55f, preferWhenPlayerRunning = true, preferWhenPlayerDodgeOnCooldown = true, aggressionWeight = 0.012f, burstWeight = 0.022f, threatWeight = -0.012f, retreatWeight = -0.014f }
        };

        ValidateAttackCooldowns();
        ValidateAttackProfiles();
        ValidateAttackCombos();
    }

    [ContextMenu("Debug/Validate Attack Combo Setup")]
    private void DebugValidateAttackComboSetup()
    {
        ValidateAttackCombos();
        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }

        bool hasTrigger = HasAnimatorParameter(attackComboTriggerName, AnimatorControllerParameterType.Trigger);
        bool hasComboIndex = HasAnimatorParameter(attackComboIndexParameterName, AnimatorControllerParameterType.Int);
        bool hasAttackIndex = HasAnimatorParameter(attackIndexParameterName, AnimatorControllerParameterType.Int);
        Debug.Log(
            $"[StrikeJaegerAI][ComboSetup] useAttackCombos={useAttackCombos}, profiles={(attackComboProfiles != null ? attackComboProfiles.Length : 0)}, trigger={attackComboTriggerName}/exists={hasTrigger}, comboIndex={attackComboIndexParameterName}/exists={hasComboIndex}, attackIndex={attackIndexParameterName}/exists={hasAttackIndex}",
            this);

        if (attackComboProfiles == null)
        {
            return;
        }

        for (int i = 0; i < attackComboProfiles.Length; i++)
        {
            AttackComboProfile combo = attackComboProfiles[i];
            if (combo == null)
            {
                Debug.LogWarning($"[StrikeJaegerAI][ComboSetup] profile[{i}] is null.", this);
                continue;
            }

            Debug.Log(
                $"[StrikeJaegerAI][ComboSetup] profile[{i}] name={combo.comboName}, sourceAttack={combo.sourceAttackIndex}, targetType={combo.targetType}, comboIndex={combo.comboIndex}, targetMove={combo.targetMoveMode}/{combo.targetMoveDirection}, chance={combo.comboChance:F2}, cooldown={combo.comboCooldown:F2}, startReady={combo.startReady}, distance={combo.minUsableDistance:F1}-{combo.maxUsableDistance:F1}, intent={combo.intent}, baseScore={combo.baseScore:F2}",
                this);
        }
    }

    private void Awake()
    {
        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }

        if (health == null)
        {
            health = GetComponent<CombatHealth>();
        }

        if (combatMemory == null)
        {
            combatMemory = GetComponent<EnemyCombatMemory>();
        }

        if (combatMemory == null)
        {
            combatMemory = gameObject.AddComponent<EnemyCombatMemory>();
        }

        if (hitMemory == null)
        {
            hitMemory = GetComponent<EnemyHitMemory>();
        }

        if (hitMemory == null)
        {
            hitMemory = gameObject.AddComponent<EnemyHitMemory>();
        }

        if (pressureModel == null)
        {
            pressureModel = GetComponent<EnemyPressureModel>();
        }

        if (pressureModel == null)
        {
            pressureModel = gameObject.AddComponent<EnemyPressureModel>();
        }

        if (hitReactionController == null)
        {
            hitReactionController = GetComponent<EnemyHitReactionController>();
        }

        ResolvePlayerReferences();
        ValidateHealthPhaseInfluences();
        InitializeAttackCooldowns();
        InitializeAttackComboCooldowns();
        EnterReady(firstActionDelay);
    }

    private void OnEnable()
    {
        KianaCombatController.DodgeStarted += OnKianaDodgeStarted;
    }

    private void OnDisable()
    {
        KianaCombatController.DodgeStarted -= OnKianaDodgeStarted;
    }

    private void OnValidate()
    {
        pointBlankDistance = Mathf.Max(0.1f, pointBlankDistance);
        closeDistance = Mathf.Max(pointBlankDistance, closeDistance);
        midDistance = Mathf.Max(closeDistance, midDistance);
        playerMovingSpeedThreshold = Mathf.Max(0f, playerMovingSpeedThreshold);
        playerRunningSpeedThreshold = Mathf.Max(playerMovingSpeedThreshold, playerRunningSpeedThreshold);
        recentDodgeMemoryTime = Mathf.Max(0f, recentDodgeMemoryTime);
        continuousTurnSpeed = Mathf.Max(0f, continuousTurnSpeed);
        preAttackTurnSpeed = Mathf.Max(0f, preAttackTurnSpeed);
        prototypeAttackIndex = Mathf.Max(1, prototypeAttackIndex);
        firstActionDelay = Mathf.Max(0f, firstActionDelay);
        actionCooldown = Mathf.Max(0f, actionCooldown);
        actionFallbackDuration = Mathf.Max(0.1f, actionFallbackDuration);
        perceptionDebugInterval = Mathf.Max(0.01f, perceptionDebugInterval);
        movementAttackCompetitionMultiplier = Mathf.Max(0f, movementAttackCompetitionMultiplier);
        minimumCompetingMovementScore = Mathf.Max(0f, minimumCompetingMovementScore);
        highThreatIntentThreshold = Mathf.Max(0f, highThreatIntentThreshold);
        retreatIntentThreshold = Mathf.Max(0f, retreatIntentThreshold);
        burstIntentThreshold = Mathf.Max(0f, burstIntentThreshold);
        aggressionIntentThreshold = Mathf.Max(0f, aggressionIntentThreshold);
        ValidateHealthPhaseInfluences();
        recentHitPressureCount = Mathf.Max(1, recentHitPressureCount);
        heavyHitRetreatBonus = Mathf.Max(0f, heavyHitRetreatBonus);
        smallHitDisengageBonus = Mathf.Max(0f, smallHitDisengageBonus);
        microHitCounterBonus = Mathf.Max(0f, microHitCounterBonus);
        repeatedHitRetreatBonus = Mathf.Max(0f, repeatedHitRetreatBonus);
        sideOrBackHitDisengageBonus = Mathf.Max(0f, sideOrBackHitDisengageBonus);
        interruptedAttackDisengageBonus = Mathf.Max(0f, interruptedAttackDisengageBonus);
        heavyHitAggressiveAttackPenalty = Mathf.Max(0f, heavyHitAggressiveAttackPenalty);
        heavyHitResetAttackBonus = Mathf.Max(0f, heavyHitResetAttackBonus);
        microHitPressureAttackBonus = Mathf.Max(0f, microHitPressureAttackBonus);
        movementActionCooldown = Mathf.Max(0f, movementActionCooldown);
        movementFallbackDuration = Mathf.Max(0.1f, movementFallbackDuration);
        maxActionBalance = Mathf.Max(0f, maxActionBalance);
        attackBalanceGain = Mathf.Max(0f, attackBalanceGain);
        movementBalanceGain = Mathf.Max(0f, movementBalanceGain);
        actionBalanceDecayPerSecond = Mathf.Max(0f, actionBalanceDecayPerSecond);
        actionBalanceScoreShift = Mathf.Max(0f, actionBalanceScoreShift);
        approachDistance = Mathf.Max(0f, approachDistance);
        retreatDistance = Mathf.Max(0f, retreatDistance);
        strafeDistance = Mathf.Max(0f, strafeDistance);
        strafeSwitchInterval = Mathf.Max(0.1f, strafeSwitchInterval);
        closeDodgeDistance = Mathf.Max(0f, closeDodgeDistance);
        aggressiveApproachDistance = Mathf.Max(0f, aggressiveApproachDistance);
        recentResultMovementMemoryTime = Mathf.Max(0f, recentResultMovementMemoryTime);
        movementScoreRandomness = Mathf.Max(0f, movementScoreRandomness);
        ValidateAttackCooldowns();
        ValidateAttackProfiles();
        ValidateAttackCombos();
    }

    private void Update()
    {
        ResolvePlayerReferences();
        UpdatePerception();
        UpdateAttackCooldowns();
        UpdateAttackComboCooldowns();
        UpdateMovementCooldowns();
        UpdateHealthPhaseInfluence();
        UpdateActionBalance();
        UpdateFacing(Time.deltaTime);

        if (!aiEnabled || IsDead())
        {
            SetState(AIActionState.Disabled);
            return;
        }

        if (_state == AIActionState.Acting)
        {
            UpdateActionFallback();
            return;
        }

        if (_state == AIActionState.Recovering)
        {
            UpdateExternalRecoveryFallback();
            return;
        }

        if (_state != AIActionState.Ready)
        {
            return;
        }

        if (_thinkTimer > 0f)
        {
            _thinkTimer -= Time.deltaTime;
            return;
        }

        if (usePrototypeAttackLoop)
        {
            if (!RequestAttack(prototypeAttackIndex))
            {
                _thinkTimer = 0.1f;
            }
        }
        else if (useAttackSelection)
        {
            if (!TrySelectAndRequestAttack())
            {
                if (!TrySelectAndRequestMovement())
                {
                    _thinkTimer = 0.15f;
                }
            }
        }
        else if (useMovementSelection)
        {
            if (!TrySelectAndRequestMovement())
            {
                _thinkTimer = 0.15f;
            }
        }
    }

    public bool RequestAttack(int attackIndex)
    {
        CancelActiveCombo("DirectAttackRequest");
        return RequestAttackInternal(attackIndex, false);
    }

    private bool RequestAttackInternal(int attackIndex, bool ignoreCooldowns)
    {
        int normalizedAttackIndex = Mathf.Max(1, attackIndex);

        if (!CanThink)
        {
            LogDebug($"RequestAttack rejected. attackIndex={normalizedAttackIndex}, state={_state}, aiEnabled={aiEnabled}, dead={IsDead()}");
            return false;
        }

        if (!ignoreCooldowns)
        {
            TryApplyDesireReuseRelief(normalizedAttackIndex);
        }

        if (!ignoreCooldowns && !IsAttackReady(normalizedAttackIndex))
        {
            LogDebug(
                $"RequestAttack rejected by cooldown. attackIndex={normalizedAttackIndex}, timeRemaining={GetAttackCooldownRemaining(normalizedAttackIndex):F2}, otherUsesRemaining={GetAttackReuseCooldownRemaining(normalizedAttackIndex)}");
            return false;
        }

        _currentAttackIndex = normalizedAttackIndex;
        _currentMoveMode = AIMoveMode.Idle;
        _currentMoveDirection = AIMoveDirection.Forward;
        _currentActionName = $"Attack{_currentAttackIndex:00}";
        _currentActionIsAttack = true;

        FacePlayerBeforeAttack();

        if (!string.IsNullOrEmpty(attackIndexParameterName))
        {
            animator.SetInteger(attackIndexParameterName, _currentAttackIndex);
        }

        if (!string.IsNullOrEmpty(actingBoolName))
        {
            animator.SetBool(actingBoolName, true);
        }

        if (!string.IsNullOrEmpty(attackTriggerName))
        {
            animator.SetTrigger(attackTriggerName);
        }

        _actionFallbackTimer = actionFallbackDuration;
        StartAttackCooldown(_currentAttackIndex);
        SetState(AIActionState.Acting);
        RegisterAttackBalance();
        combatMemory?.NotifyActionStarted(_currentActionName, _currentAttackIndex);
        LogDebug($"Attack requested. attackIndex={_currentAttackIndex}, action={_currentActionName}");
        return true;
    }

    public bool IsAttackReady(int attackIndex)
    {
        return !useAttackCooldowns
            || (GetAttackCooldownRemaining(attackIndex) <= 0f
                && GetAttackReuseCooldownRemaining(attackIndex) <= 0);
    }

    public float GetAttackCooldownRemaining(int attackIndex)
    {
        AttackCooldown cooldown = FindAttackCooldown(attackIndex);
        return cooldown != null ? cooldown.remaining : 0f;
    }

    public int GetAttackReuseCooldownRemaining(int attackIndex)
    {
        AttackCooldown cooldown = FindAttackCooldown(attackIndex);
        return cooldown != null ? cooldown.otherAttackUsesRemaining : 0;
    }

    public bool TrySelectAndRequestAttack()
    {
        if (!CanThink)
        {
            return false;
        }

        _currentIntent = SelectIntent();
        AttackProfile selectedProfile = SelectAttackProfile(_currentIntent, out float selectedScore);
        bool hasMovementCandidate = TrySelectMovementCandidate(
            out AIMoveMode movementMode,
            out AIMoveDirection movementDirection,
            out float movementScore);
        ApplyActionBalance(
            selectedScore,
            movementScore,
            out float balancedAttackScore,
            out float balancedMovementScore);
        float competingMovementScore = balancedMovementScore * Mathf.Max(0f, movementAttackCompetitionMultiplier);

        if (movementCanCompeteWithAttacks
            && hasMovementCandidate
            && movementScore >= minimumCompetingMovementScore
            && competingMovementScore >= balancedAttackScore)
        {
            LogSelectionDebug(
                $"Movement won action competition. intent={_currentIntent}, movement={movementMode}/{movementDirection}, movementScore={movementScore:F2}, balancedMovement={balancedMovementScore:F2}, weighted={competingMovementScore:F2}, attackScore={selectedScore:F2}, balancedAttack={balancedAttackScore:F2}, actionBalance={_actionBalance:F2}");
            return RequestMovement(movementMode, movementDirection);
        }

        if (selectedProfile == null)
        {
            LogSelectionDebug($"No attack selected. intent={_currentIntent}, distance={_playerDistance:F2}");
            return false;
        }

        LogSelectionDebug(
            $"Attack selected. intent={_currentIntent}, attackIndex={selectedProfile.attackIndex}, score={selectedScore:F2}, balancedScore={balancedAttackScore:F2}, movementScore={movementScore:F2}, balancedMovement={balancedMovementScore:F2}, actionBalance={_actionBalance:F2}, distance={_playerDistance:F2}");
        return RequestAttack(selectedProfile.attackIndex);
    }

    public bool TrySelectAndRequestMovement()
    {
        if (!CanThink)
        {
            return false;
        }

        if (!TrySelectMovementCandidate(out AIMoveMode moveMode, out AIMoveDirection moveDirection, out _))
        {
            return false;
        }

        return RequestMovement(moveMode, moveDirection);
    }

    private bool TrySelectMovementCandidate(out AIMoveMode moveMode, out AIMoveDirection moveDirection, out float score)
    {
        moveMode = AIMoveMode.Idle;
        moveDirection = AIMoveDirection.Forward;
        score = 0f;

        if (!useMovementSelection || _movementCooldownTimer > 0f)
        {
            return false;
        }

        return SelectMovement(out moveMode, out moveDirection, out score);
    }

    public void AE_TryEnemyAttackCombo()
    {
        LogComboDebug("AE_TryEnemyAttackCombo called.");
        TryTriggerAttackComboFromCurrentAttack();
    }

    public void AE_TryAttackCombo()
    {
        LogComboDebug("AE_TryAttackCombo called.");
        TryTriggerAttackComboFromCurrentAttack();
    }

    private bool TryTriggerAttackComboFromCurrentAttack()
    {
        if (!useAttackCombos || attackComboProfiles == null || attackComboProfiles.Length == 0)
        {
            LogComboDebug($"Combo check skipped. useAttackCombos={useAttackCombos}, profiles={(attackComboProfiles != null ? attackComboProfiles.Length : 0)}");
            return false;
        }

        LogComboDebug(
            $"Combo check started. state={_state}, currentAction={_currentActionName}, currentAttackIndex={_currentAttackIndex}, isAttack={_currentActionIsAttack}, intent={_currentIntent}, distance={_playerDistance:F2}");

        if (_state != AIActionState.Acting || !_currentActionIsAttack || _currentAttackIndex <= 0)
        {
            LogComboDebug($"Combo check rejected by action state. state={_state}, attack={_currentActionIsAttack}, attackIndex={_currentAttackIndex}");
            return false;
        }

        AttackComboProfile selectedCombo = SelectAttackComboContinuation(out float selectedScore);
        if (selectedCombo == null)
        {
            LogComboDebug($"Combo check failed. No profile passed. sourceAttack={_currentAttackIndex}, distance={_playerDistance:F2}");
            return false;
        }

        selectedCombo.remaining = selectedCombo.comboCooldown;
        _activeCombo = selectedCombo;
        _currentActionName = selectedCombo.comboName;
        int selectedComboIndex = Mathf.Max(1, selectedCombo.comboIndex);

        if (HasAnimatorParameter(attackComboIndexParameterName, AnimatorControllerParameterType.Int))
        {
            animator.SetInteger(attackComboIndexParameterName, selectedComboIndex);
        }
        else
        {
            LogComboDebug($"Animator parameter missing or wrong type. parameter={attackComboIndexParameterName}, expected=Int");
        }

        if (selectedCombo.targetType == ComboTargetType.Attack)
        {
            _currentAttackIndex = selectedComboIndex;
            _currentMoveMode = AIMoveMode.Idle;
            _currentMoveDirection = AIMoveDirection.Forward;
            _currentActionIsAttack = true;
            RegisterAttackBalance();

            if (HasAnimatorParameter(attackIndexParameterName, AnimatorControllerParameterType.Int))
            {
                animator.SetInteger(attackIndexParameterName, _currentAttackIndex);
            }
            else
            {
                LogComboDebug($"Animator parameter missing or wrong type. parameter={attackIndexParameterName}, expected=Int");
            }
        }
        else
        {
            _currentAttackIndex = selectedComboIndex;
            _currentMoveMode = selectedCombo.targetMoveMode;
            _currentMoveDirection = selectedCombo.targetMoveDirection;
            _currentActionIsAttack = false;
            _actionFallbackTimer = movementFallbackDuration;
            RegisterMovementBalance();

            if (!string.IsNullOrEmpty(moveModeParameterName))
            {
                animator.SetInteger(moveModeParameterName, (int)_currentMoveMode);
            }

            if (!string.IsNullOrEmpty(moveDirectionParameterName))
            {
                animator.SetInteger(moveDirectionParameterName, (int)_currentMoveDirection);
            }

            if (!string.IsNullOrEmpty(movingBoolName))
            {
                animator.SetBool(movingBoolName, true);
            }
        }

        if (HasAnimatorParameter(attackComboTriggerName, AnimatorControllerParameterType.Trigger))
        {
            animator.SetTrigger(attackComboTriggerName);
        }
        else
        {
            LogComboDebug($"Animator parameter missing or wrong type. parameter={attackComboTriggerName}, expected=Trigger");
        }

        LogCooldownDebug($"Combo cooldown started. combo={selectedCombo.comboName}, duration={selectedCombo.comboCooldown:F2}");
        LogDebug($"Attack combo permitted. sourceAttack={selectedCombo.sourceAttackIndex}, targetType={selectedCombo.targetType}, comboIndex={selectedComboIndex}, targetMove={selectedCombo.targetMoveMode}/{selectedCombo.targetMoveDirection}, combo={selectedCombo.comboName}, score={selectedScore:F2}, trigger={attackComboTriggerName}");
        return true;
    }

    public bool RequestMovement(AIMoveMode moveMode, AIMoveDirection moveDirection)
    {
        if (!CanThink)
        {
            LogDebug($"RequestMovement rejected. moveMode={moveMode}, moveDirection={moveDirection}, state={_state}");
            return false;
        }

        CancelActiveCombo("MovementRequest");

        _currentAttackIndex = 0;
        _currentMoveMode = moveMode;
        _currentMoveDirection = moveDirection;
        _currentActionName = $"{moveMode}_{moveDirection}";
        _currentActionIsAttack = false;

        if (!string.IsNullOrEmpty(moveModeParameterName))
        {
            animator.SetInteger(moveModeParameterName, (int)_currentMoveMode);
        }

        if (!string.IsNullOrEmpty(moveDirectionParameterName))
        {
            animator.SetInteger(moveDirectionParameterName, (int)_currentMoveDirection);
        }

        if (!string.IsNullOrEmpty(movingBoolName))
        {
            animator.SetBool(movingBoolName, true);
        }

        if (!string.IsNullOrEmpty(moveTriggerName))
        {
            animator.SetTrigger(moveTriggerName);
        }

        _movementCooldownTimer = movementActionCooldown;
        _actionFallbackTimer = movementFallbackDuration;
        SetState(AIActionState.Acting);
        RegisterMovementBalance();
        LogSelectionDebug($"Movement selected. mode={_currentMoveMode}, direction={_currentMoveDirection}, distance={_playerDistance:F2}");
        return true;
    }

    private void TestRequestMovement(AIMoveMode moveMode, AIMoveDirection moveDirection)
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[StrikeJaegerAI] Movement test can only run in Play Mode.", this);
            return;
        }

        if (_state == AIActionState.Acting)
        {
            FinishCurrentAction("TestMovementOverride");
        }

        aiEnabled = true;
        _thinkTimer = 0f;
        _movementCooldownTimer = 0f;
        if (_state == AIActionState.Disabled)
        {
            SetState(AIActionState.Ready);
        }

        bool requested = RequestMovement(moveMode, moveDirection);
        Debug.Log($"[StrikeJaegerAI][MovementTest] requested={requested}, mode={moveMode}, direction={moveDirection}", this);
    }

    public void AE_EnemyActionFinished()
    {
        FinishCurrentAction("AE_EnemyActionFinished");
    }

    public void AE_EnemyAttackFinished()
    {
        FinishCurrentAction("AE_EnemyAttackFinished");
    }

    public void AE_EnemyMovementFinished()
    {
        FinishCurrentAction("AE_EnemyMovementFinished");
    }

    public void ForceFinishCurrentAction()
    {
        FinishCurrentAction("ForceFinishCurrentAction");
    }

    public void BeginExternalRecovery(float fallbackDuration)
    {
        if (_state == AIActionState.Acting)
        {
            if (_currentActionIsAttack)
            {
                combatMemory?.NotifyInterrupted();
            }

            FinishCurrentAction("ExternalRecoveryInterrupt");
        }

        CancelActiveCombo("ExternalRecovery");
        ClearAnimatorActionFlags();
        _actionFallbackTimer = Mathf.Max(0.1f, fallbackDuration);
        _thinkTimer = 0f;
        SetState(AIActionState.Recovering);
        LogDebug($"External recovery started. fallback={_actionFallbackTimer:F2}");
    }

    public void EndExternalRecovery(float readyDelay = 0f)
    {
        if (_state != AIActionState.Recovering)
        {
            return;
        }

        if (_externalRecoveryLockCount > 0)
        {
            LogDebug($"External recovery end delayed by lock. locks={_externalRecoveryLockCount}, readyDelay={readyDelay:F2}");
            return;
        }

        LogDebug($"External recovery ended. readyDelay={readyDelay:F2}");
        EnterReady(readyDelay);
    }

    public void AddExternalRecoveryLock(string reason)
    {
        _externalRecoveryLockCount++;
        LogDebug($"External recovery lock added. reason={reason}, locks={_externalRecoveryLockCount}");
    }

    public void RemoveExternalRecoveryLock(string reason)
    {
        _externalRecoveryLockCount = Mathf.Max(0, _externalRecoveryLockCount - 1);
        LogDebug($"External recovery lock removed. reason={reason}, locks={_externalRecoveryLockCount}");
    }

    public void SetAIEnabled(bool enabled)
    {
        aiEnabled = enabled;
        if (!aiEnabled)
        {
            CancelActiveCombo("AI Disabled");
            ClearAnimatorActionFlags();
            SetState(AIActionState.Disabled);
        }
        else if (_state == AIActionState.Disabled)
        {
            EnterReady(firstActionDelay);
        }
    }

    public void OnCombatHealthDied(CombatHealth deadHealth)
    {
        if (health != null && deadHealth != null && deadHealth != health)
        {
            return;
        }

        CancelActiveCombo("CombatHealthDied");
        ClearAnimatorActionFlags();
        _externalRecoveryLockCount = 0;
        aiEnabled = false;
        SetState(AIActionState.Disabled);
        LogDebug("Combat health died. AI disabled.");
    }

    private void FinishCurrentAction(string reason)
    {
        if (_state != AIActionState.Acting)
        {
            LogDebug($"Finish ignored. reason={reason}, state={_state}");
            return;
        }

        ClearAnimatorActionFlags();
        bool finishedAttack = _currentActionIsAttack;
        bool finishedCombo = _activeCombo != null;
        float readyDelay = finishedAttack ? actionCooldown : movementActionCooldown;
        LogDebug(
            $"Action finished. reason={reason}, action={_currentActionName}, attack={finishedAttack}, combo={finishedCombo}");
        if (_currentActionIsAttack)
        {
            combatMemory?.NotifyActionFinished(_currentActionName, _currentAttackIndex);
        }

        if (finishedCombo)
        {
            CancelActiveCombo("ComboFinished");
        }

        _currentAttackIndex = 0;
        _currentMoveMode = AIMoveMode.Idle;
        _currentMoveDirection = AIMoveDirection.Forward;
        _currentActionName = string.Empty;
        _currentActionIsAttack = false;
        EnterReady(readyDelay);
    }

    private void UpdateActionFallback()
    {
        _actionFallbackTimer -= Time.deltaTime;
        if (_actionFallbackTimer > 0f)
        {
            return;
        }

        LogDebug($"Action fallback reached. action={_currentActionName}");
        FinishCurrentAction("Fallback");
    }

    private void UpdateExternalRecoveryFallback()
    {
        if (_externalRecoveryLockCount > 0)
        {
            return;
        }

        _actionFallbackTimer -= Time.deltaTime;
        if (_actionFallbackTimer > 0f)
        {
            return;
        }

        LogDebug("External recovery fallback reached.");
        EndExternalRecovery(actionCooldown);
    }

    private void EnterReady(float delay)
    {
        _thinkTimer = Mathf.Max(0f, delay);
        _actionFallbackTimer = 0f;
        SetState(AIActionState.Ready);
        LogDebug($"Ready entered. delay={_thinkTimer:F2}");
    }

    private void CancelActiveCombo(string reason)
    {
        if (_activeCombo == null)
        {
            return;
        }

        LogDebug($"Combo cleared. reason={reason}, combo={_activeCombo.comboName}");
        _activeCombo = null;
    }

    private void InitializeAttackCooldowns()
    {
        ValidateAttackCooldowns();

        for (int i = 0; i < attackCooldowns.Length; i++)
        {
            AttackCooldown cooldown = attackCooldowns[i];
            if (cooldown == null)
            {
                continue;
            }

            cooldown.remaining = cooldown.startReady ? 0f : cooldown.cooldown;
            cooldown.otherAttackUsesRemaining = 0;
        }
    }

    private void ValidateAttackCooldowns()
    {
        if (attackCooldowns == null)
        {
            attackCooldowns = new AttackCooldown[0];
            return;
        }

        for (int i = 0; i < attackCooldowns.Length; i++)
        {
            if (attackCooldowns[i] == null)
            {
                continue;
            }

            attackCooldowns[i].attackIndex = Mathf.Max(1, attackCooldowns[i].attackIndex);
            attackCooldowns[i].cooldown = Mathf.Max(0f, attackCooldowns[i].cooldown);
            attackCooldowns[i].otherAttackUsesBeforeReuse = Mathf.Max(0, attackCooldowns[i].otherAttackUsesBeforeReuse);
            attackCooldowns[i].reuseReliefThresholdPercent = Mathf.Clamp01(attackCooldowns[i].reuseReliefThresholdPercent);
            attackCooldowns[i].reuseReliefAmount = Mathf.Max(0, attackCooldowns[i].reuseReliefAmount);
            attackCooldowns[i].reuseReliefCooldown = Mathf.Max(0f, attackCooldowns[i].reuseReliefCooldown);
        }
    }

    private void ValidateHealthPhaseInfluences()
    {
        if (healthPhaseInfluences == null)
        {
            healthPhaseInfluences = new HealthPhaseInfluence[0];
            return;
        }

        for (int i = 0; i < healthPhaseInfluences.Length; i++)
        {
            HealthPhaseInfluence phase = healthPhaseInfluences[i];
            if (phase == null)
            {
                continue;
            }

            phase.minHealthPercent = Mathf.Clamp01(phase.minHealthPercent);
            phase.maxHealthPercent = Mathf.Clamp01(Mathf.Max(phase.minHealthPercent, phase.maxHealthPercent));
            phase.attackScoreMultiplier = Mathf.Max(0f, phase.attackScoreMultiplier);
            phase.lightAttackMultiplier = Mathf.Max(0f, phase.lightAttackMultiplier);
            phase.heavyAttackMultiplier = Mathf.Max(0f, phase.heavyAttackMultiplier);
            phase.daggerAttackMultiplier = Mathf.Max(0f, phase.daggerAttackMultiplier);
            phase.retreatAttackMultiplier = Mathf.Max(0f, phase.retreatAttackMultiplier);
            phase.runForwardMultiplier = Mathf.Max(0f, phase.runForwardMultiplier);
            phase.walkBackMultiplier = Mathf.Max(0f, phase.walkBackMultiplier);
            phase.strafeMultiplier = Mathf.Max(0f, phase.strafeMultiplier);
            phase.dodgeBackMultiplier = Mathf.Max(0f, phase.dodgeBackMultiplier);
            phase.dodgeSideMultiplier = Mathf.Max(0f, phase.dodgeSideMultiplier);
            phase.attackComboMultiplier = Mathf.Max(0f, phase.attackComboMultiplier);
            phase.movementComboMultiplier = Mathf.Max(0f, phase.movementComboMultiplier);
            phase.pulseInterval = Mathf.Max(0.1f, phase.pulseInterval);
            phase.attackPulseAmount = Mathf.Max(0f, phase.attackPulseAmount);
            phase.movementPulseAmount = Mathf.Max(0f, phase.movementPulseAmount);
        }
    }

    private void ValidateAttackProfiles()
    {
        if (attackProfiles == null)
        {
            attackProfiles = new AttackProfile[0];
            return;
        }

        for (int i = 0; i < attackProfiles.Length; i++)
        {
            AttackProfile profile = attackProfiles[i];
            if (profile == null)
            {
                continue;
            }

            profile.attackIndex = Mathf.Max(1, profile.attackIndex);
            profile.minUsableDistance = Mathf.Max(0f, profile.minUsableDistance);
            profile.maxUsableDistance = Mathf.Max(profile.minUsableDistance, profile.maxUsableDistance);
            profile.idealDistance = Mathf.Clamp(profile.idealDistance, profile.minUsableDistance, profile.maxUsableDistance);
            profile.distanceTolerance = Mathf.Max(0.01f, profile.distanceTolerance);
            profile.baseScore = Mathf.Max(0f, profile.baseScore);
        }
    }

    private void InitializeAttackComboCooldowns()
    {
        ValidateAttackCombos();

        for (int i = 0; i < attackComboProfiles.Length; i++)
        {
            AttackComboProfile combo = attackComboProfiles[i];
            if (combo == null)
            {
                continue;
            }

            combo.remaining = combo.startReady ? 0f : combo.comboCooldown;
        }
    }

    private void ValidateAttackCombos()
    {
        if (attackComboProfiles == null)
        {
            attackComboProfiles = new AttackComboProfile[0];
            return;
        }

        for (int i = 0; i < attackComboProfiles.Length; i++)
        {
            AttackComboProfile combo = attackComboProfiles[i];
            if (combo == null)
            {
                continue;
            }

            combo.sourceAttackIndex = Mathf.Max(1, combo.sourceAttackIndex);
            combo.comboIndex = Mathf.Max(1, combo.comboIndex);
            combo.comboChance = Mathf.Clamp01(combo.comboChance);
            combo.minUsableDistance = Mathf.Max(0f, combo.minUsableDistance);
            combo.maxUsableDistance = Mathf.Max(combo.minUsableDistance, combo.maxUsableDistance);
            combo.idealDistance = Mathf.Clamp(combo.idealDistance, combo.minUsableDistance, combo.maxUsableDistance);
            combo.distanceTolerance = Mathf.Max(0.01f, combo.distanceTolerance);
            combo.baseScore = Mathf.Max(0f, combo.baseScore);
            combo.comboCooldown = Mathf.Max(0f, combo.comboCooldown);
        }
    }

    private AIIntent SelectIntent()
    {
        if (!_hasPlayerTarget)
        {
            return AIIntent.Observe;
        }

        AIIntent hitMemoryIntent = SelectHitMemoryIntent();
        if (hitMemoryIntent != AIIntent.Observe)
        {
            return hitMemoryIntent;
        }

        AIIntent pressureIntent = SelectPressureIntent();
        if (pressureIntent != AIIntent.Observe)
        {
            return pressureIntent;
        }

        if (_playerIsAttacking && (_distanceBand == DistanceBand.Close || _distanceBand == DistanceBand.PointBlank))
        {
            return AIIntent.Disengage;
        }

        if (!_playerDodgeReady && (_distanceBand == DistanceBand.Close || _distanceBand == DistanceBand.PointBlank))
        {
            return AIIntent.Burst;
        }

        if (_playerIsRunning && (_distanceBand == DistanceBand.Far || _distanceBand == DistanceBand.Mid))
        {
            return AIIntent.Pounce;
        }

        if (_distanceBand == DistanceBand.Far)
        {
            return AIIntent.Pounce;
        }

        if (_distanceBand == DistanceBand.Mid)
        {
            return pressureModel != null && pressureModel.AggressionLevel >= aggressionIntentThreshold * 0.65f
                ? AIIntent.Burst
                : AIIntent.Pounce;
        }

        return AIIntent.Pressure;
    }

    private AIIntent SelectHitMemoryIntent()
    {
        if (!useHitMemoryDecisionInfluence || hitMemory == null || !hitMemory.WasRecentlyHit)
        {
            return AIIntent.Observe;
        }

        if (hitMemory.LastHitInterruptedAttack)
        {
            return _distanceBand == DistanceBand.Close || _distanceBand == DistanceBand.PointBlank
                ? AIIntent.Disengage
                : AIIntent.Reset;
        }

        if (hitMemory.WasRecentlyLaunchedOrHeavyHit || hitMemory.LastHitImpact == EnemyHitMemory.HitImpact.Heavy)
        {
            return _distanceBand == DistanceBand.Close || _distanceBand == DistanceBand.PointBlank
                ? AIIntent.Disengage
                : AIIntent.Reset;
        }

        if (hitMemory.RecentHitCount >= recentHitPressureCount)
        {
            return _distanceBand == DistanceBand.Close || _distanceBand == DistanceBand.PointBlank
                ? AIIntent.Disengage
                : AIIntent.Burst;
        }

        if (hitMemory.LastHitDirection == EnemyHitMemory.HitDirection.Back ||
            hitMemory.LastHitDirection == EnemyHitMemory.HitDirection.Side)
        {
            return _distanceBand == DistanceBand.Close || _distanceBand == DistanceBand.PointBlank
                ? AIIntent.Disengage
                : AIIntent.Reset;
        }

        if (hitMemory.LastHitImpact == EnemyHitMemory.HitImpact.Micro)
        {
            return _distanceBand == DistanceBand.Far ? AIIntent.Pounce : AIIntent.Pressure;
        }

        return AIIntent.Observe;
    }

    private AIIntent SelectPressureIntent()
    {
        if (pressureModel == null)
        {
            return AIIntent.Observe;
        }

        float threat = GetHealthAdjustedDesire(DesireGate.Threat);
        float retreat = GetHealthAdjustedDesire(DesireGate.Retreat);
        float burst = GetHealthAdjustedDesire(DesireGate.Burst);
        float aggression = GetHealthAdjustedDesire(DesireGate.Aggression);

        if (threat >= highThreatIntentThreshold ||
            retreat >= retreatIntentThreshold)
        {
            return _distanceBand == DistanceBand.Close || _distanceBand == DistanceBand.PointBlank
                ? AIIntent.Disengage
                : AIIntent.Reset;
        }

        if (burst >= burstIntentThreshold)
        {
            return _distanceBand == DistanceBand.Far ? AIIntent.Pounce : AIIntent.Burst;
        }

        if (aggression >= aggressionIntentThreshold)
        {
            if (_distanceBand == DistanceBand.Far)
            {
                return AIIntent.Pounce;
            }

            return _distanceBand == DistanceBand.Mid ? AIIntent.Burst : AIIntent.Pressure;
        }

        return AIIntent.Observe;
    }

    private AttackProfile SelectAttackProfile(AIIntent intent, out float selectedScore)
    {
        selectedScore = float.MinValue;
        AttackProfile selectedProfile = null;

        if (attackProfiles == null)
        {
            return null;
        }

        for (int i = 0; i < attackProfiles.Length; i++)
        {
            AttackProfile profile = attackProfiles[i];
            if (!CanUseAttackProfile(profile, intent, out float score))
            {
                continue;
            }

            LogSelectionDebug(
                $"Candidate attackIndex={profile.attackIndex}, intent={profile.intent}, score={score:F2}, cd={GetAttackCooldownRemaining(profile.attackIndex):F2}, reuse={GetAttackReuseCooldownRemaining(profile.attackIndex)}, distance={_playerDistance:F2}");

            if (score > selectedScore)
            {
                selectedScore = score;
                selectedProfile = profile;
            }
        }

        return selectedProfile;
    }

    private AttackComboProfile SelectAttackComboContinuation(out float selectedScore)
    {
        selectedScore = 0f;
        float totalWeight = 0f;

        if (!useAttackCombos || attackComboProfiles == null)
        {
            return null;
        }

        AttackComboProfile selectedCombo = null;
        for (int i = 0; i < attackComboProfiles.Length; i++)
        {
            AttackComboProfile combo = attackComboProfiles[i];
            if (!CanUseAttackComboProfile(combo, _currentIntent, out float score))
            {
                continue;
            }

            float chanceRoll = Random.value;
            if (chanceRoll > combo.comboChance)
            {
                LogComboDebug(
                    $"Candidate combo rejected by chance. source={_currentAttackIndex}, combo={combo.comboName}, chance={combo.comboChance:F2}, roll={chanceRoll:F2}");
                continue;
            }

            LogComboDebug(
                $"Candidate combo accepted. source={_currentAttackIndex}, combo={combo.comboName}, comboIndex={combo.comboIndex}, intent={combo.intent}, score={score:F2}, cd={combo.remaining:F2}, chance={combo.comboChance:F2}, trigger={attackComboTriggerName}, distance={_playerDistance:F2}");

            totalWeight += score;
            if (Random.value * totalWeight <= score)
            {
                selectedScore = score;
                selectedCombo = combo;
            }
        }

        return selectedCombo;
    }

    private bool CanUseAttackProfile(AttackProfile profile, AIIntent intent, out float score)
    {
        score = 0f;
        if (profile == null)
        {
            return false;
        }

        TryApplyDesireReuseRelief(profile.attackIndex);
        if (!IsAttackReady(profile.attackIndex))
        {
            return false;
        }

        if (_playerDistance < profile.minUsableDistance || _playerDistance > profile.maxUsableDistance)
        {
            return false;
        }

        bool intentMatches = profile.intent == intent;
        if (!intentMatches && !allowIntentFallback)
        {
            return false;
        }

        score = profile.baseScore + CalculateDistanceScore(profile);
        score += CalculatePressureScore(profile);
        score += CalculateHitMemoryAttackScore(profile.intent, profile.attackIndex);
        if (!intentMatches)
        {
            score -= intentMismatchPenalty;
        }

        if (profile.preferWhenPlayerDodgeOnCooldown && !_playerDodgeReady)
        {
            score += dodgeCooldownPreferenceBonus;
        }

        if (profile.preferWhenPlayerRunning && _playerIsRunning)
        {
            score += runningPreferenceBonus;
        }

        if (profile.preferWhenPlayerAttacking && _playerIsAttacking)
        {
            score += attackingPreferenceBonus;
        }

        score = ApplyHealthAttackInfluence(profile, score);
        return score > 0f;
    }

    private bool CanUseAttackComboProfile(AttackComboProfile combo, AIIntent intent, out float score)
    {
        score = 0f;
        if (combo == null)
        {
            LogComboDebug("Candidate combo rejected. profile=null");
            return false;
        }

        if (combo.sourceAttackIndex != _currentAttackIndex)
        {
            LogComboDebug(
                $"Candidate combo rejected by source. combo={combo.comboName}, requiredSource={combo.sourceAttackIndex}, currentAttack={_currentAttackIndex}");
            return false;
        }

        if (combo.remaining > 0f)
        {
            LogComboDebug(
                $"Candidate combo rejected by cooldown. combo={combo.comboName}, remaining={combo.remaining:F2}");
            return false;
        }

        if (_playerDistance < combo.minUsableDistance || _playerDistance > combo.maxUsableDistance)
        {
            LogComboDebug(
                $"Candidate combo rejected by distance. combo={combo.comboName}, distance={_playerDistance:F2}, range={combo.minUsableDistance:F2}-{combo.maxUsableDistance:F2}");
            return false;
        }

        bool intentMatches = combo.intent == intent;
        if (!intentMatches && !allowIntentFallback)
        {
            LogComboDebug(
                $"Candidate combo rejected by intent. combo={combo.comboName}, required={combo.intent}, current={intent}, allowFallback={allowIntentFallback}");
            return false;
        }

        score = combo.baseScore + CalculateComboDistanceScore(combo) + CalculateComboPressureScore(combo);
        if (combo.targetType == ComboTargetType.Attack)
        {
            score += CalculateHitMemoryAttackScore(combo.intent, combo.comboIndex);
        }
        if (!intentMatches)
        {
            score -= intentMismatchPenalty;
        }

        if (combo.preferWhenPlayerDodgeOnCooldown && !_playerDodgeReady)
        {
            score += dodgeCooldownPreferenceBonus;
        }

        if (combo.preferWhenPlayerRunning && _playerIsRunning)
        {
            score += runningPreferenceBonus;
        }

        if (combo.preferWhenPlayerAttacking && _playerIsAttacking)
        {
            score += attackingPreferenceBonus;
        }

        score = ApplyHealthComboInfluence(combo, score);
        if (score <= 0f)
        {
            LogComboDebug(
                $"Candidate combo rejected by score. combo={combo.comboName}, score={score:F2}, base={combo.baseScore:F2}, intentMatches={intentMatches}, distance={_playerDistance:F2}");
            return false;
        }

        return true;
    }

    private float CalculateDistanceScore(AttackProfile profile)
    {
        float distanceError = Mathf.Abs(_playerDistance - profile.idealDistance);
        return Mathf.Clamp01(1f - distanceError / profile.distanceTolerance);
    }

    private float CalculateComboDistanceScore(AttackComboProfile combo)
    {
        float distanceError = Mathf.Abs(_playerDistance - combo.idealDistance);
        return Mathf.Clamp01(1f - distanceError / combo.distanceTolerance);
    }

    private float GetHealthAdjustedDesire(DesireGate desireGate)
    {
        float value = GetRawDesireValue(desireGate);
        if (_currentHealthPhase == null)
        {
            return value;
        }

        return Mathf.Max(0f, value + GetHealthPhaseDesireAdd(_currentHealthPhase, desireGate));
    }

    private float GetRawDesireValue(DesireGate desireGate)
    {
        if (pressureModel == null)
        {
            return 0f;
        }

        switch (desireGate)
        {
            case DesireGate.Pressure:
                return pressureModel.PressureLevel;
            case DesireGate.Threat:
                return pressureModel.ThreatenedLevel;
            case DesireGate.Aggression:
                return pressureModel.AggressionLevel;
            case DesireGate.Retreat:
                return pressureModel.RetreatDesire;
            case DesireGate.Burst:
                return pressureModel.BurstDesire;
            default:
                return 0f;
        }
    }

    private float GetHealthPhaseDesireAdd(HealthPhaseInfluence phase, DesireGate desireGate)
    {
        if (phase == null)
        {
            return 0f;
        }

        switch (desireGate)
        {
            case DesireGate.Pressure:
                return phase.pressureAdd;
            case DesireGate.Threat:
                return phase.threatAdd;
            case DesireGate.Aggression:
                return phase.aggressionAdd;
            case DesireGate.Retreat:
                return phase.retreatAdd;
            case DesireGate.Burst:
                return phase.burstAdd;
            default:
                return 0f;
        }
    }

    private float GetDesireThreshold(DesireGate desireGate)
    {
        switch (desireGate)
        {
            case DesireGate.Pressure:
                return aggressionIntentThreshold;
            case DesireGate.Threat:
                return highThreatIntentThreshold;
            case DesireGate.Aggression:
                return aggressionIntentThreshold;
            case DesireGate.Retreat:
                return retreatIntentThreshold;
            case DesireGate.Burst:
                return burstIntentThreshold;
            default:
                return 0f;
        }
    }

    private float CalculatePressureScore(AttackProfile profile)
    {
        if (pressureModel == null)
        {
            return 0f;
        }

        return GetHealthAdjustedDesire(DesireGate.Pressure) * profile.pressureWeight +
            GetHealthAdjustedDesire(DesireGate.Threat) * profile.threatWeight +
            GetHealthAdjustedDesire(DesireGate.Aggression) * profile.aggressionWeight +
            GetHealthAdjustedDesire(DesireGate.Retreat) * profile.retreatWeight +
            GetHealthAdjustedDesire(DesireGate.Burst) * profile.burstWeight;
    }

    private float CalculateComboPressureScore(AttackComboProfile combo)
    {
        if (pressureModel == null)
        {
            return 0f;
        }

        return GetHealthAdjustedDesire(DesireGate.Pressure) * combo.pressureWeight +
            GetHealthAdjustedDesire(DesireGate.Threat) * combo.threatWeight +
            GetHealthAdjustedDesire(DesireGate.Aggression) * combo.aggressionWeight +
            GetHealthAdjustedDesire(DesireGate.Retreat) * combo.retreatWeight +
            GetHealthAdjustedDesire(DesireGate.Burst) * combo.burstWeight;
    }

    private float ApplyHealthAttackInfluence(AttackProfile profile, float score)
    {
        if (_currentHealthPhase == null || score <= 0f)
        {
            return score;
        }

        float multiplier = _currentHealthPhase.attackScoreMultiplier;
        if (IsLightAttackProfile(profile))
        {
            multiplier *= _currentHealthPhase.lightAttackMultiplier;
        }

        if (IsHeavyAttackProfile(profile))
        {
            multiplier *= _currentHealthPhase.heavyAttackMultiplier;
        }

        if (IsDaggerAttackProfile(profile))
        {
            multiplier *= _currentHealthPhase.daggerAttackMultiplier;
        }

        if (IsRetreatAttackProfile(profile))
        {
            multiplier *= _currentHealthPhase.retreatAttackMultiplier;
        }

        multiplier *= GetHealthPulseAttackMultiplier();
        return score * multiplier;
    }

    private float ApplyHealthComboInfluence(AttackComboProfile combo, float score)
    {
        if (_currentHealthPhase == null || score <= 0f)
        {
            return score;
        }

        float multiplier = combo.targetType == ComboTargetType.Movement
            ? _currentHealthPhase.movementComboMultiplier
            : _currentHealthPhase.attackComboMultiplier;
        multiplier *= GetHealthPulseAttackMultiplier();
        return score * multiplier;
    }

    private void ApplyHealthMovementInfluence(
        ref float runForwardScore,
        ref float walkBackScore,
        ref float strafeScore,
        ref float dodgeBackScore,
        ref float dodgeSideScore)
    {
        if (_currentHealthPhase == null)
        {
            return;
        }

        float pulseMultiplier = GetHealthPulseMovementMultiplier();
        runForwardScore *= _currentHealthPhase.runForwardMultiplier * pulseMultiplier;
        walkBackScore *= _currentHealthPhase.walkBackMultiplier * pulseMultiplier;
        strafeScore *= _currentHealthPhase.strafeMultiplier * pulseMultiplier;
        dodgeBackScore *= _currentHealthPhase.dodgeBackMultiplier * pulseMultiplier;
        dodgeSideScore *= _currentHealthPhase.dodgeSideMultiplier * pulseMultiplier;
    }

    private float GetHealthPulseAttackMultiplier()
    {
        if (_currentHealthPhase == null || !_currentHealthPhase.usePulse)
        {
            return 1f;
        }

        return Mathf.Max(0f, 1f + _currentHealthPhase.attackPulseAmount * _healthPhasePulseSign);
    }

    private float GetHealthPulseMovementMultiplier()
    {
        if (_currentHealthPhase == null || !_currentHealthPhase.usePulse)
        {
            return 1f;
        }

        return Mathf.Max(0f, 1f - _currentHealthPhase.movementPulseAmount * _healthPhasePulseSign);
    }

    private bool IsLightAttackProfile(AttackProfile profile)
    {
        return profile.attackIndex == 1 || profile.attackIndex == 3 ||
            profile.intent == AIIntent.Probe || profile.intent == AIIntent.Pressure;
    }

    private bool IsHeavyAttackProfile(AttackProfile profile)
    {
        return profile.attackIndex == 5 || profile.attackIndex == 6 ||
            profile.intent == AIIntent.Burst || profile.intent == AIIntent.Pounce;
    }

    private bool IsDaggerAttackProfile(AttackProfile profile)
    {
        return profile.attackIndex == 2 || profile.attackIndex == 4;
    }

    private bool IsRetreatAttackProfile(AttackProfile profile)
    {
        return profile.intent == AIIntent.Disengage || profile.intent == AIIntent.Reset ||
            profile.attackIndex == 2 || profile.attackIndex == 4;
    }

    private float CalculateHitMemoryAttackScore(AIIntent actionIntent, int actionIndex)
    {
        if (!useHitMemoryDecisionInfluence || hitMemory == null || !hitMemory.WasRecentlyHit)
        {
            return 0f;
        }

        float score = 0f;

        if (hitMemory.LastHitInterruptedAttack || hitMemory.WasRecentlyLaunchedOrHeavyHit)
        {
            if (actionIntent == AIIntent.Disengage || actionIntent == AIIntent.Reset)
            {
                score += heavyHitResetAttackBonus;
            }
            else if (actionIntent == AIIntent.Pounce || actionIntent == AIIntent.Burst || actionIntent == AIIntent.Pressure)
            {
                score -= heavyHitAggressiveAttackPenalty;
            }
        }

        if (hitMemory.LastHitImpact == EnemyHitMemory.HitImpact.Small)
        {
            if (actionIntent == AIIntent.Disengage || actionIntent == AIIntent.Reset)
            {
                score += smallHitDisengageBonus;
            }
        }

        if (hitMemory.LastHitImpact == EnemyHitMemory.HitImpact.Micro)
        {
            if (actionIntent == AIIntent.Pressure || actionIntent == AIIntent.Burst)
            {
                score += microHitPressureAttackBonus;
            }

            if (actionIndex == 1 || actionIndex == 3 || actionIndex == 5)
            {
                score += microHitCounterBonus;
            }
        }

        if (hitMemory.RecentHitCount >= recentHitPressureCount)
        {
            if (actionIntent == AIIntent.Disengage || actionIntent == AIIntent.Reset)
            {
                score += repeatedHitRetreatBonus;
            }
            else if (actionIntent == AIIntent.Pressure)
            {
                score -= repeatedHitRetreatBonus * 0.5f;
            }
        }

        if (hitMemory.LastHitDirection == EnemyHitMemory.HitDirection.Back ||
            hitMemory.LastHitDirection == EnemyHitMemory.HitDirection.Side)
        {
            if (actionIntent == AIIntent.Disengage || actionIntent == AIIntent.Reset)
            {
                score += sideOrBackHitDisengageBonus;
            }
        }

        return score;
    }

    private void UpdateAttackCooldowns()
    {
        if (!useAttackCooldowns || attackCooldowns == null)
        {
            return;
        }

        float deltaTime = Time.deltaTime;
        for (int i = 0; i < attackCooldowns.Length; i++)
        {
            AttackCooldown cooldown = attackCooldowns[i];
            if (cooldown == null || cooldown.remaining <= 0f)
            {
                if (cooldown != null && cooldown.reuseReliefTimer > 0f)
                {
                    cooldown.reuseReliefTimer = Mathf.Max(0f, cooldown.reuseReliefTimer - deltaTime);
                }

                continue;
            }

            cooldown.remaining = Mathf.Max(0f, cooldown.remaining - deltaTime);
            if (cooldown.reuseReliefTimer > 0f)
            {
                cooldown.reuseReliefTimer = Mathf.Max(0f, cooldown.reuseReliefTimer - deltaTime);
            }
        }
    }

    private void UpdateHealthPhaseInfluence()
    {
        HealthPhaseInfluence previousPhase = _currentHealthPhase;
        _currentHealthPhase = GetCurrentHealthPhaseInfluence();

        if (_currentHealthPhase != previousPhase)
        {
            _healthPhasePulseTimer = 0f;
            _healthPhasePulseSign = 1;
            LogSelectionDebug($"Health phase changed. phase={CurrentHealthPhaseName}, hp={(health != null ? health.HealthNormalized : 1f):P0}");
        }

        if (_currentHealthPhase == null || !_currentHealthPhase.usePulse)
        {
            return;
        }

        _healthPhasePulseTimer -= Time.deltaTime;
        if (_healthPhasePulseTimer > 0f)
        {
            return;
        }

        _healthPhasePulseTimer = _currentHealthPhase.pulseInterval;
        _healthPhasePulseSign = Random.value >= 0.5f ? 1 : -1;
        LogSelectionDebug($"Health phase pulse refreshed. phase={CurrentHealthPhaseName}, sign={_healthPhasePulseSign}");
    }

    private HealthPhaseInfluence GetCurrentHealthPhaseInfluence()
    {
        if (!useHealthPhaseInfluence || health == null || healthPhaseInfluences == null)
        {
            return null;
        }

        float normalizedHealth = health.HealthNormalized;
        for (int i = 0; i < healthPhaseInfluences.Length; i++)
        {
            HealthPhaseInfluence phase = healthPhaseInfluences[i];
            if (phase == null)
            {
                continue;
            }

            if (normalizedHealth >= phase.minHealthPercent && normalizedHealth <= phase.maxHealthPercent)
            {
                return phase;
            }
        }

        return null;
    }

    private void UpdateAttackComboCooldowns()
    {
        if (!useAttackCombos || attackComboProfiles == null)
        {
            return;
        }

        float deltaTime = Time.deltaTime;
        for (int i = 0; i < attackComboProfiles.Length; i++)
        {
            AttackComboProfile combo = attackComboProfiles[i];
            if (combo == null || combo.remaining <= 0f)
            {
                continue;
            }

            combo.remaining = Mathf.Max(0f, combo.remaining - deltaTime);
        }
    }

    private void UpdateMovementCooldowns()
    {
        if (_movementCooldownTimer <= 0f)
        {
            return;
        }

        _movementCooldownTimer = Mathf.Max(0f, _movementCooldownTimer - Time.deltaTime);
    }

    private void UpdateActionBalance()
    {
        if (!useActionBalance || Mathf.Approximately(_actionBalance, 0f) || actionBalanceDecayPerSecond <= 0f)
        {
            return;
        }

        _actionBalance = Mathf.MoveTowards(_actionBalance, 0f, actionBalanceDecayPerSecond * Time.deltaTime);
    }

    private void RegisterAttackBalance()
    {
        if (!useActionBalance || maxActionBalance <= 0f || attackBalanceGain <= 0f)
        {
            return;
        }

        _actionBalance = Mathf.Clamp(_actionBalance + attackBalanceGain, -maxActionBalance, maxActionBalance);
    }

    private void RegisterMovementBalance()
    {
        if (!useActionBalance || maxActionBalance <= 0f || movementBalanceGain <= 0f)
        {
            return;
        }

        _actionBalance = Mathf.Clamp(_actionBalance - movementBalanceGain, -maxActionBalance, maxActionBalance);
    }

    private void ApplyActionBalance(
        float attackScore,
        float movementScore,
        out float balancedAttackScore,
        out float balancedMovementScore)
    {
        balancedAttackScore = attackScore;
        balancedMovementScore = movementScore;

        if (!useActionBalance || Mathf.Approximately(_actionBalance, 0f) || actionBalanceScoreShift <= 0f)
        {
            return;
        }

        float scoreShift = _actionBalance * actionBalanceScoreShift;
        balancedAttackScore = attackScore - scoreShift;
        balancedMovementScore = movementScore + scoreShift;
    }

    private bool SelectMovement(out AIMoveMode moveMode, out AIMoveDirection moveDirection, out float selectedMovementScore)
    {
        moveMode = AIMoveMode.Idle;
        moveDirection = AIMoveDirection.Forward;
        selectedMovementScore = 0f;

        if (!_hasPlayerTarget)
        {
            return false;
        }

        float pressure = GetHealthAdjustedDesire(DesireGate.Pressure);
        float threat = GetHealthAdjustedDesire(DesireGate.Threat);
        float aggression = GetHealthAdjustedDesire(DesireGate.Aggression);
        float retreat = GetHealthAdjustedDesire(DesireGate.Retreat);
        float burst = GetHealthAdjustedDesire(DesireGate.Burst);
        bool recentlyPunished = combatMemory != null
            && (combatMemory.WasRecentlyPerfectDodged(recentResultMovementMemoryTime)
                || combatMemory.WasRecentlyInterrupted(recentResultMovementMemoryTime));
        bool recentlyWhiffed = combatMemory != null && combatMemory.WasRecentlyWhiffed(recentResultMovementMemoryTime);
        bool recentlyHeavyHit = useHitMemoryDecisionInfluence && hitMemory != null && hitMemory.WasRecentlyLaunchedOrHeavyHit;
        bool recentlyRepeatedHit = useHitMemoryDecisionInfluence && hitMemory != null && hitMemory.RecentHitCount >= recentHitPressureCount;
        bool recentlySideOrBackHit = useHitMemoryDecisionInfluence && hitMemory != null &&
            (hitMemory.LastHitDirection == EnemyHitMemory.HitDirection.Side ||
             hitMemory.LastHitDirection == EnemyHitMemory.HitDirection.Back);
        bool recentlyMicroHit = useHitMemoryDecisionInfluence && hitMemory != null &&
            hitMemory.WasRecentlyHit && hitMemory.LastHitImpact == EnemyHitMemory.HitImpact.Micro;

        float runForwardScore = ScoreRunForward(aggression, burst);
        float walkBackScore = ScoreWalkBack(threat, retreat, recentlyPunished);
        float strafeScore = ScoreStrafe(pressure, aggression, threat, retreat, recentlyWhiffed);
        float dodgeBackScore = ScoreDodgeBack(threat, retreat, recentlyPunished);
        float dodgeSideScore = ScoreDodgeSide(threat, retreat, recentlyWhiffed);

        if (recentlyHeavyHit)
        {
            walkBackScore += heavyHitRetreatBonus * 0.65f;
            dodgeBackScore += heavyHitRetreatBonus;
            dodgeSideScore += heavyHitRetreatBonus * 0.75f;
            runForwardScore -= heavyHitRetreatBonus * 0.5f;
        }

        if (recentlyRepeatedHit)
        {
            walkBackScore += repeatedHitRetreatBonus * 0.55f;
            dodgeSideScore += repeatedHitRetreatBonus;
            dodgeBackScore += repeatedHitRetreatBonus * 0.65f;
        }

        if (recentlySideOrBackHit)
        {
            strafeScore += sideOrBackHitDisengageBonus;
            dodgeSideScore += sideOrBackHitDisengageBonus;
        }

        if (recentlyMicroHit)
        {
            runForwardScore += microHitCounterBonus * 0.5f;
            strafeScore += microHitCounterBonus * 0.25f;
        }

        ApplyHealthMovementInfluence(
            ref runForwardScore,
            ref walkBackScore,
            ref strafeScore,
            ref dodgeBackScore,
            ref dodgeSideScore);

        runForwardScore = Mathf.Max(0f, runForwardScore);
        walkBackScore = Mathf.Max(0f, walkBackScore);
        strafeScore = Mathf.Max(0f, strafeScore);
        dodgeBackScore = Mathf.Max(0f, dodgeBackScore);
        dodgeSideScore = Mathf.Max(0f, dodgeSideScore);
        AIMoveDirection strafeDirection = SelectStrafeDirection();

        AIMoveMode bestMode = AIMoveMode.Idle;
        AIMoveDirection bestDirection = AIMoveDirection.Forward;
        float bestScore = 0f;

        ConsiderMovement(AIMoveMode.Run, AIMoveDirection.Forward, runForwardScore, ref bestMode, ref bestDirection, ref bestScore);
        ConsiderMovement(AIMoveMode.Walk, AIMoveDirection.Back, walkBackScore, ref bestMode, ref bestDirection, ref bestScore);
        ConsiderMovement(AIMoveMode.Walk, strafeDirection, strafeScore, ref bestMode, ref bestDirection, ref bestScore);
        ConsiderMovement(AIMoveMode.Dodge, AIMoveDirection.Back, dodgeBackScore, ref bestMode, ref bestDirection, ref bestScore);
        ConsiderMovement(AIMoveMode.Dodge, strafeDirection, dodgeSideScore, ref bestMode, ref bestDirection, ref bestScore);

        UpdateMovementScoreSnapshot(
            pressure,
            threat,
            aggression,
            retreat,
            burst,
            recentlyPunished,
            recentlyWhiffed,
            runForwardScore,
            walkBackScore,
            strafeScore,
            dodgeBackScore,
            dodgeSideScore,
            bestMode,
            bestDirection,
            bestScore);

        if (bestMode == AIMoveMode.Idle || bestScore <= 0f)
        {
            LogSelectionDebug(
                $"No movement selected. distance={_playerDistance:F2}, pressure={pressure:F1}, threat={threat:F1}, aggression={aggression:F1}, retreat={retreat:F1}, burst={burst:F1}");
            return false;
        }

        moveMode = bestMode;
        moveDirection = bestDirection;
        selectedMovementScore = bestScore;
        LogSelectionDebug(
            $"Movement scored. selected={moveMode}/{moveDirection}, score={bestScore:F2}, run={runForwardScore:F2}, back={walkBackScore:F2}, strafe={strafeScore:F2}, dodgeBack={dodgeBackScore:F2}, dodgeSide={dodgeSideScore:F2}");
        return true;
    }

    private void UpdateMovementScoreSnapshot(
        float pressure,
        float threat,
        float aggression,
        float retreat,
        float burst,
        bool recentlyPunished,
        bool recentlyWhiffed,
        float runForwardScore,
        float walkBackScore,
        float strafeScore,
        float dodgeBackScore,
        float dodgeSideScore,
        AIMoveMode selectedMode,
        AIMoveDirection selectedDirection,
        float selectedScore)
    {
        if (lastMovementScore == null)
        {
            lastMovementScore = new MovementScoreSnapshot();
        }

        lastMovementScore.distance = _playerDistance;
        lastMovementScore.distanceBand = _distanceBand;
        lastMovementScore.playerAttacking = _playerIsAttacking;
        lastMovementScore.playerRunning = _playerIsRunning;
        lastMovementScore.playerDodgeReady = _playerDodgeReady;
        lastMovementScore.recentlyPunished = recentlyPunished;
        lastMovementScore.recentlyWhiffed = recentlyWhiffed;
        lastMovementScore.pressure = pressure;
        lastMovementScore.threat = threat;
        lastMovementScore.aggression = aggression;
        lastMovementScore.retreat = retreat;
        lastMovementScore.burst = burst;
        lastMovementScore.runForwardScore = runForwardScore;
        lastMovementScore.walkBackScore = walkBackScore;
        lastMovementScore.strafeScore = strafeScore;
        lastMovementScore.dodgeBackScore = dodgeBackScore;
        lastMovementScore.dodgeSideScore = dodgeSideScore;
        lastMovementScore.selectedMode = selectedMode;
        lastMovementScore.selectedDirection = selectedDirection;
        lastMovementScore.selectedScore = selectedScore;
    }

    private float ScoreRunForward(float aggression, float burst)
    {
        if (_playerDistance <= closeDistance)
        {
            return 0f;
        }

        float score = 0.35f;
        if (_playerDistance >= approachDistance)
        {
            score += 2.6f;
        }
        else if (_playerDistance >= aggressiveApproachDistance)
        {
            score += 1.25f;
        }

        score += aggression * 0.012f;
        score += burst * 0.01f;

        if (_playerIsRunning)
        {
            score += 0.35f;
        }

        if (!_playerDodgeReady)
        {
            score += 0.25f;
        }

        return AddMovementNoise(score);
    }

    private float ScoreWalkBack(float threat, float retreat, bool recentlyPunished)
    {
        if (_playerDistance > closeDistance)
        {
            return 0f;
        }

        float score = 0.05f + threat * 0.004f + retreat * 0.007f;
        if (_playerIsAttacking)
        {
            score += 0.35f;
        }

        if (recentlyPunished)
        {
            score += 0.3f;
        }

        if (_playerDistance <= retreatDistance && !_playerIsAttacking)
        {
            score += 0.3f;
        }

        return AddMovementNoise(score);
    }

    private float ScoreStrafe(float pressure, float aggression, float threat, float retreat, bool recentlyWhiffed)
    {
        if (_playerDistance > strafeDistance || _playerDistance <= pointBlankDistance)
        {
            return 0f;
        }

        float score = 1f + pressure * 0.007f + aggression * 0.008f;
        score -= retreat * 0.004f;
        score -= threat * 0.003f;

        if (recentlyWhiffed)
        {
            score += 0.45f;
        }

        if (_playerIsAttacking)
        {
            score += 0.25f;
        }

        return AddMovementNoise(score);
    }

    private float ScoreDodgeBack(float threat, float retreat, bool recentlyPunished)
    {
        if (_playerDistance > closeDodgeDistance)
        {
            return 0f;
        }

        float score = 0.05f + threat * 0.006f + retreat * 0.01f;
        if (_playerIsAttacking)
        {
            score += 0.25f;
        }

        if (recentlyPunished)
        {
            score += 0.55f;
        }

        return AddMovementNoise(score);
    }

    private float ScoreDodgeSide(float threat, float retreat, bool recentlyWhiffed)
    {
        if (_playerDistance > closeDistance || _playerDistance <= pointBlankDistance)
        {
            return 0f;
        }

        float score = 0.45f + threat * 0.006f + retreat * 0.004f;
        if (_playerIsAttacking)
        {
            score += 0.30f;
        }

        if (recentlyWhiffed)
        {
            score += 0.5f;
        }

        return AddMovementNoise(score);
    }

    private void ConsiderMovement(
        AIMoveMode candidateMode,
        AIMoveDirection candidateDirection,
        float candidateScore,
        ref AIMoveMode bestMode,
        ref AIMoveDirection bestDirection,
        ref float bestScore)
    {
        if (candidateScore <= bestScore)
        {
            return;
        }

        bestMode = candidateMode;
        bestDirection = candidateDirection;
        bestScore = candidateScore;
    }

    private float AddMovementNoise(float score)
    {
        if (score <= 0f || movementScoreRandomness <= 0f)
        {
            return score;
        }

        return score + Random.Range(-movementScoreRandomness, movementScoreRandomness);
    }

    private AIMoveDirection SelectStrafeDirection()
    {
        _strafeSwitchTimer -= Time.deltaTime;
        if (_strafeSwitchTimer <= 0f)
        {
            _strafeRightNext = !_strafeRightNext;
            _strafeSwitchTimer = strafeSwitchInterval;
        }

        return _strafeRightNext ? AIMoveDirection.Right : AIMoveDirection.Left;
    }

    private void StartAttackCooldown(int attackIndex)
    {
        if (!useAttackCooldowns)
        {
            return;
        }

        AttackCooldown cooldown = FindAttackCooldown(attackIndex);
        if (cooldown == null)
        {
            return;
        }

        ReduceAttackReuseCooldowns(attackIndex);
        cooldown.remaining = cooldown.cooldown;
        cooldown.otherAttackUsesRemaining = cooldown.otherAttackUsesBeforeReuse;
        LogCooldownDebug(
            $"Cooldown started. attackIndex={attackIndex}, duration={cooldown.cooldown:F2}, otherUsesBeforeReuse={cooldown.otherAttackUsesBeforeReuse}");
    }

    private void TryApplyDesireReuseRelief(int attackIndex)
    {
        if (!useAttackCooldowns)
        {
            return;
        }

        AttackCooldown cooldown = FindAttackCooldown(attackIndex);
        if (cooldown == null ||
            !cooldown.allowDesireReuseRelief ||
            cooldown.otherAttackUsesRemaining <= 0 ||
            cooldown.reuseReliefAmount <= 0 ||
            cooldown.reuseReliefTimer > 0f)
        {
            return;
        }

        float threshold = GetDesireThreshold(cooldown.reuseReliefDesire) * cooldown.reuseReliefThresholdPercent;
        if (threshold <= 0f)
        {
            return;
        }

        float desire = GetHealthAdjustedDesire(cooldown.reuseReliefDesire);
        if (desire < threshold)
        {
            return;
        }

        int previous = cooldown.otherAttackUsesRemaining;
        cooldown.otherAttackUsesRemaining = Mathf.Max(0, cooldown.otherAttackUsesRemaining - cooldown.reuseReliefAmount);
        cooldown.reuseReliefTimer = cooldown.reuseReliefCooldown;
        LogCooldownDebug(
            $"Desire reuse relief applied. attackIndex={attackIndex}, desire={cooldown.reuseReliefDesire}, value={desire:F1}, threshold={threshold:F1}, reuse={previous}->{cooldown.otherAttackUsesRemaining}, reliefCooldown={cooldown.reuseReliefCooldown:F2}");
    }

    private void ReduceAttackReuseCooldowns(int usedAttackIndex)
    {
        if (attackCooldowns == null)
        {
            return;
        }

        for (int i = 0; i < attackCooldowns.Length; i++)
        {
            AttackCooldown cooldown = attackCooldowns[i];
            if (cooldown == null || cooldown.attackIndex == usedAttackIndex || cooldown.otherAttackUsesRemaining <= 0)
            {
                continue;
            }

            cooldown.otherAttackUsesRemaining = Mathf.Max(0, cooldown.otherAttackUsesRemaining - 1);
            LogCooldownDebug(
                $"Reuse cooldown reduced. attackIndex={cooldown.attackIndex}, remainingOtherUses={cooldown.otherAttackUsesRemaining}, usedAttackIndex={usedAttackIndex}");
        }
    }

    private AttackCooldown FindAttackCooldown(int attackIndex)
    {
        if (attackCooldowns == null)
        {
            return null;
        }

        for (int i = 0; i < attackCooldowns.Length; i++)
        {
            AttackCooldown cooldown = attackCooldowns[i];
            if (cooldown != null && cooldown.attackIndex == attackIndex)
            {
                return cooldown;
            }
        }

        return null;
    }

    private void FacePlayerBeforeAttack()
    {
        if (!facePlayerBeforeAttack || facePlayerContinuously)
        {
            return;
        }

        FacePlayer(snapFacingBeforeAttack, preAttackTurnSpeed, Time.deltaTime);
    }

    public void FacePlayerForExternalAction(bool snap = true)
    {
        FacePlayer(snap, preAttackTurnSpeed, Time.deltaTime);
    }

    public void FacePlayerForExternalAction(float turnSpeed, float deltaTime)
    {
        FacePlayer(false, turnSpeed, deltaTime);
    }

    private void UpdateFacing(float deltaTime)
    {
        if (!facePlayerContinuously)
        {
            return;
        }

        // 受击动作守卫：独立于 AI 状态机。
        // 受击的 External Recovery（尤其是 fromIdle 的 0.35s）可能在动画结束前就把状态推回 Ready，
        // 导致 FacePlayer 在 Hit_Low/High root motion 仍在播放时恢复旋转，造成位移方向偏转。
        if (!facePlayerWhileHitReacting && hitReactionController != null && hitReactionController.IsHitReacting)
        {
            return;
        }

        if (_state == AIActionState.Acting)
        {
            // 攻击动作和移动动作分别由独立开关控制。
            // 移动动作（闪避/走路等）带 root motion 时，若持续追踪玩家朝向，
            // root motion 的位移方向会随旋转偏转，导致本应直线位移的动作变成围绕玩家的圆弧。
            bool shouldFace = _currentActionIsAttack ? facePlayerWhileActing : facePlayerWhileMovementActing;
            if (!shouldFace)
            {
                return;
            }
        }

        if (_state == AIActionState.Recovering && !facePlayerWhileRecovering)
        {
            return;
        }

        FacePlayer(false, continuousTurnSpeed, deltaTime);
    }

    private void FacePlayer(bool snap, float turnSpeed, float deltaTime)
    {
        if (playerTarget == null)
        {
            return;
        }

        Vector3 toPlayer = playerTarget.position - transform.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude <= 0.001f)
        {
            return;
        }

        Quaternion targetRotation = Quaternion.LookRotation(toPlayer.normalized, Vector3.up);
        if (!Mathf.Approximately(facingModelYawOffset, 0f))
        {
            targetRotation *= Quaternion.Euler(0f, facingModelYawOffset, 0f);
        }

        if (snap || turnSpeed <= 0f)
        {
            transform.rotation = targetRotation;
            return;
        }

        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            targetRotation,
            turnSpeed * deltaTime);
    }

    private void ResolvePlayerReferences()
    {
        if (playerTarget == null && !string.IsNullOrEmpty(playerTag))
        {
            GameObject playerObject = GameObject.FindGameObjectWithTag(playerTag);
            if (playerObject != null)
            {
                playerTarget = playerObject.transform;
            }
        }

        if (playerTarget == null)
        {
            return;
        }

        if (playerController == null)
        {
            playerController = playerTarget.GetComponentInChildren<CharacterController>();
        }

        if (playerAnimator == null)
        {
            playerAnimator = playerTarget.GetComponentInChildren<Animator>();
        }

        if (playerDodgeCooldown == null)
        {
            playerDodgeCooldown = playerTarget.GetComponentInChildren<KianaDodgeCooldown>();
        }
    }

    private void UpdatePerception()
    {
        if (_recentPlayerDodgeTimer > 0f)
        {
            _recentPlayerDodgeTimer = Mathf.Max(0f, _recentPlayerDodgeTimer - Time.deltaTime);
        }

        _hasPlayerTarget = playerTarget != null;
        if (!_hasPlayerTarget)
        {
            _playerDistance = 0f;
            _playerFacingAngle = 0f;
            _distanceBand = DistanceBand.Far;
            _angleBand = AngleBand.Front;
            _playerIsMoving = false;
            _playerIsRunning = false;
            _playerIsAttacking = false;
            _playerIsDodging = false;
            _playerDodgeReady = false;
            return;
        }

        Vector3 toPlayer = playerTarget.position - transform.position;
        toPlayer.y = 0f;
        _playerDistance = toPlayer.magnitude;
        _distanceBand = ResolveDistanceBand(_playerDistance);

        if (toPlayer.sqrMagnitude > 0.001f)
        {
            _playerFacingAngle = Vector3.SignedAngle(transform.forward, toPlayer.normalized, Vector3.up);
            _angleBand = ResolveAngleBand(Mathf.Abs(_playerFacingAngle));
        }
        else
        {
            _playerFacingAngle = 0f;
            _angleBand = AngleBand.Front;
        }

        float playerSpeed = playerController != null
            ? new Vector3(playerController.velocity.x, 0f, playerController.velocity.z).magnitude
            : 0f;
        _playerIsMoving = playerSpeed > playerMovingSpeedThreshold;
        _playerIsRunning = playerSpeed > playerRunningSpeedThreshold;
        _playerIsAttacking = IsPlayerAnimatorInAttackState();
        _playerIsDodging = _recentPlayerDodgeTimer > 0f;
        _playerDodgeReady = playerDodgeCooldown == null || playerDodgeCooldown.CanDodge;

        LogPerceptionDebug(playerSpeed);
    }

    private DistanceBand ResolveDistanceBand(float distance)
    {
        if (distance <= pointBlankDistance)
        {
            return DistanceBand.PointBlank;
        }

        if (distance <= closeDistance)
        {
            return DistanceBand.Close;
        }

        if (distance <= midDistance)
        {
            return DistanceBand.Mid;
        }

        return DistanceBand.Far;
    }

    private static AngleBand ResolveAngleBand(float absAngle)
    {
        if (absAngle <= 45f)
        {
            return AngleBand.Front;
        }

        if (absAngle <= 135f)
        {
            return AngleBand.Side;
        }

        return AngleBand.Back;
    }

    private bool IsPlayerAnimatorInAttackState()
    {
        if (playerAnimator == null)
        {
            return false;
        }

        AnimatorStateInfo current = playerAnimator.GetCurrentAnimatorStateInfo(0);
        if (current.IsTag(playerAttackStateTag))
        {
            return true;
        }

        if (playerAnimator.IsInTransition(0))
        {
            AnimatorStateInfo next = playerAnimator.GetNextAnimatorStateInfo(0);
            return next.IsTag(playerAttackStateTag);
        }

        return false;
    }

    private void OnKianaDodgeStarted(KianaCombatController kiana)
    {
        if (kiana == null)
        {
            return;
        }

        if (playerTarget == null
            || kiana.transform == playerTarget
            || kiana.transform.IsChildOf(playerTarget)
            || playerTarget.IsChildOf(kiana.transform))
        {
            playerTarget = kiana.transform;
            _recentPlayerDodgeTimer = recentDodgeMemoryTime;
        }
    }

    private void ClearAnimatorActionFlags()
    {
        if (animator == null)
        {
            return;
        }

        if (!string.IsNullOrEmpty(actingBoolName))
        {
            animator.SetBool(actingBoolName, false);
        }

        if (!string.IsNullOrEmpty(movingBoolName))
        {
            animator.SetBool(movingBoolName, false);
        }

        if (!string.IsNullOrEmpty(moveModeParameterName))
        {
            animator.SetInteger(moveModeParameterName, (int)AIMoveMode.Idle);
        }
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

    private void SetState(AIActionState nextState)
    {
        if (_state == nextState)
        {
            return;
        }

        AIActionState previousState = _state;
        _state = nextState;
        LogDebug($"State changed. {previousState} -> {_state}");
    }

    private bool IsDead()
    {
        return health != null && health.IsDead;
    }

    private void LogDebug(string message)
    {
        if (!logDebug)
        {
            return;
        }

        Debug.Log($"[StrikeJaegerAI] {message} time={Time.time:F3}", this);
    }

    private void LogCooldownDebug(string message)
    {
        if (!logCooldownDebug)
        {
            return;
        }

        Debug.Log($"[StrikeJaegerAI][Cooldown] {message} time={Time.time:F3}", this);
    }

    private void LogSelectionDebug(string message)
    {
        if (!logSelectionDebug)
        {
            return;
        }

        Debug.Log($"[StrikeJaegerAI][Selection] {message} time={Time.time:F3}", this);
    }

    private void LogComboDebug(string message)
    {
        if (!logAttackComboDebug)
        {
            return;
        }

        Debug.Log($"[StrikeJaegerAI][Combo] {message} time={Time.time:F3}", this);
    }

    private void LogPerceptionDebug(float playerSpeed)
    {
        if (!logPerceptionDebug || Time.time < _nextPerceptionDebugTime)
        {
            return;
        }

        _nextPerceptionDebugTime = Time.time + perceptionDebugInterval;
        Debug.Log(
            $"[StrikeJaegerAI][Perception] " +
            $"target={(_hasPlayerTarget ? playerTarget.name : "None")}, " +
            $"distance={_playerDistance:F2}, distanceBand={_distanceBand}, angle={_playerFacingAngle:F1}, angleBand={_angleBand}, " +
            $"playerSpeed={playerSpeed:F2}, moving={_playerIsMoving}, running={_playerIsRunning}, attacking={_playerIsAttacking}, " +
            $"dodging={_playerIsDodging}, dodgeReady={_playerDodgeReady}, state={_state}, action={_currentActionName}",
            this);
    }
}
