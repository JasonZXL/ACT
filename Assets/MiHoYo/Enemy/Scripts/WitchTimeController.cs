using UnityEngine;

public class WitchTimeController : MonoBehaviour
{
    public event System.Action WitchTimeStopped;
    [Header("Witch Time")]
    [SerializeField, Range(0.05f, 1f)] private float enemyAnimatorSpeed = 0.5f;
    [SerializeField] private float duration = 1.5f;
    [SerializeField] private Animator[] affectedAnimators;
    [SerializeField] private bool fadeOutWhenDodgeCounterDeclined = true;
    [SerializeField] private float dodgeCounterDeclinedFadeOutDuration = 1f;

    [Header("Player Dodge Slow Motion")]
    [SerializeField] private bool slowPerfectDodger = true;
    [SerializeField, Range(0.05f, 1f)] private float perfectDodgerAnimatorSpeed = 0.35f;
    [SerializeField] private float perfectDodgerSlowDuration = 0.18f;

    [Header("Visual")]
    [SerializeField] private WitchTimeVisualController visualController;

    [Header("Debug")]
    [SerializeField] private bool logDebug;

    private float[] _originalAnimatorSpeeds;
    private float _timer;
    private bool _active;
    private bool _fadeOutActive;
    private float _fadeOutTimer;
    private float _fadeOutDuration;
    private float[] _fadeOutStartSpeeds;
    private KianaCombatController _perfectDodger;
    private Animator _slowedDodgerAnimator;
    private float _slowedDodgerOriginalSpeed = 1f;
    private float _slowedDodgerTimer;

    public bool Active => _active;
    public float Duration => duration;

    private void Awake()
    {
        if (affectedAnimators == null || affectedAnimators.Length == 0)
        {
            Animator animator = GetComponentInChildren<Animator>();
            if (animator != null)
            {
                affectedAnimators = new[] { animator };
            }
        }

        if (visualController == null)
        {
            visualController = GetComponent<WitchTimeVisualController>();
        }

        CacheOriginalSpeeds();
    }

    private void OnValidate()
    {
        duration = Mathf.Max(0.01f, duration);
        dodgeCounterDeclinedFadeOutDuration = Mathf.Max(0.01f, dodgeCounterDeclinedFadeOutDuration);
        perfectDodgerSlowDuration = Mathf.Max(0f, perfectDodgerSlowDuration);
    }

    private void OnEnable()
    {
        KianaCombatController.DodgeCounterDeclinedByMoveInput += OnDodgeCounterDeclinedByMoveInput;
    }

    private void Update()
    {
        UpdateSlowedDodger();

        if (!_active)
        {
            return;
        }

        if (_fadeOutActive)
        {
            UpdateFadeOut();
            return;
        }

        _timer -= Time.unscaledDeltaTime;
        if (_timer <= 0f)
        {
            StopWitchTime();
        }
    }

    public void TriggerWitchTime()
    {
        TriggerWitchTime(null);
    }

    public void TriggerWitchTime(KianaCombatController perfectDodger)
    {
        if (affectedAnimators == null || affectedAnimators.Length == 0)
        {
            return;
        }

        if (!_active)
        {
            CacheOriginalSpeeds();
            ApplyAnimatorSpeed(enemyAnimatorSpeed);
            LogDebug($"Witch time started. speed={enemyAnimatorSpeed:F2}, duration={duration:F2}");
        }
        else
        {
            LogDebug($"Witch time refreshed. duration={duration:F2}");
        }

        _active = true;
        _fadeOutActive = false;
        _perfectDodger = perfectDodger;
        _timer = duration;
        SlowPerfectDodger(perfectDodger);
        visualController?.Play(duration, perfectDodger != null ? perfectDodger.transform : null);
    }

    public void StopWitchTime()
    {
        if (!_active)
        {
            return;
        }

        RestoreAnimatorSpeeds();
        _active = false;
        _fadeOutActive = false;
        _timer = 0f;
        _fadeOutTimer = 0f;
        _perfectDodger = null;
        visualController?.Stop();
        LogDebug("Witch time ended.");
        WitchTimeStopped?.Invoke();
    }

    public void FadeOutWitchTime(float fadeOutDuration)
    {
        if (!_active)
        {
            return;
        }

        BeginFadeOut(Mathf.Max(0.01f, fadeOutDuration), "Manual");
    }

    private void OnDodgeCounterDeclinedByMoveInput(KianaCombatController kiana)
    {
        if (!fadeOutWhenDodgeCounterDeclined || !_active)
        {
            return;
        }

        if (_perfectDodger != null && kiana != _perfectDodger)
        {
            return;
        }

        BeginFadeOut(dodgeCounterDeclinedFadeOutDuration, "DodgeCounterDeclinedByMoveInput");
    }

    private void BeginFadeOut(float fadeOutDuration, string reason)
    {
        CacheFadeOutStartSpeeds();
        _fadeOutDuration = Mathf.Max(0.01f, fadeOutDuration);
        _fadeOutTimer = 0f;
        _fadeOutActive = true;
        _timer = _fadeOutDuration;
        visualController?.Stop();
        LogDebug($"Witch time fade-out started. reason={reason}, duration={_fadeOutDuration:F2}");
    }

