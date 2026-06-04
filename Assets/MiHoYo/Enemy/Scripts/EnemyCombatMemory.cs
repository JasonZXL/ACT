using System;
using UnityEngine;

public class EnemyCombatMemory : MonoBehaviour
{
    public event Action<EnemyCombatMemory, AttackResult> AttackResultRecorded;

    public enum AttackResult
    {
        None = 0,
        Hit = 1,
        Whiff = 2,
        PerfectDodged = 3,
        Interrupted = 4
    }

    [Header("References")]
    [SerializeField] private EnemyAttackWarningWindow attackWarningWindow;

    [Header("Debug")]
    [SerializeField] private bool logDebug;

    private AttackResult _lastAttackResult = AttackResult.None;
    private string _lastAction;
    private string _lastAttackId;
    private float _lastHitTime = -999f;
    private float _lastWhiffTime = -999f;
    private float _lastPerfectDodgedTime = -999f;
    private float _lastInterruptedTime = -999f;
    private int _comboSuccessCount;
    private int _consecutiveWhiffCount;
    private int _currentAttackIndex;
    private string _currentAction;
    private bool _actionActive;
    private bool _currentActionHit;
    private bool _currentActionPerfectDodged;
    private bool _currentActionInterrupted;

    public AttackResult LastAttackResult => _lastAttackResult;
    public string LastAction => _lastAction;
    public string LastAttackId => _lastAttackId;
    public float LastHitTime => _lastHitTime;
    public float LastWhiffTime => _lastWhiffTime;
    public float LastPerfectDodgedTime => _lastPerfectDodgedTime;
    public float LastInterruptedTime => _lastInterruptedTime;
    public int ComboSuccessCount => _comboSuccessCount;
    public int ConsecutiveWhiffCount => _consecutiveWhiffCount;
    public bool ActionActive => _actionActive;
    public int CurrentAttackIndex => _currentAttackIndex;
    public string CurrentAction => _currentAction;

    private void Awake()
    {
        if (attackWarningWindow == null)
        {
            attackWarningWindow = GetComponent<EnemyAttackWarningWindow>();
        }
    }

    private void OnEnable()
    {
        EnemyAttackHitboxController.EnemyAttackHitConfirmed += OnEnemyAttackHitConfirmed;
        EnemyAttackWarningWindow.PerfectDodgeConfirmed += OnPerfectDodgeConfirmed;
    }

    private void OnDisable()
    {
        EnemyAttackHitboxController.EnemyAttackHitConfirmed -= OnEnemyAttackHitConfirmed;
        EnemyAttackWarningWindow.PerfectDodgeConfirmed -= OnPerfectDodgeConfirmed;
    }

    public void NotifyActionStarted(string actionName, int attackIndex)
    {
        _currentAction = actionName;
        _currentAttackIndex = Mathf.Max(0, attackIndex);
        _actionActive = true;
        _currentActionHit = false;
        _currentActionPerfectDodged = false;
        _currentActionInterrupted = false;
        LogDebug($"Action started. action={_currentAction}, attackIndex={_currentAttackIndex}");
    }

    public void NotifyActionFinished(string actionName, int attackIndex)
    {
        if (!_actionActive)
        {
            return;
        }

        if (_currentActionInterrupted)
        {
            RecordResult(AttackResult.Interrupted, _currentAction, _lastAttackId);
        }
        else if (_currentActionPerfectDodged)
        {
            RecordResult(AttackResult.PerfectDodged, _currentAction, _lastAttackId);
        }
        else if (_currentActionHit)
        {
            RecordResult(AttackResult.Hit, _currentAction, _lastAttackId);
        }
        else
        {
            RecordResult(AttackResult.Whiff, _currentAction, _lastAttackId);
        }

        LogDebug($"Action finished. action={actionName}, attackIndex={attackIndex}, result={_lastAttackResult}");
        ClearCurrentAction();
    }

