using System;
using System.Collections.Generic;
using System.Linq;
using Retinues.Game;
using Retinues.Game.Wrappers;
using Retinues.GUI.Helpers;
using Retinues.Managers;
using Retinues.Mods.Coop;
using Retinues.Utils;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;

namespace Retinues.Features.AutoJoin
{
    /// <summary>
    /// Tracks per-retinue hire caps, accumulates renown toward the next hire,
    /// and spawns queued retinues into the main party on the daily tick.
    /// </summary>
    [SafeClass]
    public class AutoJoinBehavior : CampaignBehaviorBase
    {
        public static AutoJoinBehavior Instance { get; private set; }

        public AutoJoinBehavior() => Instance = this;

        private static readonly Random rng = new();

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ //
        //                        Sync Data                       //
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ //

        private Dictionary<string, int> _caps = [];

        // Renown bookkeeping of the main player's clan.
        private float _renownReserve;
        private float _lastRenown;

        // The same for every other player clan (co-op), keyed by clan id. Saved in hundredths
        // of renown, since Dictionary<string, int> is already a known save container.
        private Dictionary<string, int> _otherReserveCenti = [];
        private Dictionary<string, int> _otherLastRenownCenti = [];

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("Retinues_RetinueHire_Caps", ref _caps);
            dataStore.SyncData("Retinues_RetinueHire_RenownReserve", ref _renownReserve);
            dataStore.SyncData("Retinues_RetinueHire_LastRenown", ref _lastRenown);
            dataStore.SyncData("Retinues_RetinueHire_ReserveByClan", ref _otherReserveCenti);
            dataStore.SyncData("Retinues_RetinueHire_LastRenownByClan", ref _otherLastRenownCenti);

            if (dataStore.IsLoading)
                if (_lastRenown == 0)
                    _lastRenown = Player.Renown;

            _caps ??= [];
            _otherReserveCenti ??= [];
            _otherLastRenownCenti ??= [];
        }

        private bool IsMainClan(Clan clan) => clan == null || clan == Clan.PlayerClan;

        private float GetReserve(Clan clan) =>
            IsMainClan(clan) ? _renownReserve
            : _otherReserveCenti.TryGetValue(clan.StringId, out var v) ? v / 100f
            : 0f;

        private void SetReserve(Clan clan, float value)
        {
            if (IsMainClan(clan))
                _renownReserve = value;
            else
                _otherReserveCenti[clan.StringId] = (int)Math.Round(value * 100f);
        }

        private float GetLastRenown(Clan clan)
        {
            if (IsMainClan(clan))
                return _lastRenown;

            // First tick for this clan: start counting from its current renown.
            return _otherLastRenownCenti.TryGetValue(clan.StringId, out var v)
                ? v / 100f
                : clan.Renown;
        }

