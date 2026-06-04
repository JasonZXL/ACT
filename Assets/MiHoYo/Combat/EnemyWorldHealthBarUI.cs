using UnityEngine;
using UnityEngine.UI;

public class EnemyWorldHealthBarUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private CombatHealth health;
    [SerializeField] private RectTransform root;
    [SerializeField] private Image backgroundImage;
    [SerializeField] private Image fillImage;
    [SerializeField] private Image delayedFillImage;
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("World Billboard")]
    [SerializeField] private Transform followTarget;
    [SerializeField] private Camera targetCamera;
    [SerializeField] private Vector3 localOffset = new Vector3(0f, 2.2f, 0f);
    [SerializeField] private bool followTargetPosition = true;
    [SerializeField] private bool faceCamera = true;

    [Header("Display")]
    [SerializeField] private bool hideWhenFull = true;
    [SerializeField] private bool hideWhenDead = true;
    [SerializeField] private float visibleAfterChangeTime = 1.25f;

    [Header("Animation")]
    [SerializeField] private bool instantRealFill = true;
    [SerializeField] private bool enforceVisualOrder = true;
    [SerializeField] private float delayedFillHoldDuration = 0.45f;
    [SerializeField] private float delayedFillLerpSpeed = 5f;

    private float _displayFill = 1f;
    private float _delayedDisplayFill = 1f;
    private float _delayedFillHoldTimer;
    private float _visibleTimer;
    private float _lastHealthNormalized = 1f;

    private void Awake()
    {
        if (root == null)
        {
            root = transform as RectTransform;
        }

        ResolveReferences();
        ConfigureImage(fillImage);
        ConfigureImage(delayedFillImage);
        ConfigureVisualOrder();
        SnapToHealth();
    }

    private void OnValidate()
    {
        visibleAfterChangeTime = Mathf.Max(0f, visibleAfterChangeTime);
        delayedFillHoldDuration = Mathf.Max(0f, delayedFillHoldDuration);
        delayedFillLerpSpeed = Mathf.Max(0.01f, delayedFillLerpSpeed);
        ConfigureImage(fillImage);
        ConfigureImage(delayedFillImage);
        ConfigureVisualOrder();
    }

    private void LateUpdate()
    {
        ResolveReferences();
        UpdateWorldTransform();
        UpdateHealthBar();
    }

    private void ResolveReferences()
    {
        if (health == null)
        {
            health = GetComponentInParent<CombatHealth>();
        }

        if (followTarget == null && health != null)
        {
            followTarget = health.transform;
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

    private void UpdateHealthBar()
    {
        if (health == null)
        {
            SetVisible(false);
            return;
        }

        float targetFill = health.HealthNormalized;
        if (targetFill < _lastHealthNormalized)
        {
            _visibleTimer = visibleAfterChangeTime;
            _delayedFillHoldTimer = delayedFillHoldDuration;
            _displayFill = targetFill;
        }
        else if (targetFill > _lastHealthNormalized)
        {
            _visibleTimer = visibleAfterChangeTime;
            _displayFill = targetFill;
            _delayedDisplayFill = targetFill;
            _delayedFillHoldTimer = 0f;
        }

        _lastHealthNormalized = targetFill;

        if (!instantRealFill)
        {
            _displayFill = Mathf.MoveTowards(_displayFill, targetFill, delayedFillLerpSpeed * Time.unscaledDeltaTime);
        }
        else
        {
            _displayFill = targetFill;
        }

        if (_delayedFillHoldTimer > 0f)
        {
            _delayedFillHoldTimer -= Time.unscaledDeltaTime;
        }
        else
        {
            _delayedDisplayFill = Mathf.MoveTowards(_delayedDisplayFill, targetFill, delayedFillLerpSpeed * Time.unscaledDeltaTime);
        }

        if (_delayedDisplayFill < targetFill)
        {
            _delayedDisplayFill = targetFill;
        }

        if (fillImage != null)
        {
            fillImage.fillAmount = _displayFill;
        }

        if (delayedFillImage != null)
        {
            delayedFillImage.fillAmount = _delayedDisplayFill;
        }

        bool visible = true;
        if (hideWhenDead && health.IsDead)
        {
            visible = false;
        }
        else if (hideWhenFull && targetFill >= 0.999f)
        {
            visible = _visibleTimer > 0f;
        }

        if (_visibleTimer > 0f)
        {
            _visibleTimer -= Time.unscaledDeltaTime;
        }

        SetVisible(visible);
    }

    private void SnapToHealth()
    {
        float fill = health != null ? health.HealthNormalized : 1f;
        _displayFill = fill;
        _delayedDisplayFill = fill;
        _lastHealthNormalized = fill;
        _delayedFillHoldTimer = 0f;

        if (fillImage != null)
        {
            fillImage.fillAmount = fill;
        }

        if (delayedFillImage != null)
        {
            delayedFillImage.fillAmount = fill;
        }
    }

    private void SetVisible(bool visible)
    {
        if (canvasGroup != null)
        {
            canvasGroup.alpha = visible ? 1f : 0f;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
            return;
        }

        if (fillImage != null)
        {
            fillImage.enabled = visible;
        }

        if (delayedFillImage != null)
        {
            delayedFillImage.enabled = visible;
        }

        if (backgroundImage != null)
        {
            backgroundImage.enabled = visible;
        }
    }

    private static void ConfigureImage(Image image)
    {
        if (image == null)
        {
            return;
        }

        image.type = Image.Type.Filled;
        image.fillMethod = Image.FillMethod.Horizontal;
        image.fillOrigin = 0;
    }

    private void ConfigureVisualOrder()
    {
        if (!enforceVisualOrder)
        {
            return;
        }

        Transform parent = null;
        if (backgroundImage != null)
        {
            parent = backgroundImage.transform.parent;
        }
        else if (delayedFillImage != null)
        {
            parent = delayedFillImage.transform.parent;
        }
        else if (fillImage != null)
        {
            parent = fillImage.transform.parent;
        }

        if (parent == null)
        {
            return;
        }

        if (backgroundImage != null && backgroundImage.transform.parent == parent)
        {
            backgroundImage.transform.SetSiblingIndex(0);
        }

        if (delayedFillImage != null && delayedFillImage.transform.parent == parent)
        {
            delayedFillImage.transform.SetSiblingIndex(1);
        }

        if (fillImage != null && fillImage.transform.parent == parent)
        {
            fillImage.transform.SetSiblingIndex(2);
        }
    }
}
