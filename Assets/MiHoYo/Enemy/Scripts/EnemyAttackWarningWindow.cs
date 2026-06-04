using System;
using UnityEngine;

public class EnemyAttackWarningWindow : MonoBehaviour
{
    public static event Action<EnemyAttackWarningWindow, KianaCombatController> PerfectDodgeConfirmed;

    [Header("Warning")]
    [SerializeField] private GameObject warningPrefab;
    [SerializeField] private Transform warningOrigin;
    [SerializeField] private Vector3 warningOffset = new Vector3(0f, 0.02f, 1.5f);
    [SerializeField] private bool parentWarningToOrigin = true;

    [Header("Perfect Dodge")]
    [SerializeField] private bool perfectDodgeEnabled = true;
    [SerializeField] private EnemyParryWindow parryWindow;
    [SerializeField] private Transform threatOrigin;
    [SerializeField] private float perfectDodgeRange = 4f;
    [SerializeField, Range(1f, 360f)] private float perfectDodgeAngle = 140f;
    [SerializeField] private WitchTimeController witchTimeController;
    [SerializeField] private bool useDodgeConfirmationWindow = true;
    [SerializeField] private float dodgeConfirmGraceAfterWarningClosed = 0.15f;

    [Header("Parry Buffer")]
    [SerializeField] private bool enableParryBufferDuringWarning = true;
    [SerializeField] private bool requireParryInsideThreat = true;
    [SerializeField] private float parryConfirmGraceAfterWarningClosed = 0.12f;

    [Header("Window Timeout")]
    [SerializeField] private bool enableWindowTimeout = true;
    [SerializeField] private float maxWindowDuration = 3f;

    [Header("Debug")]
    [SerializeField] private bool logDebug;

    private GameObject _activeWarning;
    private bool _windowOpen;
    private float _windowOpenTime = -999f;
    private float _lastWindowCloseTime = -999f;
    private bool _perfectDodgeTriggered;
    private bool _witchTimeTriggered;
    private KianaCombatController _pendingDodger;
    private KianaCombatController _bufferedDodger;
    private KianaCombatController _perfectDodger;
    private float _bufferedDodgeTime = -999f;
    private bool _dodgeConfirmWindowOpen;
    private float _dodgeConfirmWindowOpenTime = -999f;
    private EnemyParryWindow _warningSuppressor;
    private PlayerParryController _bufferedParrier;
    private float _bufferedParryTime = -999f;
    private bool _parryBufferAllowedThisWindow;
    private DefenseRecordResolution _defenseRecordResolution = DefenseRecordResolution.None;

    private enum DefenseRecordResolution
    {
        None = 0,
        Dodge = 1,
        Parry = 2
    }

    public bool WindowOpen => _windowOpen;
    public bool PerfectDodgeTriggered => _perfectDodgeTriggered;
    public KianaCombatController PerfectDodger => _perfectDodger;
    public float LastWindowOpenTime => _windowOpenTime;
    public bool DodgeConfirmWindowOpen => _dodgeConfirmWindowOpen;
    public bool HasBufferedDodge => _bufferedDodger != null;
    public KianaCombatController BufferedDodger => _bufferedDodger;
    public bool HasBufferedParry => _bufferedParrier != null;
    public PlayerParryController BufferedParrier => _bufferedParrier;

    private void Awake()
    {
        if (witchTimeController == null)
        {
            witchTimeController = GetComponent<WitchTimeController>();
        }

        if (parryWindow == null)
        {
            parryWindow = GetComponent<EnemyParryWindow>();
        }
    }

    private void OnEnable()
    {
        KianaCombatController.DodgeStarted += OnKianaDodgeStarted;
        KianaCombatController.PerfectDodgeWindowOpened += OnKianaPerfectDodgeWindowOpened;
        KianaCombatController.PerfectDodgeWindowClosed += OnKianaPerfectDodgeWindowClosed;
        PlayerParryController.ParryWindowOpened += OnPlayerParryWindowOpened;
        PlayerParryController.ParryWindowClosed += OnPlayerParryWindowClosed;
        if (witchTimeController != null)
        {
            witchTimeController.WitchTimeStopped += OnWitchTimeStopped;
        }
    }

