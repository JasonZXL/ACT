using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class WitchTimeVisualController : MonoBehaviour
{
    [Header("Scene Dimming")]
    [SerializeField] private bool dimScene = true;
    [SerializeField, Range(0.05f, 1f)] private float sceneBrightness = 0.45f;
    [SerializeField] private float fadeInDuration = 0.08f;
    [SerializeField] private float fadeOutDuration = 0.18f;
    [SerializeField] private Renderer[] affectedRenderers;
    [SerializeField] private Transform[] excludedRoots;

    [Header("Lights")]
    [SerializeField] private bool dimLights = true;
    [SerializeField, Range(0.05f, 1f)] private float lightIntensityMultiplier = 0.45f;
    [SerializeField] private Light[] affectedLights;

    [Header("Post Processing")]
    [SerializeField] private bool drivePostProcessing = true;
    [SerializeField] private Volume witchTimeVolume;
    [SerializeField] private bool autoResolveVolumeFromChildren = true;
    [SerializeField] private bool usePostExposure = true;
    [SerializeField] private float witchTimePostExposure = -0.9f;
    [SerializeField] private bool useSaturation = true;
    [SerializeField, Range(-100f, 100f)] private float witchTimeSaturation = -22f;
    [SerializeField] private bool useColorFilter = true;
    [SerializeField] private Color witchTimeColorFilter = new Color(0.86f, 0.91f, 1f, 1f);
    [SerializeField] private bool useVignette = true;
    [SerializeField, Range(0f, 1f)] private float witchTimeVignetteIntensity = 0.22f;
    [SerializeField, Range(0f, 1f)] private float witchTimeVignetteSmoothness = 0.42f;

    [Header("Debug")]
    [SerializeField] private bool logDebug;

    private readonly Dictionary<Renderer, MaterialPropertyBlock> _blocks = new Dictionary<Renderer, MaterialPropertyBlock>();
    private RendererState[] _rendererStates;
    private LightState[] _lightStates;
    private ColorAdjustments _colorAdjustments;
    private Vignette _vignette;
    private PostProcessState _postProcessState;
    private float _timer;
    private float _duration;
    private float _fade;
    private bool _active;
    private bool _postProcessStatusLogged;
    private bool _postProcessAppliedLogged;

    private class RendererState
    {
        public Renderer Renderer;
        public MaterialPropertyBlock OriginalBlock;
        public Color BaseColor = Color.white;
        public Color Color = Color.white;
        public bool HasBaseColor;
        public bool HasColor;
    }

    private struct LightState
    {
        public Light Light;
        public float Intensity;
    }

    private struct PostProcessState
    {
        public bool Valid;
        public float Exposure;
        public float Saturation;
        public Color ColorFilter;
        public float VignetteIntensity;
        public float VignetteSmoothness;
    }

    public void Play(float duration, Transform playerRoot)
    {
        if (!dimScene && !dimLights && !drivePostProcessing)
        {
            return;
        }

        if (!_active)
        {
            BuildTargets(playerRoot);
            _fade = 0f;
            _active = true;
            LogPostProcessingStatus();
        }

        _duration = Mathf.Max(0.01f, duration);
        _timer = _duration;
        LogDebug($"Visual effect started. duration={_duration:F2}");
    }

    public void Stop()
    {
        if (!_active)
        {
            return;
        }

        _timer = 0f;
    }

    private void Update()
    {
        if (!_active)
        {
            return;
        }

        _timer -= Time.unscaledDeltaTime;

        if (_timer > 0f)
        {
            float fadeStep = fadeInDuration <= 0f ? 1f : Time.unscaledDeltaTime / fadeInDuration;
            _fade = Mathf.MoveTowards(_fade, 1f, fadeStep);
        }
        else
        {
            float fadeStep = fadeOutDuration <= 0f ? 1f : Time.unscaledDeltaTime / fadeOutDuration;
            _fade = Mathf.MoveTowards(_fade, 0f, fadeStep);
        }

        ApplyVisuals(_fade);

        if (_timer <= 0f && _fade <= 0f)
        {
            RestoreVisuals();
            _active = false;
            _postProcessStatusLogged = false;
            _postProcessAppliedLogged = false;
            LogDebug("Visual effect ended.");
        }
    }

    private void BuildTargets(Transform playerRoot)
    {
        ResolvePostProcessing();

        if (affectedRenderers == null || affectedRenderers.Length == 0)
        {
            affectedRenderers = FindObjectsOfType<Renderer>();
        }

        if (affectedLights == null || affectedLights.Length == 0)
        {
            affectedLights = FindObjectsOfType<Light>();
        }

        List<RendererState> rendererStates = new List<RendererState>();
        if (dimScene && affectedRenderers != null)
        {
            for (int i = 0; i < affectedRenderers.Length; i++)
            {
                Renderer targetRenderer = affectedRenderers[i];
                if (targetRenderer == null || IsExcluded(targetRenderer.transform, playerRoot))
                {
                    continue;
                }

                rendererStates.Add(CreateRendererState(targetRenderer));
            }
        }

        _rendererStates = rendererStates.ToArray();

        List<LightState> lightStates = new List<LightState>();
        if (dimLights && affectedLights != null)
        {
            for (int i = 0; i < affectedLights.Length; i++)
            {
                Light targetLight = affectedLights[i];
                if (targetLight == null || IsExcluded(targetLight.transform, playerRoot))
                {
                    continue;
                }

                lightStates.Add(new LightState
                {
                    Light = targetLight,
                    Intensity = targetLight.intensity
                });
            }
        }

        _lightStates = lightStates.ToArray();
        CachePostProcessingState();
    }

    private RendererState CreateRendererState(Renderer targetRenderer)
    {
        MaterialPropertyBlock originalBlock = new MaterialPropertyBlock();
        targetRenderer.GetPropertyBlock(originalBlock);

        RendererState state = new RendererState
        {
            Renderer = targetRenderer,
            OriginalBlock = originalBlock
        };

        Material sharedMaterial = targetRenderer.sharedMaterial;
        if (sharedMaterial != null)
        {
            if (sharedMaterial.HasProperty("_BaseColor"))
            {
                state.BaseColor = sharedMaterial.GetColor("_BaseColor");
                state.HasBaseColor = true;
            }

            if (sharedMaterial.HasProperty("_Color"))
            {
                state.Color = sharedMaterial.GetColor("_Color");
                state.HasColor = true;
            }
        }

        return state;
    }

    private void ApplyVisuals(float fade)
    {
        if (_rendererStates != null)
        {
            float brightness = Mathf.Lerp(1f, sceneBrightness, fade);
            for (int i = 0; i < _rendererStates.Length; i++)
            {
                ApplyRendererBrightness(_rendererStates[i], brightness);
            }
        }

        if (_lightStates != null)
        {
            float lightMultiplier = Mathf.Lerp(1f, lightIntensityMultiplier, fade);
            for (int i = 0; i < _lightStates.Length; i++)
            {
                if (_lightStates[i].Light != null)
                {
                    _lightStates[i].Light.intensity = _lightStates[i].Intensity * lightMultiplier;
                }
            }
        }

        ApplyPostProcessing(fade);
    }

    private void ApplyRendererBrightness(RendererState state, float brightness)
    {
        if (state == null || state.Renderer == null)
        {
            return;
        }

        MaterialPropertyBlock block = GetBlock(state.Renderer);
        state.Renderer.GetPropertyBlock(block);

        if (state.HasBaseColor)
        {
            block.SetColor("_BaseColor", ScaleColor(state.BaseColor, brightness));
        }

        if (state.HasColor)
        {
            block.SetColor("_Color", ScaleColor(state.Color, brightness));
        }

        state.Renderer.SetPropertyBlock(block);
    }

    private void RestoreVisuals()
    {
        if (_rendererStates != null)
        {
            for (int i = 0; i < _rendererStates.Length; i++)
            {
                RendererState state = _rendererStates[i];
                if (state?.Renderer != null)
                {
                    state.Renderer.SetPropertyBlock(state.OriginalBlock);
                }
            }
        }

        if (_lightStates != null)
        {
            for (int i = 0; i < _lightStates.Length; i++)
            {
                if (_lightStates[i].Light != null)
                {
                    _lightStates[i].Light.intensity = _lightStates[i].Intensity;
                }
            }
        }

        RestorePostProcessing();
    }

    private MaterialPropertyBlock GetBlock(Renderer targetRenderer)
    {
        if (!_blocks.TryGetValue(targetRenderer, out MaterialPropertyBlock block))
        {
            block = new MaterialPropertyBlock();
            _blocks[targetRenderer] = block;
        }

        return block;
    }

    private void ResolvePostProcessing()
    {
        if (!drivePostProcessing)
        {
            return;
        }

        if (witchTimeVolume == null && autoResolveVolumeFromChildren)
        {
            witchTimeVolume = GetComponentInChildren<Volume>(true);
        }

        VolumeProfile profile = witchTimeVolume != null ? witchTimeVolume.profile : null;
        if (profile == null)
        {
            _colorAdjustments = null;
            _vignette = null;
            return;
        }

        profile.TryGet(out _colorAdjustments);
        profile.TryGet(out _vignette);
    }

    private void CachePostProcessingState()
    {
        _postProcessState = default;
        if (!drivePostProcessing)
        {
            return;
        }

        bool hasColorAdjustments = _colorAdjustments != null;
        bool hasVignette = _vignette != null;
        if (!hasColorAdjustments && !hasVignette)
        {
            return;
        }

        _postProcessState.Valid = true;
        if (hasColorAdjustments)
        {
            _postProcessState.Exposure = _colorAdjustments.postExposure.value;
            _postProcessState.Saturation = _colorAdjustments.saturation.value;
            _postProcessState.ColorFilter = _colorAdjustments.colorFilter.value;
        }

        if (hasVignette)
        {
            _postProcessState.VignetteIntensity = _vignette.intensity.value;
            _postProcessState.VignetteSmoothness = _vignette.smoothness.value;
        }
    }

    private void ApplyPostProcessing(float fade)
    {
        if (!drivePostProcessing || !_postProcessState.Valid)
        {
            return;
        }

        if (_colorAdjustments != null)
        {
            if (usePostExposure)
            {
                _colorAdjustments.postExposure.Override(Mathf.Lerp(_postProcessState.Exposure, witchTimePostExposure, fade));
            }

            if (useSaturation)
            {
                _colorAdjustments.saturation.Override(Mathf.Lerp(_postProcessState.Saturation, witchTimeSaturation, fade));
            }

            if (useColorFilter)
            {
                _colorAdjustments.colorFilter.Override(Color.Lerp(_postProcessState.ColorFilter, witchTimeColorFilter, fade));
            }
        }

        if (_vignette != null && useVignette)
        {
            _vignette.intensity.Override(Mathf.Lerp(_postProcessState.VignetteIntensity, witchTimeVignetteIntensity, fade));
            _vignette.smoothness.Override(Mathf.Lerp(_postProcessState.VignetteSmoothness, witchTimeVignetteSmoothness, fade));
        }

        if (!_postProcessAppliedLogged && logDebug && fade >= 0.95f)
        {
            _postProcessAppliedLogged = true;
            LogDebug(
                $"PostProcess applied. fade={fade:F2}, exposure={(_colorAdjustments != null ? _colorAdjustments.postExposure.value : 0f):F2}, " +
                $"saturation={(_colorAdjustments != null ? _colorAdjustments.saturation.value : 0f):F1}, " +
                $"vignette={(_vignette != null ? _vignette.intensity.value : 0f):F2}/{(_vignette != null ? _vignette.smoothness.value : 0f):F2}");
        }
    }

    private void RestorePostProcessing()
    {
        if (!drivePostProcessing || !_postProcessState.Valid)
        {
            return;
        }

        if (_colorAdjustments != null)
        {
            _colorAdjustments.postExposure.Override(_postProcessState.Exposure);
            _colorAdjustments.saturation.Override(_postProcessState.Saturation);
            _colorAdjustments.colorFilter.Override(_postProcessState.ColorFilter);
        }

        if (_vignette != null)
        {
            _vignette.intensity.Override(_postProcessState.VignetteIntensity);
            _vignette.smoothness.Override(_postProcessState.VignetteSmoothness);
        }
    }

    private void LogPostProcessingStatus()
    {
        if (_postProcessStatusLogged || !logDebug)
        {
            return;
        }

        _postProcessStatusLogged = true;
        string volumeName = witchTimeVolume != null ? witchTimeVolume.name : "Missing";
        string profileName = witchTimeVolume != null && witchTimeVolume.profile != null
            ? witchTimeVolume.profile.name
            : "Missing";

        LogDebug(
            $"PostProcess status. drive={drivePostProcessing}, volume={volumeName}, profile={profileName}, " +
            $"colorAdjustments={(_colorAdjustments != null)}, vignette={(_vignette != null)}, valid={_postProcessState.Valid}, " +
            $"targetExposure={witchTimePostExposure:F2}, targetSaturation={witchTimeSaturation:F1}, " +
            $"targetFilter=({witchTimeColorFilter.r:F2},{witchTimeColorFilter.g:F2},{witchTimeColorFilter.b:F2}), " +
            $"targetVignette={witchTimeVignetteIntensity:F2}/{witchTimeVignetteSmoothness:F2}");
    }

    private bool IsExcluded(Transform target, Transform playerRoot)
    {
        if (playerRoot != null && (target == playerRoot || target.IsChildOf(playerRoot)))
        {
            return true;
        }

        if (excludedRoots == null)
        {
            return false;
        }

        for (int i = 0; i < excludedRoots.Length; i++)
        {
            Transform excludedRoot = excludedRoots[i];
            if (excludedRoot != null && (target == excludedRoot || target.IsChildOf(excludedRoot)))
            {
                return true;
            }
        }

        return false;
    }

    private static Color ScaleColor(Color color, float brightness)
    {
        return new Color(color.r * brightness, color.g * brightness, color.b * brightness, color.a);
    }

    private void OnDisable()
    {
        RestoreVisuals();
        _active = false;
    }

    private void LogDebug(string message)
    {
        if (!logDebug)
        {
            return;
        }

        Debug.Log($"[WitchTimeVisual] {message} time={Time.time:F3}", this);
    }
}
