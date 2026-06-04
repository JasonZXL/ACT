using UnityEngine;
using UnityEngine.UI;

public class KianaDodgeCooldownUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private KianaDodgeCooldown dodgeCooldown;
    [SerializeField] private RectTransform root;
    [SerializeField] private Image backgroundImage;
    [SerializeField] private Image fillImage;
    [SerializeField] private Image readyPulseImage;
    [SerializeField] private CanvasGroup cooldownCanvasGroup;

    [Header("World Billboard")]
    [SerializeField] private Transform followTarget;
    [SerializeField] private Camera targetCamera;
    [SerializeField] private Vector3 localOffset = new Vector3(0.55f, 1.25f, 0f);
    [SerializeField] private bool followTargetPosition = true;
    [SerializeField] private bool faceCamera = true;

    [Header("Display")]
    [SerializeField] private bool hideWhenReady = true;
    [SerializeField] private float visibleAfterReadyTime = 0.08f;

    [Header("Ready Pulse")]
    [SerializeField] private bool playReadyPulse = true;
    [SerializeField] private float readyPulseDuration = 0.22f;
    [SerializeField] private float readyPulseStartScale = 1.1f;
    [SerializeField] private float readyPulseEndScale = 1.55f;
    [SerializeField, Range(0f, 1f)] private float readyPulseStartAlpha = 0.55f;

    private float _readyHideTimer;
    private bool _wasCoolingDown;
    private float _readyPulseTimer;
    private Color _readyPulseBaseColor = Color.white;

    private void Awake()
    {
        if (root == null)
        {
            root = transform as RectTransform;
        }

        CanvasGroup rootCanvasGroup = GetComponent<CanvasGroup>();
        if (rootCanvasGroup != null && rootCanvasGroup != cooldownCanvasGroup)
        {
            rootCanvasGroup.alpha = 1f;
            rootCanvasGroup.blocksRaycasts = false;
            rootCanvasGroup.interactable = false;
        }

        if (readyPulseImage != null)
        {
            _readyPulseBaseColor = readyPulseImage.color;
            SetReadyPulseVisible(false);
        }
    }

    private void LateUpdate()
    {
        ResolveWorldReferences();
        UpdateWorldTransform();
        UpdateFill();
        UpdateReadyPulse();
    }

    private void ResolveWorldReferences()
    {
        if (followTarget == null && dodgeCooldown != null)
        {
            followTarget = dodgeCooldown.transform;
        }

        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }
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

    private void UpdateFill()
    {
        if (dodgeCooldown == null || fillImage == null)
        {
            SetCooldownVisible(false);
            return;
        }

        fillImage.fillAmount = dodgeCooldown.CooldownFillAmount;

        if (dodgeCooldown.IsCoolingDown)
        {
            _wasCoolingDown = true;
            _readyHideTimer = visibleAfterReadyTime;
            SetCooldownVisible(true);
            return;
        }

        if (_wasCoolingDown)
        {
            _wasCoolingDown = false;
            StartReadyPulse();
        }

        if (!hideWhenReady)
        {
            SetCooldownVisible(true);
            return;
        }

        if (_readyHideTimer > 0f)
        {
            _readyHideTimer -= Time.deltaTime;
            SetCooldownVisible(true);
            return;
        }

        SetCooldownVisible(false);
    }

    private void SetCooldownVisible(bool visible)
    {
        if (cooldownCanvasGroup != null)
        {
            cooldownCanvasGroup.alpha = visible ? 1f : 0f;
            cooldownCanvasGroup.blocksRaycasts = false;
            cooldownCanvasGroup.interactable = false;
            return;
        }

        if (backgroundImage != null)
        {
            backgroundImage.enabled = visible;
        }

        if (fillImage != null)
        {
            fillImage.enabled = visible;
        }
    }

    private void StartReadyPulse()
    {
        if (!playReadyPulse || readyPulseImage == null || readyPulseDuration <= 0f)
        {
            return;
        }

        _readyPulseTimer = readyPulseDuration;
        SetReadyPulseVisible(true);
    }

    private void UpdateReadyPulse()
    {
        if (readyPulseImage == null || _readyPulseTimer <= 0f)
        {
            return;
        }

        _readyPulseTimer = Mathf.Max(0f, _readyPulseTimer - Time.deltaTime);
        float progress = 1f - (_readyPulseTimer / readyPulseDuration);
        float scale = Mathf.Lerp(readyPulseStartScale, readyPulseEndScale, progress);
        float alpha = Mathf.Lerp(readyPulseStartAlpha, 0f, progress);

        readyPulseImage.rectTransform.localScale = Vector3.one * scale;
        Color color = _readyPulseBaseColor;
        color.a = alpha;
        readyPulseImage.color = color;

        if (_readyPulseTimer <= 0f)
        {
            SetReadyPulseVisible(false);
        }
    }

    private void SetReadyPulseVisible(bool visible)
    {
        if (readyPulseImage == null)
        {
            return;
        }

        readyPulseImage.enabled = visible;
        if (!visible)
        {
            readyPulseImage.rectTransform.localScale = Vector3.one * readyPulseStartScale;
            Color color = _readyPulseBaseColor;
            color.a = 0f;
            readyPulseImage.color = color;
        }
    }
}
