using Unity.Cinemachine;
using System.Collections.Generic;
using UnityEngine;

public class PlayerCameraImpulseController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private CinemachineImpulseSource impulseSource;

    [Header("Impulse Force")]
    [SerializeField] private float lightImpulseForce = 0.12f;
    [SerializeField] private float mediumImpulseForce = 0.25f;
    [SerializeField] private float heavyImpulseForce = 0.45f;
    [SerializeField] private float perfectDodgeImpulseForce = 0.2f;

    [Header("Hit Confirmation")]
    [SerializeField] private bool requirePlayerHitForAttackImpulse = true;
    [SerializeField] private float hitConfirmWindow = 0.08f;
    [SerializeField] private int maxPendingHitConfirmedImpulses = 6;

    [Header("Debug")]
    [SerializeField] private bool logDebug;

    private bool _pendingHitConfirmedImpulse;
    private float _pendingHitConfirmedImpulseForce;
    private float _pendingHitConfirmedImpulseExpireTime;
    private string _pendingHitConfirmedImpulseReason;
    private float _lastOwnHitTime = -999f;
    private PlayerAttackHitData _lastOwnHitData;
    private bool _lastOwnHitConsumedForImpulse = true;
    private readonly List<PendingHitConfirmedImpulse> _pendingHitConfirmedImpulses = new List<PendingHitConfirmedImpulse>();

    private struct PendingHitConfirmedImpulse
    {
        public float Force;
        public float ExpireTime;
        public string Reason;
        public float RequestTime;
    }

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        PlayerAttackHitboxController.PlayerAttackHitConfirmed += OnPlayerAttackHitConfirmed;
    }

    private void OnDisable()
    {
        PlayerAttackHitboxController.PlayerAttackHitConfirmed -= OnPlayerAttackHitConfirmed;
    }

    public void AE_Camera_LightImpulse()
    {
        RequestAttackImpulse(lightImpulseForce, "Light");
    }

    public void AE_Camera_MediumImpulse()
    {
        RequestAttackImpulse(mediumImpulseForce, "Medium");
    }

    public void AE_Camera_HeavyImpulse()
    {
        RequestAttackImpulse(heavyImpulseForce, "Heavy");
    }

    public void AE_Camera_PerfectDodge()
    {
        GenerateImpulse(perfectDodgeImpulseForce, "PerfectDodge");
    }

    public void AE_Camera_BranchChargeFocus()
    {
        RequestAttackImpulse(lightImpulseForce, "BranchChargeFocus");
    }

    public void AE_Camera_BranchReleaseMedium()
    {
        RequestAttackImpulse(mediumImpulseForce, "BranchReleaseMedium");
    }

    public void AE_Camera_BranchReleaseHeavy()
    {
        RequestAttackImpulse(heavyImpulseForce, "BranchReleaseHeavy");
    }

    public void AE_Camera_Impulse(float force)
    {
        RequestAttackImpulse(force, "Custom");
    }

    public void AE_PlayCameraTimeline(string legacyTimelineName)
    {
        if (string.IsNullOrEmpty(legacyTimelineName))
        {
            GenerateImpulse(mediumImpulseForce, "LegacyTimeline");
            return;
        }

        bool heavy =
            legacyTimelineName.IndexOf("QTE", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            legacyTimelineName.IndexOf("Heavy", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            legacyTimelineName.IndexOf("03_03", System.StringComparison.OrdinalIgnoreCase) >= 0;

        RequestAttackImpulse(heavy ? heavyImpulseForce : mediumImpulseForce, $"LegacyTimeline:{legacyTimelineName}");
    }

    public void AE_StopCameraTimeline()
    {
        LogDebug("Legacy timeline stop ignored. Impulse controller has no persistent camera state.");
    }

    private void RequestAttackImpulse(float force, string reason)
    {
        if (!requirePlayerHitForAttackImpulse)
        {
            GenerateImpulse(force, reason);
            return;
        }

        if (!_lastOwnHitConsumedForImpulse && Time.time - _lastOwnHitTime <= hitConfirmWindow)
        {
            GenerateImpulse(force, $"{reason}:ConfirmedRecentHit:{_lastOwnHitData?.AttackId}");
            _lastOwnHitConsumedForImpulse = true;
            return;
        }

        QueuePendingHitConfirmedImpulse(force, reason);
    }

    private void OnPlayerAttackHitConfirmed(PlayerAttackHitData hitData)
    {
        if (hitData == null || !IsOwnAttack(hitData.Attacker))
        {
            return;
        }

        _lastOwnHitTime = Time.time;
        _lastOwnHitData = hitData;
        _lastOwnHitConsumedForImpulse = false;

        ClearExpiredPendingHitConfirmedImpulses();

        if (_pendingHitConfirmedImpulses.Count <= 0)
        {
            LogDebug($"Own hit confirmed without armed impulse. attackId={hitData.AttackId}");
            return;
        }

        PendingHitConfirmedImpulse pending = _pendingHitConfirmedImpulses[0];
        _pendingHitConfirmedImpulses.RemoveAt(0);
        _lastOwnHitConsumedForImpulse = true;
        float force = pending.Force;
        string reason = pending.Reason;
        GenerateImpulse(force, $"{reason}:ConfirmedHit:{hitData.AttackId}");
    }

    private bool IsOwnAttack(GameObject attacker)
    {
        if (attacker == null)
        {
            return false;
        }

        return attacker == gameObject || attacker.transform.IsChildOf(transform) || transform.IsChildOf(attacker.transform);
    }

    private void ClearPendingHitConfirmedImpulse()
    {
        _pendingHitConfirmedImpulse = false;
        _pendingHitConfirmedImpulseForce = 0f;
        _pendingHitConfirmedImpulseExpireTime = 0f;
        _pendingHitConfirmedImpulseReason = string.Empty;
        _pendingHitConfirmedImpulses.Clear();
    }

    private void QueuePendingHitConfirmedImpulse(float force, string reason)
    {
        float expireTime = Time.time + hitConfirmWindow;
        PendingHitConfirmedImpulse pending = new PendingHitConfirmedImpulse
        {
            Force = force,
            ExpireTime = expireTime,
            Reason = reason,
            RequestTime = Time.time
        };

        _pendingHitConfirmedImpulses.Add(pending);
        while (_pendingHitConfirmedImpulses.Count > Mathf.Max(1, maxPendingHitConfirmedImpulses))
        {
            LogDebug($"Pending attack impulse dropped by queue limit. reason={_pendingHitConfirmedImpulses[0].Reason}");
            _pendingHitConfirmedImpulses.RemoveAt(0);
        }

        _pendingHitConfirmedImpulse = true;
        _pendingHitConfirmedImpulseForce = force;
        _pendingHitConfirmedImpulseExpireTime = expireTime;
        _pendingHitConfirmedImpulseReason = reason;
        LogDebug($"Attack impulse armed. reason={reason}, force={force:F2}, expire={expireTime:F3}, pendingCount={_pendingHitConfirmedImpulses.Count}");
    }

    private void ClearExpiredPendingHitConfirmedImpulses()
    {
        for (int i = _pendingHitConfirmedImpulses.Count - 1; i >= 0; i--)
        {
            if (Time.time <= _pendingHitConfirmedImpulses[i].ExpireTime)
            {
                continue;
            }

            LogDebug($"Armed impulse expired before hit. reason={_pendingHitConfirmedImpulses[i].Reason}, requestTime={_pendingHitConfirmedImpulses[i].RequestTime:F3}");
            _pendingHitConfirmedImpulses.RemoveAt(i);
        }

        _pendingHitConfirmedImpulse = _pendingHitConfirmedImpulses.Count > 0;
        if (!_pendingHitConfirmedImpulse)
        {
            _pendingHitConfirmedImpulseForce = 0f;
            _pendingHitConfirmedImpulseExpireTime = 0f;
            _pendingHitConfirmedImpulseReason = string.Empty;
        }
    }

    private void GenerateImpulse(float force, string reason)
    {
        ResolveReferences();

        if (impulseSource == null)
        {
            LogDebug($"Impulse skipped. Missing CinemachineImpulseSource. reason={reason}, force={force:F2}");
            return;
        }

        impulseSource.GenerateImpulse(force);
        LogDebug($"Impulse generated. reason={reason}, force={force:F2}");
    }

    private void ResolveReferences()
    {
        if (impulseSource != null)
        {
            return;
        }

        impulseSource = GetComponent<CinemachineImpulseSource>();
        if (impulseSource != null)
        {
            return;
        }

        impulseSource = FindObjectOfType<CinemachineImpulseSource>();
    }

    private void LogDebug(string message)
    {
        if (!logDebug)
        {
            return;
        }

        Debug.Log($"[PlayerCameraImpulse] {message} time={Time.time:F3}", this);
    }
}
