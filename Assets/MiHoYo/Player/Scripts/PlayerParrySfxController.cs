using UnityEngine;

public class PlayerParrySfxController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerParryController parryController;
    [SerializeField] private AudioSource audioSource;

    [Header("Parry Success SFX")]
    [SerializeField] private AudioClip[] parrySuccessClips;
    [Range(0f, 1f)][SerializeField] private float parrySuccessVolume = 1f;
    [SerializeField] private Vector2 parrySuccessPitchRange = Vector2.one;

    [Header("Projectile Parry SFX")]
    [SerializeField] private AudioClip[] projectileParryClips;
    [Range(0f, 1f)][SerializeField] private float projectileParryVolume = 1f;
    [SerializeField] private Vector2 projectileParryPitchRange = Vector2.one;

    [Header("Options")]
    [SerializeField] private bool autoCreateAudioSource = true;

    [Header("Debug")]
    [SerializeField] private bool logDebug;

    private void Awake()
    {
        if (parryController == null)
        {
            parryController = GetComponentInParent<PlayerParryController>();
        }

        ResolveAudioSource();
    }

    private void OnEnable()
    {
        PlayerParryController.ParrySucceeded += OnParrySucceeded;
        PlayerParryController.ProjectileParried += OnProjectileParried;
    }

    private void OnDisable()
    {
        PlayerParryController.ParrySucceeded -= OnParrySucceeded;
        PlayerParryController.ProjectileParried -= OnProjectileParried;
    }

    private void OnParrySucceeded(PlayerParryController source, EnemyParryWindow parriedWindow)
    {
        if (parryController != null && source != parryController)
        {
            return;
        }

        AudioClip clip = PickClip(parrySuccessClips);
        if (clip == null)
        {
            LogDebug("Parry success SFX skipped. Missing clip.");
            return;
        }

        PlayOneShot(clip, parrySuccessVolume, parrySuccessPitchRange);

        LogDebug(parriedWindow != null
            ? $"Parry success SFX played. clip={clip.name}, enemy={parriedWindow.name}"
            : $"Parry success SFX played. clip={clip.name}");
    }

    private void OnProjectileParried(PlayerParryController source, EnemyDaggerProjectile projectile)
    {
        if (parryController != null && source != parryController)
        {
            return;
        }

        AudioClip clip = PickClip(projectileParryClips);
        if (clip == null)
        {
            clip = PickClip(parrySuccessClips);
        }

        if (clip == null)
        {
            LogDebug("Projectile parry SFX skipped. Missing clip.");
            return;
        }

        PlayOneShot(clip, projectileParryVolume, projectileParryPitchRange);

        LogDebug(projectile != null
            ? $"Projectile parry SFX played. clip={clip.name}, projectile={projectile.name}"
            : $"Projectile parry SFX played. clip={clip.name}");
    }

    private void PlayOneShot(AudioClip clip, float volume, Vector2 pitchRange)
    {
        ResolveAudioSource();
        if (audioSource == null)
        {
            LogDebug("SFX skipped. Missing AudioSource.");
            return;
        }

        float originalPitch = audioSource.pitch;
        audioSource.pitch = ResolvePitch(pitchRange);
        audioSource.PlayOneShot(clip, Mathf.Clamp01(volume));
        audioSource.pitch = originalPitch;
    }

    private void ResolveAudioSource()
    {
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }

        if (audioSource == null && autoCreateAudioSource)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;
        }
    }

    private AudioClip PickClip(AudioClip[] clips)
    {
        if (clips == null || clips.Length == 0)
        {
            return null;
        }

        for (int attempt = 0; attempt < clips.Length; attempt++)
        {
            AudioClip clip = clips[Random.Range(0, clips.Length)];
            if (clip != null)
            {
                return clip;
            }
        }

        return null;
    }

    private float ResolvePitch(Vector2 pitchRange)
    {
        float min = Mathf.Min(pitchRange.x, pitchRange.y);
        float max = Mathf.Max(pitchRange.x, pitchRange.y);
        if (min <= 0f && max <= 0f)
        {
            return 1f;
        }

        min = Mathf.Max(0.01f, min);
        max = Mathf.Max(min, max);
        return Random.Range(min, max);
    }

    private void LogDebug(string message)
    {
        if (!logDebug)
        {
            return;
        }

        Debug.Log($"[PlayerParrySfx] {message} time={Time.time:F3}", this);
    }
}