    private void UpdateFadeOut()
    {
        _fadeOutTimer += Time.unscaledDeltaTime;
        float t = Mathf.Clamp01(_fadeOutTimer / Mathf.Max(0.01f, _fadeOutDuration));

        if (affectedAnimators != null && _originalAnimatorSpeeds != null && _fadeOutStartSpeeds != null)
        {
            for (int i = 0; i < affectedAnimators.Length; i++)
            {
                Animator animator = affectedAnimators[i];
                if (animator == null)
                {
                    continue;
                }

                float startSpeed = i < _fadeOutStartSpeeds.Length ? _fadeOutStartSpeeds[i] : animator.speed;
                float targetSpeed = i < _originalAnimatorSpeeds.Length ? _originalAnimatorSpeeds[i] : 1f;
                animator.speed = Mathf.Lerp(startSpeed, targetSpeed, t);
            }
        }

        if (t >= 1f)
        {
            StopWitchTime();
        }
    }

    private void CacheFadeOutStartSpeeds()
    {
        if (affectedAnimators == null)
        {
            _fadeOutStartSpeeds = null;
            return;
        }

        if (_fadeOutStartSpeeds == null || _fadeOutStartSpeeds.Length != affectedAnimators.Length)
        {
            _fadeOutStartSpeeds = new float[affectedAnimators.Length];
        }

        for (int i = 0; i < affectedAnimators.Length; i++)
        {
            _fadeOutStartSpeeds[i] = affectedAnimators[i] != null ? affectedAnimators[i].speed : 1f;
        }
    }

    private void SlowPerfectDodger(KianaCombatController perfectDodger)
    {
        if (!slowPerfectDodger || perfectDodger == null || perfectDodgerSlowDuration <= 0f)
        {
            return;
        }

        Animator dodgerAnimator = perfectDodger.GetComponentInChildren<Animator>();
        if (dodgerAnimator == null)
        {
            return;
        }

        if (_slowedDodgerAnimator != null && _slowedDodgerAnimator != dodgerAnimator)
        {
            RestoreSlowedDodger();
        }

        if (_slowedDodgerAnimator == null)
        {
            _slowedDodgerAnimator = dodgerAnimator;
            _slowedDodgerOriginalSpeed = dodgerAnimator.speed;
        }

        dodgerAnimator.speed = perfectDodgerAnimatorSpeed;
        _slowedDodgerTimer = perfectDodgerSlowDuration;
        LogDebug($"Perfect dodger slowed. speed={perfectDodgerAnimatorSpeed:F2}, duration={perfectDodgerSlowDuration:F3}");
    }

    private void UpdateSlowedDodger()
    {
        if (_slowedDodgerAnimator == null)
        {
            return;
        }

        _slowedDodgerTimer -= Time.unscaledDeltaTime;
        if (_slowedDodgerTimer <= 0f)
        {
            RestoreSlowedDodger();
        }
    }

    private void RestoreSlowedDodger()
    {
        if (_slowedDodgerAnimator != null)
        {
            _slowedDodgerAnimator.speed = _slowedDodgerOriginalSpeed;
        }

        _slowedDodgerAnimator = null;
        _slowedDodgerOriginalSpeed = 1f;
        _slowedDodgerTimer = 0f;
    }

    private void CacheOriginalSpeeds()
    {
        if (affectedAnimators == null)
        {
            _originalAnimatorSpeeds = null;
            return;
        }

        if (_originalAnimatorSpeeds == null || _originalAnimatorSpeeds.Length != affectedAnimators.Length)
        {
            _originalAnimatorSpeeds = new float[affectedAnimators.Length];
        }

        for (int i = 0; i < affectedAnimators.Length; i++)
        {
            _originalAnimatorSpeeds[i] = affectedAnimators[i] != null ? affectedAnimators[i].speed : 1f;
        }
    }

    private void ApplyAnimatorSpeed(float speed)
    {
        if (affectedAnimators == null)
        {
            return;
        }

        for (int i = 0; i < affectedAnimators.Length; i++)
        {
            if (affectedAnimators[i] != null)
            {
                affectedAnimators[i].speed = speed;
            }
        }
    }

    private void RestoreAnimatorSpeeds()
    {
        if (affectedAnimators == null || _originalAnimatorSpeeds == null)
        {
            return;
        }

        for (int i = 0; i < affectedAnimators.Length; i++)
        {
            if (affectedAnimators[i] != null)
            {
                affectedAnimators[i].speed = _originalAnimatorSpeeds[i];
            }
        }
    }

    private void OnDisable()
    {
        KianaCombatController.DodgeCounterDeclinedByMoveInput -= OnDodgeCounterDeclinedByMoveInput;
        RestoreSlowedDodger();
        StopWitchTime();
    }

    private void LogDebug(string message)
    {
        if (!logDebug)
        {
            return;
        }

        Debug.Log($"[WitchTime] {message} time={Time.time:F3}", this);
    }
}
