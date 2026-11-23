using System.Collections.Generic;
using Retinues.Troops.Save;
using TaleWorlds.SaveSystem;

namespace Retinues.Mods.Coop
{
    /// <summary>
    /// One faction's troops as the co-op server holds them: the save data Retinues writes for it, plus each
    /// troop's XP pool and each retinue's join limit, which live in other behaviours' save data. Encoded with SaveDataCodec; never saved.
    /// </summary>
    public sealed class CoopSnapshot
    {
        [SaveableField(1)]
        public FactionSaveData Troops;

        [SaveableField(2)]
        public List<string> XpTroopIds = [];

        [SaveableField(3)]
        public List<int> XpPools = [];

        // Each retinue's join limit, which lives in the auto-join behaviour's save data.
        [SaveableField(4)]
        public List<string> JoinCapTroopIds = [];

        [SaveableField(5)]
        public List<int> JoinCaps = [];

        // Where each equipment set may be used and its gender override (CombatAgentBehavior), one entry per
        // stored (troop, set): the troop id, the set index and the policy bits.
        [SaveableField(6)]
        public List<string> PolicyTroopIds = [];

        [SaveableField(7)]
        public List<int> PolicySets = [];

        [SaveableField(8)]
        public List<int> PolicyBits = [];
    }
}
