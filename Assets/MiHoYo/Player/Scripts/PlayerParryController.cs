using System;
using UnityEngine;

public class PlayerParryController : MonoBehaviour
{
    public static event Action<PlayerParryController> ParryStarted;
    public static event Action<PlayerParryController> ParryWindowOpened;
    public static event Action<PlayerParryController> ParryWindowClosed;
    public static event Action<PlayerParryController, EnemyParryWindow> ParrySucceeded;
    public static event Action<PlayerParryController, EnemyDaggerProjectile> ProjectileParried;

    private static PlayerParryController _activeParryWindowOwner;

    [Header("References")]
    [SerializeField] private Animator animator;

    [Header("Input Action")]
    [SerializeField] private string parryTriggerName = "Parry";
    [SerializeField] private bool openWindowOnInput;
    [SerializeField] private float inputWindowDuration = 0.12f;
    [SerializeField] private float parryCooldownDuration = 0.35f;

    [Header("Counter")]
    [SerializeField] private float counterOpenDelay = 0.05f;
    [SerializeField] private float counterWindowDuration = 0.9f;

    [Header("Debug")]
    [SerializeField] private bool logDebug;

    private int _parryTriggerHash;
    private bool _parryWindowOpen;
    private bool _counterPending;
    private bool _counterWindowOpen;
    private float _parryWindowTimer;
    private float _parryCooldownTimer;
    private float _counterOpenTimer;
    private float _counterWindowTimer;
    private EnemyParryWindow _lastParriedWindow;

    public static PlayerParryController ActiveParryWindowOwner => _activeParryWindowOwner;
    public bool ParryWindowOpen => _parryWindowOpen;
    public bool CanStartParry => !_parryWindowOpen && _parryCooldownTimer <= 0f;
    public float ParryCooldownRemaining => Mathf.Max(0f, _parryCooldownTimer);
    public bool CounterReady => _counterWindowOpen;
    public bool CounterPending => _counterPending;
    public EnemyParryWindow LastParriedWindow => _lastParriedWindow;

    public static bool TryGetActiveParry(Transform target, out PlayerParryController parrier)
    {
        parrier = null;
        if (_activeParryWindowOwner == null || target == null)
        {
            return false;
        }

        Transform parryTransform = _activeParryWindowOwner.transform;
        if (target == parryTransform || target.IsChildOf(parryTransform) || parryTransform.IsChildOf(target))
        {
            parrier = _activeParryWindowOwner;
            return true;
        }

        return false;
    }

    private void Awake()
    {
        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }

