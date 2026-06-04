using UnityEngine;

[RequireComponent(typeof(Animator))]
public class EnemySimpleAttackController : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform target;
    [SerializeField] private string targetTag = "Player";
    [SerializeField] private bool faceTargetBeforeAttack = true;
    [SerializeField] private float turnSpeed = 720f;

    [Header("Attack")]
    [SerializeField] private bool attackEnabled = true;
    [SerializeField] private float firstAttackDelay = 1.5f;
    [SerializeField] private float attackInterval = 3f;
    [SerializeField] private float attackRange = 4f;
    [SerializeField] private float attackFallbackDuration = 2f;
    [SerializeField] private int minAttackIndex = 1;
    [SerializeField] private int maxAttackIndex = 1;

    [Header("Animator")]
    [SerializeField] private string attackTriggerName = "Attack";
    [SerializeField] private string attackIndexParameterName = "AttackIndex";
    [SerializeField] private string attackingBoolName = "Attacking";

    [Header("Debug")]
    [SerializeField] private bool logDebug;

    private Animator _animator;
    private EnemyHealth _health;
    private CombatHealth _combatHealth;
    private float _attackTimer;
    private float _attackFallbackTimer;
    private bool _attacking;

    public bool IsAttacking => _attacking;
    public Transform Target
    {
        get => target;
        set => target = value;
    }

    private void Awake()
    {
        _animator = GetComponent<Animator>();
        _health = GetComponent<EnemyHealth>();
        _combatHealth = GetComponent<CombatHealth>();
        _attackTimer = Mathf.Max(0f, firstAttackDelay);
    }

    private void OnValidate()
    {
        firstAttackDelay = Mathf.Max(0f, firstAttackDelay);
        attackInterval = Mathf.Max(0.1f, attackInterval);
        attackRange = Mathf.Max(0.1f, attackRange);
        attackFallbackDuration = Mathf.Max(0.1f, attackFallbackDuration);
        minAttackIndex = Mathf.Max(1, minAttackIndex);
        maxAttackIndex = Mathf.Max(minAttackIndex, maxAttackIndex);
        turnSpeed = Mathf.Max(1f, turnSpeed);
    }

    private void Update()
    {
        if (!attackEnabled || IsDead())
        {
            return;
        }

        EnsureTarget();
        FaceTarget();

        if (_attacking)
        {
            _attackFallbackTimer -= Time.deltaTime;
            if (_attackFallbackTimer <= 0f)
            {
                LogDebug("Attack fallback duration reached.");
                FinishAttack();
            }

            return;
        }

        _attackTimer -= Time.deltaTime;
        if (_attackTimer > 0f)
        {
            return;
        }

        if (target != null && Vector3.Distance(transform.position, target.position) > attackRange)
        {
            return;
        }

        StartAttack();
    }

    public void StartAttack()
    {
        if (_attacking || IsDead())
        {
            return;
        }

        int attackIndex = Random.Range(minAttackIndex, maxAttackIndex + 1);
        if (!string.IsNullOrEmpty(attackIndexParameterName))
        {
            _animator.SetInteger(attackIndexParameterName, attackIndex);
        }

        if (!string.IsNullOrEmpty(attackingBoolName))
        {
            _animator.SetBool(attackingBoolName, true);
        }

        _attacking = true;
        if (!string.IsNullOrEmpty(attackTriggerName))
        {
            _animator.SetTrigger(attackTriggerName);
        }

        _attackFallbackTimer = attackFallbackDuration;
        LogDebug($"Attack started. attackIndex={attackIndex}");
    }

    public void AE_EnemyAttackFinished()
    {
        FinishAttack();
    }

    public void FinishAttack()
    {
        if (!string.IsNullOrEmpty(attackingBoolName))
        {
            _animator.SetBool(attackingBoolName, false);
        }

        _attacking = false;
        _attackTimer = attackInterval;
        LogDebug("Attack finished.");
    }

    private void EnsureTarget()
    {
        if (target != null || string.IsNullOrEmpty(targetTag))
        {
            return;
        }

        GameObject targetObject = GameObject.FindGameObjectWithTag(targetTag);
        if (targetObject != null)
        {
            target = targetObject.transform;
        }
    }

    private void FaceTarget()
    {
        if (!faceTargetBeforeAttack || target == null)
        {
            return;
        }

        Vector3 toTarget = target.position - transform.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude <= 0.001f)
        {
            return;
        }

        Quaternion targetRotation = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, turnSpeed * Time.deltaTime);
    }

    private bool IsDead()
    {
        return (_health != null && _health.IsDead) || (_combatHealth != null && _combatHealth.IsDead);
    }

    private void LogDebug(string message)
    {
        if (!logDebug)
        {
            return;
        }

        Debug.Log($"[EnemySimpleAttack] {message} time={Time.time:F3}", this);
    }
}
