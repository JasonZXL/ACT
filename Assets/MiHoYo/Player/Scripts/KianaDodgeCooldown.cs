using UnityEngine;

public class KianaDodgeCooldown : MonoBehaviour
{
    [Header("Cooldown")]
    [SerializeField] private float cooldownDuration = 1f;
    [SerializeField] private int maxCharges = 2;
    [SerializeField] private bool startReady = true;

    [Header("Debug")]
    [SerializeField] private bool logDebug;

    private float _cooldownTimer;
    private int _currentCharges;

    public float CooldownDuration => Mathf.Max(0f, cooldownDuration);
    public float CooldownRemaining => Mathf.Max(0f, _cooldownTimer);
    public int MaxCharges => Mathf.Max(1, maxCharges);
    public int CurrentCharges => Mathf.Clamp(_currentCharges, 0, MaxCharges);
    public bool IsCoolingDown => CurrentCharges < MaxCharges;
    public bool CanDodge => CurrentCharges > 0;

    public float CooldownNormalized
    {
        get
        {
            if (!IsCoolingDown)
            {
                return 0f;
            }

            float duration = CooldownDuration;
            if (duration <= 0f)
            {
                return 0f;
            }

            return Mathf.Clamp01(_cooldownTimer / duration);
        }
    }

    public float CooldownFillAmount => 1f - CooldownNormalized;

    private void OnEnable()
    {
        _currentCharges = startReady ? MaxCharges : 0;
        _cooldownTimer = CurrentCharges < MaxCharges ? CooldownDuration : 0f;
    }

    private void Update()
    {
        if (CurrentCharges >= MaxCharges)
        {
            _cooldownTimer = 0f;
            return;
        }

        if (_cooldownTimer <= 0f)
        {
            _cooldownTimer = CooldownDuration;
        }

        _cooldownTimer = Mathf.Max(0f, _cooldownTimer - Time.deltaTime);
        if (_cooldownTimer <= 0f)
        {
            _currentCharges = Mathf.Min(MaxCharges, CurrentCharges + 1);
            LogDebug($"Dodge charge restored. charges={CurrentCharges}/{MaxCharges}.");
            _cooldownTimer = CurrentCharges < MaxCharges ? CooldownDuration : 0f;
        }
    }

    public bool TryStartCooldown()
    {
        if (!CanDodge)
        {
            LogDebug($"Dodge cooldown rejected. charges={CurrentCharges}/{MaxCharges}, remaining={CooldownRemaining:F2}s.");
            return false;
        }

        StartCooldown();
        return true;
    }

    public void StartCooldown()
    {
        if (!CanDodge)
        {
            LogDebug($"Dodge charge consume ignored. charges={CurrentCharges}/{MaxCharges}, remaining={CooldownRemaining:F2}s.");
            return;
        }

        _currentCharges = Mathf.Max(0, CurrentCharges - 1);
        if (CurrentCharges < MaxCharges && _cooldownTimer <= 0f)
        {
            _cooldownTimer = CooldownDuration;
        }

        LogDebug($"Dodge charge consumed. charges={CurrentCharges}/{MaxCharges}, nextChargeIn={CooldownRemaining:F2}s.");
    }

    public void ResetCooldown()
    {
        _currentCharges = MaxCharges;
        _cooldownTimer = 0f;
        LogDebug($"Dodge cooldown reset. charges={CurrentCharges}/{MaxCharges}.");
    }

    private void OnValidate()
    {
        cooldownDuration = Mathf.Max(0f, cooldownDuration);
        maxCharges = Mathf.Max(1, maxCharges);
    }

    private void LogDebug(string message)
    {
        if (!logDebug)
        {
            return;
        }

        Debug.Log($"[KianaDodgeCooldown] {message}", this);
    }
}
