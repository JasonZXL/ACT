using UnityEngine;

[CreateAssetMenu(fileName = "PlayerAttackBattleWillGainTable", menuName = "ACT/Combat/Player Attack Battle Will Gain Table")]
public class PlayerAttackBattleWillGainTable : ScriptableObject
{
    [System.Serializable]
    public class Entry
    {
        public string attackId = "Light_Attack_01";
        public float battleWillGain = 10f;
    }

    [SerializeField] private float defaultBattleWillGain;
    [SerializeField] private Entry[] entries;

    public float GetBattleWillGain(string attackId)
    {
        if (!string.IsNullOrEmpty(attackId) && entries != null)
        {
            for (int i = 0; i < entries.Length; i++)
            {
                Entry entry = entries[i];
                if (entry != null && entry.attackId == attackId)
                {
                    return Mathf.Max(0f, entry.battleWillGain);
                }
            }
        }

        return Mathf.Max(0f, defaultBattleWillGain);
    }
}
