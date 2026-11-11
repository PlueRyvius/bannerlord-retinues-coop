using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Retinues.Configuration;
using Retinues.Features.AutoJoin;
using Retinues.Features.Experience;
using Retinues.Game;
using Retinues.Game.Wrappers;
using Retinues.Troops.Save;
using Retinues.Utils;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace Retinues.Mods.Coop
{
    /// <summary>
    /// The link between Retinues and its Coop adapter (Retinues.Coop.dll). The adapter fills the senders when its
    /// handler arms for a session and clears them when the session ends; Retinues calls them. The adapter calls
    /// the receivers below on the game thread. Nothing here does anything in single player.
    ///
    /// In a co-op session the server owns every custom troop. A player's game:
    /// - sends troop edits that cost something or change the tree (equip, skills, upgrades, retinue conversions,
    ///   removal...) to the server instead of doing them, and sends appearance edits after applying them locally;
    /// - receives every troop-owning faction's troops from the server and loads them through Retinues' own
    ///   save-loading path, so troops look and fight the same on every game.
    /// </summary>
    [SafeClass]
    public static class CoopBridge
    {
        /// <summary>Goes up whenever a message or payload changes shape. Both sides must agree.</summary>
        public const int ProtocolVersion = 2;

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ //
        //                     Adapter-filled                     //
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ //

        /// <summary>Client: true once the server accepted this game's hello for the current session.</summary>
        public static bool Ready { get; set; }

        /// <summary>Client: sends (op, args) to the server.</summary>
        public static Action<string, IList<string>> SendAction { get; set; }

        /// <summary>Server: sends (factionId, payload) to every accepted player.</summary>
        public static Action<string, string> Broadcast { get; set; }

        /// <summary>The session ended: nothing is sent or held any more.</summary>
        public static void SessionEnded()
        {
            Ready = false;
            SendAction = null;
            Broadcast = null;
            _lastSent.Clear();
            _clientIds.Clear();
            _appearanceSent.Clear();
            SessionOverrides.Clear();
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ //
        //                    Session settings                    //
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ //

        /// <summary>
        /// Off in co-op v1, on every game: doctrines and feats, the global (culture) editor, and staged edits
        /// (equipment and training apply at once). Applied when a session arms.
        /// </summary>
        public static void ApplyForcedSettings()
        {
            SessionOverrides.Set("EnableDoctrines", false);
            SessionOverrides.Set("EnableGlobalEditor", false);
            SessionOverrides.Set("EquippingTroopsTakesTime", false);
            SessionOverrides.Set("TrainingTroopsTakesTime", false);
        }

        /// <summary>Server: its settings as "key=value" lines, for a joining player.</summary>
        public static List<string> SettingsForJoin() =>
            [.. Config.CurrentValues().Select(kv => kv.Key + "=" + kv.Value)];

        /// <summary>Client: the server's settings win for this session.</summary>
        public static void ApplyServerSettings(IList<string> lines)
        {
            foreach (var line in lines ?? [])
            {
                var cut = line.IndexOf('=');
                if (cut > 0)
                    SessionOverrides.Set(line.Substring(0, cut), line.Substring(cut + 1));
            }
            ApplyForcedSettings();
            Log.Info($"Co-op: using the server's {lines?.Count ?? 0} Retinues settings.");
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ //
        //                     Client: relaying                   //
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ //

        /// <summary>
        /// On a player's game in a session, sends the edit to the server and returns true: the caller must then
        /// return without changing anything. Returns false everywhere else (single player, the server).
        /// </summary>
        public static bool Relay(string op, params object[] args)
        {
            if (!CoopState.IsClient)
                return false;

            if (!Ready || SendAction == null)
            {
                Notify(
                    L.S(
                        "coop_not_ready",
                        "The server has not accepted Retinues on this game yet; troop edits are off."
                    )
                );
                return true;
            }

            SendAction(op, [.. args.Select(ToArg)]);
            return true;
        }

        /// <summary>
        /// Client: after the editor refreshed a troop, sends its appearance (name, gender, culture, race, body,
        /// formation, mariner) if it differs from what the server last sent. Appearance edits are applied locally
        /// first, since they cost nothing; the server copies only these fields.
        /// </summary>
        public static void SendAppearanceIfChanged(WCharacter troop)
        {
            if (!CoopState.IsClient || !Ready || troop?.IsCustom != true)
                return;

            var args = AppearanceArgs(troop);
            var key = string.Join("\u001f", args);
            if (_appearanceSent.TryGetValue(troop.StringId, out var last) && last == key)
                return;

            _appearanceSent[troop.StringId] = key;
            SendAction?.Invoke(CoopActions.Appearance, [troop.StringId, .. args]);
        }

        /// <summary>Client: the server refused or failed an edit.</summary>
        public static void ShowResult(bool ok, string message)
        {
            if (!ok && !string.IsNullOrEmpty(message))
                Notify(message);
        }

        // Appearance last sent or received, per troop id.
        private static readonly Dictionary<string, string> _appearanceSent = new(
            StringComparer.Ordinal
        );

        internal static List<string> AppearanceArgs(WCharacter troop)
        {
            var body = new TroopBodySaveData(troop.Body);
            return
            [
                troop.Name ?? "",
                ToArg(troop.IsFemale),
                troop.Culture?.StringId ?? "",
                ToArg(troop.Race),
                ToArg((int)troop.FormationClassOverride),
                ToArg(troop.IsMariner),
                ToArg(body.AgeMin),
                ToArg(body.AgeMax),
                ToArg(body.WeightMin),
                ToArg(body.WeightMax),
                ToArg(body.BuildMin),
                ToArg(body.BuildMax),
                ToArg(body.HeightMin),
                ToArg(body.HeightMax),
            ];
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ //
        //                   Server: running edits                //
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ //

        /// <summary>
        /// Server: carries out a player's relayed edit as that player's hero, then sends the changed troops to
        /// everyone. Returns whether it worked and a line to show the player when it did not.
        /// </summary>
        public static (bool Ok, string Message) RunAction(Hero hero, string op, IList<string> args)
        {
            if (!CoopState.IsServer)
                return (false, "Not the server.");

            (bool Ok, string Message) result;
            using (Player.ActAs(hero))
                result = CoopActions.Run(hero, op, args ?? []);
            Log.Info(
                $"Co-op: '{op}' for {hero?.Name}: {(result.Ok ? "done" : "refused: " + result.Message)}"
            );

            SnapshotTick(force: true);
            return result;
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ //
        //                   Server: snapshots                    //
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ //

        // Payload last broadcast, per faction id.
        private static readonly Dictionary<string, string> _lastSent = new(StringComparer.Ordinal);
        private static DateTime _nextTick;

        /// <summary>
        /// Server, every application tick: about once a second (or now, when forced) sends every troop-owning
        /// faction whose troops changed since the last send. Covers relayed edits and everything the server's own
        /// ticks change (unlocks, training, new troops on a fief or kingdom).
        /// </summary>
        public static void SnapshotTick(bool force = false)
        {
            if (Broadcast == null || !CoopState.IsServer || Campaign.Current == null)
                return;
            if (!force && DateTime.UtcNow < _nextTick)
                return;
            _nextTick = DateTime.UtcNow.AddSeconds(1);

            foreach (var (id, payload) in CaptureAll())
            {
                if (_lastSent.TryGetValue(id, out var last) && last == payload)
                    continue;
                _lastSent[id] = payload;
                Broadcast(id, payload);
            }
        }

        /// <summary>Server: every troop-owning faction's troops, for a joining player.</summary>
        public static List<(string FactionId, string Payload)> SnapshotsForJoin() => CaptureAll();

        private static List<(string, string)> CaptureAll()
        {
            var result = new List<(string, string)>();
            IEnumerable<IFaction> factions = TroopOwners
                .Clans.Cast<IFaction>()
                .Concat(TroopOwners.Kingdoms);
            foreach (var faction in factions)
            {
                var wrapper = WFaction.Get(faction);
                if (wrapper == null)
                    continue;
                var snapshot = new CoopSnapshot { Troops = new FactionSaveData(wrapper) };
                foreach (var troop in wrapper.Troops)
                {
                    if (troop?.IsCustom != true)
                        continue;
                    snapshot.XpTroopIds.Add(troop.StringId);
                    snapshot.XpPools.Add(TroopXpBehavior.Get(troop));
                    if (troop.IsRetinue)
                    {
                        snapshot.JoinCapTroopIds.Add(troop.StringId);
                        snapshot.JoinCaps.Add(AutoJoinBehavior.GetJoinCap(troop));
                    }
                }
                result.Add((faction.StringId, SaveDataCodec.Encode(snapshot)));
            }
            return result;
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ //
        //                  Client: applying troops               //
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ //

        // Stub ids each faction held after the last snapshot, per faction id.
        private static readonly Dictionary<string, HashSet<string>> _clientIds = new(
            StringComparer.Ordinal
        );

        /// <summary>
        /// Client: loads one faction's troops as the server holds them, through the same path a save load uses.
        /// The faction's stub ids are released first so every troop lands on the exact stub the server uses.
        /// </summary>
        public static void ApplySnapshot(string factionId, string payload)
        {
            if (!CoopState.IsClient || Campaign.Current == null)
                return;

            IFaction faction =
                (IFaction)Clan.All.FirstOrDefault(c => c.StringId == factionId)
                ?? Kingdom.All.FirstOrDefault(k => k.StringId == factionId);
            var wrapper = WFaction.Get(faction);
            if (wrapper == null)
            {
                Log.Warn($"Co-op: troops for unknown faction '{factionId}' ignored.");
                return;
            }

            var snapshot = SaveDataCodec.Decode<CoopSnapshot>(payload);
            var data = snapshot.Troops ?? new FactionSaveData();

            // Release what this faction holds now and what the snapshot will claim.
            var release = new HashSet<string>(StringComparer.Ordinal);
            foreach (var troop in wrapper.Troops)
                if (troop?.IsCustom == true)
                    release.Add(troop.StringId);
            if (_clientIds.TryGetValue(factionId, out var previous))
                release.UnionWith(previous);
            var incoming = IdsOf(data);
            release.UnionWith(incoming);
            WCharacter.ActiveStubIds.RemoveAll(release.Contains);

            wrapper.ClearRoots();
            data.Apply(wrapper);
            _clientIds[factionId] = incoming;

            // XP pools: the editor checks them before letting a skill go up.
            for (int i = 0; i < snapshot.XpTroopIds.Count && i < snapshot.XpPools.Count; i++)
                TroopXpBehavior.Set(
                    WCharacter.FromStringId(snapshot.XpTroopIds[i]),
                    snapshot.XpPools[i]
                );

            // Join limits: the editor shows them, the server hires by them.
            for (int i = 0; i < snapshot.JoinCapTroopIds.Count && i < snapshot.JoinCaps.Count; i++)
                AutoJoinBehavior.ApplyServerJoinCap(
                    snapshot.JoinCapTroopIds[i],
                    snapshot.JoinCaps[i]
                );

            // What the server holds is now what this game shows; don't echo it back.
            foreach (var troop in wrapper.Troops)
                if (troop?.IsCustom == true)
                    _appearanceSent[troop.StringId] = string.Join("\u001f", AppearanceArgs(troop));

            Log.Debug($"Co-op: loaded {incoming.Count} troop(s) of '{factionId}' from the server.");
            TroopsChanged?.Invoke(wrapper);
        }

        /// <summary>Client: raised after a faction's troops were replaced (the editor refreshes).</summary>
        public static event Action<WFaction> TroopsChanged;

        private static HashSet<string> IdsOf(FactionSaveData data)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            void Walk(TroopSaveData t)
            {
                if (t == null)
                    return;
                if (!string.IsNullOrEmpty(t.StringId))
                    ids.Add(t.StringId);
                Walk(t.Captain);
                foreach (var child in t.UpgradeTargets ?? [])
                    Walk(child);
            }

            foreach (
                var root in new[]
                {
                    data.RetinueElite,
                    data.RetinueBasic,
                    data.RootElite,
                    data.RootBasic,
                    data.MilitiaMelee,
                    data.MilitiaMeleeElite,
                    data.MilitiaRanged,
                    data.MilitiaRangedElite,
                    data.CaravanGuard,
                    data.CaravanMaster,
                    data.Villager,
                }
            )
                Walk(root);
            return ids;
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ //
        //                         Helpers                        //
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ //

        internal static string ToArg(object value) =>
            value switch
            {
                null => "",
                bool b => b ? "1" : "0",
                float f => f.ToString("R", CultureInfo.InvariantCulture),
                IFormattable x => x.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString(),
            };

        private static void Notify(string text)
        {
            try
            {
                InformationManager.DisplayMessage(new InformationMessage(text, Colors.Yellow));
            }
            catch { }
        }
    }
}
