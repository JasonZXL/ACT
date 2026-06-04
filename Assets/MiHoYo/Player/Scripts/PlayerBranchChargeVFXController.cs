using UnityEngine;

public class PlayerBranchChargeVFXController : MonoBehaviour
{
    private enum BranchChargeVisualState
    {
        Hidden,
        Standby,
        Available,
        Charging,
        Cancel
    }

    [Header("References")]
    [SerializeField] private Transform vfxAnchor;
    [SerializeField] private GameObject visualRoot;
    [SerializeField] private GameObject visualPrefab;
    [SerializeField] private Renderer[] renderers;
    [SerializeField] private Light chargeLight;
    [SerializeField] private ParticleSystem[] particles;

    [Header("Colors")]
    [SerializeField] private Color standbyColor = new Color(0.35f, 0.75f, 1f, 0.32f);
    [SerializeField] private Color availableColor = new Color(0.45f, 0.85f, 1f, 0.45f);
    [SerializeField] private Color[] levelColors =
    {
        new Color(0.45f, 0.85f, 1f, 0.55f),
        new Color(0.2f, 0.95f, 1f, 0.75f),
        new Color(1f, 0.74f, 0.18f, 0.95f),
        new Color(1f, 0.35f, 0.08f, 1f)
    };
    [SerializeField] private Color cancelColor = new Color(1f, 0.18f, 0.12f, 0.75f);

    [Header("Scale And Intensity")]
    [SerializeField] private float standbyScale = 0.6f;
    [SerializeField] private float availableScale = 0.75f;
    [SerializeField] private float levelScaleStep = 0.2f;
    [SerializeField] private float standbyLightIntensity = 0.18f;
    [SerializeField] private float availableLightIntensity = 0.35f;
    [SerializeField] private float levelLightIntensityStep = 0.45f;
    [SerializeField] private float emissionMultiplier = 1.6f;

    [Header("Pulse")]
    [SerializeField] private bool usePulse = true;
    [SerializeField] private float standbyPulseAmount = 0.025f;
    [SerializeField] private float availablePulseAmount = 0.04f;
    [SerializeField] private float chargingPulseAmount = 0.12f;
    [SerializeField] private float pulseSpeed = 7f;

    [Header("Fade")]
    [SerializeField] private float fadeInDuration = 0.08f;
    [SerializeField] private float fadeOutDuration = 0.12f;
    [SerializeField] private float cancelFlashDuration = 0.16f;

    [Header("Debug")]
    [SerializeField] private bool logDebug;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    private MaterialPropertyBlock _propertyBlock;
    private BranchChargeVisualState _state = BranchChargeVisualState.Hidden;
    private Vector3 _baseScale = Vector3.one;
    private Color _targetColor;
    private float _targetScale = 1f;
    private float _targetAlpha;
    private float _currentAlpha;
    private float _targetLightIntensity;
    private float _cancelFlashTimer;
    private int _currentLevel;
    private bool _resourceReady;

    private void Awake()
    {
        _propertyBlock = new MaterialPropertyBlock();
        EnsureVisualRoot();

        if (visualRoot != null)
        {
            _baseScale = visualRoot.transform.localScale;
        }

        HideImmediate();
    }

    private void OnValidate()
    {
        availableScale = Mathf.Max(0.01f, availableScale);
        standbyScale = Mathf.Max(0.01f, standbyScale);
        levelScaleStep = Mathf.Max(0f, levelScaleStep);
        standbyLightIntensity = Mathf.Max(0f, standbyLightIntensity);
        availableLightIntensity = Mathf.Max(0f, availableLightIntensity);
        levelLightIntensityStep = Mathf.Max(0f, levelLightIntensityStep);
        emissionMultiplier = Mathf.Max(0f, emissionMultiplier);
        standbyPulseAmount = Mathf.Max(0f, standbyPulseAmount);
        availablePulseAmount = Mathf.Max(0f, availablePulseAmount);
        chargingPulseAmount = Mathf.Max(0f, chargingPulseAmount);
        pulseSpeed = Mathf.Max(0.01f, pulseSpeed);
        fadeInDuration = Mathf.Max(0.01f, fadeInDuration);
        fadeOutDuration = Mathf.Max(0.01f, fadeOutDuration);
        cancelFlashDuration = Mathf.Max(0.01f, cancelFlashDuration);
    }

