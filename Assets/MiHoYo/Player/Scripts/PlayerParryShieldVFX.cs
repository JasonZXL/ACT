using System.Collections;
using UnityEngine;

public class PlayerParryShieldVFX : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerParryController parryController;
    [SerializeField] private GameObject shieldRoot;
    [SerializeField] private Renderer shieldRenderer;
    [SerializeField] private bool disableShieldRootWhenHidden = true;

    [Header("Material Properties")]
    [SerializeField] private string colorProperty = "_BaseColor";
    [SerializeField] private string alphaProperty = "_Alpha";
    [SerializeField] private Color shieldColor = new Color(0.4f, 0.85f, 1f, 0.45f);
    [SerializeField] private Color successColor = new Color(1f, 1f, 1f, 0.75f);

    [Header("Parry Start")]
    [SerializeField] private Vector3 startScale = Vector3.one * 0.8f;
    [SerializeField] private Vector3 peakScale = Vector3.one * 1.2f;
    [SerializeField] private Vector3 endScale = Vector3.one;
    [SerializeField] private float appearDuration = 0.04f;
    [SerializeField] private float holdDuration = 0.1f;
    [SerializeField] private float fadeDuration = 0.18f;

    [Header("Parry Success")]
    [SerializeField] private bool flashOnSuccess = true;
    [SerializeField] private Vector3 successScale = Vector3.one * 1.35f;
    [SerializeField] private float successFlashDuration = 0.12f;
    [SerializeField] private float successFadeDuration = 0.16f;

    [Header("Debug")]
    [SerializeField] private bool logDebug;

    private MaterialPropertyBlock _propertyBlock;
    private Coroutine _playRoutine;

    private void Awake()
    {
        _propertyBlock = new MaterialPropertyBlock();

        if (parryController == null)
        {
            parryController = GetComponentInParent<PlayerParryController>();
        }

        if (shieldRoot == null && shieldRenderer != null)
        {
            shieldRoot = shieldRenderer.gameObject;
        }

        HideImmediate();
    }

    private void OnEnable()
    {
        PlayerParryController.ParryWindowOpened += OnParryWindowOpened;
        PlayerParryController.ParrySucceeded += OnParrySucceeded;
    }

    private void OnDisable()
    {
        PlayerParryController.ParryWindowOpened -= OnParryWindowOpened;
        PlayerParryController.ParrySucceeded -= OnParrySucceeded;
    }

    public void PlayParryStart()
    {
        PlayRoutine(PlayStartRoutine());
    }

    public void PlayParrySuccess()
    {
        if (!flashOnSuccess)
        {
            return;
        }

        PlayRoutine(PlaySuccessRoutine());
    }

    public void AE_ShowParryShield()
    {
        PlayParryStart();
    }

    public void AE_HideParryShield()
    {
        HideImmediate();
    }

    private void OnParryWindowOpened(PlayerParryController source)
    {
        if (source != parryController)
        {
            return;
        }

        LogDebug("Parry shield start from parry window.");
        PlayParryStart();
    }

    private void OnParrySucceeded(PlayerParryController source, EnemyParryWindow parriedWindow)
    {
        if (source != parryController)
        {
            return;
        }

        LogDebug(parriedWindow != null ? $"Parry shield success. enemy={parriedWindow.name}" : "Parry shield success.");
        PlayParrySuccess();
    }

    private IEnumerator PlayStartRoutine()
    {
        Show();
        yield return Animate(startScale, peakScale, 0f, shieldColor.a, appearDuration, shieldColor);
        yield return Animate(peakScale, endScale, shieldColor.a, shieldColor.a, holdDuration, shieldColor);
        yield return Animate(endScale, endScale, shieldColor.a, 0f, fadeDuration, shieldColor);
        HideImmediate();
    }

    private IEnumerator PlaySuccessRoutine()
    {
        Show();
        Vector3 currentScale = shieldRoot != null ? shieldRoot.transform.localScale : endScale;
        yield return Animate(currentScale, successScale, successColor.a, successColor.a, successFlashDuration, successColor);
        yield return Animate(successScale, endScale, successColor.a, 0f, successFadeDuration, successColor);
        HideImmediate();
    }

    private IEnumerator Animate(Vector3 fromScale, Vector3 toScale, float fromAlpha, float toAlpha, float duration, Color color)
    {
        if (duration <= 0f)
        {
            ApplyVisual(toScale, toAlpha, color);
            yield break;
        }

        float timer = 0f;
        while (timer < duration)
        {
            timer += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(timer / duration);
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            ApplyVisual(Vector3.LerpUnclamped(fromScale, toScale, eased), Mathf.Lerp(fromAlpha, toAlpha, eased), color);
            yield return null;
        }

        ApplyVisual(toScale, toAlpha, color);
    }

    private void PlayRoutine(IEnumerator routine)
    {
        if (_playRoutine != null)
        {
            StopCoroutine(_playRoutine);
        }

        _playRoutine = StartCoroutine(routine);
    }

    private void Show()
    {
        if (shieldRoot != null && CanToggleShieldRoot())
        {
            shieldRoot.SetActive(true);
        }

        if (shieldRenderer != null)
        {
            shieldRenderer.enabled = true;
        }
    }

    private void HideImmediate()
    {
        if (_playRoutine != null)
        {
            StopCoroutine(_playRoutine);
            _playRoutine = null;
        }

        ApplyVisual(endScale, 0f, shieldColor);
        if (shieldRenderer != null)
        {
            shieldRenderer.enabled = false;
        }

        if (shieldRoot != null && disableShieldRootWhenHidden && CanToggleShieldRoot())
        {
            shieldRoot.SetActive(false);
        }
    }

    private bool CanToggleShieldRoot()
    {
        return shieldRoot != gameObject && !transform.IsChildOf(shieldRoot.transform);
    }

    private void ApplyVisual(Vector3 scale, float alpha, Color color)
    {
        if (shieldRoot != null)
        {
            shieldRoot.transform.localScale = scale;
        }

        if (shieldRenderer == null)
        {
            return;
        }

        if (_propertyBlock == null)
        {
            _propertyBlock = new MaterialPropertyBlock();
        }

        Color appliedColor = color;
        appliedColor.a = alpha;
        shieldRenderer.GetPropertyBlock(_propertyBlock);
        _propertyBlock.SetColor(colorProperty, appliedColor);
        _propertyBlock.SetFloat(alphaProperty, alpha);
        shieldRenderer.SetPropertyBlock(_propertyBlock);
    }

    private void LogDebug(string message)
    {
        if (!logDebug)
        {
            return;
        }

        Debug.Log($"[PlayerParryShieldVFX] {message} time={Time.time:F3}", this);
    }
}
