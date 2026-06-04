using System;
using UnityEngine;

public class PlayerPoiseController : MonoBehaviour
{
    public event Action<PlayerPoiseController> PoiseChanged;
    public event Action<PlayerPoiseController> PoiseBroken;
    public event Action<PlayerPoiseController> PoiseRecovered;

    [Header("Poise")]
    [SerializeField] private float maxPoise = 100f;
    [SerializeField] private float naturalRecoverPerSecond = 2f;
    [SerializeField] private float brokenRecoverCooldown = 5f;
    [SerializeField] private bool resetCooldownWhenHitWhileBroken = true;

    [Header("Heavy Hit")]
    [SerializeField] private bool heavyHitBypassesPoise = true;
    [SerializeField] private bool heavyHitConsumesPoise = true;

    [Header("Reward Recovery")]
    [SerializeField, Range(0f, 1f)] private float parrySuccessRecoverRatio = 1f;
    [SerializeField, Range(0f, 1f)] private float projectileParryRecoverRatio = 1f;
    [SerializeField, Range(0f, 1f)] private float perfectDodgeRecoverRatio = 0.5f;

    [Header("Debug")]
    [SerializeField] private bool logDebug;

    private float _currentPoise;
    private float _brokenCooldownTimer;
    private bool _broken;

    public float CurrentPoise => _currentPoise;
    public float MaxPoise => maxPoise;
    public float PoiseNormalized => maxPoise <= 0f ? 0f : Mathf.Clamp01(_currentPoise / maxPoise);
    public bool Broken => _broken;
    public float BrokenCooldownTimer => _brokenCooldownTimer;

    private void Awake()
    {
        _currentPoise = maxPoise;
    }

    private void OnEnable()
    {
        PlayerParryController.ParrySucceeded += OnParrySucceeded;
        PlayerParryController.ProjectileParried += OnProjectileParried;
        EnemyAttackWarningWindow.PerfectDodgeConfirmed += OnPerfectDodgeConfirmed;
    }

    private void OnDisable()
    {
        PlayerParryController.ParrySucceeded -= OnParrySucceeded;
        PlayerParryController.ProjectileParried -= OnProjectileParried;
        EnemyAttackWarningWindow.PerfectDodgeConfirmed -= OnPerfectDodgeConfirmed;
    }

    private void OnValidate()
    {
        maxPoise = Mathf.Max(1f, maxPoise);
        naturalRecoverPerSecond = Mathf.Max(0f, naturalRecoverPerSecond);
        brokenRecoverCooldown = Mathf.Max(0f, brokenRecoverCooldown);
    }

    private void Update()
    {
        if (_currentPoise >= maxPoise)
        {
            return;
        }

        if (_brokenCooldownTimer > 0f)
        {
            _brokenCooldownTimer -= Time.deltaTime;
            return;
        }

        if (naturalRecoverPerSecond <= 0f)
        {
            return;
        }

        float previousPoise = _currentPoise;
        _currentPoise = Mathf.Min(maxPoise, _currentPoise + naturalRecoverPerSecond * Time.deltaTime);
        if (!Mathf.Approximately(previousPoise, _currentPoise))
        {
            if (_broken && _currentPoise > 0f)
            {
                _broken = false;
                PoiseRecovered?.Invoke(this);
                LogDebug("Poise recovered from broken state by natural recovery.");
            }

            PoiseChanged?.Invoke(this);
        }
    }

    public bool ShouldPlayHitReaction(EnemyAttackHitData hitData)
    {
        if (hitData == null)
        {
            return true;
        }

        bool heavyHit = hitData.HitReactionType == EnemyHitReactionType.Heavy;
        if (heavyHit && heavyHitBypassesPoise)
        {
            if (heavyHitConsumesPoise)
            {
                ApplyPoiseDamage(hitData.PoiseDamage, true, $"HeavyBypass:{hitData.AttackId}");
            }
            else
            {
                ResetBrokenCooldownIfNeeded($"HeavyBypass:{hitData.AttackId}");
            }

            LogDebug($"Hit reaction allowed by heavy bypass. attackId={hitData.AttackId}, poise={_currentPoise:F1}/{maxPoise:F1}, broken={_broken}");
            return true;
        }

        if (_broken)
        {
            ResetBrokenCooldownIfNeeded($"BrokenHit:{hitData.AttackId}");
            LogDebug($"Hit reaction allowed because poise is broken. attackId={hitData.AttackId}, poiseDamage={hitData.PoiseDamage:F1}");
            return true;
        }

        if (_currentPoise > 0f)
        {
            ApplyPoiseDamage(hitData.PoiseDamage, true, $"Absorb:{hitData.AttackId}");
            LogDebug($"Hit reaction absorbed by poise. attackId={hitData.AttackId}, poiseDamage={hitData.PoiseDamage:F1}, poise={_currentPoise:F1}/{maxPoise:F1}, broken={_broken}");
            return false;
        }

        BreakPoise($"ZeroBeforeHit:{hitData.AttackId}");
        return true;
    }