    private void Update()
    {
        if (!enableWindowTimeout || !_windowOpen)
        {
            return;
        }

        if (Time.time - _windowOpenTime >= maxWindowDuration)
        {
            LogDebug($"Attack warning window timed out after {maxWindowDuration:F2}s. Force closing.");
            CloseWindow();
        }

        ClearExpiredBufferedDodge();
        ClearExpiredBufferedParry();
    }

    private void OnDisable()
    {
        KianaCombatController.DodgeStarted -= OnKianaDodgeStarted;
        KianaCombatController.PerfectDodgeWindowOpened -= OnKianaPerfectDodgeWindowOpened;
        KianaCombatController.PerfectDodgeWindowClosed -= OnKianaPerfectDodgeWindowClosed;
        PlayerParryController.ParryWindowOpened -= OnPlayerParryWindowOpened;
        PlayerParryController.ParryWindowClosed -= OnPlayerParryWindowClosed;
        if (witchTimeController != null)
        {
            witchTimeController.WitchTimeStopped -= OnWitchTimeStopped;
        }

        _windowOpen = false;
        _perfectDodgeTriggered = false;
        _witchTimeTriggered = false;
        _pendingDodger = null;
        _perfectDodger = null;
        _dodgeConfirmWindowOpen = false;
        _dodgeConfirmWindowOpenTime = -999f;
        _warningSuppressor = null;
        _defenseRecordResolution = DefenseRecordResolution.None;
        ClearBufferedDodge("Disabled");
        ClearBufferedParry("Disabled");
        ClearActiveWarningVisual();
    }

    private void OnValidate()
    {
        perfectDodgeRange = Mathf.Max(0.1f, perfectDodgeRange);
        dodgeConfirmGraceAfterWarningClosed = Mathf.Max(0f, dodgeConfirmGraceAfterWarningClosed);
        parryConfirmGraceAfterWarningClosed = Mathf.Max(0f, parryConfirmGraceAfterWarningClosed);
    }

    public void AE_StartAttackWarningWindow()
    {
        OpenWindow(false);
    }

    public void AE_StartParryableAttackWarningWindow()
    {
        OpenWindow(true);
    }

    public void AE_EndAttackWarningWindow()
    {
        CloseWindow();
    }

    public void AE_OpenDodgeWindow()
    {
        OpenDodgeWindow();
    }

    public void AE_CloseDodgeWindow()
    {
        CloseDodgeWindow();
    }

    public void OpenWindow()
    {
        OpenWindow(false);
    }

    public void OpenWindow(bool allowParryBuffer)
    {
        if (_windowOpen)
        {
            TryUpgradeOpenWindow(allowParryBuffer);
            return;
        }

        _windowOpen = true;
        _parryBufferAllowedThisWindow = allowParryBuffer;
        _windowOpenTime = Time.time;
        _lastWindowCloseTime = -999f;
        _perfectDodgeTriggered = false;
        _witchTimeTriggered = false;
        _pendingDodger = null;
        _perfectDodger = null;
        _dodgeConfirmWindowOpen = false;
        _dodgeConfirmWindowOpenTime = -999f;
        _defenseRecordResolution = DefenseRecordResolution.None;
        ClearBufferedDodge("WindowOpened");
        ClearBufferedParry("WindowOpened");
        SpawnActiveWarningVisual();
        LogDebug($"Attack warning window opened. parryBufferAllowed={_parryBufferAllowedThisWindow}");
        TryTriggerPerfectDodge(KianaCombatController.ActivePerfectDodgeWindowOwner, "enemy window opened while player dodge window was active");
        TryBufferPlayerParry(PlayerParryController.ActiveParryWindowOwner, "enemy warning opened while player parry window was active");
    }

    private void TryUpgradeOpenWindow(bool allowParryBuffer)
    {
        if (!allowParryBuffer)
        {
            LogDebug($"Attack warning window open request ignored. existingParryBufferAllowed={_parryBufferAllowedThisWindow}, requestedParryBuffer=False");
            return;
        }

        if (_parryBufferAllowedThisWindow)
        {
            LogDebug("Parryable attack warning window open request ignored because parry warning is already active.");
            return;
        }

        _parryBufferAllowedThisWindow = true;
        ClearWarning();
        SpawnActiveWarningVisual();
        LogDebug("Attack warning window upgraded to parryable warning. Parry warning visual has priority.");
        TryBufferPlayerParry(PlayerParryController.ActiveParryWindowOwner, "enemy warning upgraded while player parry window was active");
    }

