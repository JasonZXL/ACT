using UnityEngine;

[CreateAssetMenu(fileName = "PlayerAttackHitStopTable", menuName = "ACT/Combat/Player Attack Hit Stop Table")]
public class PlayerAttackHitStopTable : ScriptableObject
{
    [System.Serializable]
    public class Entry
    {
        public string attackId = "Light_Attack_01";
        public float duration = 0.04f;
        public bool freezeAttacker = true;
        public bool freezeTarget = true;
    }

    [SerializeField] private Entry defaultEntry = new Entry();
    [SerializeField] private Entry[] entries;

    public bool TryGetHitStop(string attackId, out float duration, out bool freezeAttacker, out bool freezeTarget)
    {
        Entry entry = GetEntry(attackId);
        if (entry == null)
        {
            duration = 0f;
            freezeAttacker = false;
            freezeTarget = false;
            return false;
        }

        duration = Mathf.Max(0f, entry.duration);
        freezeAttacker = entry.freezeAttacker;
        freezeTarget = entry.freezeTarget;
        return duration > 0f && (freezeAttacker || freezeTarget);
    }

    private Entry GetEntry(string attackId)
    {
        if (!string.IsNullOrEmpty(attackId) && entries != null)
        {
            for (int i = 0; i < entries.Length; i++)
            {
                Entry entry = entries[i];
                if (entry != null && entry.attackId == attackId)
                {
                    return entry;
                }
            }
        }

        return defaultEntry;
    }
}
