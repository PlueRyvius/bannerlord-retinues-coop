using System.Collections.Generic;
using TaleWorlds.SaveSystem;

namespace Retinues.Mods.Coop
{
    /// <summary>
    /// The campaign's shared equipment state as the co-op server holds it: which items are
    /// unlocked, kill and discard progress toward the others, and item stocks. Every player's
    /// editor reads these, so they travel next to the troop snapshots. Encoded with
    /// SaveDataCodec, keys in order; never saved.
    /// </summary>
    public sealed class CoopUnlocks
    {
        [SaveableField(1)]
        public List<string> Unlocked = [];

        [SaveableField(2)]
        public List<string> ProgressIds = [];

        [SaveableField(3)]
        public List<int> Progress = [];

        [SaveableField(4)]
        public List<string> StockIds = [];

        [SaveableField(5)]
        public List<int> Stocks = [];

        // Items the server announced as unlocked since its last send, and the number of that
        // announcement: each player's game shows a batch once.
        [SaveableField(6)]
        public int AnnounceSequence;

        [SaveableField(7)]
        public List<string> Announced = [];
    }
}