        _parryTriggerHash = Animator.StringToHash(parryTriggerName);
    }

    private void OnValidate()
    {
        inputWindowDuration = Mathf.Max(0.01f, inputWindowDuration);
        parryCooldownDuration = Mathf.Max(0f, parryCooldownDuration);
        counterOpenDelay = Mathf.Max(0f, counterOpenDelay);
        counterWindowDuration = Mathf.Max(0.01f, counterWindowDuration);
    }

    private void Update()
    {
        if (_parryWindowOpen)
        {
            _parryWindowTimer -= Time.deltaTime;
            if (_parryWindowTimer <= 0f)
            {
                CloseParryWindow("Timer");
            }
        }

        if (_parryCooldownTimer > 0f)
        {
            _parryCooldownTimer -= Time.deltaTime;
        }

        if (_counterPending)
        {
            _counterOpenTimer -= Time.deltaTime;
            if (_counterOpenTimer <= 0f)
            {
                _counterPending = false;
                _counterWindowOpen = true;
                _counterWindowTimer = counterWindowDuration;
                LogDebug($"Parry counter window opened. duration={counterWindowDuration:F3}");
            }
        }

        if (_counterWindowOpen)
        {
            _counterWindowTimer -= Time.deltaTime;
            if (_counterWindowTimer <= 0f)
            {
                ClearCounter("Expired");
            }
        }
    }

    public bool TryStartParry()
    {
        if (!CanStartParry)
        {
            LogDebug($"Parry start rejected. windowOpen={_parryWindowOpen}, cooldownRemaining={ParryCooldownRemaining:F3}");
            return false;
        }

        _parryCooldownTimer = parryCooldownDuration;
        SetAnimatorTrigger(_parryTriggerHash, parryTriggerName);
        if (openWindowOnInput)
        {
            OpenParryWindow(inputWindowDuration, "Input");
        }

        LogDebug("Parry started.");
        ParryStarted?.Invoke(this);
        return true;
    }

    public bool TryConsumeCounter()
    {
        if (!_counterWindowOpen)
        {
            return false;
        }

        ClearCounter("Consumed");
        LogDebug("Parry counter consumed.");
        return true;
    }

    public void NotifyParrySucceeded(EnemyParryWindow parriedWindow)
    {
        _lastParriedWindow = parriedWindow;
        CloseParryWindow("Succeeded");
        _counterPending = counterOpenDelay > 0f;
        _counterWindowOpen = counterOpenDelay <= 0f;
        _counterOpenTimer = counterOpenDelay;
        _counterWindowTimer = _counterWindowOpen ? counterWindowDuration : 0f;
        LogDebug(parriedWindow != null
            ? $"Parry succeeded. enemy={parriedWindow.name}, counterOpenDelay={counterOpenDelay:F3}, counterWindow={counterWindowDuration:F3}"
            : $"Parry succeeded. counterOpenDelay={counterOpenDelay:F3}, counterWindow={counterWindowDuration:F3}");
        ParrySucceeded?.Invoke(this, parriedWindow);
    }

    public void NotifyProjectileParried(EnemyDaggerProjectile projectile)
    {
        CloseParryWindow("ProjectileParried");
        LogDebug(projectile != null
            ? $"Projectile parried. projectile={projectile.name}"
            : "Projectile parried.");
        ProjectileParried?.Invoke(this, projectile);
    }

    public void AE_OpenPlayerParryWindow()
    {
        OpenParryWindow(inputWindowDuration, "AnimationEvent");
    }

    public void AE_ClosePlayerParryWindow()
    {
        CloseParryWindow("AnimationEvent");
    }

    private void OpenParryWindow(float duration, string reason)
    {
        _parryWindowOpen = true;
        _parryWindowTimer = Mathf.Max(0.01f, duration);
        _activeParryWindowOwner = this;
        LogDebug($"Parry window opened. reason={reason}, duration={_parryWindowTimer:F3}");
        ParryWindowOpened?.Invoke(this);

        if (!EnemyParryWindow.TryResolveParry(this, out _))
        {
            LogDebug("No active enemy parry window matched.");
        }
    }

    private void CloseParryWindow(string reason)
    {
        if (!_parryWindowOpen)
        {
            return;
        }

        _parryWindowOpen = false;
        _parryWindowTimer = 0f;
        if (_activeParryWindowOwner == this)
        {
            _activeParryWindowOwner = null;
        }

        LogDebug($"Parry window closed. reason={reason}");
        ParryWindowClosed?.Invoke(this);
    }

    private void ClearCounter(string reason)
    {
        if (!_counterPending && !_counterWindowOpen)
        {
            return;
        }

        _counterPending = false;
        _counterWindowOpen = false;
        _counterOpenTimer = 0f;
        _counterWindowTimer = 0f;
        LogDebug($"Parry counter cleared. reason={reason}");
    }

    private void SetAnimatorTrigger(int triggerHash, string triggerName)
    {
        if (animator == null || string.IsNullOrEmpty(triggerName))
        {
            return;
        }

        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.type == AnimatorControllerParameterType.Trigger && parameter.nameHash == triggerHash)
            {
                animator.SetTrigger(triggerHash);
                return;
            }
        }

        LogDebug($"Animator trigger skipped. Missing parameter={triggerName}");
    }

    private void OnDisable()
    {
        CloseParryWindow("Disabled");
        _parryCooldownTimer = 0f;
        ClearCounter("Disabled");
    }

    private void LogDebug(string message)
    {
        if (!logDebug)
        {
            return;
        }

        Debug.Log($"[PlayerParry] {message} time={Time.time:F3}", this);
    }
}
