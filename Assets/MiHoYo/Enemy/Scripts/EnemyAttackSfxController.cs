using System.Collections;
using UnityEngine;

public class EnemyAttackSfxController : MonoBehaviour
{
    [System.Serializable]
    public class MultiHitAttackSfxRule
    {
        public string attackId = "";
        public bool matchByContains = true;
        public int heavyHitIndex = 2;
        public float comboResetDelay = 1.2f;
        public AudioClip[] lightHitClips;
        [Range(0f, 1f)] public float lightHitVolume = 0.9f;
        public Vector2 lightPitchRange = Vector2.one;
        public AudioClip[] heavyHitClips;
        [Range(0f, 1f)] public float heavyHitVolume = 1f;
        public Vector2 heavyPitchRange = Vector2.one;

        [System.NonSerialized] public int CurrentHitStep;
        [System.NonSerialized] public float LastHitStepTime = -999f;
    }

    [System.Serializable]
    public class SwingSfxRule
    {
        public string attackId = "";
        public bool matchByContains = true;
        public AudioClip[] clips;
        [Range(0f, 1f)] public float volume = 0.85f;
        public Vector2 pitchRange = Vector2.one;
    }

    [System.Serializable]
    public class ThrowDaggerSfxRule
    {
        public string attackId = "";
        public bool matchByContains = true;
        public AudioClip[] clips;
        [Range(0f, 1f)] public float volume = 0.9f;
        public Vector2 pitchRange = Vector2.one;
    }

    [Header("References")]
    [SerializeField] private AudioSource hitAudioSource;
    [SerializeField] private AudioSource swingAudioSource;
    [SerializeField] private AudioSource throwAudioSource;

    [Header("Hit SFX")]
    [SerializeField] private MultiHitAttackSfxRule[] multiHitRules;

    [Header("Swing SFX")]
    [SerializeField] private AudioClip[] defaultSwingClips;
    [Range(0f, 1f)][SerializeField] private float defaultSwingVolume = 0.8f;
    [SerializeField] private Vector2 defaultSwingPitchRange = Vector2.one;
    [SerializeField] private SwingSfxRule[] swingRules;

    [Header("Throw Dagger SFX")]
    [SerializeField] private AudioClip[] defaultThrowDaggerClips;
    [Range(0f, 1f)][SerializeField] private float defaultThrowDaggerVolume = 0.9f;
    [SerializeField] private Vector2 defaultThrowDaggerPitchRange = Vector2.one;
    [SerializeField] private ThrowDaggerSfxRule[] throwDaggerRules;

