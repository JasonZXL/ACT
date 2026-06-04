using UnityEngine;
using UnityEngine.UI;

public class PlayerPoiseBarUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerPoiseController playerPoise;
    [SerializeField] private Image backgroundImage;
    [SerializeField] private Image fillImage;
    [SerializeField] private Image delayedFillImage;
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("Options")]
    [SerializeField] private bool autoFindPlayerPoise = true;
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private bool hideWhenFull;
    [SerializeField] private bool showWhileBroken = true;

    [Header("Animation")]
    [SerializeField] private bool instantRealFill = true;
    [SerializeField] private bool enforceVisualOrder = true;
    [SerializeField] private float delayedFillHoldDuration = 0.35f;
    [SerializeField] private float delayedFillLerpSpeed = 5f;
    [SerializeField] private float restoreLerpSpeed = 6f;

    [Header("Broken Flash")]
    [SerializeField] private bool flashWhenBroken = true;
    [SerializeField] private Graphic[] flashGraphics;
    [SerializeField] private Color brokenFlashColor = new Color(1f, 0.28f, 0.18f, 1f);
    [SerializeField] private float flashSpeed = 9f;

    private float _displayFill = 1f;
    private float _delayedDisplayFill = 1f;
    private float _delayedFillHoldTimer;
    private float _lastPoiseNormalized = 1f;
    private Color[] _originalFlashColors;

    private void Awake()
    {
        ConfigureImage(fillImage);
        ConfigureImage(delayedFillImage);
        ConfigureVisualOrder();
        CacheFlashColors();
        ResolveReferences();
        SnapToPoise();
    }

    private void OnValidate()
    {
        delayedFillHoldDuration = Mathf.Max(0f, delayedFillHoldDuration);
        delayedFillLerpSpeed = Mathf.Max(0.01f, delayedFillLerpSpeed);
        restoreLerpSpeed = Mathf.Max(0.01f, restoreLerpSpeed);
        flashSpeed = Mathf.Max(0.01f, flashSpeed);
        ConfigureImage(fillImage);
        ConfigureImage(delayedFillImage);
        ConfigureVisualOrder();
    }

    private void Update()
    {
        ResolveReferences();
        UpdatePoiseBar();
        UpdateBrokenFlash();
    }

    private void ResolveReferences()
    {
        if (!autoFindPlayerPoise || playerPoise != null)
        {
            return;
        }

        GameObject playerObject = !string.IsNullOrEmpty(playerTag)
            ? GameObject.FindGameObjectWithTag(playerTag)
            : null;

        if (playerObject != null)
        {
            playerPoise = playerObject.GetComponentInChildren<PlayerPoiseController>();
        }

        if (playerPoise == null)
        {
            playerPoise = FindFirstObjectByType<PlayerPoiseController>();
        }
    }

    private void UpdatePoiseBar()
    {
        if (playerPoise == null)
        {
            SetVisible(false);
            return;
        }

        float targetFill = playerPoise.PoiseNormalized;
        bool visible = (!hideWhenFull || targetFill < 0.999f) && (showWhileBroken || !playerPoise.Broken);
        SetVisible(visible);

        if (targetFill < _lastPoiseNormalized)
        {
            _delayedFillHoldTimer = delayedFillHoldDuration;
            _displayFill = targetFill;
        }
        else if (targetFill > _lastPoiseNormalized)
        {
            _displayFill = instantRealFill
                ? targetFill
                : Mathf.MoveTowards(_displayFill, targetFill, restoreLerpSpeed * Time.unscaledDeltaTime);
        }

        _lastPoiseNormalized = targetFill;

        if (!instantRealFill)
        {
            float speed = targetFill >= _displayFill ? restoreLerpSpeed : delayedFillLerpSpeed;
            _displayFill = Mathf.MoveTowards(_displayFill, targetFill, speed * Time.unscaledDeltaTime);
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
            float speed = targetFill >= _delayedDisplayFill ? restoreLerpSpeed : delayedFillLerpSpeed;
            _delayedDisplayFill = Mathf.MoveTowards(_delayedDisplayFill, targetFill, speed * Time.unscaledDeltaTime);
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

    private void SnapToPoise()
    {
        float fill = playerPoise != null ? playerPoise.PoiseNormalized : 1f;
        _displayFill = fill;
        _delayedDisplayFill = fill;
        _lastPoiseNormalized = fill;
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

    private void CacheFlashColors()
    {
        if (flashGraphics == null || flashGraphics.Length == 0)
        {
            return;
        }

        _originalFlashColors = new Color[flashGraphics.Length];
        for (int i = 0; i < flashGraphics.Length; i++)
        {
            _originalFlashColors[i] = flashGraphics[i] != null ? flashGraphics[i].color : Color.white;
        }
    }

    private void UpdateBrokenFlash()
    {
        if (!flashWhenBroken || flashGraphics == null || flashGraphics.Length == 0)
        {
            return;
        }

        if (_originalFlashColors == null || _originalFlashColors.Length != flashGraphics.Length)
        {
            CacheFlashColors();
        }

        bool broken = playerPoise != null && playerPoise.Broken;
        float pulse = broken ? (Mathf.Sin(Time.unscaledTime * flashSpeed) + 1f) * 0.5f : 0f;
        for (int i = 0; i < flashGraphics.Length; i++)
        {
            Graphic graphic = flashGraphics[i];
            if (graphic == null)
            {
                continue;
            }

            Color original = _originalFlashColors != null && i < _originalFlashColors.Length
                ? _originalFlashColors[i]
                : graphic.color;
            graphic.color = broken ? Color.Lerp(original, brokenFlashColor, pulse) : original;
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
