using System;
using System.IO;
using System.Text;
using UnityEngine;

public class EnemyAITestScenarioLogger : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private StrikeJaegerAIController aiController;
    [SerializeField] private EnemyPressureModel pressureModel;
    [SerializeField] private EnemyCombatMemory combatMemory;
    [SerializeField] private EnemyHitMemory hitMemory;
    [SerializeField] private EnemyStaggerController staggerController;
    [SerializeField] private CombatHealth health;

    [Header("Logging")]
    [SerializeField] private bool logOnEnable = true;
    [SerializeField] private bool logPeriodicSnapshot = true;
    [SerializeField] private float periodicInterval = 1.25f;
    [SerializeField] private bool logStateChanges = true;
    [SerializeField] private bool logActionChanges = true;
    [SerializeField] private bool logIntentChanges = true;
    [SerializeField] private bool logMovementPickChanges = true;
    [SerializeField] private bool logCombatResults = true;
    [SerializeField] private bool logHitMemoryChanges = true;
    [SerializeField] private bool logStaggerChanges = true;

    [Header("File Output")]
    [SerializeField] private bool writeToTextFile = true;
    [SerializeField] private bool mirrorToConsole = true;
    [SerializeField] private string logFolderName = "EnemyAITestLogs";
    [SerializeField] private string fileNamePrefix = "EnemyAI";
    [SerializeField] private bool createNewFileOnEnable = true;
    [SerializeField] private bool flushFileEveryWrite = true;

    [Header("Debug Log Capture")]
    [SerializeField] private bool captureWatchedDebugLogs = true;
    [SerializeField] private string[] watchedDebugPrefixes =
    {
        "[EnemyHitReaction]",
        "[EnemyStagger]",
        "[StrikeJaegerUltimateAttack]"
    };

    [Header("Snapshot Detail")]
    [SerializeField] private bool includePressure = true;
    [SerializeField] private bool includeCombatMemory = true;
    [SerializeField] private bool includeHitMemory = true;
    [SerializeField] private bool includeMovementScores = true;
    [SerializeField] private bool includeCooldowns = true;
    [SerializeField] private bool includeHealth = true;

    private readonly StringBuilder _builder = new StringBuilder(1536);
    private float _nextPeriodicLogTime;
    private StrikeJaegerAIController.AIActionState _lastState;
    private StrikeJaegerAIController.AIIntent _lastIntent;
    private StrikeJaegerAIController.AIMoveMode _lastMoveMode;
    private StrikeJaegerAIController.AIMoveDirection _lastMoveDirection;
    private string _lastActionName;
    private int _lastAttackIndex;
    private bool _lastStaggered;
    private int _lastStaggerPoints;
    private bool _hasSnapshot;
    private string _logFilePath;
    private StreamWriter _logWriter;

    private void Awake()
    {
        ResolveReferences();
        CaptureSnapshotState();
    }

    private void OnEnable()
    {
        ResolveReferences();
        SubscribeEvents();
        SubscribeUnityLogCapture();
        CaptureSnapshotState();
        PrepareFileOutput();
        _nextPeriodicLogTime = Time.time + periodicInterval;

        if (logOnEnable)
        {
            LogSnapshot("LoggerEnabled");
        }
    }

    private void OnDisable()
    {
        UnsubscribeEvents();
        UnsubscribeUnityLogCapture();
        CloseFileOutput();
    }

    private void OnValidate()
    {
        periodicInterval = Mathf.Max(0.1f, periodicInterval);
        if (string.IsNullOrWhiteSpace(logFolderName))
        {
            logFolderName = "EnemyAITestLogs";
        }

        if (string.IsNullOrWhiteSpace(fileNamePrefix))
        {
            fileNamePrefix = "EnemyAI";
        }
    }

    private void Update()
    {
        ResolveReferences();
        LogChangedState();

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

    private void SubscribeEvents()
    {
        if (combatMemory != null)
        {
            combatMemory.AttackResultRecorded -= OnAttackResultRecorded;
            combatMemory.AttackResultRecorded += OnAttackResultRecorded;
        }

        if (hitMemory != null)
        {
            hitMemory.HitMemoryChanged -= OnHitMemoryChanged;
            hitMemory.HitMemoryChanged += OnHitMemoryChanged;
        }
    }

    private void UnsubscribeEvents()
    {
        if (combatMemory != null)
        {
            combatMemory.AttackResultRecorded -= OnAttackResultRecorded;
        }

        if (hitMemory != null)
        {
            hitMemory.HitMemoryChanged -= OnHitMemoryChanged;
        }
    }

    private void OnAttackResultRecorded(EnemyCombatMemory memory, EnemyCombatMemory.AttackResult result)
    {
        if (!logCombatResults || memory != combatMemory)
        {
            return;
        }

        LogSnapshot($"CombatResult:{result}");
    }

    private void OnHitMemoryChanged(EnemyHitMemory memory)
    {
        if (!logHitMemoryChanges || memory != hitMemory)
        {
            return;
        }

        LogSnapshot($"HitMemory:{memory.LastHitImpact}/{memory.LastHitDirection}");
    }

    private void LogChangedState()
    {
        if (aiController == null)
        {
            return;
        }

        if (!_hasSnapshot)
        {
            CaptureSnapshotState();
            return;
        }

        if (logStateChanges && aiController.State != _lastState)
        {
            LogSnapshot($"State:{_lastState}->{aiController.State}");
        }

        if (logIntentChanges && aiController.CurrentIntent != _lastIntent)
        {
            LogSnapshot($"Intent:{_lastIntent}->{aiController.CurrentIntent}");
        }

        bool actionChanged = aiController.CurrentAttackIndex != _lastAttackIndex ||
            !string.Equals(aiController.CurrentActionName, _lastActionName, System.StringComparison.Ordinal);
        if (logActionChanges && actionChanged)
        {
            LogSnapshot($"Action:{FormatAction(_lastActionName, _lastAttackIndex)}->{FormatAction(aiController.CurrentActionName, aiController.CurrentAttackIndex)}");
        }

        bool movementChanged = aiController.CurrentMoveMode != _lastMoveMode ||
            aiController.CurrentMoveDirection != _lastMoveDirection;
        if (logMovementPickChanges && movementChanged)
        {
            LogSnapshot($"Move:{_lastMoveMode}/{_lastMoveDirection}->{aiController.CurrentMoveMode}/{aiController.CurrentMoveDirection}");
        }

        if (logStaggerChanges && staggerController != null)
        {
            bool staggerChanged = staggerController.IsStaggered != _lastStaggered ||
                staggerController.CurrentStaggerPoints != _lastStaggerPoints;
            if (staggerChanged)
            {
                LogSnapshot($"Stagger:{_lastStaggered}/{_lastStaggerPoints}->{staggerController.IsStaggered}/{staggerController.CurrentStaggerPoints}");
            }
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

    private void PrepareFileOutput()
    {
        if (!writeToTextFile)
        {
            return;
        }

        if (_logWriter != null && !createNewFileOnEnable)
        {
            return;
        }

        CloseFileOutput();

        string folderPath = Path.Combine(Application.persistentDataPath, logFolderName);
        Directory.CreateDirectory(folderPath);

        string safePrefix = MakeSafeFileName(fileNamePrefix);
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        _logFilePath = Path.Combine(folderPath, $"{safePrefix}_{timestamp}.txt");
        _logWriter = new StreamWriter(_logFilePath, false, Encoding.UTF8);
        _logWriter.WriteLine($"Enemy AI test log created at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        _logWriter.WriteLine($"Scene time={Time.time:F3}");
        _logWriter.WriteLine($"Path={_logFilePath}");
        _logWriter.WriteLine();

        if (flushFileEveryWrite)
        {
            _logWriter.Flush();
        }

        if (mirrorToConsole)
        {
            Debug.Log($"[EnemyAITest] Writing log file: {_logFilePath}", this);
        }
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

    private void WriteExternalDebugLogToFile(string message, string stackTrace, LogType type)
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

        _logWriter.Write("[UnityLog] type=");
        _logWriter.Write(type);
        _logWriter.Write(" time=");
        _logWriter.Write(Time.time.ToString("F3"));
        _logWriter.WriteLine();
        _logWriter.WriteLine(message);

        if (flushFileEveryWrite)
        {
            _logWriter.Flush();
        }
    }

    private void SubscribeUnityLogCapture()
    {
        Application.logMessageReceived -= OnUnityLogMessageReceived;
        Application.logMessageReceived += OnUnityLogMessageReceived;
    }

    private void UnsubscribeUnityLogCapture()
    {
        Application.logMessageReceived -= OnUnityLogMessageReceived;
    }

    private void OnUnityLogMessageReceived(string condition, string stackTrace, LogType type)
    {
        if (!captureWatchedDebugLogs || !ShouldCaptureUnityLog(condition))
        {
            return;
        }

        WriteExternalDebugLogToFile(condition, stackTrace, type);
    }

    private bool ShouldCaptureUnityLog(string condition)
    {
        if (string.IsNullOrEmpty(condition) || watchedDebugPrefixes == null)
        {
            return false;
        }

        for (int i = 0; i < watchedDebugPrefixes.Length; i++)
        {
            string prefix = watchedDebugPrefixes[i];
            if (string.IsNullOrEmpty(prefix))
            {
                continue;
            }

            if (condition.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private void CloseFileOutput()
    {
        if (_logWriter == null)
        {
            return;
        }

        _logWriter.Flush();
        _logWriter.Dispose();
        _logWriter = null;
    }

    private string MakeSafeFileName(string rawName)
    {
        string safeName = string.IsNullOrWhiteSpace(rawName) ? "EnemyAI" : rawName.Trim();
        foreach (char invalidChar in Path.GetInvalidFileNameChars())
        {
            safeName = safeName.Replace(invalidChar, '_');
        }

        return safeName;
    }

    private void BuildSnapshot(string reason)
    {
        _builder.Length = 0;
        _builder.Append("[EnemyAITest] reason=").Append(reason)
            .Append(" time=").Append(Time.time.ToString("F3"))
            .AppendLine();

        if (aiController == null)
        {
            _builder.AppendLine("AI: Missing");
            return;
        }

        _builder.Append("AI state=").Append(aiController.State)
            .Append(", intent=").Append(aiController.CurrentIntent)
            .Append(", healthPhase=").Append(aiController.CurrentHealthPhaseName)
            .Append(", action=").Append(FormatAction(aiController.CurrentActionName, aiController.CurrentAttackIndex))
            .Append(", move=").Append(aiController.CurrentMoveMode).Append("/").Append(aiController.CurrentMoveDirection)
            .Append(", actingAttack=").Append(aiController.CurrentActionIsAttack)
            .Append(", actionBalance=").Append(aiController.ActionBalance.ToString("F2"))
            .AppendLine();

        _builder.Append("Player distance=").Append(aiController.PlayerDistance.ToString("F2"))
            .Append(", band=").Append(aiController.CurrentDistanceBand)
            .Append(", angle=").Append(aiController.CurrentAngleBand)
            .Append(", running=").Append(aiController.PlayerIsRunning)
            .Append(", attacking=").Append(aiController.PlayerIsAttacking)
            .Append(", dodging=").Append(aiController.PlayerIsDodging)
            .Append(", dodgeReady=").Append(aiController.PlayerDodgeReady)
            .AppendLine();

        AppendHealth();
        AppendPressure();
        AppendCombatMemory();
        AppendHitMemory();
        AppendStagger();
        AppendMovementScores();
        AppendCooldowns();
    }

    private void AppendHealth()
    {
        if (!includeHealth || health == null)
        {
            return;
        }

        _builder.Append("Health hp=").Append(health.CurrentHealth.ToString("F1"))
            .Append("/").Append(health.MaxHealth.ToString("F1"))
            .Append(", normalized=").Append(health.HealthNormalized.ToString("F2"))
            .Append(", dead=").Append(health.IsDead)
            .AppendLine();
    }

    private void AppendPressure()
    {
        if (!includePressure || pressureModel == null)
        {
            return;
        }

        _builder.Append("Pressure p=").Append(pressureModel.PressureLevel.ToString("F1"))
            .Append(", threat=").Append(pressureModel.ThreatenedLevel.ToString("F1"))
            .Append(", aggression=").Append(pressureModel.AggressionLevel.ToString("F1"))
            .Append(", retreat=").Append(pressureModel.RetreatDesire.ToString("F1"))
            .Append(", burst=").Append(pressureModel.BurstDesire.ToString("F1"))
            .AppendLine();
    }

    private void AppendCombatMemory()
    {
        if (!includeCombatMemory || combatMemory == null)
        {
            return;
        }

        _builder.Append("CombatMemory result=").Append(combatMemory.LastAttackResult)
            .Append(", lastAction=").Append(string.IsNullOrEmpty(combatMemory.LastAction) ? "None" : combatMemory.LastAction)
            .Append(", lastAttackId=").Append(string.IsNullOrEmpty(combatMemory.LastAttackId) ? "None" : combatMemory.LastAttackId)
            .Append(", comboHit=").Append(combatMemory.ComboSuccessCount)
            .Append(", whiff=").Append(combatMemory.ConsecutiveWhiffCount)
            .AppendLine();
    }

    private void AppendHitMemory()
    {
        if (!includeHitMemory || hitMemory == null)
        {
            return;
        }

        _builder.Append("HitMemory last=").Append(hitMemory.LastHitImpact)
            .Append("/").Append(hitMemory.LastHitDirection)
            .Append(", react=").Append(hitMemory.LastHitReactType)
            .Append(", interrupted=").Append(hitMemory.LastHitInterruptedAttack)
            .Append(", recent=").Append(hitMemory.RecentHitCount)
            .Append(", micro=").Append(hitMemory.RecentLightHitCount)
            .Append(", small=").Append(hitMemory.RecentSmallHitCount)
            .Append(", heavy=").Append(hitMemory.RecentHeavyHitCount)
            .Append(", heavyRecent=").Append(hitMemory.WasRecentlyLaunchedOrHeavyHit)
            .AppendLine();
    }

    private void AppendStagger()
    {
        if (staggerController == null)
        {
            return;
        }

        _builder.Append("Stagger active=").Append(staggerController.IsStaggered)
            .Append(", points=").Append(staggerController.CurrentStaggerPoints)
            .Append("/").Append(staggerController.RequiredStaggerPoints)
            .Append(", timer=").Append(staggerController.StaggerTimer.ToString("F2"))
            .AppendLine();
    }

    private void AppendMovementScores()
    {
        if (!includeMovementScores || aiController == null || aiController.LastMovementScore == null)
        {
            return;
        }

        StrikeJaegerAIController.MovementScoreSnapshot score = aiController.LastMovementScore;
        _builder.Append("MoveScore selected=").Append(score.selectedMode)
            .Append("/").Append(score.selectedDirection)
            .Append("(").Append(score.selectedScore.ToString("F2")).Append(")")
            .Append(", runF=").Append(score.runForwardScore.ToString("F2"))
            .Append(", back=").Append(score.walkBackScore.ToString("F2"))
            .Append(", strafe=").Append(score.strafeScore.ToString("F2"))
            .Append(", dodgeB=").Append(score.dodgeBackScore.ToString("F2"))
            .Append(", dodgeS=").Append(score.dodgeSideScore.ToString("F2"))
            .AppendLine();
    }

    private void AppendCooldowns()
    {
        if (!includeCooldowns || aiController == null)
        {
            return;
        }

        _builder.Append("Cooldowns");
        int count = aiController.AttackCooldownCount;
        for (int i = 0; i < count; i++)
        {
            if (!aiController.TryGetAttackCooldownDebugInfo(i, out int attackIndex, out float remaining, out int otherUses, out float cooldown))
            {
                continue;
            }

            _builder.Append(" A").Append(attackIndex)
                .Append("=").Append(remaining.ToString("F1"))
                .Append("/").Append(cooldown.ToString("F1"));

            if (otherUses > 0)
            {
                _builder.Append(":u").Append(otherUses);
            }
        }

        _builder.AppendLine();
    }

    private void CaptureSnapshotState()
    {
        _hasSnapshot = aiController != null;
        if (aiController == null)
        {
            return;
        }

        _lastState = aiController.State;
        _lastIntent = aiController.CurrentIntent;
        _lastActionName = aiController.CurrentActionName;
        _lastAttackIndex = aiController.CurrentAttackIndex;
        _lastMoveMode = aiController.CurrentMoveMode;
        _lastMoveDirection = aiController.CurrentMoveDirection;

        if (staggerController != null)
        {
            _lastStaggered = staggerController.IsStaggered;
            _lastStaggerPoints = staggerController.CurrentStaggerPoints;
        }
    }

    private void ResolveReferences()
    {
        if (aiController == null)
        {
            aiController = GetComponent<StrikeJaegerAIController>();
        }

        if (pressureModel == null)
        {
            pressureModel = GetComponent<EnemyPressureModel>();
        }

        if (combatMemory == null)
        {
            combatMemory = GetComponent<EnemyCombatMemory>();
        }

        if (hitMemory == null)
        {
            hitMemory = GetComponent<EnemyHitMemory>();
        }

        if (staggerController == null)
        {
            staggerController = GetComponent<EnemyStaggerController>();
        }

        if (health == null)
        {
            health = GetComponent<CombatHealth>();
        }
    }

    private string FormatAction(string actionName, int attackIndex)
    {
        string safeAction = string.IsNullOrEmpty(actionName) ? "None" : actionName;
        return $"{safeAction}/A{attackIndex}";
    }
}