        private void SetLastRenown(Clan clan, float value)
        {
            if (IsMainClan(clan))
                _lastRenown = value;
            else
                _otherLastRenownCenti[clan.StringId] = (int)Math.Round(value * 100f);
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ //
        //                    Event Registration                  //
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ //

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickPartyEvent.AddNonSerializedListener(this, OnDailyTickParty);
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ //
        //                         Events                         //
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ //

        private void OnDailyTickParty(MobileParty party)
        {
            if (CoopState.IsClient)
                return; // Co-op: the server runs this for everyone

            var hero = TroopOwners.PlayerOf(party);
            if (hero == null)
                return; // Not a player's party

            // Player.* below resolves to this party's player.
            using (Player.ActAs(hero))
                HireRetinues(party, hero.Clan);
        }

        private void HireRetinues(MobileParty party, Clan clan)
        {
            var retinues = GetHireableRetinues();
            var reserve = GetReserve(clan);
            Log.Info(
                $"[RetinueHire] Hireable: {retinues.Count}, Renown: {Player.Renown}, Reserve: {reserve}"
            );

            if (retinues.Count == 0)
            {
                SetReserve(clan, 0);
                return;
            }

            var renownGained = Player.Renown - GetLastRenown(clan);
            SetLastRenown(clan, Player.Renown);
            if (renownGained > 0)
                reserve += renownGained;

            try
            {
                Hire(party, retinues, ref reserve);
            }
            finally
            {
                SetReserve(clan, reserve);
            }
        }

        private static void Hire(MobileParty party, List<WCharacter> retinues, ref float reserve)
        {
            var wparty = new WParty(party);
            var space = Math.Max(0, wparty.PartySizeLimit - wparty.MemberRoster.Count);
            if (space <= 0)
                return;

            // Snapshot counts before adding, so the message reflects what actually stuck
            var before = new Dictionary<string, int>();
            foreach (var r in retinues)
                before[r.StringId] = wparty.MemberRoster.CountOf(r);

            // Max attempt (safety in case something goes wrong)
            var maxAttempts = 1000;
            var budget = reserve;
            // Attempt hires
            while (space > 0 && maxAttempts-- > 0)
            {
                var affordable = retinues
                    .Where(r =>
                    {
                        if (GetCountOf(r) >= GetJoinCap(r))
                            return false;
                        var cost = RetinueManager.RenownRequiredPerUnit(r);
                        return cost > 0 && cost <= budget;
                    })
                    .ToList();

                if (affordable.Count == 0)
                    break;

                var retinue = affordable[rng.Next(affordable.Count)];
                var unitCost = RetinueManager.RenownRequiredPerUnit(retinue);

                reserve -= unitCost;
                budget = reserve;
                wparty.MemberRoster.AddTroop(retinue, 1);
                space--;
            }

            // Compute effective deltas
            var deltas = new Dictionary<WCharacter, int>();
            foreach (var r in retinues)
            {
                var after = wparty.MemberRoster.CountOf(r);
                var prev = before.TryGetValue(r.StringId, out var b) ? b : 0;
                var delta = after - prev;
                if (delta > 0)
                    deltas[r] = delta;
            }

            if (deltas.Count > 0)
                ShowUnlockMessage(deltas);
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ //
        //                       Public API                       //
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ //

        public static int GetJoinCap(WCharacter troop)
        {
            if (troop == null || !troop.IsRetinue)
                return 0;
            if (Instance._caps.TryGetValue(troop.StringId, out var cap))
                return cap;
            return 0;
        }

        public static void SetJoinCap(WCharacter troop, int cap)
        {
            if (troop == null || !troop.IsRetinue)
                return;
            if (CoopBridge.Relay(CoopActions.JoinCap, troop.StringId, cap))
                return;
            if (cap <= 0)
                Instance._caps.Remove(troop.StringId);
            else
                Instance._caps[troop.StringId] = cap;
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ //
        //                         Helpers                        //
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ //

        private static int GetCountOf(WCharacter troop)
        {
            if (troop == null || !troop.IsRetinue)
                return 0;
            var wparty = Player.Party;
            return wparty?.MemberRoster?.CountOf(troop) ?? 0;
        }

        private static List<WCharacter> GetHireableRetinues()
        {
            // Copy first to avoid mutating the live list
            var retinues = new List<WCharacter>(Player.Clan.RetinueTroops);

            if (Player.Kingdom != null)
                retinues.AddRange(Player.Kingdom.RetinueTroops);

            // De-dup on StringId
            var unique = retinues
                .Where(r => r != null)
                .GroupBy(r => r.StringId)
                .Select(g => g.First())
                .ToList();

            return [.. unique.Where(r => GetJoinCap(r) > GetCountOf(r))];
        }

        private static void ShowUnlockMessage(Dictionary<WCharacter, int> hires)
        {
            var joined = string.Join(
                ", ",
                hires.Where(h => h.Key != null).Select(h => $"{h.Value} {h.Key.Name}")
            );

            var description = L.T(
                    "retinue_hire_inquiry_body",
                    "The following retinues have joined your party: {JOINED}."
                )
                .SetTextVariable("JOINED", joined);

            Notifications.Log(description, "#c7f5caff");
        }
    }
}
