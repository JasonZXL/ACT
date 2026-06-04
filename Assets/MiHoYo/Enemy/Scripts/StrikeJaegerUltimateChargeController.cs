using System;
using UnityEngine;

public class StrikeJaegerUltimateChargeController : MonoBehaviour
{
    public event Action<StrikeJaegerUltimateChargeController, int, int> ChargeChanged;
    public event Action<StrikeJaegerUltimateChargeController, bool> UltimateReadyChanged;

    [Header("Charge")]
    [SerializeField] private int requiredConfirmedDaggerHits = 3;
    [SerializeField] private bool stopChargingWhenReady = true;
    [SerializeField] private bool clampChargeToRequirement = true;

    [Header("Accepted Dagger Attack Ids")]
    [SerializeField] private string[] acceptedDaggerAttackIds =
    {
        "Enemy_Attack02_Dagger",
        "Enemy_Attack04_Dagger"
    };

    [Header("Debug")]
    [SerializeField] private bool logDebug;

    private int _currentConfirmedDaggerHits;
    private bool _ultimateReady;

    public int CurrentConfirmedDaggerHits => _currentConfirmedDaggerHits;
    public int RequiredConfirmedDaggerHits => requiredConfirmedDaggerHits;
    public bool UltimateReady => _ultimateReady;
    public float ChargeNormalized => requiredConfirmedDaggerHits <= 0
        ? 1f
        : Mathf.Clamp01((float)_currentConfirmedDaggerHits / requiredConfirmedDaggerHits);

    private void OnEnable()
    {
        CombatHealth.EnemyDamageApplied += OnEnemyDamageApplied;
        CombatHealth.EnemyDamageRolledBack += OnEnemyDamageRolledBack;
    }

    private void OnDisable()
    {
        CombatHealth.EnemyDamageApplied -= OnEnemyDamageApplied;
        CombatHealth.EnemyDamageRolledBack -= OnEnemyDamageRolledBack;
    }

    private void OnValidate()
    {
        requiredConfirmedDaggerHits = Mathf.Max(1, requiredConfirmedDaggerHits);
    }

    public void ResetCharge()
    {
        SetCharge(0, "ResetCharge");
    }

    public bool ConsumeReadyCharge()
    {
        if (!_ultimateReady)
        {
            LogDebug("ConsumeReadyCharge ignored: ultimate is not ready.");
            return false;
        }

        SetCharge(0, "ConsumeReadyCharge");
        return true;
    }

    public void ForceReady()
    {
        SetCharge(requiredConfirmedDaggerHits, "ForceReady");
    }

    private void OnEnemyDamageApplied(CombatHealth targetHealth, EnemyAttackHitData hitData, float appliedHealthDamage)
    {
        if (hitData == null || appliedHealthDamage <= 0f)
        {
            return;
        }

        if (stopChargingWhenReady && _ultimateReady)
        {
            LogDebug($"Charge skipped: ultimate already ready. attackId={hitData.AttackId}");
            return;
        }

        if (!IsDamageFromThisEnemy(hitData))
        {
            return;
        }

        if (!IsAcceptedDaggerAttack(hitData.AttackId))
        {
            return;
        }

        int nextCharge = _currentConfirmedDaggerHits + 1;
        SetCharge(nextCharge, $"Confirmed dagger damage. attackId={hitData.AttackId}, damage={appliedHealthDamage:F1}");
    }

    private void OnEnemyDamageRolledBack(CombatHealth targetHealth, EnemyAttackHitData hitData, float rolledBackHealthDamage)
    {
        if (hitData == null || rolledBackHealthDamage <= 0f)
        {
            return;
        }

        if (!IsDamageFromThisEnemy(hitData) || !IsAcceptedDaggerAttack(hitData.AttackId))
        {
            return;
        }

        SetCharge(_currentConfirmedDaggerHits - 1, $"Confirmed dagger damage rolled back. attackId={hitData.AttackId}, damage={rolledBackHealthDamage:F1}");
    }

    private bool IsDamageFromThisEnemy(EnemyAttackHitData hitData)
    {
        if (hitData.Attacker == null)
        {
            return false;
        }

        Transform attackerTransform = hitData.Attacker.transform;
        return hitData.Attacker == gameObject ||
               attackerTransform == transform ||
               attackerTransform.IsChildOf(transform);
    }

    private bool IsAcceptedDaggerAttack(string attackId)
    {
        if (string.IsNullOrEmpty(attackId) || acceptedDaggerAttackIds == null)
        {
            return false;
        }

        for (int i = 0; i < acceptedDaggerAttackIds.Length; i++)
        {
            if (string.Equals(acceptedDaggerAttackIds[i], attackId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private void SetCharge(int value, string reason)
    {
        int clampedCharge = Mathf.Max(0, value);
        if (clampChargeToRequirement)
        {
            clampedCharge = Mathf.Min(clampedCharge, requiredConfirmedDaggerHits);
        }

        bool chargeChanged = clampedCharge != _currentConfirmedDaggerHits;
        _currentConfirmedDaggerHits = clampedCharge;

        bool nextReady = _currentConfirmedDaggerHits >= requiredConfirmedDaggerHits;
        bool readyChanged = nextReady != _ultimateReady;
        _ultimateReady = nextReady;

        if (chargeChanged)
        {
            ChargeChanged?.Invoke(this, _currentConfirmedDaggerHits, requiredConfirmedDaggerHits);
        }

        if (readyChanged)
        {
            UltimateReadyChanged?.Invoke(this, _ultimateReady);
        }

        if (chargeChanged || readyChanged)
        {
            LogDebug(
                $"{reason}. charge={_currentConfirmedDaggerHits}/{requiredConfirmedDaggerHits}, ready={_ultimateReady}");
        }
    }

    private void LogDebug(string message)
    {
        if (!logDebug)
        {
            return;
        }

        Debug.Log($"[StrikeJaegerUltimateCharge] {message} time={Time.time:F3}", this);
    }
}
