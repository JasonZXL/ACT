using System.Collections;
using UnityEngine;

public class PlayerAttackSfxController : MonoBehaviour
{
    [System.Serializable]
    public class AttackSfxRule
    {
        public string attackId = "";
        public bool matchByContains = true;
        public AudioClip[] hitClips;
        public AudioClip[] swingClips;
        [Range(0f, 1f)] public float volume = 1f;
        public Vector2 pitchRange = Vector2.one;
    }

    [System.Serializable]
    public class MultiHitAttackSfxRule
    {
        public string attackId = "";
        public bool matchByContains = true;
        public int heavyHitIndex = 4;
        public float comboResetDelay = 1.2f;
        public AudioClip[] lightHitClips;
        [Range(0f, 1f)] public float lightHitVolume = 0.9f;
        public Vector2 lightPitchRange = Vector2.one;
        public AudioClip[] heavyHitClips;
        [Range(0f, 1f)] public float heavyHitVolume = 1f;
        public Vector2 heavyPitchRange = Vector2.one;

        [System.NonSerialized] public int ConfirmedHitCount;
        [System.NonSerialized] public float LastHitTime = -999f;
    }

    [Header("References")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioSource swingAudioSource;

    [Header("Default SFX")]
    [SerializeField] private AudioClip[] defaultHitClips;
    [SerializeField] private AudioClip[] defaultSwingClips;
    [Range(0f, 1f)][SerializeField] private float defaultHitVolume = 1f;
    [Range(0f, 1f)][SerializeField] private float defaultSwingVolume = 0.8f;
    [SerializeField] private Vector2 defaultPitchRange = Vector2.one;

    [Header("Attack Rules")]
    [SerializeField] private AttackSfxRule[] attackRules;
    [SerializeField] private string[] ignoreHitSfxAttackIds;

    [Header("Multi Hit Rules")]
    [SerializeField] private MultiHitAttackSfxRule[] multiHitRules;

    [Header("Options")]
    [SerializeField] private bool playHitSfx = true;
    [SerializeField] private bool playSwingSfx = true;
    [SerializeField] private bool hitSfxSuppressesNearSwing = true;
    [SerializeField] private float swingPlaybackDelay = 0.03f;
    [SerializeField] private bool fadeOutSwingOnHit = true;
    [SerializeField] private float swingFadeOutDuration = 0.06f;
    [SerializeField] private bool autoCreateAudioSource = true;

    [Header("Debug")]
    [SerializeField] private bool logDebug;

    private Coroutine _pendingSwingRoutine;
    private Coroutine _swingFadeRoutine;
    private float _lastOwnHitTime = -999f;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnValidate()
    {
        swingPlaybackDelay = Mathf.Max(0f, swingPlaybackDelay);
        swingFadeOutDuration = Mathf.Max(0f, swingFadeOutDuration);

        if (multiHitRules == null)
        {
            return;
        }

        for (int i = 0; i < multiHitRules.Length; i++)
        {
            MultiHitAttackSfxRule rule = multiHitRules[i];
            if (rule == null)
            {
                continue;
            }

            rule.heavyHitIndex = Mathf.Max(1, rule.heavyHitIndex);
            rule.comboResetDelay = Mathf.Max(0.05f, rule.comboResetDelay);
        }
    }

    private void OnEnable()
    {
        PlayerAttackHitboxController.PlayerAttackHitConfirmed += OnPlayerAttackHitConfirmed;
    }

    private void OnDisable()
    {
        PlayerAttackHitboxController.PlayerAttackHitConfirmed -= OnPlayerAttackHitConfirmed;
        ClearPendingSwing();
        StopSwingImmediate();
    }

    public void AE_PlayAttackSwingSfx(string attackId)
    {
        if (!playSwingSfx)
        {
            return;
        }

        AttackSfxRule rule = FindRule(attackId);
        AudioClip clip = PickClip(rule != null && HasClips(rule.swingClips) ? rule.swingClips : defaultSwingClips);
        if (clip == null)
        {
            LogDebug($"Swing SFX skipped. Missing clip. attackId={attackId}");
            return;
        }

        float volume = rule != null && HasClips(rule.swingClips) ? rule.volume : defaultSwingVolume;
        Vector2 pitchRange = rule != null && HasClips(rule.swingClips) ? rule.pitchRange : defaultPitchRange;

        if (!hitSfxSuppressesNearSwing || swingPlaybackDelay <= 0f)
        {
            if (ShouldSuppressSwingNow())
            {
                LogDebug($"Swing SFX suppressed by recent hit. attackId={attackId}");
                return;
            }

            PlaySwing(clip, volume, pitchRange, $"Swing:{attackId}");
            return;
        }

        ClearPendingSwing();
        _pendingSwingRoutine = StartCoroutine(DelayedSwingRoutine(clip, volume, pitchRange, attackId));
    }

    public void AE_PlayAttackSwingSfx()
    {
        AE_PlayAttackSwingSfx(string.Empty);
    }

    private void OnPlayerAttackHitConfirmed(PlayerAttackHitData hitData)
    {
        if (!playHitSfx || hitData == null || !IsOwnAttack(hitData.Attacker))
        {
            return;
        }

        _lastOwnHitTime = Time.time;
        ClearPendingSwing();
        FadeOutSwingForHit();

        if (ShouldIgnoreHitSfx(hitData.AttackId))
        {
            LogDebug($"Hit SFX ignored by attack id. attackId={hitData.AttackId}");
            return;
        }

        MultiHitAttackSfxRule multiHitRule = FindMultiHitRule(hitData.AttackId);
        if (multiHitRule != null)
        {
            PlayMultiHitSfx(multiHitRule, hitData.AttackId);
            return;
        }

        AttackSfxRule rule = FindRule(hitData.AttackId);
        AudioClip clip = PickClip(rule != null && HasClips(rule.hitClips) ? rule.hitClips : defaultHitClips);
        if (clip == null)
        {
            LogDebug($"Hit SFX skipped. Missing clip. attackId={hitData.AttackId}");
            return;
        }

        float volume = rule != null && HasClips(rule.hitClips) ? rule.volume : defaultHitVolume;
        Vector2 pitchRange = rule != null && HasClips(rule.hitClips) ? rule.pitchRange : defaultPitchRange;
        PlayOneShot(clip, volume, pitchRange, $"Hit:{hitData.AttackId}");
    }

    private IEnumerator DelayedSwingRoutine(AudioClip clip, float volume, Vector2 pitchRange, string attackId)
    {
        float requestTime = Time.time;
        yield return new WaitForSecondsRealtime(swingPlaybackDelay);

        if (_lastOwnHitTime >= requestTime)
        {
            LogDebug($"Delayed swing SFX cancelled by hit. attackId={attackId}, requestTime={requestTime:F3}, hitTime={_lastOwnHitTime:F3}");
            _pendingSwingRoutine = null;
            yield break;
        }

        PlaySwing(clip, volume, pitchRange, $"Swing:{attackId}");
        _pendingSwingRoutine = null;
    }

    private bool ShouldSuppressSwingNow()
    {
        return hitSfxSuppressesNearSwing &&
            swingPlaybackDelay > 0f &&
            Time.time - _lastOwnHitTime <= swingPlaybackDelay;
    }

    private void ClearPendingSwing()
    {
        if (_pendingSwingRoutine == null)
        {
            return;
        }

        StopCoroutine(_pendingSwingRoutine);
        _pendingSwingRoutine = null;
    }

    public void AE_ResetMultiHitAttackSfx(string attackId)
    {
        MultiHitAttackSfxRule rule = FindMultiHitRule(attackId);
        if (rule == null)
        {
            LogDebug($"Multi-hit SFX reset skipped. Missing rule. attackId={attackId}");
            return;
        }

        ResetMultiHitRule(rule, $"AnimationEvent:{attackId}");
    }

    public void AE_ResetAllMultiHitAttackSfx()
    {
        if (multiHitRules == null)
        {
            return;
        }

        for (int i = 0; i < multiHitRules.Length; i++)
        {
            ResetMultiHitRule(multiHitRules[i], "AnimationEvent:All");
        }
    }

    public void AE_QteAttackSfxStart()
    {
        AE_ResetMultiHitAttackSfx("Kiana_Attack_QTE");
    }

    public void AE_QteAttackSfxEnd()
    {
        AE_ResetMultiHitAttackSfx("Kiana_Attack_QTE");
    }

    private void PlayOneShot(AudioClip clip, float volume, Vector2 pitchRange, string reason)
    {
        ResolveReferences();
        if (audioSource == null || clip == null)
        {
            return;
        }

        float originalPitch = audioSource.pitch;
        audioSource.pitch = ResolvePitch(pitchRange);
        audioSource.PlayOneShot(clip, volume);
        audioSource.pitch = originalPitch;
        LogDebug($"SFX played. reason={reason}, clip={clip.name}, volume={volume:F2}");
    }

    private void PlaySwing(AudioClip clip, float volume, Vector2 pitchRange, string reason)
    {
        ResolveReferences();
        if (swingAudioSource == null || clip == null)
        {
            return;
        }

        if (_swingFadeRoutine != null)
        {
            StopCoroutine(_swingFadeRoutine);
            _swingFadeRoutine = null;
        }

        swingAudioSource.Stop();
        swingAudioSource.clip = clip;
        swingAudioSource.volume = Mathf.Clamp01(volume);
        swingAudioSource.pitch = ResolvePitch(pitchRange);
        swingAudioSource.Play();
        LogDebug($"Swing SFX played. reason={reason}, clip={clip.name}, volume={volume:F2}");
    }

    private void FadeOutSwingForHit()
    {
        if (!fadeOutSwingOnHit || swingAudioSource == null || !swingAudioSource.isPlaying)
        {
            return;
        }

        if (_swingFadeRoutine != null)
        {
            StopCoroutine(_swingFadeRoutine);
        }

        _swingFadeRoutine = StartCoroutine(FadeOutSwingRoutine(swingFadeOutDuration));
    }

    private IEnumerator FadeOutSwingRoutine(float duration)
    {
        if (swingAudioSource == null)
        {
            _swingFadeRoutine = null;
            yield break;
        }

        float startVolume = swingAudioSource.volume;
        if (duration <= 0f)
        {
            StopSwingPlayback();
            yield break;
        }

        float timer = 0f;
        while (timer < duration && swingAudioSource != null && swingAudioSource.isPlaying)
        {
            timer += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(timer / duration);
            swingAudioSource.volume = Mathf.Lerp(startVolume, 0f, t);
            yield return null;
        }

        StopSwingPlayback();
    }

    private void StopSwingImmediate()
    {
        if (_swingFadeRoutine != null)
        {
            StopCoroutine(_swingFadeRoutine);
            _swingFadeRoutine = null;
        }

        if (swingAudioSource == null)
        {
            return;
        }

        StopSwingPlayback();
    }

    private void StopSwingPlayback()
    {
        if (swingAudioSource == null)
        {
            _swingFadeRoutine = null;
            return;
        }

        swingAudioSource.Stop();
        swingAudioSource.clip = null;
        swingAudioSource.volume = 1f;
        _swingFadeRoutine = null;
    }

    private void PlayMultiHitSfx(MultiHitAttackSfxRule rule, string attackId)
    {
        if (rule == null)
        {
            return;
        }

        float timeSinceLastHit = Time.time - rule.LastHitTime;
        if (rule.ConfirmedHitCount > 0 && timeSinceLastHit > rule.comboResetDelay)
        {
            ResetMultiHitRule(rule, $"Timeout:{attackId}, elapsed={timeSinceLastHit:F3}, delay={rule.comboResetDelay:F3}");
        }

        rule.LastHitTime = Time.time;
        rule.ConfirmedHitCount++;

        bool heavy = rule.ConfirmedHitCount >= Mathf.Max(1, rule.heavyHitIndex);
        AudioClip clip = PickClip(heavy ? rule.heavyHitClips : rule.lightHitClips);
        if (clip == null)
        {
            LogDebug($"Multi-hit SFX skipped. Missing clip. attackId={attackId}, hitIndex={rule.ConfirmedHitCount}, heavy={heavy}");
            return;
        }

        PlayOneShot(
            clip,
            heavy ? rule.heavyHitVolume : rule.lightHitVolume,
            heavy ? rule.heavyPitchRange : rule.lightPitchRange,
            $"MultiHit:{attackId}:{rule.ConfirmedHitCount}");

        LogDebug($"Multi-hit SFX resolved. attackId={attackId}, hitIndex={rule.ConfirmedHitCount}, heavyHitIndex={rule.heavyHitIndex}, heavy={heavy}, elapsed={timeSinceLastHit:F3}, resetDelay={rule.comboResetDelay:F3}");
    }

    private void ResetMultiHitRule(MultiHitAttackSfxRule rule, string reason)
    {
        if (rule == null)
        {
            return;
        }

        rule.ConfirmedHitCount = 0;
        rule.LastHitTime = -999f;
        LogDebug($"Multi-hit SFX counter reset. attackId={rule.attackId}, reason={reason}");
    }

    private AttackSfxRule FindRule(string attackId)
    {
        if (string.IsNullOrEmpty(attackId) || attackRules == null)
        {
            return null;
        }

        for (int i = 0; i < attackRules.Length; i++)
        {
            AttackSfxRule rule = attackRules[i];
            if (rule == null || string.IsNullOrEmpty(rule.attackId))
            {
                continue;
            }

            if (rule.matchByContains)
            {
                if (attackId.IndexOf(rule.attackId, System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return rule;
                }
            }
            else if (string.Equals(attackId, rule.attackId, System.StringComparison.OrdinalIgnoreCase))
            {
                return rule;
            }
        }

        return null;
    }

    private MultiHitAttackSfxRule FindMultiHitRule(string attackId)
    {
        if (string.IsNullOrEmpty(attackId) || multiHitRules == null)
        {
            return null;
        }

        for (int i = 0; i < multiHitRules.Length; i++)
        {
            MultiHitAttackSfxRule rule = multiHitRules[i];
            if (rule == null || string.IsNullOrEmpty(rule.attackId))
            {
                continue;
            }

            if (rule.matchByContains)
            {
                if (attackId.IndexOf(rule.attackId, System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return rule;
                }
            }
            else if (string.Equals(attackId, rule.attackId, System.StringComparison.OrdinalIgnoreCase))
            {
                return rule;
            }
        }

        return null;
    }

    private bool ShouldIgnoreHitSfx(string attackId)
    {
        if (string.IsNullOrEmpty(attackId) || ignoreHitSfxAttackIds == null)
        {
            return false;
        }

        for (int i = 0; i < ignoreHitSfxAttackIds.Length; i++)
        {
            string ignoredId = ignoreHitSfxAttackIds[i];
            if (string.IsNullOrEmpty(ignoredId))
            {
                continue;
            }

            if (attackId.IndexOf(ignoredId, System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private AudioClip PickClip(AudioClip[] clips)
    {
        if (!HasClips(clips))
        {
            return null;
        }

        return clips[Random.Range(0, clips.Length)];
    }

    private static bool HasClips(AudioClip[] clips)
    {
        if (clips == null || clips.Length == 0)
        {
            return false;
        }

        for (int i = 0; i < clips.Length; i++)
        {
            if (clips[i] != null)
            {
                return true;
            }
        }

        return false;
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

    private bool IsOwnAttack(GameObject attacker)
    {
        if (attacker == null)
        {
            return false;
        }

        return attacker == gameObject ||
            attacker.transform.IsChildOf(transform) ||
            transform.IsChildOf(attacker.transform);
    }

    private void ResolveReferences()
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

        if (swingAudioSource == null && autoCreateAudioSource)
        {
            swingAudioSource = gameObject.AddComponent<AudioSource>();
            swingAudioSource.playOnAwake = false;
            swingAudioSource.spatialBlend = audioSource != null ? audioSource.spatialBlend : 0f;
        }
    }

    private void LogDebug(string message)
    {
        if (!logDebug)
        {
            return;
        }

        Debug.Log($"[PlayerAttackSfx] {message} time={Time.time:F3}", this);
    }
}
