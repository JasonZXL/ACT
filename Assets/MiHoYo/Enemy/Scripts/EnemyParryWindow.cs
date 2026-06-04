using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnemyParryWindow : MonoBehaviour
{
    private static readonly List<EnemyParryWindow> ActiveWindows = new List<EnemyParryWindow>();

    [Header("Parry Window")]
    [SerializeField] private ParryAttackType attackType = ParryAttackType.Light;
    [SerializeField] private bool parryEnabled = true;
    [SerializeField] private CombatHealth combatHealth;
    [SerializeField] private EnemyAttackWarningWindow attackWarningWindow;
    [SerializeField] private bool suppressDodgeWarningVisual = true;

    [Header("Warning")]
    [SerializeField] private GameObject parryWarningPrefab;
    [SerializeField] private Transform warningOrigin;
    [SerializeField] private Vector3 warningOffset = new Vector3(0f, 0.02f, 1.2f);
    [SerializeField] private bool parentWarningToOrigin = true;
    [SerializeField] private bool useAttackWarningVisualWhenActive = true;

    [Header("Threat Check")]
    [SerializeField] private Transform threatOrigin;
    [SerializeField] private float parryRange = 3f;
    [SerializeField, Range(1f, 360f)] private float parryAngle = 100f;

    [Header("Reaction")]
    [SerializeField] private float parryBreakDelay = 0.1f;
    [SerializeField] private bool useParryHitPause = true;
    [SerializeField] private float parryHitPauseDuration = 0.04f;
    [SerializeField] private bool slowEnemyBeforeBreak = true;
    [SerializeField, Range(0.05f, 1f)] private float enemyPreBreakAnimatorSpeed = 0.25f;
    [SerializeField] private Animator enemyAnimator;

    [Header("Window Timeout")]
    [SerializeField] private bool enableWindowTimeout = true;
    [SerializeField] private float maxWindowDuration = 1f;
    [SerializeField] private float parriedHitIgnoreDuration = 1.25f;

    [Header("Debug")]
    [SerializeField] private bool logDebug;

    private GameObject _activeWarning;
    private bool _windowOpen;
    private bool _parried;
    private PlayerParryController _successfulParrier;
    private float _windowOpenTime = -999f;
    private float _parrySuccessTime = -999f;
    private Coroutine _parryBreakRoutine;
    private Animator _parryEffectEnemyAnimator;
    private Animator _parryEffectPlayerAnimator;
    private float _parryEffectEnemyOriginalSpeed = 1f;
    private float _parryEffectPlayerOriginalSpeed = 1f;
    private bool _parryEffectEnemySpeedOverridden;
    private bool _parryEffectPlayerSpeedOverridden;

    public bool WindowOpen => _windowOpen;
    public bool WasParried => _parried;
    public ParryAttackType AttackType => attackType;
    public float LastWindowOpenTime => _windowOpenTime;

    private void Awake()
    {
        if (combatHealth == null)
        {
            combatHealth = GetComponent<CombatHealth>();
        }

        if (attackWarningWindow == null)
        {
            attackWarningWindow = GetComponent<EnemyAttackWarningWindow>();
        }

        if (enemyAnimator == null)
        {
            enemyAnimator = GetComponentInChildren<Animator>();
        }
    }

    private void OnValidate()
    {
        parryRange = Mathf.Max(0.1f, parryRange);
        maxWindowDuration = Mathf.Max(0.01f, maxWindowDuration);
        parriedHitIgnoreDuration = Mathf.Max(0.01f, parriedHitIgnoreDuration);
        parryBreakDelay = Mathf.Max(0f, parryBreakDelay);
        parryHitPauseDuration = Mathf.Max(0f, parryHitPauseDuration);
    }

    private void Update()
    {
        if (!enableWindowTimeout || !_windowOpen)
        {
            return;
        }

        if (Time.time - _windowOpenTime >= maxWindowDuration)
        {
            CloseWindow();
        }
    }

    private void OnDisable()
    {
        CloseWindow();
        _parried = false;
        _successfulParrier = null;
        _parrySuccessTime = -999f;
        if (_parryBreakRoutine != null)
        {
            StopCoroutine(_parryBreakRoutine);
            _parryBreakRoutine = null;
        }

        RestoreParryEffectAnimatorSpeeds();
        ClearWarning();
    }

    public static bool TryResolveParry(PlayerParryController parrier, out EnemyParryWindow parriedWindow)
    {
        parriedWindow = null;
        if (parrier == null)
        {
            return false;
        }

        for (int i = ActiveWindows.Count - 1; i >= 0; i--)
        {
            EnemyParryWindow window = ActiveWindows[i];
            if (window == null)
            {
                ActiveWindows.RemoveAt(i);
                continue;
            }

            if (window.TryParry(parrier))
            {
                parriedWindow = window;
                return true;
            }
        }

        return false;
    }

    public void AE_OpenParryWindow()
    {
        OpenWindow();
    }

    public void AE_CloseParryWindow()
    {
        CloseWindow();
    }

    public void ShowParryWarningVisual(string reason = "Direct")
    {
        SpawnWarning();
        LogDebug($"Parry warning visual shown. reason={reason}");
    }

    public void HideParryWarningVisual(string reason = "Direct")
    {
        ClearWarning();
        LogDebug($"Parry warning visual hidden. reason={reason}");
    }

    public void OpenWindow()
    {
        if (_windowOpen)
        {
            return;
        }

        _windowOpen = true;
        _parried = false;
        _successfulParrier = null;
        _parrySuccessTime = -999f;
        _windowOpenTime = Time.time;
        if (!ActiveWindows.Contains(this))
        {
            ActiveWindows.Add(this);
        }

        bool useExistingAttackWarningVisual = useAttackWarningVisualWhenActive &&
            attackWarningWindow != null &&
            attackWarningWindow.WindowOpen;

        if (!useExistingAttackWarningVisual && suppressDodgeWarningVisual && attackWarningWindow != null)
        {
            attackWarningWindow.SuppressWarningFromParryWindow(this, true);
        }

        if (useExistingAttackWarningVisual)
        {
            LogDebug("Parry warning visual skipped because attack warning visual is already active.");
        }
        else
        {
            SpawnWarning();
        }

        LogDebug($"Parry window opened. attackType={attackType}");

        if (attackWarningWindow != null &&
            attackWarningWindow.TryConsumeBufferedParry(out PlayerParryController bufferedParrier) &&
            TryParry(bufferedParrier))
        {
            LogDebug($"Parry resolved from attack warning buffer. player={bufferedParrier.name}");
            return;
        }

        TryResolveParry(PlayerParryController.ActiveParryWindowOwner, out _);
    }

    public void CloseWindow()
    {
        if (!_windowOpen)
        {
            return;
        }

        _windowOpen = false;
        ActiveWindows.Remove(this);
        if (suppressDodgeWarningVisual && attackWarningWindow != null)
        {
            attackWarningWindow.SuppressWarningFromParryWindow(this, false);
        }

        ClearWarning();
        LogDebug("Parry window closed.");
    }

    public bool IsParriedBy(Transform target)
    {
        if (!_parried || _successfulParrier == null || target == null)
        {
            return false;
        }

        if (Time.time - _parrySuccessTime > parriedHitIgnoreDuration)
        {
            LogDebug($"Parried hit ignore expired. elapsed={Time.time - _parrySuccessTime:F3}, duration={parriedHitIgnoreDuration:F3}");
            _parried = false;
            _successfulParrier = null;
            _parrySuccessTime = -999f;
            return false;
        }

        return target == _successfulParrier.transform || target.IsChildOf(_successfulParrier.transform);
    }

    public void ClearParriedState(string reason)
    {
        if (!_parried && _successfulParrier == null)
        {
            _parrySuccessTime = -999f;
            return;
        }

        LogDebug($"Parried state cleared. reason={reason}, parrier={(_successfulParrier != null ? _successfulParrier.name : "None")}");
        _parried = false;
        _successfulParrier = null;
        _parrySuccessTime = -999f;
    }

    private bool TryParry(PlayerParryController parrier)
    {
        if (!parryEnabled || !_windowOpen || _parried || parrier == null)
        {
            return false;
        }

        if (attackType != ParryAttackType.Light)
        {
            LogDebug($"Parry rejected. reason=AttackType, attackType={attackType}, player={parrier.name}");
            return false;
        }

        if (!IsPlayerInsideThreat(parrier.transform))
        {
            LogDebug($"Parry rejected. reason=OutsideThreat, player={parrier.name}");
            return false;
        }

        _parried = true;
        _successfulParrier = parrier;
        _parrySuccessTime = Time.time;
        ClearWarning();
        if (_parryBreakRoutine != null)
        {
            StopCoroutine(_parryBreakRoutine);
            RestoreParryEffectAnimatorSpeeds();
        }

        _parryBreakRoutine = StartCoroutine(ApplyParryBreakAfterDelay(parrier.gameObject));
        parrier.NotifyParrySucceeded(this);
        LogDebug($"Parry succeeded. player={parrier.name}");
        return true;
    }

    private IEnumerator ApplyParryBreakAfterDelay(GameObject parrySource)
    {
        BeginParryEffectAnimatorSpeeds(parrySource);

        float remainingDelay = parryBreakDelay;
        if (useParryHitPause && parryHitPauseDuration > 0f)
        {
            if (_parryEffectEnemyAnimator != null)
            {
                _parryEffectEnemyAnimator.speed = 0f;
            }

            // 注意：不在此处冻结玩家 Animator。
            // 玩家一侧的 hit-stop 由 PlayerHitStopSystem 独立管理（已将 animator.speed 设为 0）。
            // 若此处再读取并记录 speed 作为"原始值"，将捕获到 0 而非真实的原始速度，
            // 导致 WaitForSecondsRealtime 结束后把玩家 Animator 永久冻结在 speed=0。

            float pauseDuration = parryHitPauseDuration;
            yield return new WaitForSecondsRealtime(pauseDuration);
            remainingDelay = Mathf.Max(0f, remainingDelay - pauseDuration);
        }

        if (slowEnemyBeforeBreak && _parryEffectEnemyAnimator != null)
        {
            _parryEffectEnemyAnimator.speed = enemyPreBreakAnimatorSpeed;
        }
        else if (_parryEffectEnemyAnimator != null && _parryEffectEnemySpeedOverridden)
        {
            _parryEffectEnemyAnimator.speed = _parryEffectEnemyOriginalSpeed;
        }

        if (remainingDelay > 0f)
        {
            yield return new WaitForSecondsRealtime(remainingDelay);
        }

        RestoreParryEffectAnimatorSpeeds();
        _parryBreakRoutine = null;
        combatHealth?.ApplyParryBreak(parrySource);
    }

    private void BeginParryEffectAnimatorSpeeds(GameObject parrySource)
    {
        RestoreParryEffectAnimatorSpeeds();

        _parryEffectEnemyAnimator = enemyAnimator != null ? enemyAnimator : GetComponentInChildren<Animator>();
        if (_parryEffectEnemyAnimator != null)
        {
            _parryEffectEnemyOriginalSpeed = _parryEffectEnemyAnimator.speed;
            _parryEffectEnemySpeedOverridden = true;
        }

        // 不捕获玩家 Animator：玩家的 hit-stop 由 PlayerHitStopSystem 管理，
        // 此处读取 speed 可能拿到已被冻结的 0，进而在弹刀 hit-pause 结束后把玩家永久冻结。
        // _parryEffectPlayerAnimator 保持 null，RestoreParryEffectAnimatorSpeeds 对其无操作。
        _parryEffectPlayerAnimator = null;
        _parryEffectPlayerSpeedOverridden = false;
    }

    private void RestoreParryEffectAnimatorSpeeds()
    {
        if (_parryEffectEnemyAnimator != null && _parryEffectEnemySpeedOverridden)
        {
            _parryEffectEnemyAnimator.speed = _parryEffectEnemyOriginalSpeed;
        }

        if (_parryEffectPlayerAnimator != null && _parryEffectPlayerSpeedOverridden)
        {
            _parryEffectPlayerAnimator.speed = _parryEffectPlayerOriginalSpeed;
        }

        _parryEffectEnemyAnimator = null;
        _parryEffectPlayerAnimator = null;
        _parryEffectEnemyOriginalSpeed = 1f;
        _parryEffectPlayerOriginalSpeed = 1f;
        _parryEffectEnemySpeedOverridden = false;
        _parryEffectPlayerSpeedOverridden = false;
    }

    private bool IsPlayerInsideThreat(Transform player)
    {
        if (player == null)
        {
            return false;
        }

        Transform origin = threatOrigin != null ? threatOrigin : transform;
        Vector3 toPlayer = player.position - origin.position;
        toPlayer.y = 0f;

        float distance = toPlayer.magnitude;
        if (distance > parryRange || distance <= 0.001f)
        {
            return false;
        }

        Vector3 forward = origin.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude <= 0.001f)
        {
            return true;
        }

        float angle = Vector3.Angle(forward.normalized, toPlayer / distance);
        return angle <= parryAngle * 0.5f;
    }

    private void SpawnWarning()
    {
        if (_activeWarning != null)
        {
            return;
        }

        if (parryWarningPrefab == null)
        {
            return;
        }

        Transform origin = warningOrigin != null ? warningOrigin : transform;
        Vector3 position = origin.position + origin.rotation * warningOffset;
        Quaternion rotation = Quaternion.Euler(0f, origin.eulerAngles.y, 0f);
        Transform parent = parentWarningToOrigin ? origin : null;
        _activeWarning = Instantiate(parryWarningPrefab, position, rotation, parent);
    }

    private void ClearWarning()
    {
        if (_activeWarning == null)
        {
            return;
        }

        Destroy(_activeWarning);
        _activeWarning = null;
    }

    private void LogDebug(string message)
    {
        if (!logDebug)
        {
            return;
        }

        Debug.Log($"[EnemyParryWindow] {message} time={Time.time:F3}", this);
    }
}
