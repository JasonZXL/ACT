using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class EnemyAIDebugOverlay : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private StrikeJaegerAIController aiController;
    [SerializeField] private EnemyPressureModel pressureModel;
    [SerializeField] private EnemyCombatMemory combatMemory;
    [SerializeField] private EnemyHitMemory hitMemory;
    [SerializeField] private EnemyStaggerController staggerController;
    [SerializeField] private CombatHealth health;
    [SerializeField] private RectTransform root;
    [SerializeField] private TextMeshProUGUI debugText;
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("World Billboard")]
    [SerializeField] private Transform followTarget;
    [SerializeField] private Camera targetCamera;
    [SerializeField] private Vector3 localOffset = new Vector3(1.8f, 2.7f, 0f);
    [SerializeField] private bool followTargetPosition = true;
    [SerializeField] private bool faceCamera = true;

    [Header("Display")]
    [SerializeField] private bool visible = true;
    [SerializeField] private bool hideWhenDead = true;
    [SerializeField] private float updateInterval = 0.1f;
    [SerializeField] private bool showCooldowns = true;
    [SerializeField] private bool showMovementScores = true;
    [SerializeField] private bool showMemory = true;
    [SerializeField] private bool showPressure = true;

    private readonly StringBuilder _builder = new StringBuilder(1024);
    private float _nextUpdateTime;

    private void Awake()
    {
        if (root == null)
        {
            root = transform as RectTransform;
        }

        ResolveReferences();
        RefreshText();
    }

    private void OnValidate()
    {
        updateInterval = Mathf.Max(0.02f, updateInterval);
    }

    private void LateUpdate()
    {
        ResolveReferences();
        UpdateWorldTransform();

        if (Time.unscaledTime >= _nextUpdateTime)
        {
            _nextUpdateTime = Time.unscaledTime + updateInterval;
            RefreshText();
        }
    }

    private void ResolveReferences()
    {
        if (aiController == null)
        {
            aiController = GetComponentInParent<StrikeJaegerAIController>();
            if (aiController == null)
            {
                aiController = FindFirstObjectByType<StrikeJaegerAIController>();
            }
        }

        if (pressureModel == null)
        {
            pressureModel = GetComponentInParent<EnemyPressureModel>();
            if (pressureModel == null && aiController != null)
            {
                pressureModel = aiController.GetComponent<EnemyPressureModel>();
            }
        }

        if (combatMemory == null)
        {
            combatMemory = GetComponentInParent<EnemyCombatMemory>();
            if (combatMemory == null && aiController != null)
            {
                combatMemory = aiController.GetComponent<EnemyCombatMemory>();
            }
        }

        if (hitMemory == null)
        {
            hitMemory = GetComponentInParent<EnemyHitMemory>();
            if (hitMemory == null && aiController != null)
            {
                hitMemory = aiController.GetComponent<EnemyHitMemory>();
            }
        }

        if (staggerController == null)
        {
            staggerController = GetComponentInParent<EnemyStaggerController>();
            if (staggerController == null && aiController != null)
            {
                staggerController = aiController.GetComponent<EnemyStaggerController>();
            }
        }

        if (health == null)
        {
            health = GetComponentInParent<CombatHealth>();
            if (health == null && aiController != null)
            {
                health = aiController.GetComponent<CombatHealth>();
            }
        }

        if (debugText == null)
        {
            debugText = GetComponentInChildren<TextMeshProUGUI>();
        }

        if (canvasGroup == null)
        {
            canvasGroup = GetComponent<CanvasGroup>();
        }

        if (followTarget == null && aiController != null)
        {
            followTarget = aiController.transform;
        }

        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }
    }

    private void RefreshText()
    {
        bool shouldShow = visible && debugText != null;
        if (hideWhenDead && health != null && health.IsDead)
        {
            shouldShow = false;
        }

        SetVisible(shouldShow);
        if (!shouldShow)
        {
            return;
        }

        _builder.Length = 0;
        AppendAIState();
        AppendPressure();
        AppendCombatMemory();
        AppendHitMemory();
        AppendStagger();
        AppendMovementScores();
        AppendCooldowns();

        debugText.text = _builder.ToString();
    }

    private void AppendAIState()
    {
        if (aiController == null)
        {
            _builder.AppendLine("AI: Missing");
            return;
        }

        _builder.AppendLine("[StrikeJaeger AI]");
        _builder.Append("State: ").Append(aiController.State)
            .Append(" | Intent: ").Append(aiController.CurrentIntent)
            .Append(" | HPPhase: ").Append(aiController.CurrentHealthPhaseName)
            .AppendLine();
        _builder.Append("Action: ").Append(string.IsNullOrEmpty(aiController.CurrentActionName) ? "None" : aiController.CurrentActionName)
            .Append(" | Attack: ").Append(aiController.CurrentAttackIndex)
            .Append(aiController.CurrentActionIsAttack ? " *" : string.Empty)
            .AppendLine();
        _builder.Append("Dist: ").Append(aiController.PlayerDistance.ToString("F2"))
            .Append(" / ").Append(aiController.CurrentDistanceBand)
            .Append(" | Angle: ").Append(aiController.CurrentAngleBand)
            .AppendLine();
        _builder.Append("Player: ")
            .Append(aiController.PlayerIsRunning ? "Run " : string.Empty)
            .Append(aiController.PlayerIsAttacking ? "Atk " : string.Empty)
            .Append(aiController.PlayerIsDodging ? "Dodge " : string.Empty)
            .Append("DodgeReady=").Append(aiController.PlayerDodgeReady ? "Y" : "N")
            .AppendLine();
    }

    private void AppendPressure()
    {
        if (!showPressure || pressureModel == null)
        {
            return;
        }

        _builder.Append("P/T/A/R/B: ")
            .Append(pressureModel.PressureLevel.ToString("F0")).Append("/")
            .Append(pressureModel.ThreatenedLevel.ToString("F0")).Append("/")
            .Append(pressureModel.AggressionLevel.ToString("F0")).Append("/")
            .Append(pressureModel.RetreatDesire.ToString("F0")).Append("/")
            .Append(pressureModel.BurstDesire.ToString("F0"))
            .AppendLine();
    }

    private void AppendCombatMemory()
    {
        if (!showMemory || combatMemory == null)
        {
            return;
        }

        _builder.Append("LastResult: ").Append(combatMemory.LastAttackResult)
            .Append(" | HitCombo: ").Append(combatMemory.ComboSuccessCount)
            .Append(" | Whiff: ").Append(combatMemory.ConsecutiveWhiffCount)
            .AppendLine();
    }

    private void AppendHitMemory()
    {
        if (!showMemory || hitMemory == null)
        {
            return;
        }

        _builder.Append("LastHit: ").Append(hitMemory.LastHitImpact)
            .Append(" ").Append(hitMemory.LastHitDirection)
            .Append(" | React: ").Append(hitMemory.LastHitReactType)
            .Append(hitMemory.LastHitInterruptedAttack ? " Interrupt" : string.Empty)
            .AppendLine();
        _builder.Append("RecentHit: ").Append(hitMemory.RecentHitCount)
            .Append(" M:").Append(hitMemory.RecentLightHitCount)
            .Append(" S:").Append(hitMemory.RecentSmallHitCount)
            .Append(" H:").Append(hitMemory.RecentHeavyHitCount)
            .Append(" | HeavyRecent=").Append(hitMemory.WasRecentlyLaunchedOrHeavyHit ? "Y" : "N")
            .AppendLine();
    }

    private void AppendStagger()
    {
        if (staggerController == null)
        {
            return;
        }

        _builder.Append("Stagger: ")
            .Append(staggerController.IsStaggered ? "ON " : "OFF ")
            .Append(staggerController.CurrentStaggerPoints)
            .Append("/").Append(staggerController.RequiredStaggerPoints);

        if (staggerController.IsStaggered)
        {
            _builder.Append(" | Timer: ").Append(staggerController.StaggerTimer.ToString("F1"));
        }

        _builder.AppendLine();
    }

    private void AppendMovementScores()
    {
        if (!showMovementScores || aiController == null || aiController.LastMovementScore == null)
        {
            return;
        }

        StrikeJaegerAIController.MovementScoreSnapshot score = aiController.LastMovementScore;
        _builder.Append("MoveScore: RunF ").Append(score.runForwardScore.ToString("F1"))
            .Append(" Back ").Append(score.walkBackScore.ToString("F1"))
            .Append(" Strafe ").Append(score.strafeScore.ToString("F1"))
            .Append(" DB ").Append(score.dodgeBackScore.ToString("F1"))
            .Append(" DS ").Append(score.dodgeSideScore.ToString("F1"))
            .AppendLine();
        _builder.Append("MovePick: ").Append(score.selectedMode)
            .Append("/").Append(score.selectedDirection)
            .Append(" ").Append(score.selectedScore.ToString("F1"))
            .AppendLine();
    }

    private void AppendCooldowns()
    {
        if (!showCooldowns || aiController == null)
        {
            return;
        }

        _builder.Append("CD:");
        int count = aiController.AttackCooldownCount;
        for (int i = 0; i < count; i++)
        {
            if (!aiController.TryGetAttackCooldownDebugInfo(i, out int attackIndex, out float remaining, out int otherUses, out float cooldown))
            {
                continue;
            }

            _builder.Append(" A").Append(attackIndex)
                .Append("(").Append(remaining.ToString("F1"));

            if (otherUses > 0)
            {
                _builder.Append(",u").Append(otherUses);
            }

            _builder.Append("/").Append(cooldown.ToString("F1")).Append(")");
        }

        _builder.AppendLine();
    }

    private void UpdateWorldTransform()
    {
        if (root == null)
        {
            return;
        }

        if (followTargetPosition && followTarget != null)
        {
            root.position = followTarget.TransformPoint(localOffset);
        }

        if (!faceCamera || targetCamera == null)
        {
            return;
        }

        Vector3 cameraDirection = root.position - targetCamera.transform.position;
        if (cameraDirection.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        root.rotation = Quaternion.LookRotation(cameraDirection.normalized, Vector3.up);
    }

    private void SetVisible(bool shouldShow)
    {
        if (canvasGroup != null)
        {
            canvasGroup.alpha = shouldShow ? 1f : 0f;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
            return;
        }

        if (debugText != null)
        {
            debugText.enabled = shouldShow;
        }
    }
}
