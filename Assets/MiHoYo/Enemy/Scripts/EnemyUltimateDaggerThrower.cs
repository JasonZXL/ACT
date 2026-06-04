using System;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class EnemyUltimateDaggerThrower : MonoBehaviour
{
    public static event Action<GameObject, string> UltimateDaggerFired;

    [Serializable]
    public class UltimateDaggerFirePoint
    {
        public string id = "Back";
        public Transform spawnPoint;
        public string attackId = "Enemy_Ultimate_Dagger";
        public bool aimAtPlayer = true;
        public bool aimHorizontalOnly = false;
        public Vector3 aimTargetOffset = new Vector3(0f, 1f, 0f);
        public float speed = 18f;
        public float damage = 5f;
        public float poiseDamage = 8f;
        public float knockbackForce = 2f;
        public Vector3 rotationOffset;
        public bool canBeReflectedByPlayerParry = true;
    }

    [Header("Projectile")]
    [SerializeField] private EnemyDaggerProjectile daggerPrefab;
    [SerializeField] private UltimateDaggerFirePoint[] firePoints = new UltimateDaggerFirePoint[0];

    [Header("Shared")]
    [SerializeField] private LayerMask targetLayers = ~0;
    [SerializeField] private float projectileLifetime = 3f;
    [SerializeField] private EnemyAttackWarningWindow attackWarningWindow;
    [SerializeField] private Transform playerTarget;
    [SerializeField] private string playerTag = "Player";

    [Header("Debug")]
    [SerializeField] private bool logDebug;
    [SerializeField] private bool logSpawnTransformDebug = true;

    [Header("Gizmos")]
    [SerializeField] private bool showThrowGizmos = true;
    [SerializeField] private bool showThrowGizmosOnlyWhenSelected = true;
    [SerializeField] private float throwGizmoLength = 2f;
    [SerializeField] private float throwGizmoSphereRadius = 0.08f;
    [SerializeField] private Color throwGizmoColor = new Color(0.85f, 0.15f, 1f, 1f);
    [SerializeField] private Color rawForwardGizmoColor = new Color(0.65f, 0.65f, 0.65f, 0.75f);

    private void Awake()
    {
        if (attackWarningWindow == null)
        {
            attackWarningWindow = GetComponent<EnemyAttackWarningWindow>();
        }

        ResolvePlayerTarget();
    }

    private void OnValidate()
    {
        projectileLifetime = Mathf.Max(0.05f, projectileLifetime);

        if (firePoints == null)
        {
            return;
        }

        for (int i = 0; i < firePoints.Length; i++)
        {
            UltimateDaggerFirePoint point = firePoints[i];
            if (point == null)
            {
                continue;
            }

            point.speed = Mathf.Max(0f, point.speed);
            point.damage = Mathf.Max(0f, point.damage);
            point.poiseDamage = Mathf.Max(0f, point.poiseDamage);
            point.knockbackForce = Mathf.Max(0f, point.knockbackForce);
        }
    }

    public void FireUltimateDagger(string firePointId)
    {
        UltimateDaggerFirePoint firePoint = FindFirePoint(firePointId);
        if (firePoint == null)
        {
            LogDebug($"Fire ignored: fire point missing. id={firePointId}");
            return;
        }

        FireUltimateDagger(firePoint);
    }

    public void AE_FireUltimateDagger(string firePointId)
    {
        FireUltimateDagger(firePointId);
    }

    private void FireUltimateDagger(UltimateDaggerFirePoint firePoint)
    {
        if (daggerPrefab == null)
        {
            LogDebug($"Fire ignored: prefab missing. id={firePoint.id}, attackId={firePoint.attackId}");
            return;
        }

        Transform origin = firePoint.spawnPoint != null ? firePoint.spawnPoint : transform;
        Quaternion spawnRotation = ResolveLaunchRotation(origin, firePoint);
        Vector3 launchDirection = spawnRotation * Vector3.forward;

        if (logSpawnTransformDebug)
        {
            LogDebug(
                $"Preparing ultimate dagger. id={firePoint.id}, attackId={firePoint.attackId}, spawn={(firePoint.spawnPoint != null ? firePoint.spawnPoint.name : name)}, " +
                $"originPos={FormatVector(origin.position)}, originEuler={FormatVector(origin.rotation.eulerAngles)}, rotationOffset={FormatVector(firePoint.rotationOffset)}, " +
                $"finalEuler={FormatVector(spawnRotation.eulerAngles)}, launchDir={FormatVector(launchDirection)}, speed={firePoint.speed:F2}, damage={firePoint.damage:F1}, " +
                $"poise={firePoint.poiseDamage:F1}, knockback={firePoint.knockbackForce:F1}, canReflect={firePoint.canBeReflectedByPlayerParry}, " +
                $"lifetime={projectileLifetime:F2}, targetMask={targetLayers.value}");
        }

        EnemyDaggerProjectile projectile = Instantiate(daggerPrefab, origin.position, spawnRotation);
        projectile.Initialize(
            gameObject,
            firePoint.attackId,
            launchDirection,
            firePoint.speed,
            firePoint.damage,
            firePoint.poiseDamage,
            firePoint.knockbackForce,
            projectileLifetime,
            targetLayers,
            attackWarningWindow,
            firePoint.canBeReflectedByPlayerParry);

        UltimateDaggerFired?.Invoke(gameObject, firePoint.attackId);
        LogDebug($"Ultimate dagger fired. id={firePoint.id}, attackId={firePoint.attackId}, projectile={(projectile != null ? projectile.name : "None")}");
    }

    private UltimateDaggerFirePoint FindFirePoint(string firePointId)
    {
        if (firePoints == null || firePoints.Length == 0)
        {
            return null;
        }

        if (string.IsNullOrEmpty(firePointId))
        {
            return firePoints[0];
        }

        for (int i = 0; i < firePoints.Length; i++)
        {
            UltimateDaggerFirePoint point = firePoints[i];
            if (point != null && string.Equals(point.id, firePointId, StringComparison.Ordinal))
            {
                return point;
            }
        }

        return null;
    }

    private Quaternion ResolveLaunchRotation(Transform origin, UltimateDaggerFirePoint firePoint)
    {
        Quaternion baseRotation = origin.rotation;
        if (firePoint.aimAtPlayer)
        {
            Transform target = ResolvePlayerTarget();
            if (target != null)
            {
                Vector3 aimPoint = target.position + firePoint.aimTargetOffset;
                Vector3 toTarget = aimPoint - origin.position;
                if (firePoint.aimHorizontalOnly)
                {
                    toTarget.y = 0f;
                }

                if (toTarget.sqrMagnitude > 0.001f)
                {
                    baseRotation = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
                }
            }
            else
            {
                LogDebug($"Aim target missing. Falling back to spawn forward. firePoint={firePoint.id}");
            }
        }

        return baseRotation * Quaternion.Euler(firePoint.rotationOffset);
    }

    private Transform ResolvePlayerTarget()
    {
        if (playerTarget != null)
        {
            return playerTarget;
        }

        if (!string.IsNullOrEmpty(playerTag))
        {
            GameObject playerObject = GameObject.FindGameObjectWithTag(playerTag);
            if (playerObject != null)
            {
                playerTarget = playerObject.transform;
            }
        }

        return playerTarget;
    }

    private void LogDebug(string message)
    {
        if (!logDebug)
        {
            return;
        }

        Debug.Log($"[EnemyUltimateDaggerThrower] {message} time={Time.time:F3}", this);
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
        if (!showThrowGizmos || firePoints == null)
        {
            return;
        }

        for (int i = 0; i < firePoints.Length; i++)
        {
            UltimateDaggerFirePoint point = firePoints[i];
            if (point == null)
            {
                continue;
            }

            DrawThrowGizmo(point);
        }
    }

    private void DrawThrowGizmo(UltimateDaggerFirePoint firePoint)
    {
        Transform origin = firePoint.spawnPoint != null ? firePoint.spawnPoint : transform;
        Quaternion rawRotation = origin.rotation;
        Quaternion finalRotation = ResolveLaunchRotation(origin, firePoint);
        Vector3 position = origin.position;
        Vector3 rawDirection = rawRotation * Vector3.forward;
        Vector3 finalDirection = finalRotation * Vector3.forward;
        float length = Mathf.Max(0.1f, throwGizmoLength);

        Gizmos.color = rawForwardGizmoColor;
        Gizmos.DrawLine(position, position + rawDirection.normalized * length * 0.65f);

        Gizmos.color = throwGizmoColor;
        Gizmos.DrawSphere(position, Mathf.Max(0.01f, throwGizmoSphereRadius));
        Gizmos.DrawLine(position, position + finalDirection.normalized * length);

#if UNITY_EDITOR
        Handles.color = throwGizmoColor;
        Handles.ArrowHandleCap(
            0,
            position,
            Quaternion.LookRotation(finalDirection.normalized, Vector3.up),
            length,
            EventType.Repaint);
        Handles.Label(position + finalDirection.normalized * (length + 0.15f), $"Ultimate {firePoint.id}");
#endif
    }
}
