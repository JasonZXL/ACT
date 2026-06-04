using UnityEngine;

public class PlayerAttackVfxController : MonoBehaviour
{
    [System.Serializable]
    public class AttackVfxRule
    {
        public string attackId = "";
        public bool matchByContains = true;
        public GameObject[] hitPrefabs;
        public Vector3 positionOffset;
        public Vector3 rotationOffset;
        public Vector3 scale = Vector3.one;
        public float lifetime = 2f;
    }

    [Header("Default VFX")]
    [SerializeField] private GameObject[] defaultHitPrefabs;
    [SerializeField] private Vector3 defaultPositionOffset;
    [SerializeField] private Vector3 defaultRotationOffset;
    [SerializeField] private Vector3 defaultScale = Vector3.one;
    [SerializeField] private float defaultLifetime = 2f;

    [Header("Attack Rules")]
    [SerializeField] private AttackVfxRule[] attackRules;

    [Header("Parry Success VFX")]
    [SerializeField] private bool spawnParrySuccessVfx = true;
    [SerializeField] private GameObject[] parrySuccessPrefabs;
    [SerializeField] private Vector3 parrySuccessPositionOffset = new Vector3(0f, 1.15f, 0.9f);
    [SerializeField] private Vector3 parrySuccessRotationOffset;
    [SerializeField] private Vector3 parrySuccessScale = Vector3.one;
    [SerializeField] private float parrySuccessLifetime = 2f;
    [SerializeField] private bool parentParryVfxToEnemy;

    [Header("Projectile Parry VFX")]
    [SerializeField] private bool spawnProjectileParryVfx = true;
    [SerializeField] private GameObject[] projectileParryPrefabs;
    [SerializeField] private Vector3 projectileParryPositionOffset;
    [SerializeField] private Vector3 projectileParryRotationOffset;
    [SerializeField] private Vector3 projectileParryScale = Vector3.one;
    [SerializeField] private float projectileParryLifetime = 2f;

    [Header("Spawn Options")]
    [SerializeField] private bool spawnHitVfx = true;
    [SerializeField] private bool alignToHitDirection = true;
    [SerializeField] private bool parentToHitTarget;

    [Header("Debug")]
    [SerializeField] private bool logDebug;

    private void OnEnable()
    {
        PlayerAttackHitboxController.PlayerAttackHitConfirmed += OnPlayerAttackHitConfirmed;
        PlayerParryController.ParrySucceeded += OnPlayerParrySucceeded;
        PlayerParryController.ProjectileParried += OnPlayerProjectileParried;
    }

    private void OnDisable()
    {
        PlayerAttackHitboxController.PlayerAttackHitConfirmed -= OnPlayerAttackHitConfirmed;
        PlayerParryController.ParrySucceeded -= OnPlayerParrySucceeded;
        PlayerParryController.ProjectileParried -= OnPlayerProjectileParried;
    }

    private void OnPlayerAttackHitConfirmed(PlayerAttackHitData hitData)
    {
        if (!spawnHitVfx || hitData == null || !IsOwnAttack(hitData.Attacker))
        {
            return;
        }

        AttackVfxRule rule = FindRule(hitData.AttackId);
        GameObject prefab = PickPrefab(rule != null && HasPrefabs(rule.hitPrefabs) ? rule.hitPrefabs : defaultHitPrefabs);
        if (prefab == null)
        {
            LogDebug($"Hit VFX skipped. Missing prefab. attackId={hitData.AttackId}");
            return;
        }

        Vector3 positionOffset = rule != null && HasPrefabs(rule.hitPrefabs) ? rule.positionOffset : defaultPositionOffset;
        Vector3 rotationOffset = rule != null && HasPrefabs(rule.hitPrefabs) ? rule.rotationOffset : defaultRotationOffset;
        Vector3 scale = rule != null && HasPrefabs(rule.hitPrefabs) ? rule.scale : defaultScale;
        float lifetime = rule != null && HasPrefabs(rule.hitPrefabs) ? rule.lifetime : defaultLifetime;

        SpawnHitVfx(prefab, hitData, positionOffset, rotationOffset, scale, lifetime);
    }

    private void OnPlayerParrySucceeded(PlayerParryController source, EnemyParryWindow parriedWindow)
    {
        if (!spawnParrySuccessVfx || !IsOwnParry(source))
        {
            return;
        }

        GameObject prefab = PickPrefab(parrySuccessPrefabs);
        if (prefab == null)
        {
            LogDebug("Parry success VFX skipped. Missing prefab.");
            return;
        }

        Transform parriedTransform = parriedWindow != null ? parriedWindow.transform : null;
        Vector3 direction = ResolveParryDirection(parriedTransform);
        Quaternion rotation = Quaternion.LookRotation(direction, Vector3.up) * Quaternion.Euler(parrySuccessRotationOffset);
        Vector3 position = transform.position + Vector3.up * parrySuccessPositionOffset.y + direction * parrySuccessPositionOffset.z;
        Vector3 right = Vector3.Cross(Vector3.up, direction).normalized;
        if (right.sqrMagnitude > 0.0001f)
        {
            position += right * parrySuccessPositionOffset.x;
        }

        Transform parent = parentParryVfxToEnemy && parriedTransform != null ? parriedTransform : null;
        SpawnSimpleVfx(prefab, position, rotation, parrySuccessScale, parrySuccessLifetime, parent, "ParrySuccess");
    }

