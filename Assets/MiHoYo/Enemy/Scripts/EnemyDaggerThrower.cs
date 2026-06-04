using System;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class EnemyDaggerThrower : MonoBehaviour
{
    public static event Action<GameObject, string> DaggerFired;

    [Header("Projectile")]
    [SerializeField] private EnemyDaggerProjectile daggerPrefab;
    [SerializeField] private Transform attack02SpawnPoint;
    [SerializeField] private Transform attack04SpawnPoint;

    [Header("Attack02")]
    [SerializeField] private string attack02Id = "Enemy_Attack02_Dagger";
    [SerializeField] private float attack02Speed = 18f;
    [SerializeField] private float attack02Damage = 5f;
    [SerializeField] private float attack02PoiseDamage = 8f;
    [SerializeField] private float attack02KnockbackForce = 2f;
    [SerializeField] private Vector3 attack02RotationOffset;
    [SerializeField] private bool attack02CanBeReflectedByPlayerParry;

    [Header("Attack04")]
    [SerializeField] private string attack04Id = "Enemy_Attack04_Dagger";
    [SerializeField] private float attack04Speed = 16f;
    [SerializeField] private float attack04Damage = 5f;
    [SerializeField] private float attack04PoiseDamage = 8f;
    [SerializeField] private float attack04KnockbackForce = 2f;
    [SerializeField] private Vector3 attack04RotationOffset;
    [SerializeField] private bool attack04CanBeReflectedByPlayerParry;

    [Header("Shared")]
    [SerializeField] private LayerMask targetLayers = ~0;
    [SerializeField] private float projectileLifetime = 3f;
    [SerializeField] private EnemyAttackWarningWindow attackWarningWindow;
    [SerializeField] private bool logDebug;
    [SerializeField] private bool logSpawnTransformDebug = true;

    [Header("Gizmos")]
    [SerializeField] private bool showThrowGizmos = true;
    [SerializeField] private bool showThrowGizmosOnlyWhenSelected = true;
    [SerializeField] private float throwGizmoLength = 2f;
    [SerializeField] private float throwGizmoSphereRadius = 0.08f;
    [SerializeField] private Color attack02GizmoColor = new Color(1f, 0.55f, 0.1f, 1f);
    [SerializeField] private Color attack04GizmoColor = new Color(0.25f, 0.75f, 1f, 1f);
    [SerializeField] private Color rawForwardGizmoColor = new Color(0.65f, 0.65f, 0.65f, 0.75f);

    private void Awake()
    {
        if (attackWarningWindow == null)
        {
            attackWarningWindow = GetComponent<EnemyAttackWarningWindow>();
        }
    }

    public void AE_FireAttack02Dagger()
    {
        FireDagger(
            attack02SpawnPoint,
            attack02Id,
            attack02Speed,
            attack02Damage,
            attack02PoiseDamage,
            attack02KnockbackForce,
            attack02RotationOffset,
            attack02CanBeReflectedByPlayerParry);
    }

    public void AE_FireAttack04Dagger()
    {
        FireDagger(
            attack04SpawnPoint,
            attack04Id,
            attack04Speed,
            attack04Damage,
            attack04PoiseDamage,
            attack04KnockbackForce,
            attack04RotationOffset,
            attack04CanBeReflectedByPlayerParry);
    }

    private void FireDagger(
        Transform spawnPoint,
        string attackId,
        float speed,
        float damage,
        float poiseDamage,
        float knockbackForce,
        Vector3 rotationOffset,
        bool canBeReflectedByPlayerParry)
    {
        if (daggerPrefab == null)
        {
            LogDebug($"Fire dagger ignored: prefab missing. attackId={attackId}");
            return;
        }

        Transform origin = spawnPoint != null ? spawnPoint : transform;
        Quaternion spawnRotation = origin.rotation * Quaternion.Euler(rotationOffset);
        Vector3 launchDirection = spawnRotation * Vector3.forward;
        if (logSpawnTransformDebug)
        {
            LogDebug(
                $"Preparing dagger fire. attackId={attackId}, spawn={(spawnPoint != null ? spawnPoint.name : name)}, " +
            $"originPos={FormatVector(origin.position)}, originEuler={FormatVector(origin.rotation.eulerAngles)}, " +
            $"rotationOffset={FormatVector(rotationOffset)}, finalEuler={FormatVector(spawnRotation.eulerAngles)}, " +
            $"launchDir={FormatVector(launchDirection)}, speed={speed:F2}, damage={damage:F1}, poise={poiseDamage:F1}, knockback={knockbackForce:F1}, " +
            $"canReflect={canBeReflectedByPlayerParry}, lifetime={projectileLifetime:F2}, targetMask={targetLayers.value}");
        }

        EnemyDaggerProjectile projectile = Instantiate(daggerPrefab, origin.position, spawnRotation);
        projectile.Initialize(
            gameObject,
            attackId,
            launchDirection,
            speed,
            damage,
            poiseDamage,
            knockbackForce,
            projectileLifetime,
            targetLayers,
            attackWarningWindow,
            canBeReflectedByPlayerParry);

        DaggerFired?.Invoke(gameObject, attackId);
        LogDebug($"Dagger fired. attackId={attackId}, projectile={(projectile != null ? projectile.name : "None")}, spawn={(spawnPoint != null ? spawnPoint.name : name)}, speed={speed:F1}, rotationOffset={rotationOffset}");
    }

    private void LogDebug(string message)
    {
        if (!logDebug)
        {
            return;
        }

        Debug.Log($"[EnemyDaggerThrower] {message} time={Time.time:F3}", this);
    }

    private static string FormatVector(Vector3 value)
    {
        return $"({value.x:F3},{value.y:F3},{value.z:F3})";
    }

    private void OnDrawGizmos()
    {
        if (showThrowGizmosOnlyWhenSelected)
        {
            return;
        }

        DrawThrowGizmos();
    }

    private void OnDrawGizmosSelected()
    {
        DrawThrowGizmos();
    }

    private void DrawThrowGizmos()
    {
        if (!showThrowGizmos)
        {
            return;
        }

        DrawThrowGizmo(attack02SpawnPoint, attack02RotationOffset, attack02GizmoColor, "Attack02 Dagger");
        DrawThrowGizmo(attack04SpawnPoint, attack04RotationOffset, attack04GizmoColor, "Attack04 Dagger");
    }

    private void DrawThrowGizmo(Transform spawnPoint, Vector3 rotationOffset, Color color, string label)
    {
        Transform origin = spawnPoint != null ? spawnPoint : transform;
        Quaternion rawRotation = origin.rotation;
        Quaternion finalRotation = rawRotation * Quaternion.Euler(rotationOffset);
        Vector3 position = origin.position;
        Vector3 rawDirection = rawRotation * Vector3.forward;
        Vector3 finalDirection = finalRotation * Vector3.forward;
        float length = Mathf.Max(0.1f, throwGizmoLength);

        Gizmos.color = rawForwardGizmoColor;
        Gizmos.DrawLine(position, position + rawDirection.normalized * length * 0.65f);

        Gizmos.color = color;
        Gizmos.DrawSphere(position, Mathf.Max(0.01f, throwGizmoSphereRadius));
        Gizmos.DrawLine(position, position + finalDirection.normalized * length);

#if UNITY_EDITOR
        Handles.color = color;
        Handles.ArrowHandleCap(
            0,
            position,
            Quaternion.LookRotation(finalDirection.normalized, Vector3.up),
            length,
            EventType.Repaint);
        Handles.Label(position + finalDirection.normalized * (length + 0.15f), label);
#endif
    }
}