    private void Update()
    {
        float deltaTime = Time.unscaledDeltaTime;
        if (_cancelFlashTimer > 0f)
        {
            _cancelFlashTimer -= deltaTime;
            if (_cancelFlashTimer <= 0f)
            {
                if (_resourceReady)
                {
                    ShowStandby();
                }
                else
                {
                    Hide();
                }
            }
        }

        float fadeDuration = _targetAlpha > _currentAlpha ? fadeInDuration : fadeOutDuration;
        _currentAlpha = Mathf.MoveTowards(_currentAlpha, _targetAlpha, deltaTime / fadeDuration);

        ApplyVisual();

        if (_state == BranchChargeVisualState.Hidden && _currentAlpha <= 0f && visualRoot != null && visualRoot.activeSelf)
        {
            visualRoot.SetActive(false);
        }
    }

    public void ShowChargeAvailable()
    {
        EnsureVisualRoot();
        _state = BranchChargeVisualState.Available;
        _currentLevel = 0;
        _targetColor = availableColor;
        _targetScale = availableScale;
        _targetAlpha = Mathf.Clamp01(availableColor.a);
        _targetLightIntensity = availableLightIntensity;
        _cancelFlashTimer = 0f;
        SetVisualActive(true);
        SetParticlesPlaying(true);
        UpdateParticleColor(availableColor);
        LogDebug("Charge available visual shown.");
    }

    public void SetResourceReady(bool ready)
    {
        SetResourceReady(ready, false);
    }

    public void SetResourceReady(bool ready, bool forceVisualState)
    {
        if (_resourceReady == ready && !forceVisualState)
        {
            if (ready && _state == BranchChargeVisualState.Hidden)
            {
                ShowStandby();
            }

            return;
        }

        _resourceReady = ready;
        if (!forceVisualState &&
            (_state == BranchChargeVisualState.Available ||
            _state == BranchChargeVisualState.Charging ||
            _state == BranchChargeVisualState.Cancel))
        {
            LogDebug($"Resource ready changed during active charge visual. ready={ready}");
            return;
        }

        if (ready)
        {
            ShowStandby();
        }
        else
        {
            Hide();
        }
    }

    public void ShowStandby()
    {
        EnsureVisualRoot();
        _state = BranchChargeVisualState.Standby;
        _currentLevel = 0;
        _targetColor = standbyColor;
        _targetScale = standbyScale;
        _targetAlpha = Mathf.Clamp01(standbyColor.a);
        _targetLightIntensity = standbyLightIntensity;
        _cancelFlashTimer = 0f;
        SetVisualActive(true);
        SetParticlesPlaying(true);
        UpdateParticleColor(standbyColor);
        LogDebug("Resource standby visual shown.");
    }

    public void StartCharging()
    {
        EnsureVisualRoot();
        _state = BranchChargeVisualState.Charging;
        _cancelFlashTimer = 0f;
        SetChargeLevel(0);
        SetVisualActive(true);
        SetParticlesPlaying(true);
        LogDebug("Charge visual started.");
    }

    public void SetChargeLevel(int level)
    {
        _currentLevel = Mathf.Max(0, level);
        _targetColor = ResolveLevelColor(_currentLevel);
        _targetScale = availableScale + levelScaleStep * _currentLevel;
        _targetAlpha = Mathf.Clamp01(_targetColor.a);
        _targetLightIntensity = availableLightIntensity + levelLightIntensityStep * _currentLevel;
        if (_state == BranchChargeVisualState.Hidden)
        {
            _state = BranchChargeVisualState.Charging;
        }

        SetVisualActive(true);
        UpdateParticleColor(_targetColor);
        LogDebug($"Charge visual level set. level={_currentLevel}");
    }

    public void StopCharging(bool releasedSuccessfully)
    {
        if (releasedSuccessfully)
        {
            Hide();
        }
        else
        {
            CancelCharging();
        }
    }

    public void CancelCharging()
    {
        EnsureVisualRoot();
        _state = BranchChargeVisualState.Cancel;
        _targetColor = cancelColor;
        _targetScale = availableScale * 0.9f;
        _targetAlpha = Mathf.Clamp01(cancelColor.a);
        _targetLightIntensity = availableLightIntensity;
        _cancelFlashTimer = cancelFlashDuration;
        SetVisualActive(true);
        UpdateParticleColor(cancelColor);
        LogDebug("Charge visual cancelled.");
    }

    public void Hide()
    {
        _state = BranchChargeVisualState.Hidden;
        _targetAlpha = 0f;
        _targetLightIntensity = 0f;
        _cancelFlashTimer = 0f;
        SetParticlesPlaying(false);
        LogDebug("Charge visual hidden.");
    }