    public void RestoreFull(string reason)
    {
        RestorePoise(maxPoise, reason);
    }

    public void RestoreByRatio(float ratio, string reason)
    {
        RestorePoise(maxPoise * Mathf.Clamp01(ratio), reason);
    }

    public void RestorePoise(float amount, string reason)
    {
        if (amount <= 0f)
        {
            return;
        }

        float previousPoise = _currentPoise;
        bool wasBroken = _broken;
        _currentPoise = Mathf.Min(maxPoise, _currentPoise + amount);
        if (_currentPoise > 0f)
        {
            _broken = false;
            _brokenCooldownTimer = 0f;
        }

        if (!Mathf.Approximately(previousPoise, _currentPoise) || wasBroken != _broken)
        {
            PoiseChanged?.Invoke(this);
            if (wasBroken && !_broken)
            {
                PoiseRecovered?.Invoke(this);
            }
        }

        LogDebug($"Poise restored. reason={reason}, amount={amount:F1}, poise={_currentPoise:F1}/{maxPoise:F1}, broken={_broken}");
    }

    public void ResetPoise()
    {
        _currentPoise = maxPoise;
        _broken = false;
        _brokenCooldownTimer = 0f;
        PoiseChanged?.Invoke(this);
        LogDebug("Poise reset.");
    }

    private void ApplyPoiseDamage(float poiseDamage, bool startCooldownOnBreak, string reason)
    {
        float damage = Mathf.Max(0f, poiseDamage);
        if (damage <= 0f)
        {
            return;
        }

        float previousPoise = _currentPoise;
        _currentPoise = Mathf.Max(0f, _currentPoise - damage);
        if (!Mathf.Approximately(previousPoise, _currentPoise))
        {
            PoiseChanged?.Invoke(this);
        }

        if (_currentPoise <= 0f && startCooldownOnBreak)
        {
            BreakPoise(reason);
        }
    }

    private void BreakPoise(string reason)
    {
        bool wasBroken = _broken;
        _currentPoise = 0f;
        _broken = true;
        _brokenCooldownTimer = brokenRecoverCooldown;
        PoiseChanged?.Invoke(this);
        if (!wasBroken)
        {
            PoiseBroken?.Invoke(this);
        }

        LogDebug($"Poise broken. reason={reason}, cooldown={_brokenCooldownTimer:F2}");
    }

    private void ResetBrokenCooldownIfNeeded(string reason)
    {
        if (!_broken || !resetCooldownWhenHitWhileBroken)
        {
            return;
        }

        _brokenCooldownTimer = brokenRecoverCooldown;
        LogDebug($"Broken cooldown reset. reason={reason}, cooldown={_brokenCooldownTimer:F2}");
    }

    private void OnParrySucceeded(PlayerParryController source, EnemyParryWindow parriedWindow)
    {
        if (!IsOwnParry(source))
        {
            return;
        }

        RestoreByRatio(parrySuccessRecoverRatio, "ParrySuccess");
    }

    private void OnProjectileParried(PlayerParryController source, EnemyDaggerProjectile projectile)
    {
        if (!IsOwnParry(source))
        {
            return;
        }

        RestoreByRatio(projectileParryRecoverRatio, "ProjectileParry");
    }

    private void OnPerfectDodgeConfirmed(EnemyAttackWarningWindow warningWindow, KianaCombatController dodger)
    {
        if (dodger == null || dodger.gameObject != gameObject)
        {
            return;
        }

        RestoreByRatio(perfectDodgeRecoverRatio, "PerfectDodge");
    }

    private bool IsOwnParry(PlayerParryController source)
    {
        if (source == null)
        {
            return false;
        }

        return source.gameObject == gameObject ||
            source.transform.IsChildOf(transform) ||
            transform.IsChildOf(source.transform);
    }

    private void LogDebug(string message)
    {
        if (!logDebug)
        {
            return;
        }

        Debug.Log($"[PlayerPoise] {message} time={Time.time:F3}", this);
    }
}
