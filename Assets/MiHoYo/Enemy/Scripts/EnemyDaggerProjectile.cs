using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class EnemyDaggerProjectile : MonoBehaviour
{
    [Header("Collision")]
    [SerializeField] private Collider hitCollider;
    [SerializeField] private bool destroyOnFirstHit = true;
    [SerializeField] private bool ignorePerfectDodgedTargets = true;

    [Header("Visual")]
    [SerializeField] private Transform visualRoot;
    [SerializeField] private bool applyVisualRotationOffsetToVisualRoot = true;
    [SerializeField] private Vector3 visualRotationOffset;
    [SerializeField] private bool spinVisualWhileFlying = true;
    [SerializeField] private float visualSpinSpeedY = 1080f;
    [SerializeField] private bool lockVisualZRotation = true;
    [SerializeField] private float lockedVisualZRotation = 90f;

    [Header("Defaults")]
    [SerializeField] private float defaultSpeed = 16f;
    [SerializeField] private float defaultDamage = 5f;
    [SerializeField] private float defaultPoiseDamage = 8f;
    [SerializeField] private float defaultKnockbackForce = 2f;
    [SerializeField] private float defaultLifetime = 3f;
    [SerializeField] private LayerMask defaultTargetLayers = ~0;
    [SerializeField] private EnemyHitReactionType hitReactionType = EnemyHitReactionType.Light;

    [Header("Parry Reflection")]
    [SerializeField] private bool canBeReflectedByPlayerParry = true;
    [SerializeField] private string reflectedAttackId = "Player_Reflected_Dagger";
    [SerializeField] private float reflectedSpeed = 24f;
    [SerializeField] private float reflectedDamage = 8f;
    [SerializeField] private float reflectedPoiseDamage = 30f;
    [SerializeField] private float reflectedKnockbackForce = 1.5f;
    [SerializeField] private float reflectedLifetime = 2.5f;
    [SerializeField] private LayerMask reflectedTargetLayers = ~0;
    [SerializeField] private Vector3 reflectedAimOffset = new Vector3(0f, 1f, 0f);
    [SerializeField] private bool destroyReflectedOnFirstHit = true;

    [Header("Debug")]
    [SerializeField] private bool logDebug;
    [SerializeField] private bool logFlightDebug;
    [SerializeField] private float flightDebugInterval = 0.15f;
    [SerializeField] private bool logRejectedCollisionDebug = true;

    private readonly HashSet<int> _hitTargets = new HashSet<int>();
    private Rigidbody _rigidbody;
    private GameObject _attacker;
    private EnemyAttackWarningWindow _attackWarningWindow;
    private string _attackId = "Enemy_Dagger";
    private Vector3 _direction = Vector3.forward;
    private float _speed;
    private float _damage;
    private float _poiseDamage;
    private float _knockbackForce;
    private float _lifeTimer;
    private LayerMask _targetLayers;
    private float _visualSpinAngleY;
    private float _nextFlightDebugTime;
    private bool _isReflected;
    private bool _canBeReflectedThisFlight;
    private GameObject _reflectedTarget;
    private GameObject _parrySource;

    private void Awake()
    {
        EnsureComponents();
        ApplyPhysicsSettings();

        _speed = defaultSpeed;
        _damage = defaultDamage;
        _poiseDamage = defaultPoiseDamage;
        _knockbackForce = defaultKnockbackForce;
        _lifeTimer = defaultLifetime;
        _targetLayers = defaultTargetLayers;
    }

    private void Reset()
    {
        EnsureComponents();
        ApplyPhysicsSettings();
    }

    private void Update()
    {
        transform.position += _direction * (_speed * Time.deltaTime);
        UpdateVisualSpin();
        LogFlightDebug();

        _lifeTimer -= Time.deltaTime;
        if (_lifeTimer <= 0f)
        {
            LogDebug($"Lifetime expired. attackId={_attackId}, position={FormatVector(transform.position)}");
            Destroy(gameObject);
        }
    }

    public void Initialize(
        GameObject attacker,
        string attackId,
        Vector3 direction,
        float speed,
        float damage,
        float poiseDamage,
        float knockbackForce,
        float lifetime,
        LayerMask targetLayers,
        EnemyAttackWarningWindow attackWarningWindow)
    {
        Initialize(
            attacker,
            attackId,
            direction,
            speed,
            damage,
            poiseDamage,
            knockbackForce,
            lifetime,
            targetLayers,
            attackWarningWindow,
            canBeReflectedByPlayerParry);
    }

    public void Initialize(
        GameObject attacker,
        string attackId,
        Vector3 direction,
        float speed,
        float damage,
        float poiseDamage,
        float knockbackForce,
        float lifetime,
        LayerMask targetLayers,
        EnemyAttackWarningWindow attackWarningWindow,
        bool canBeReflectedThisFlight)
    {
        _attacker = attacker;
        _attackId = attackId;
        _direction = direction.sqrMagnitude > 0.001f ? direction.normalized : transform.forward;
        _speed = speed > 0f ? speed : defaultSpeed;
        _damage = Mathf.Max(0f, damage);
        _poiseDamage = Mathf.Max(0f, poiseDamage);
        _knockbackForce = Mathf.Max(0f, knockbackForce);
        _lifeTimer = lifetime > 0f ? lifetime : defaultLifetime;
        _targetLayers = targetLayers;
        _attackWarningWindow = attackWarningWindow;
        _hitTargets.Clear();
        _isReflected = false;
        _canBeReflectedThisFlight = canBeReflectedByPlayerParry && canBeReflectedThisFlight;
        _reflectedTarget = attacker;
        _parrySource = null;
        _nextFlightDebugTime = 0f;

        transform.rotation = Quaternion.LookRotation(_direction, Vector3.up);
        _visualSpinAngleY = 0f;
        ApplyVisualRotationOffset();
        LogDebug(
            $"Initialized. attackId={_attackId}, attacker={(_attacker != null ? _attacker.name : "None")}, " +
            $"position={FormatVector(transform.position)}, direction={FormatVector(_direction)}, speed={_speed:F2}, " +
            $"damage={_damage:F1}, poise={_poiseDamage:F1}, knockback={_knockbackForce:F1}, lifetime={_lifeTimer:F2}, " +
            $"targetMask={_targetLayers.value}, reaction={hitReactionType}, canReflect={_canBeReflectedThisFlight}, collider={(hitCollider != null ? hitCollider.GetType().Name : "None")}");
    }

    private void ApplyVisualRotationOffset()
    {
        Quaternion offsetRotation = GetVisualRotation();
        if (applyVisualRotationOffsetToVisualRoot && visualRoot != null)
        {
            visualRoot.localRotation = offsetRotation;
            return;
        }

        transform.rotation *= offsetRotation;
    }

    private void UpdateVisualSpin()
    {
        if (!spinVisualWhileFlying)
        {
            return;
        }

        _visualSpinAngleY += visualSpinSpeedY * Time.deltaTime;
        if (applyVisualRotationOffsetToVisualRoot && visualRoot != null)
        {
            visualRoot.localRotation = GetVisualRotation();
            return;
        }

        transform.rotation = Quaternion.LookRotation(_direction, Vector3.up) * GetVisualRotation();
    }

    private Quaternion GetVisualRotation()
    {
        float zRotation = lockVisualZRotation ? lockedVisualZRotation : visualRotationOffset.z;
        return Quaternion.Euler(
            visualRotationOffset.x,
            visualRotationOffset.y + _visualSpinAngleY,
            zRotation);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other == null)
        {
            LogRejectedCollision("Trigger ignored: collider is null.");
            return;
        }

        LogDebug(
            $"Trigger entered. attackId={_attackId}, other={other.name}, otherLayer={LayerMask.LayerToName(other.gameObject.layer)}({other.gameObject.layer}), " +
            $"position={FormatVector(transform.position)}, targetMask={_targetLayers.value}");

        if (_attacker != null && (other.transform == _attacker.transform || other.transform.IsChildOf(_attacker.transform)))
        {
            LogRejectedCollision($"Trigger ignored: collided with attacker hierarchy. other={other.name}, attacker={_attacker.name}");
            return;
        }

        if (!IsInTargetLayer(other.gameObject.layer))
        {
            LogRejectedCollision($"Trigger ignored: target layer not included. other={other.name}, layer={LayerMask.LayerToName(other.gameObject.layer)}({other.gameObject.layer}), mask={_targetLayers.value}");
            return;
        }

        if (!_isReflected && TryReflectByPlayerParry(other))
        {
            return;
        }

        if (!_isReflected &&
            ignorePerfectDodgedTargets &&
            _attackWarningWindow != null &&
            _attackWarningWindow.IsPerfectDodgedBy(other.transform))
        {
            LogDebug($"Hit ignored by perfect dodge. target={other.name}, attackId={_attackId}");
            if (destroyOnFirstHit)
            {
                Destroy(gameObject);
            }
            return;
        }

        int targetId = GetTargetId(other);
        if (_hitTargets.Contains(targetId))
        {
            LogRejectedCollision($"Trigger ignored: target already hit. other={other.name}, targetId={targetId}");
            return;
        }

        _hitTargets.Add(targetId);
        Vector3 point = other.ClosestPoint(transform.position);
        if (_isReflected)
        {
            DeliverReflectedHit(other, point);
            if (destroyReflectedOnFirstHit)
            {
                LogDebug($"Reflected projectile destroyed after hit. attackId={_attackId}, target={other.name}");
                Destroy(gameObject);
            }

            return;
        }

        EnemyAttackHitData hitData = new EnemyAttackHitData(
            _attacker != null ? _attacker : gameObject,
            other,
            _attackId,
            _damage,
            _poiseDamage,
            _knockbackForce,
            point,
            _direction,
            hitReactionType);

        DeliverHit(other, hitData);
        EnemyAttackHitboxController.PublishEnemyAttackHit(hitData);
        LogDebug(
            $"Hit registered. target={other.name}, attackId={_attackId}, damage={_damage:F1}, poise={_poiseDamage:F1}, " +
            $"knockback={_knockbackForce:F1}, point={FormatVector(point)}, direction={FormatVector(_direction)}, destroyOnFirstHit={destroyOnFirstHit}");

        if (destroyOnFirstHit)
        {
            LogDebug($"Projectile destroyed after first hit. attackId={_attackId}, target={other.name}");
            Destroy(gameObject);
        }
    }

    private bool TryReflectByPlayerParry(Collider hitCollider)
    {
        if (!_canBeReflectedThisFlight ||
            _isReflected ||
            hitCollider == null ||
            !PlayerParryController.TryGetActiveParry(hitCollider.transform, out PlayerParryController parrier))
        {
            return false;
        }

        Reflect(parrier);
        parrier.NotifyProjectileParried(this);
        return true;
    }

    private void Reflect(PlayerParryController parrier)
    {
        _isReflected = true;
        _parrySource = parrier != null ? parrier.gameObject : null;
        _attacker = _parrySource != null ? _parrySource : gameObject;
        _attackId = reflectedAttackId;
        _speed = reflectedSpeed > 0f ? reflectedSpeed : _speed;
        _damage = Mathf.Max(0f, reflectedDamage);
        _poiseDamage = Mathf.Max(0f, reflectedPoiseDamage);
        _knockbackForce = Mathf.Max(0f, reflectedKnockbackForce);
        _lifeTimer = reflectedLifetime > 0f ? reflectedLifetime : _lifeTimer;
        _targetLayers = reflectedTargetLayers;
        _hitTargets.Clear();
        _nextFlightDebugTime = 0f;

        Vector3 nextDirection = ResolveReflectedDirection();
        _direction = nextDirection.sqrMagnitude > 0.001f ? nextDirection.normalized : -_direction;
        transform.rotation = Quaternion.LookRotation(_direction, Vector3.up);
        _visualSpinAngleY = 0f;
        ApplyVisualRotationOffset();

        LogDebug(
            $"Projectile reflected. parrier={(_parrySource != null ? _parrySource.name : "None")}, " +
            $"target={(_reflectedTarget != null ? _reflectedTarget.name : "None")}, attackId={_attackId}, " +
            $"direction={FormatVector(_direction)}, speed={_speed:F2}, damage={_damage:F1}, poise={_poiseDamage:F1}, targetMask={_targetLayers.value}");
    }

    private Vector3 ResolveReflectedDirection()
    {
        if (_reflectedTarget != null)
        {
            Vector3 aimPoint = _reflectedTarget.transform.position + reflectedAimOffset;
            Vector3 toTarget = aimPoint - transform.position;
            if (toTarget.sqrMagnitude > 0.001f)
            {
                return toTarget.normalized;
            }
        }

        if (_parrySource != null)
        {
            Vector3 fromParrier = transform.position - _parrySource.transform.position;
            if (fromParrier.sqrMagnitude > 0.001f)
            {
                return fromParrier.normalized;
            }
        }

        return -_direction;
    }

    private void DeliverReflectedHit(Collider hitCollider, Vector3 point)
    {
        PlayerAttackHitData hitData = new PlayerAttackHitData(
            _parrySource != null ? _parrySource : gameObject,
            hitCollider,
            _attackId,
            _damage,
            _poiseDamage,
            _knockbackForce,
            point,
            _direction);

        IPlayerAttackReceiver receiver = FindPlayerAttackReceiver(hitCollider);
        if (receiver != null)
        {
            LogDebug($"Reflected hit delivered through IPlayerAttackReceiver. target={hitCollider.name}, receiver={receiver.GetType().Name}");
            receiver.ReceivePlayerAttackHit(hitData);
        }
        else
        {
            LogDebug($"Reflected hit delivered through SendMessage fallback. target={hitCollider.name}");
            hitCollider.SendMessageUpwards("ReceivePlayerAttackHit", hitData, SendMessageOptions.DontRequireReceiver);
            hitCollider.SendMessageUpwards("TakePlayerAttackHit", hitData, SendMessageOptions.DontRequireReceiver);
            hitCollider.SendMessageUpwards("TakeDamage", hitData.Damage, SendMessageOptions.DontRequireReceiver);
        }

        LogDebug(
            $"Reflected hit registered. target={hitCollider.name}, attackId={_attackId}, damage={_damage:F1}, poise={_poiseDamage:F1}, " +
            $"knockback={_knockbackForce:F1}, point={FormatVector(point)}, direction={FormatVector(_direction)}");
    }

    private void DeliverHit(Collider hitCollider, EnemyAttackHitData hitData)
    {
        IEnemyAttackReceiver receiver = FindReceiver(hitCollider);
        if (receiver != null)
        {
            LogDebug($"Hit delivered through IEnemyAttackReceiver. target={hitCollider.name}, receiver={receiver.GetType().Name}");
            receiver.ReceiveEnemyAttackHit(hitData);
            return;
        }

        LogDebug($"Hit delivered through SendMessage fallback. target={hitCollider.name}");
        hitCollider.SendMessageUpwards("ReceiveEnemyAttackHit", hitData, SendMessageOptions.DontRequireReceiver);
        hitCollider.SendMessageUpwards("TakeEnemyAttackHit", hitData, SendMessageOptions.DontRequireReceiver);
        hitCollider.SendMessageUpwards("TakeDamage", hitData.Damage, SendMessageOptions.DontRequireReceiver);
    }

    private IEnemyAttackReceiver FindReceiver(Collider hitCollider)
    {
        MonoBehaviour[] behaviours = hitCollider.GetComponentsInParent<MonoBehaviour>();
        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] is IEnemyAttackReceiver receiver)
            {
                return receiver;
            }
        }

        return null;
    }

    private IPlayerAttackReceiver FindPlayerAttackReceiver(Collider hitCollider)
    {
        MonoBehaviour[] behaviours = hitCollider.GetComponentsInParent<MonoBehaviour>();
        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] is IPlayerAttackReceiver receiver)
            {
                return receiver;
            }
        }

        return null;
    }

    private bool IsInTargetLayer(int layer)
    {
        return (_targetLayers.value & (1 << layer)) != 0;
    }

    private int GetTargetId(Collider hitCollider)
    {
        if (hitCollider.attachedRigidbody != null)
        {
            return hitCollider.attachedRigidbody.GetInstanceID();
        }

        return hitCollider.transform.root.GetInstanceID();
    }

    private void EnsureComponents()
    {
        if (_rigidbody == null)
        {
            _rigidbody = GetComponent<Rigidbody>();
        }

        if (hitCollider == null)
        {
            hitCollider = GetComponent<Collider>();
        }

        if (hitCollider == null)
        {
            hitCollider = gameObject.AddComponent<SphereCollider>();
            LogDebug("Hit collider auto-created as SphereCollider.");
        }
    }

    private void ApplyPhysicsSettings()
    {
        if (_rigidbody != null)
        {
            _rigidbody.isKinematic = true;
            _rigidbody.useGravity = false;
            _rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        }

        if (hitCollider != null)
        {
            hitCollider.isTrigger = true;
        }
    }

    private void LogFlightDebug()
    {
        if (!logDebug || !logFlightDebug || Time.time < _nextFlightDebugTime)
        {
            return;
        }

        _nextFlightDebugTime = Time.time + Mathf.Max(0.02f, flightDebugInterval);
        LogDebug(
            $"Flight sample. attackId={_attackId}, position={FormatVector(transform.position)}, direction={FormatVector(_direction)}, " +
            $"speed={_speed:F2}, lifetime={_lifeTimer:F2}, spinY={_visualSpinAngleY:F1}, visualRoot={(visualRoot != null ? visualRoot.name : "None")}");
    }

    private void LogRejectedCollision(string message)
    {
        if (!logRejectedCollisionDebug)
        {
            return;
        }

        LogDebug(message);
    }

    private static string FormatVector(Vector3 value)
    {
        return $"({value.x:F3},{value.y:F3},{value.z:F3})";
    }

    private void LogDebug(string message)
    {
        if (!logDebug)
        {
            return;
        }

        Debug.Log($"[EnemyDaggerProjectile] {message} time={Time.time:F3}", this);
    }
}
