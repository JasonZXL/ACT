using UnityEngine;
using UnityEngine.UI;

public class PlayerHealthBarUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private CombatHealth playerHealth;
    [SerializeField] private Image backgroundImage;
    [SerializeField] private Image fillImage;
    [SerializeField] private Image delayedFillImage;
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("Options")]
    [SerializeField] private bool autoFindPlayerHealth = true;
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private bool hideWhenDead;

    [Header("Animation")]
    [SerializeField] private bool instantRealFill = true;
    [SerializeField] private bool enforceVisualOrder = true;
    [SerializeField] private float delayedFillHoldDuration = 0.45f;
    [SerializeField] private float delayedFillLerpSpeed = 5f;

    private float _displayFill = 1f;
    private float _delayedDisplayFill = 1f;
    private float _delayedFillHoldTimer;
    private float _lastHealthNormalized = 1f;

    private void Awake()
    {
        ConfigureImage(fillImage);
        ConfigureImage(delayedFillImage);
        ConfigureVisualOrder();
        ResolveReferences();
        SnapToHealth();
    }

    private void OnValidate()
    {
        delayedFillHoldDuration = Mathf.Max(0f, delayedFillHoldDuration);
        delayedFillLerpSpeed = Mathf.Max(0.01f, delayedFillLerpSpeed);
        ConfigureImage(fillImage);
        ConfigureImage(delayedFillImage);
        ConfigureVisualOrder();
    }

    private void Update()
    {
        ResolveReferences();
        UpdateHealthBar();
    }

    private void ResolveReferences()
    {
        if (!autoFindPlayerHealth || playerHealth != null)
        {
            return;
        }

        GameObject playerObject = !string.IsNullOrEmpty(playerTag)
            ? GameObject.FindGameObjectWithTag(playerTag)
            : null;

        if (playerObject != null)
        {
            playerHealth = playerObject.GetComponentInChildren<CombatHealth>();
        }

        if (playerHealth == null)
        {
            playerHealth = FindFirstObjectByType<CombatHealth>();
        }
    }

    private void UpdateHealthBar()
    {
        if (playerHealth == null)
        {
            SetVisible(false);
            return;
        }

        float targetFill = playerHealth.HealthNormalized;
        SetVisible(!hideWhenDead || !playerHealth.IsDead);

        if (targetFill < _lastHealthNormalized)
        {
            _delayedFillHoldTimer = delayedFillHoldDuration;
            _displayFill = targetFill;
        }
        else if (targetFill > _lastHealthNormalized)
        {
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
    }

    private void SnapToHealth()
    {
        float fill = playerHealth != null ? playerHealth.HealthNormalized : 1f;
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
