using UnityEngine;

public class EnemyHealth : MonoBehaviour, IPlayerAttackReceiver
{
    [Header("Health")]
    [SerializeField] private float maxHealth = 300f;
    [SerializeField] private bool destroyOnDeath;

    [Header("Poise")]
    [SerializeField] private float maxPoise = 100f;
    [SerializeField] private float poiseRecoverDelay = 1.2f;
    [SerializeField] private float poiseRecoverSpeed = 35f;

    [Header("Animator")]
    [SerializeField] private Animator animator;
    [SerializeField] private string hitLightTriggerName = "HitLight";
    [SerializeField] private string hitHeavyTriggerName = "HitHeavy";
    [SerializeField] private string stunTriggerName = "Stun";
    [SerializeField] private string dieTriggerName = "Die";
    [SerializeField] private string deadBoolName = "Dead";

    [Header("Debug")]
    [SerializeField] private bool logDebug;

    private float _currentHealth;
    private float _currentPoise;
    private float _poiseRecoverTimer;
    private bool _isDead;

    public float CurrentHealth => _currentHealth;
    public float MaxHealth => maxHealth;
    public float CurrentPoise => _currentPoise;
    public bool IsDead => _isDead;

    private void Awake()
    {
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        _currentHealth = maxHealth;
        _currentPoise = maxPoise;
    }

    private void OnValidate()
    {
        maxHealth = Mathf.Max(1f, maxHealth);
        maxPoise = Mathf.Max(1f, maxPoise);
        poiseRecoverDelay = Mathf.Max(0f, poiseRecoverDelay);
        poiseRecoverSpeed = Mathf.Max(0f, poiseRecoverSpeed);
    }

    private void Update()
    {
        if (_isDead || _currentPoise >= maxPoise)
        {
            return;
        }

        if (_poiseRecoverTimer > 0f)
        {
            _poiseRecoverTimer -= Time.deltaTime;
            return;
        }

        _currentPoise = Mathf.Min(maxPoise, _currentPoise + poiseRecoverSpeed * Time.deltaTime);
    }

    public void ReceivePlayerAttackHit(PlayerAttackHitData hitData)
    {
        if (_isDead)
        {
            return;
        }

        TakeDamage(hitData.Damage, hitData.PoiseDamage, hitData.AttackId);
        SendMessage("OnCombatHealthPlayerHitReceived", hitData, SendMessageOptions.DontRequireReceiver);
    }

    public void TakeDamage(float damage)
    {
        TakeDamage(damage, 0f, string.Empty);
    }

    public void TakeDamage(float damage, float poiseDamage, string attackId)
    {
        if (_isDead)
        {
            return;
        }

        _currentHealth = Mathf.Max(0f, _currentHealth - Mathf.Max(0f, damage));
        _currentPoise = Mathf.Max(0f, _currentPoise - Mathf.Max(0f, poiseDamage));
        _poiseRecoverTimer = poiseRecoverDelay;

        LogDebug($"Hit received. attackId={attackId}, damage={damage:F1}, poiseDamage={poiseDamage:F1}, health={_currentHealth:F1}/{maxHealth:F1}, poise={_currentPoise:F1}/{maxPoise:F1}");

        if (_currentHealth <= 0f)
        {
            Die();
            return;
        }

        if (_currentPoise <= 0f)
        {
            _currentPoise = maxPoise;
            SetAnimatorTrigger(stunTriggerName);
            return;
        }

        SetAnimatorTrigger(poiseDamage > 0f ? hitHeavyTriggerName : hitLightTriggerName);
    }

    public void ResetHealth()
    {
        _isDead = false;
        _currentHealth = maxHealth;
        _currentPoise = maxPoise;
        _poiseRecoverTimer = 0f;
        SetAnimatorBool(deadBoolName, false);
    }

    private void Die()
    {
        _isDead = true;
        SetAnimatorBool(deadBoolName, true);
        SetAnimatorTrigger(dieTriggerName);
        LogDebug("Enemy died.");

        if (destroyOnDeath)
        {
            Destroy(gameObject);
        }
    }

    private void SetAnimatorTrigger(string triggerName)
    {
        if (animator == null || string.IsNullOrEmpty(triggerName))
        {
            return;
        }

        if (!HasAnimatorParameter(triggerName, AnimatorControllerParameterType.Trigger))
        {
            LogDebug($"Animator trigger skipped. Missing parameter={triggerName}");
            return;
        }

        animator.SetTrigger(triggerName);
    }

    private void SetAnimatorBool(string boolName, bool value)
    {
        if (animator == null || string.IsNullOrEmpty(boolName))
        {
            return;
        }

        if (!HasAnimatorParameter(boolName, AnimatorControllerParameterType.Bool))
        {
            LogDebug($"Animator bool skipped. Missing parameter={boolName}");
            return;
        }

        animator.SetBool(boolName, value);
    }

    private bool HasAnimatorParameter(string parameterName, AnimatorControllerParameterType parameterType)
    {
        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.type == parameterType && parameter.name == parameterName)
            {
                return true;
            }
        }

        return false;
    }

    private void LogDebug(string message)
    {
        if (!logDebug)
        {
            return;
        }

        Debug.Log($"[EnemyHealth] {message} time={Time.time:F3}", this);
    }
}
