using UnityEngine;

/// <summary>
/// 非 Root Motion 动作的移动锁与攻击锁控制器。
/// 移动锁：AE_LockMovement / AE_UnlockMovement — 锁定/解除方向键移动。
/// 攻击锁：AE_LockAttack  / AE_UnlockAttack  — 锁定/解除攻击输入（含队列缓冲）。
/// 由 KianaCombatController 读取 IsMovementLocked / IsAttackLocked 属性。
/// </summary>
public class PlayerMovementLockController : MonoBehaviour
{
    [SerializeField] private bool logDebug;

    /// <summary>当前方向键移动是否被锁定。</summary>
    public bool IsMovementLocked { get; private set; }

    /// <summary>当前攻击输入是否被锁定（包括缓冲队列）。</summary>
    public bool IsAttackLocked { get; private set; }

    private void OnEnable()
    {
        ResetLock();
    }

    /// <summary>
    /// 重置所有锁状态（供外部在状态重置时调用）。
    /// </summary>
    public void ResetLock()
    {
        IsMovementLocked = false;
        IsAttackLocked = false;
        Log("ResetLock called. Movement and attack unlocked.");
    }

    // ── 移动锁 ────────────────────────────────────────────────

    /// <summary>
    /// 动画事件：锁定方向键移动。
    /// </summary>
    public void AE_LockMovement()
    {
        IsMovementLocked = true;
        Log("AE_LockMovement called. Movement locked.");
    }

    /// <summary>
    /// 动画事件：解除方向键移动锁定。
    /// </summary>
    public void AE_UnlockMovement()
    {
        IsMovementLocked = false;
        Log("AE_UnlockMovement called. Movement unlocked.");
    }

    // ── 攻击锁 ────────────────────────────────────────────────

    /// <summary>
    /// 动画事件：锁定攻击输入。
    /// 锁定期间所有攻击指令（轻攻击、冲刺攻击、分支攻击）均被完全屏蔽，
    /// 不进入缓冲队列，不会在解锁后自动触发。
    /// </summary>
    public void AE_LockAttack()
    {
        IsAttackLocked = true;
        Log("AE_LockAttack called. Attack locked.");
    }

    /// <summary>
    /// 动画事件：解除攻击输入锁定。
    /// </summary>
    public void AE_UnlockAttack()
    {
        IsAttackLocked = false;
        Log("AE_UnlockAttack called. Attack unlocked.");
    }

    // ── 工具 ──────────────────────────────────────────────────

    private void Log(string message)
    {
        if (!logDebug)
        {
            return;
        }

        Debug.Log(
            $"[MovementLock] {message} " +
            $"time={Time.time:F3}, movementLocked={IsMovementLocked}, attackLocked={IsAttackLocked}",
            this);
    }
}