    public void CloseWindow()
    {
        if (!_windowOpen)
        {
            return;
        }

        _windowOpen = false;
        _lastWindowCloseTime = Time.time;
        bool wasParryableWarning = _parryBufferAllowedThisWindow;
        _parryBufferAllowedThisWindow = false;
        _pendingDodger = null;
        _dodgeConfirmWindowOpen = false;
        _dodgeConfirmWindowOpenTime = -999f;
        ClearActiveWarningVisual(wasParryableWarning);
        LogDebug("Attack warning window closed.");
    }

    public void OpenDodgeWindow()
    {
        if (!_windowOpen && !CanConfirmBufferedDodgeAfterWindowClosed())
        {
            LogDebug("Dodge confirm window ignored because attack warning window is not open.");
            return;
        }

        if (_dodgeConfirmWindowOpen)
        {
            return;
        }

        _dodgeConfirmWindowOpen = true;
        _dodgeConfirmWindowOpenTime = Time.time;
        LogDebug("Dodge confirm window opened.");

        if (_bufferedDodger != null)
        {
            ConfirmPerfectDodge(_bufferedDodger, "buffered perfect dodge consumed by enemy dodge window");
            return;
        }

        TryTriggerPerfectDodge(
            KianaCombatController.ActivePerfectDodgeWindowOwner,
            "enemy dodge window opened while player dodge window was active");
    }

    public void CloseDodgeWindow()
    {
        if (!_dodgeConfirmWindowOpen)
        {
            return;
        }

        _dodgeConfirmWindowOpen = false;
        LogDebug($"Dodge confirm window closed. openedAt={_dodgeConfirmWindowOpenTime:F3}");
        _dodgeConfirmWindowOpenTime = -999f;
    }

    public bool TryConsumeBufferedParry(out PlayerParryController parrier)
    {
        parrier = _bufferedParrier;
        if (parrier == null)
        {
            return false;
        }

        if (_defenseRecordResolution == DefenseRecordResolution.Dodge || _perfectDodgeTriggered)
        {
            ClearBufferedParry("IgnoredByPerfectDodge");
            parrier = null;
            return false;
        }

        if (!_windowOpen && !CanConfirmBufferedParryAfterWindowClosed())
        {
            ClearBufferedParry("ConfirmGraceExpiredBeforeConsume");
            parrier = null;
            return false;
        }

        _defenseRecordResolution = DefenseRecordResolution.Parry;
        ClearBufferedDodge("IgnoredByParryConsume");
        ClearBufferedParry("Consumed");
        return true;
    }

    public void SuppressWarningFromParryWindow(EnemyParryWindow parryWindow, bool suppressed)
    {
        if (parryWindow == null)
        {
            return;
        }

        if (suppressed)
        {
            _warningSuppressor = parryWindow;
            ClearWarning();
            LogDebug($"Warning visual suppressed by parry window. parryWindow={parryWindow.name}");
            return;
        }

        if (_warningSuppressor == parryWindow)
        {
            _warningSuppressor = null;
            LogDebug($"Warning visual suppression cleared by parry window. parryWindow={parryWindow.name}");
        }
    }

    public bool IsPerfectDodgedBy(Transform target)
    {
        return _perfectDodgeTriggered &&
            _perfectDodger != null &&
            target != null &&
            (target == _perfectDodger.transform || target.IsChildOf(_perfectDodger.transform));
    }

    private void OnKianaDodgeStarted(KianaCombatController kiana)
    {
        if (!perfectDodgeEnabled || !_windowOpen || _perfectDodgeTriggered || kiana == null)
        {
            return;
        }

        if (!IsDodgeInsideThreat(kiana.transform))
        {
            LogDebug($"Perfect dodge rejected. player={kiana.name}");
            return;
        }

        _pendingDodger = kiana;
        LogDebug($"Perfect dodge candidate registered from dodge input. player={kiana.name}");
        TryTriggerPerfectDodge(kiana, "dodge input while player dodge window was already active");
    }

    private void OnKianaPerfectDodgeWindowOpened(KianaCombatController kiana)
    {
        TryTriggerPerfectDodge(kiana, "player dodge window opened");
    }

