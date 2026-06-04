using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerHitStopSystem : MonoBehaviour
{
    private class FrozenAnimator
    {
        public float OriginalSpeed;
        public float EndTime;
    }

    [Header("Hit Stop")]
    [SerializeField] private PlayerAttackHitStopTable hitStopTable;
    [SerializeField] private bool enableHitStop = true;

    [Header("References")]
    [SerializeField] private Animator attackerAnimator;

    [Header("Debug")]
    [SerializeField] private bool logHitStopDebug;

    private readonly Dictionary<Animator, FrozenAnimator> _frozenAnimators = new Dictionary<Animator, FrozenAnimator>();
    private Coroutine _hitStopRoutine;

    private void Awake()
    {
        if (attackerAnimator == null)
        {
            attackerAnimator = GetComponentInChildren<Animator>();
        }
    }

    private void OnEnable()
    {
        PlayerAttackHitboxController.PlayerAttackHitConfirmed += HandlePlayerAttackHitConfirmed;
    }

    private void OnDisable()
    {
        PlayerAttackHitboxController.PlayerAttackHitConfirmed -= HandlePlayerAttackHitConfirmed;
        RestoreAllAnimators();
    }

    private void HandlePlayerAttackHitConfirmed(PlayerAttackHitData hitData)
    {
        if (!enableHitStop || hitStopTable == null || hitData == null || !IsOwnAttack(hitData.Attacker))
        {
            return;
        }

        if (!hitStopTable.TryGetHitStop(hitData.AttackId, out float duration, out bool freezeAttacker, out bool freezeTarget))
        {
            return;
        }

        if (freezeAttacker)
        {
            FreezeAnimator(attackerAnimator, duration);
        }

        if (freezeTarget)
        {
            FreezeAnimator(FindTargetAnimator(hitData.HitCollider), duration);
        }

        if (_hitStopRoutine == null)
        {
            _hitStopRoutine = StartCoroutine(HitStopRoutine());
        }

        LogHitStopDebug($"Hit stop applied. attackId={hitData.AttackId}, duration={duration:F3}, freezeAttacker={freezeAttacker}, freezeTarget={freezeTarget}");
    }

    private void FreezeAnimator(Animator targetAnimator, float duration)
    {
        if (targetAnimator == null || duration <= 0f)
        {
            return;
        }

        float endTime = Time.realtimeSinceStartup + duration;
        if (_frozenAnimators.TryGetValue(targetAnimator, out FrozenAnimator frozenAnimator))
        {
            frozenAnimator.EndTime = Mathf.Max(frozenAnimator.EndTime, endTime);
            return;
        }

        _frozenAnimators[targetAnimator] = new FrozenAnimator
        {
            OriginalSpeed = targetAnimator.speed,
            EndTime = endTime
        };

        targetAnimator.speed = 0f;
    }

    private IEnumerator HitStopRoutine()
    {
        while (_frozenAnimators.Count > 0)
        {
            float now = Time.realtimeSinceStartup;
            List<Animator> animatorsToRestore = null;

            foreach (KeyValuePair<Animator, FrozenAnimator> pair in _frozenAnimators)
            {
                Animator targetAnimator = pair.Key;
                FrozenAnimator frozenAnimator = pair.Value;
                if (targetAnimator == null || now >= frozenAnimator.EndTime)
                {
                    if (animatorsToRestore == null)
                    {
                        animatorsToRestore = new List<Animator>();
                    }

                    animatorsToRestore.Add(targetAnimator);
                }
            }

            if (animatorsToRestore != null)
            {
                for (int i = 0; i < animatorsToRestore.Count; i++)
                {
                    RestoreAnimator(animatorsToRestore[i]);
                }
            }

            yield return null;
        }

        _hitStopRoutine = null;
    }

    private void RestoreAnimator(Animator targetAnimator)
    {
        if (targetAnimator == null)
        {
            _frozenAnimators.Remove(targetAnimator);
            return;
        }

        if (!_frozenAnimators.TryGetValue(targetAnimator, out FrozenAnimator frozenAnimator))
        {
            return;
        }

        targetAnimator.speed = frozenAnimator.OriginalSpeed;
        _frozenAnimators.Remove(targetAnimator);
    }

    private void RestoreAllAnimators()
    {
        foreach (KeyValuePair<Animator, FrozenAnimator> pair in _frozenAnimators)
        {
            if (pair.Key != null)
            {
                pair.Key.speed = pair.Value.OriginalSpeed;
            }
        }

        _frozenAnimators.Clear();
        if (_hitStopRoutine != null)
        {
            StopCoroutine(_hitStopRoutine);
            _hitStopRoutine = null;
        }
    }

    private Animator FindTargetAnimator(Collider hitCollider)
    {
        if (hitCollider == null)
        {
            return null;
        }

        return hitCollider.GetComponentInParent<Animator>();
    }

    private bool IsOwnAttack(GameObject attacker)
    {
        if (attacker == null)
        {
            return false;
        }

        return attacker == gameObject || attacker.transform.IsChildOf(transform);
    }

    private void LogHitStopDebug(string message)
    {
        if (!logHitStopDebug)
        {
            return;
        }

        Debug.Log($"[PlayerHitStop] {message} time={Time.time:F3}, realtime={Time.realtimeSinceStartup:F3}", this);
    }
}