    [Header("Options")]
    [SerializeField] private bool playHitSfx = true;
    [SerializeField] private bool playSwingSfx = true;
    [SerializeField] private bool playThrowDaggerSfx = true;
    [SerializeField] private bool hitSfxSuppressesNearSwing = true;
    [SerializeField] private float swingPlaybackDelay = 0.03f;
    [SerializeField] private bool fadeOutSwingOnHit = true;
    [SerializeField] private float swingFadeOutDuration = 0.06f;
    [SerializeField] private bool autoCreateAudioSources = true;

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
        EnemyAttackHitboxController.EnemyAttackHitConfirmed += OnEnemyAttackHitConfirmed;
        EnemyAttackHitboxController.EnemyAttackHitWindowStarted += OnEnemyAttackHitWindowStarted;
        EnemyDaggerThrower.DaggerFired += OnDaggerFired;
        EnemyUltimateDaggerThrower.UltimateDaggerFired += OnDaggerFired;
    }

    private void OnDisable()
    {
        EnemyAttackHitboxController.EnemyAttackHitConfirmed -= OnEnemyAttackHitConfirmed;
        EnemyAttackHitboxController.EnemyAttackHitWindowStarted -= OnEnemyAttackHitWindowStarted;
        EnemyDaggerThrower.DaggerFired -= OnDaggerFired;
        EnemyUltimateDaggerThrower.UltimateDaggerFired -= OnDaggerFired;
        ClearPendingSwing();
        StopSwingImmediate();
    }

    public void AE_PlayEnemyAttackSwingSfx(string attackId)
    {
        if (!playSwingSfx)
        {
            return;
        }

        ResolveReferences();
        SwingSfxRule rule = FindSwingRule(attackId);
        AudioClip clip = PickClip(rule != null && HasClips(rule.clips) ? rule.clips : defaultSwingClips);
        if (clip == null)
        {
            LogDebug($"Swing SFX skipped. Missing clip. attackId={attackId}");
            return;
        }

        float volume = rule != null && HasClips(rule.clips) ? rule.volume : defaultSwingVolume;
        Vector2 pitchRange = rule != null && HasClips(rule.clips) ? rule.pitchRange : defaultSwingPitchRange;

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

    public void AE_PlayEnemyAttackSwingSfx()
    {
        AE_PlayEnemyAttackSwingSfx(string.Empty);
    }

    public void AE_ResetEnemyMultiHitSfx(string attackId)
    {
        MultiHitAttackSfxRule rule = FindMultiHitRule(attackId);
        if (rule == null)
        {
            LogDebug($"Multi-hit SFX reset skipped. Missing rule. attackId={attackId}");
            return;
        }

        ResetMultiHitRule(rule, $"AnimationEvent:{attackId}");
    }

    public void AE_ResetAllEnemyMultiHitSfx()
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

    private void OnEnemyAttackHitConfirmed(EnemyAttackHitData hitData)
    {
        if (!playHitSfx || hitData == null || !IsOwnAttack(hitData.Attacker))
        {
            return;
        }

        _lastOwnHitTime = Time.time;
        ClearPendingSwing();
        FadeOutSwingForHit();

        MultiHitAttackSfxRule rule = FindMultiHitRule(hitData.AttackId);
        if (rule == null)
        {
            LogDebug($"Hit SFX skipped. Missing multi-hit rule. attackId={hitData.AttackId}");
            return;
        }

        PlayMultiHitSfx(rule, hitData.AttackId);
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

    private void OnEnemyAttackHitWindowStarted(GameObject attacker, string attackId)
    {
        if (!playHitSfx || !IsOwnAttack(attacker))
        {
            return;
        }

        MultiHitAttackSfxRule rule = FindMultiHitRule(attackId);
        if (rule == null)
        {
            return;
        }

        AdvanceMultiHitStep(rule, attackId);
    }

    private void OnDaggerFired(GameObject attacker, string attackId)
    {
        if (!playThrowDaggerSfx || !IsOwnAttack(attacker))
        {
            return;
        }

        ResolveReferences();
        ThrowDaggerSfxRule rule = FindThrowDaggerRule(attackId);
        AudioClip clip = PickClip(rule != null && HasClips(rule.clips) ? rule.clips : defaultThrowDaggerClips);
        if (clip == null)
        {
            LogDebug($"Throw dagger SFX skipped. Missing clip. attackId={attackId}");
            return;
        }

        float volume = rule != null && HasClips(rule.clips) ? rule.volume : defaultThrowDaggerVolume;
        Vector2 pitchRange = rule != null && HasClips(rule.clips) ? rule.pitchRange : defaultThrowDaggerPitchRange;
        PlayOneShot(throwAudioSource, clip, volume, pitchRange, $"ThrowDagger:{attackId}");
    }

    private void PlayMultiHitSfx(MultiHitAttackSfxRule rule, string attackId)
    {
        ResolveReferences();
        int hitStep = Mathf.Max(1, rule.CurrentHitStep);
        bool hasHeavyHitClips = HasClips(rule.heavyHitClips);
        bool heavy = hasHeavyHitClips && hitStep >= Mathf.Max(1, rule.heavyHitIndex);
        AudioClip clip = PickClip(heavy ? rule.heavyHitClips : rule.lightHitClips);
        if (clip == null)
        {
            LogDebug($"Multi-hit SFX skipped. Missing clip. attackId={attackId}, hitStep={hitStep}, heavy={heavy}, heavyConfigured={hasHeavyHitClips}");
            return;
        }

        PlayOneShot(
            hitAudioSource,
            clip,
            heavy ? rule.heavyHitVolume : rule.lightHitVolume,
            heavy ? rule.heavyPitchRange : rule.lightPitchRange,
            $"MultiHit:{attackId}:{hitStep}");

        LogDebug($"Multi-hit SFX resolved. attackId={attackId}, hitStep={hitStep}, heavyHitIndex={rule.heavyHitIndex}, heavy={heavy}, heavyConfigured={hasHeavyHitClips}, resetDelay={rule.comboResetDelay:F3}");
    }

    private void AdvanceMultiHitStep(MultiHitAttackSfxRule rule, string attackId)
    {
        float elapsed = Time.time - rule.LastHitStepTime;
        if (rule.CurrentHitStep > 0 && elapsed > rule.comboResetDelay)
        {
            ResetMultiHitRule(rule, $"WindowTimeout:{attackId}, elapsed={elapsed:F3}, delay={rule.comboResetDelay:F3}");
        }

        rule.CurrentHitStep++;
        rule.LastHitStepTime = Time.time;
        LogDebug($"Multi-hit SFX step advanced. attackId={attackId}, hitStep={rule.CurrentHitStep}, elapsed={elapsed:F3}, resetDelay={rule.comboResetDelay:F3}");
    }

    private void ResetMultiHitRule(MultiHitAttackSfxRule rule, string reason)
    {
        if (rule == null)
        {
            return;
        }

        rule.CurrentHitStep = 0;
        rule.LastHitStepTime = -999f;
        LogDebug($"Multi-hit SFX counter reset. attackId={rule.attackId}, reason={reason}");
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
            if (RuleMatches(rule != null ? rule.attackId : null, rule != null && rule.matchByContains, attackId))
            {
                return rule;
            }
        }

        return null;
    }

    private SwingSfxRule FindSwingRule(string attackId)
    {
        if (string.IsNullOrEmpty(attackId) || swingRules == null)
        {
            return null;
        }

        for (int i = 0; i < swingRules.Length; i++)
        {
            SwingSfxRule rule = swingRules[i];
            if (RuleMatches(rule != null ? rule.attackId : null, rule != null && rule.matchByContains, attackId))
            {
                return rule;
            }
        }

        return null;
    }

    private ThrowDaggerSfxRule FindThrowDaggerRule(string attackId)
    {
        if (string.IsNullOrEmpty(attackId) || throwDaggerRules == null)
        {
            return null;
        }

        for (int i = 0; i < throwDaggerRules.Length; i++)
        {
            ThrowDaggerSfxRule rule = throwDaggerRules[i];
            if (RuleMatches(rule != null ? rule.attackId : null, rule != null && rule.matchByContains, attackId))
            {
                return rule;
            }
        }

        return null;
    }

    private static bool RuleMatches(string configuredAttackId, bool matchByContains, string attackId)
    {
        if (string.IsNullOrEmpty(configuredAttackId) || string.IsNullOrEmpty(attackId))
        {
            return false;
        }

        if (matchByContains)
        {
            return attackId.IndexOf(configuredAttackId, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        return string.Equals(attackId, configuredAttackId, System.StringComparison.OrdinalIgnoreCase);
    }

    private void PlayOneShot(AudioSource source, AudioClip clip, float volume, Vector2 pitchRange, string reason)
    {
        if (source == null || clip == null)
        {
            return;
        }

        float originalPitch = source.pitch;
        source.pitch = ResolvePitch(pitchRange);
        source.PlayOneShot(clip, Mathf.Clamp01(volume));
        source.pitch = originalPitch;
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
        if (hitAudioSource == null)
        {
            hitAudioSource = GetComponent<AudioSource>();
        }

        if (hitAudioSource == null && autoCreateAudioSources)
        {
            hitAudioSource = CreateAudioSource();
        }

        if (swingAudioSource == null && autoCreateAudioSources)
        {
            swingAudioSource = CreateAudioSource();
        }

        if (throwAudioSource == null && autoCreateAudioSources)
        {
            throwAudioSource = CreateAudioSource();
        }
    }

    private AudioSource CreateAudioSource()
    {
        AudioSource source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = hitAudioSource != null ? hitAudioSource.spatialBlend : 0f;
        return source;
    }

    private void LogDebug(string message)
    {
        if (!logDebug)
        {
            return;
        }

        Debug.Log($"[EnemyAttackSfx] {message} time={Time.time:F3}", this);
    }
}