    private void OnKianaPerfectDodgeWindowClosed(KianaCombatController kiana)
    {
        if (_pendingDodger == kiana)
        {
            _pendingDodger = null;
            LogDebug($"Perfect dodge candidate cleared because player dodge window closed. player={kiana.name}");
        }

        if (_bufferedDodger == kiana)
        {
            LogDebug($"Buffered perfect dodge remains after player dodge window closed. player={kiana.name}, bufferedAt={_bufferedDodgeTime:F3}");
        }
    }

    private void OnPlayerParryWindowOpened(PlayerParryController parrier)
    {
        TryBufferPlayerParry(parrier, "player parry window opened");
    }

    private void OnPlayerParryWindowClosed(PlayerParryController parrier)
    {
        if (_bufferedParrier == parrier)
        {
            LogDebug($"Buffered parry remains after player parry window closed. player={parrier.name}, bufferedAt={_bufferedParryTime:F3}");
        }
    }

    private void OnWitchTimeStopped()
    {
        if (!_perfectDodgeTriggered)
        {
            return;
        }

        LogDebug("Witch time stopped. Perfect dodge state kept until next attack warning window.");
    }

    private void TryTriggerPerfectDodge(KianaCombatController kiana, string reason)
    {
        if (!perfectDodgeEnabled || !_windowOpen || _perfectDodgeTriggered || kiana == null)
        {
            return;
        }

        if (_defenseRecordResolution == DefenseRecordResolution.Parry)
        {
            LogDebug($"Perfect dodge ignored because parry record was already consumed. reason={reason}, player={kiana.name}");
            return;
        }

        if (_pendingDodger != null && _pendingDodger != kiana)
        {
            return;
        }

        if (KianaCombatController.ActivePerfectDodgeWindowOwner != kiana)
        {
            LogDebug($"Perfect dodge waiting for player dodge window. reason={reason}, player={kiana.name}");
            return;
        }

        if (!IsDodgeInsideThreat(kiana.transform))
        {
            LogDebug($"Perfect dodge rejected at trigger step. reason={reason}, player={kiana.name}");
            return;
        }

        if (useDodgeConfirmationWindow && !_dodgeConfirmWindowOpen)
        {
            BufferPerfectDodge(kiana, reason);
            return;
        }

        ConfirmPerfectDodge(kiana, reason);
    }

    private void ConfirmPerfectDodge(KianaCombatController kiana, string reason)
    {
        if (!perfectDodgeEnabled || _perfectDodgeTriggered || kiana == null)
        {
            return;
        }

        _perfectDodgeTriggered = true;
        _defenseRecordResolution = DefenseRecordResolution.Dodge;
        _pendingDodger = null;
        _perfectDodger = kiana;
        ClearBufferedDodge("Confirmed");
        ClearBufferedParry("IgnoredByPerfectDodge");
        PerfectDodgeConfirmed?.Invoke(this, kiana);
        LogDebug($"Perfect dodge confirmed. Triggering witch time immediately. reason={reason}, player={kiana.name}");
        TryStartWitchTimeEffect(kiana, "perfect dodge confirmed by enemy dodge window");
    }

    private void TryStartWitchTimeEffect(KianaCombatController kiana, string reason)
    {
        if (!_perfectDodgeTriggered || _witchTimeTriggered || _perfectDodger == null || kiana != _perfectDodger)
        {
            return;
        }

        _witchTimeTriggered = true;
        witchTimeController?.TriggerWitchTime(kiana);
        LogDebug($"Witch time effect triggered. reason={reason}, player={kiana.name}");
    }

    private void BufferPerfectDodge(KianaCombatController kiana, string reason)
    {
        if (kiana == null)
        {
            return;
        }

        if (_defenseRecordResolution == DefenseRecordResolution.Parry)
        {
            LogDebug($"Buffered perfect dodge ignored because parry record was already consumed. reason={reason}, player={kiana.name}");
            return;
        }

        _bufferedDodger = kiana;
        _bufferedDodgeTime = Time.time;
        LogDebug($"Buffered perfect dodge registered. reason={reason}, player={kiana.name}");
    }

    private void ClearBufferedDodge(string reason)
    {
        if (_bufferedDodger == null)
        {
            _bufferedDodgeTime = -999f;
            return;
        }

        LogDebug($"Buffered perfect dodge cleared. reason={reason}, player={_bufferedDodger.name}, bufferedAt={_bufferedDodgeTime:F3}");
        _bufferedDodger = null;
        _bufferedDodgeTime = -999f;
    }