    public void HideImmediate()
    {
        _state = BranchChargeVisualState.Hidden;
        _resourceReady = false;
        _targetAlpha = 0f;
        _currentAlpha = 0f;
        _targetLightIntensity = 0f;
        _cancelFlashTimer = 0f;
        SetParticlesPlaying(false);
        ApplyVisual();

        if (visualRoot != null)
        {
            visualRoot.SetActive(false);
        }
    }

    private void EnsureVisualRoot()
    {
        if (visualRoot == null && visualPrefab != null)
        {
            Transform parent = vfxAnchor != null ? vfxAnchor : transform;
            visualRoot = Instantiate(visualPrefab, parent);
            visualRoot.transform.localPosition = Vector3.zero;
            visualRoot.transform.localRotation = Quaternion.identity;
            visualRoot.transform.localScale = Vector3.one;
        }

        if (visualRoot == null)
        {
            return;
        }

        if (renderers == null || renderers.Length == 0)
        {
            renderers = visualRoot.GetComponentsInChildren<Renderer>(true);
        }

        if (chargeLight == null)
        {
            chargeLight = visualRoot.GetComponentInChildren<Light>(true);
        }

        if (particles == null || particles.Length == 0)
        {
            particles = visualRoot.GetComponentsInChildren<ParticleSystem>(true);
        }
    }

    private Color ResolveLevelColor(int level)
    {
        if (levelColors == null || levelColors.Length == 0)
        {
            return availableColor;
        }

        int index = Mathf.Clamp(level, 0, levelColors.Length - 1);
        return levelColors[index];
    }

    private void SetVisualActive(bool active)
    {
        if (visualRoot != null && visualRoot.activeSelf != active)
        {
            visualRoot.SetActive(active);
        }
    }

    private void ApplyVisual()
    {
        if (visualRoot != null)
        {
            float pulseAmount = 0f;
            if (usePulse && _currentAlpha > 0f)
            {
                if (_state == BranchChargeVisualState.Charging)
                {
                    pulseAmount = chargingPulseAmount;
                }
                else if (_state == BranchChargeVisualState.Standby)
                {
                    pulseAmount = standbyPulseAmount;
                }
                else
                {
                    pulseAmount = availablePulseAmount;
                }
                pulseAmount *= (Mathf.Sin(Time.unscaledTime * pulseSpeed) + 1f) * 0.5f;
            }

            visualRoot.transform.localScale = _baseScale * (_targetScale + pulseAmount);
        }

        Color color = _targetColor;
        color.a *= _currentAlpha;
        Color emissionColor = new Color(
            _targetColor.r * emissionMultiplier * _currentAlpha,
            _targetColor.g * emissionMultiplier * _currentAlpha,
            _targetColor.b * emissionMultiplier * _currentAlpha,
            1f);

        if (renderers != null)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer targetRenderer = renderers[i];
                if (targetRenderer == null)
                {
                    continue;
                }

                targetRenderer.GetPropertyBlock(_propertyBlock);
                _propertyBlock.SetColor(BaseColorId, color);
                _propertyBlock.SetColor(ColorId, color);
                _propertyBlock.SetColor(EmissionColorId, emissionColor);
                targetRenderer.SetPropertyBlock(_propertyBlock);
            }
        }

        if (chargeLight != null)
        {
            chargeLight.color = _targetColor;
            chargeLight.intensity = _targetLightIntensity * _currentAlpha;
        }
    }

    private void SetParticlesPlaying(bool playing)
    {
        if (particles == null)
        {
            return;
        }

        for (int i = 0; i < particles.Length; i++)
        {
            ParticleSystem particle = particles[i];
            if (particle == null)
            {
                continue;
            }

            if (playing)
            {
                if (!particle.isPlaying)
                {
                    particle.Play(true);
                }
            }
            else
            {
                particle.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }
    }

    private void UpdateParticleColor(Color color)
    {
        if (particles == null)
        {
            return;
        }

        for (int i = 0; i < particles.Length; i++)
        {
            ParticleSystem particle = particles[i];
            if (particle == null)
            {
                continue;
            }

            ParticleSystem.MainModule main = particle.main;
            main.startColor = color;
        }
    }

    private void LogDebug(string message)
    {
        if (!logDebug)
        {
            return;
        }

        Debug.Log($"[PlayerBranchChargeVFX] {message} time={Time.time:F3}", this);
    }
}
