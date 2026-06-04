using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

public interface IBattleWillValueSource
{
    float CurrentBattleWill { get; }
    float MaxBattleWill { get; }
    float NormalizedBattleWill { get; }
}

public class PlayerBattleWillSystem : MonoBehaviour, IBattleWillValueSource
{
    public event Action<PlayerBattleWillSystem, float, float> BattleWillChanged;

    [Header("Battle Will")]
    [SerializeField] private float maxBattleWill = 300f;
    [SerializeField] private float initialBattleWill;

    [Header("Attack Gain")]
    [SerializeField] private PlayerAttackBattleWillGainTable attackGainTable;
    [SerializeField] private bool gainBattleWillOnPlayerAttackHit = true;
    [SerializeField] private bool logAttackGainDebug;

    [Header("Test")]
    [SerializeField] private bool testMode;
    [SerializeField] private Key fillBattleWillKey = Key.B;

    private float _currentBattleWill;

    public float CurrentBattleWill => _currentBattleWill;
    public float MaxBattleWill => maxBattleWill;
    public float NormalizedBattleWill => maxBattleWill <= 0f ? 0f : _currentBattleWill / maxBattleWill;

    private void Awake()
    {
        SetBattleWill(initialBattleWill);
    }

    private void OnEnable()
    {
        PlayerAttackHitboxController.PlayerAttackHitConfirmed += HandlePlayerAttackHitConfirmed;
    }

    private void OnDisable()
    {
        PlayerAttackHitboxController.PlayerAttackHitConfirmed -= HandlePlayerAttackHitConfirmed;
    }

    private void Update()
    {
        ReadTestInput();
    }

    public void GainBattleWill(float amount)
    {
        SetBattleWill(_currentBattleWill + amount);
    }

    public bool TrySpendBattleWill(float amount)
    {
        if (amount <= 0f)
        {
            return true;
        }

        if (_currentBattleWill < amount)
        {
            return false;
        }

        SetBattleWill(_currentBattleWill - amount);
        return true;
    }

    public void FillBattleWill()
    {
        SetBattleWill(maxBattleWill);
    }

    public void SetBattleWill(float value)
    {
        float previousValue = _currentBattleWill;
        _currentBattleWill = Mathf.Clamp(value, 0f, maxBattleWill);
        if (!Mathf.Approximately(previousValue, _currentBattleWill))
        {
            BattleWillChanged?.Invoke(this, previousValue, _currentBattleWill);
        }
    }

    private void ReadTestInput()
    {
        if (!testMode || fillBattleWillKey == Key.None)
        {
            return;
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        KeyControl key = keyboard[fillBattleWillKey];
        if (key != null && key.wasPressedThisFrame)
        {
            FillBattleWill();
        }
    }

    private void HandlePlayerAttackHitConfirmed(PlayerAttackHitData hitData)
    {
        if (!gainBattleWillOnPlayerAttackHit || attackGainTable == null || hitData == null)
        {
            return;
        }

        if (!IsOwnAttack(hitData.Attacker))
        {
            return;
        }

        float gainAmount = attackGainTable.GetBattleWillGain(hitData.AttackId);
        if (gainAmount <= 0f)
        {
            return;
        }

        float beforeValue = _currentBattleWill;
        GainBattleWill(gainAmount);

        if (logAttackGainDebug)
        {
            Debug.Log(
                $"[PlayerBattleWill] Attack hit gain. " +
                $"attackId={hitData.AttackId}, gain={gainAmount:F1}, " +
                $"battleWill={beforeValue:F1}->{_currentBattleWill:F1}/{maxBattleWill:F1}",
                this);
        }
    }

    private bool IsOwnAttack(GameObject attacker)
    {
        if (attacker == null)
        {
            return false;
        }

        return attacker == gameObject || attacker.transform.IsChildOf(transform);
    }
}