    private bool CanConfirmBufferedDodgeAfterWindowClosed()
    {
        return _bufferedDodger != null &&
            _lastWindowCloseTime > 0f &&
            Time.time - _lastWindowCloseTime <= dodgeConfirmGraceAfterWarningClosed;
    }

    private void ClearExpiredBufferedDodge()
    {
        if (_windowOpen || _bufferedDodger == null || _lastWindowCloseTime <= 0f)
        {
            return;
        }

        if (Time.time - _lastWindowCloseTime > dodgeConfirmGraceAfterWarningClosed)
        {
            ClearBufferedDodge("ConfirmGraceExpired");
        }
    }

    private bool IsDodgeInsideThreat(Transform player)
    {
        Transform origin = threatOrigin != null ? threatOrigin : transform;
        Vector3 toPlayer = player.position - origin.position;
        toPlayer.y = 0f;

        float distance = toPlayer.magnitude;
        if (distance > perfectDodgeRange || distance <= 0.001f)
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
        return angle <= perfectDodgeAngle * 0.5f;
    }

    private void TryBufferPlayerParry(PlayerParryController parrier, string reason)
    {
        if (!enableParryBufferDuringWarning || !_parryBufferAllowedThisWindow || !_windowOpen || parrier == null)
        {
            return;
        }

        if (_defenseRecordResolution == DefenseRecordResolution.Dodge || _perfectDodgeTriggered)
        {
            LogDebug($"Buffered parry ignored because perfect dodge was already confirmed. source={reason}, player={parrier.name}");
            return;
        }

        if (requireParryInsideThreat && !IsDodgeInsideThreat(parrier.transform))
        {
            LogDebug($"Buffered parry rejected. reason=OutsideThreat, source={reason}, player={parrier.name}");
            return;
        }

        _bufferedParrier = parrier;
        _bufferedParryTime = Time.time;
        LogDebug($"Buffered parry registered. reason={reason}, player={parrier.name}");
    }

    private void ClearBufferedParry(string reason)
    {
        if (_bufferedParrier == null)
        {
            _bufferedParryTime = -999f;
            return;
        }

        LogDebug($"Buffered parry cleared. reason={reason}, player={_bufferedParrier.name}, bufferedAt={_bufferedParryTime:F3}");
        _bufferedParrier = null;
        _bufferedParryTime = -999f;
    }

    private bool CanConfirmBufferedParryAfterWindowClosed()
    {
        return _bufferedParrier != null &&
            _lastWindowCloseTime > 0f &&
            Time.time - _lastWindowCloseTime <= parryConfirmGraceAfterWarningClosed;
    }

    private void ClearExpiredBufferedParry()
    {
        if (_windowOpen || _bufferedParrier == null || _lastWindowCloseTime <= 0f)
        {
            return;
        }

        if (Time.time - _lastWindowCloseTime > parryConfirmGraceAfterWarningClosed)
        {
            ClearBufferedParry("ConfirmGraceExpired");
        }
    }

    private void SpawnActiveWarningVisual()
    {
        if (_parryBufferAllowedThisWindow && parryWindow != null)
        {
            parryWindow.ShowParryWarningVisual("AttackWarningWindow");
            return;
        }

        SpawnWarning();
    }

    private void ClearActiveWarningVisual()
    {
        ClearActiveWarningVisual(_parryBufferAllowedThisWindow);
    }

    private void ClearActiveWarningVisual(bool wasParryableWarning)
    {
        if (wasParryableWarning && parryWindow != null)
        {
            parryWindow.HideParryWarningVisual("AttackWarningWindow");
            return;
        }

        ClearWarning();
    }

    private void SpawnWarning()
    {
        if (_warningSuppressor != null)
        {
            return;
        }

        if (warningPrefab == null)
        {
            return;
        }

        Transform origin = warningOrigin != null ? warningOrigin : transform;
        Vector3 position = origin.position + origin.rotation * warningOffset;
        Quaternion rotation = Quaternion.Euler(0f, origin.eulerAngles.y, 0f);
        Transform parent = parentWarningToOrigin ? origin : null;
        _activeWarning = Instantiate(warningPrefab, position, rotation, parent);
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

        Debug.Log($"[EnemyAttackWarning] {message} time={Time.time:F3}", this);
    }
}
