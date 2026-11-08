using System.Collections.Generic;
using Retinues.Troops.Save;
using TaleWorlds.SaveSystem;

namespace Retinues.Mods.Coop
{
    /// <summary>
    /// One faction's troops as the co-op server holds them: the save data Retinues writes for it, plus each
    /// troop's XP pool, which lives in another behaviour's save data. Encoded with SaveDataCodec; never saved.
    /// </summary>
    public sealed class CoopSnapshot
    {
        [SaveableField(1)]
        public FactionSaveData Troops;

        [SaveableField(2)]
        public List<string> XpTroopIds = [];

        [SaveableField(3)]
        public List<int> XpPools = [];
    }
}