    private void OnPlayerProjectileParried(PlayerParryController source, EnemyDaggerProjectile projectile)
    {
        if (!spawnProjectileParryVfx || !IsOwnParry(source))
        {
            return;
        }

        GameObject prefab = PickPrefab(HasPrefabs(projectileParryPrefabs) ? projectileParryPrefabs : parrySuccessPrefabs);
        if (prefab == null)
        {
            LogDebug("Projectile parry VFX skipped. Missing prefab.");
            return;
        }

        Vector3 direction = source != null ? source.transform.forward : transform.forward;
        Quaternion rotation = Quaternion.LookRotation(direction, Vector3.up) * Quaternion.Euler(projectileParryRotationOffset);
        Vector3 basePosition = projectile != null ? projectile.transform.position : transform.position + direction;
        Vector3 position = basePosition + rotation * projectileParryPositionOffset;
        Vector3 scale = projectileParryScale == Vector3.zero ? parrySuccessScale : projectileParryScale;
        float lifetime = projectileParryLifetime > 0f ? projectileParryLifetime : parrySuccessLifetime;

        SpawnSimpleVfx(prefab, position, rotation, scale, lifetime, null, "ProjectileParry");
    }

    private void SpawnHitVfx(
        GameObject prefab,
        PlayerAttackHitData hitData,
        Vector3 positionOffset,
        Vector3 rotationOffset,
        Vector3 scale,
        float lifetime)
    {
        Quaternion rotation = ResolveRotation(hitData.Direction) * Quaternion.Euler(rotationOffset);
        Vector3 position = hitData.Point + rotation * positionOffset;
        Transform parent = parentToHitTarget && hitData.HitCollider != null ? hitData.HitCollider.transform : null;
        GameObject instance = Instantiate(prefab, position, rotation, parent);

        if (scale != Vector3.zero)
        {
            instance.transform.localScale = Vector3.Scale(instance.transform.localScale, scale);
        }

        if (lifetime > 0f)
        {
            Destroy(instance, lifetime);
        }

        LogDebug($"Hit VFX spawned. attackId={hitData.AttackId}, prefab={prefab.name}, point={hitData.Point}");
    }

    private void SpawnSimpleVfx(
        GameObject prefab,
        Vector3 position,
        Quaternion rotation,
        Vector3 scale,
        float lifetime,
        Transform parent,
        string reason)
    {
        GameObject instance = Instantiate(prefab, position, rotation, parent);

        if (scale != Vector3.zero)
        {
            instance.transform.localScale = Vector3.Scale(instance.transform.localScale, scale);
        }

        if (lifetime > 0f)
        {
            Destroy(instance, lifetime);
        }

        LogDebug($"{reason} VFX spawned. prefab={prefab.name}, position={position}");
    }

    private Vector3 ResolveParryDirection(Transform parriedTransform)
    {
        Vector3 direction = parriedTransform != null
            ? parriedTransform.position - transform.position
            : transform.forward;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f)
        {
            direction = transform.forward;
            direction.y = 0f;
        }

        if (direction.sqrMagnitude <= 0.0001f)
        {
            return Vector3.forward;
        }

        return direction.normalized;
    }

    private Quaternion ResolveRotation(Vector3 hitDirection)
    {
        if (!alignToHitDirection)
        {
            return transform.rotation;
        }

        Vector3 forward = hitDirection;
        if (forward.sqrMagnitude <= 0.0001f)
        {
            forward = transform.forward;
        }

        forward.Normalize();
        return Quaternion.LookRotation(forward, Vector3.up);
    }

    private AttackVfxRule FindRule(string attackId)
    {
        if (string.IsNullOrEmpty(attackId) || attackRules == null)
        {
            return null;
        }

        for (int i = 0; i < attackRules.Length; i++)
        {
            AttackVfxRule rule = attackRules[i];
            if (rule == null || string.IsNullOrEmpty(rule.attackId))
            {
                continue;
            }

            if (rule.matchByContains)
            {
                if (attackId.IndexOf(rule.attackId, System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return rule;
                }
            }
            else if (string.Equals(attackId, rule.attackId, System.StringComparison.OrdinalIgnoreCase))
            {
                return rule;
            }
        }

        return null;
    }

    private GameObject PickPrefab(GameObject[] prefabs)
    {
        if (!HasPrefabs(prefabs))
        {
            return null;
        }

        return prefabs[Random.Range(0, prefabs.Length)];
    }

    private static bool HasPrefabs(GameObject[] prefabs)
    {
        if (prefabs == null || prefabs.Length == 0)
        {
            return false;
        }

        for (int i = 0; i < prefabs.Length; i++)
        {
            if (prefabs[i] != null)
            {
                return true;
            }
        }

        return false;
    }

    private bool IsOwnAttack(GameObject attacker)
    {
        if (attacker == null)
        {
            return false;
        }

        return attacker == gameObject ||
            attacker.transform.IsChildOf(transform) ||
            transform.IsChildOf(attacker.transform);
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

        Debug.Log($"[PlayerAttackVfx] {message} time={Time.time:F3}", this);
    }
}