    public void NotifyInterrupted()
    {
        if (!_actionActive)
        {
            return;
        }

        _currentActionInterrupted = true;
        _lastInterruptedTime = Time.time;
        LogDebug($"Action interrupted. action={_currentAction}, attackIndex={_currentAttackIndex}");
    }

    public bool WasRecentlyHit(float recentTime)
    {
        return Time.time - _lastHitTime <= recentTime;
    }

    public bool WasRecentlyWhiffed(float recentTime)
    {
        return Time.time - _lastWhiffTime <= recentTime;
    }

    public bool WasRecentlyPerfectDodged(float recentTime)
    {
        return Time.time - _lastPerfectDodgedTime <= recentTime;
    }

    public bool WasRecentlyInterrupted(float recentTime)
    {
        return Time.time - _lastInterruptedTime <= recentTime;
    }

    public void ResetMemory()
    {
        _lastAttackResult = AttackResult.None;
        _lastAction = string.Empty;
        _lastAttackId = string.Empty;
        _lastHitTime = -999f;
        _lastWhiffTime = -999f;
        _lastPerfectDodgedTime = -999f;
        _lastInterruptedTime = -999f;
        _comboSuccessCount = 0;
        _consecutiveWhiffCount = 0;
        ClearCurrentAction();
        LogDebug("Memory reset.");
    }

    private void OnEnemyAttackHitConfirmed(EnemyAttackHitData hitData)
    {
        if (hitData == null || hitData.Attacker == null)
        {
            return;
        }

        if (hitData.Attacker != gameObject && !hitData.Attacker.transform.IsChildOf(transform))
        {
            return;
        }

        _currentActionHit = true;
        _lastAttackId = hitData.AttackId;
        _lastHitTime = Time.time;
        LogDebug($"Hit confirmed. action={_currentAction}, attackId={_lastAttackId}, target={hitData.HitCollider.name}");
    }

    private void OnPerfectDodgeConfirmed(EnemyAttackWarningWindow warningWindow, KianaCombatController dodger)
    {
        if (warningWindow == null || warningWindow != attackWarningWindow)
        {
            return;
        }

        _currentActionPerfectDodged = true;
        _lastPerfectDodgedTime = Time.time;
        LogDebug($"Perfect dodge confirmed. action={_currentAction}, dodger={(dodger != null ? dodger.name : "None")}");
    }

    private void RecordResult(AttackResult result, string actionName, string attackId)
    {
        _lastAttackResult = result;
        _lastAction = actionName;
        _lastAttackId = attackId;

        switch (result)
        {
            case AttackResult.Hit:
                _comboSuccessCount++;
                _consecutiveWhiffCount = 0;
                _lastHitTime = Time.time;
                break;
            case AttackResult.Whiff:
                _comboSuccessCount = 0;
                _consecutiveWhiffCount++;
                _lastWhiffTime = Time.time;
                break;
            case AttackResult.PerfectDodged:
                _comboSuccessCount = 0;
                _consecutiveWhiffCount = 0;
                _lastPerfectDodgedTime = Time.time;
                break;
            case AttackResult.Interrupted:
                _comboSuccessCount = 0;
                _consecutiveWhiffCount = 0;
                _lastInterruptedTime = Time.time;
                break;
        }

        LogDebug(
            $"Result recorded. result={result}, action={_lastAction}, attackId={_lastAttackId}, comboSuccess={_comboSuccessCount}, whiffs={_consecutiveWhiffCount}");
        AttackResultRecorded?.Invoke(this, result);
    }

    private void ClearCurrentAction()
    {
        _actionActive = false;
        _currentAction = string.Empty;
        _currentAttackIndex = 0;
        _currentActionHit = false;
        _currentActionPerfectDodged = false;
        _currentActionInterrupted = false;
    }

    private void LogDebug(string message)
    {
        if (!logDebug)
        {
            return;
        }

        Debug.Log($"[EnemyCombatMemory] {message} time={Time.time:F3}", this);
    }
}
