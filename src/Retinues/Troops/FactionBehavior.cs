using System;
using System.Collections.Generic;
using System.Linq;
using Retinues.Configuration;
using Retinues.Game;
using Retinues.Game.Wrappers;
using Retinues.Troops.Save;
using Retinues.Utils;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.ObjectSystem;

namespace Retinues.Troops
{
    /// <summary>
    /// Campaign behavior for saving and loading custom troop definitions.
    /// Handles serialization, event registration, and tree rebuilding for custom troops.
    /// </summary>
    [SafeClass]
    public class FactionBehavior : CampaignBehaviorBase
    {
        // Flag to reset culture troops on next save
        public static bool ResetCultureTroops = false;

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ //
        //                        Sync Data                       //
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ //

        // Legacy single slots: the main player's clan and kingdom. Still written so a save
        // opened in a build without per-faction troops keeps the main player's troops.
        private FactionSaveData _clanTroops;
        private FactionSaveData _kingdomTroops;

        // One entry per troop-owning clan / kingdom, keyed by FactionSaveData.FactionId.
        private List<FactionSaveData> _clanTroopsByFaction;
        private List<FactionSaveData> _kingdomTroopsByFaction;

        private List<FactionSaveData> _cultureTroops;
        private List<FactionSaveData> _minorClanTroops;

        /// <summary>
        /// Syncs custom troop data to and from the campaign save file.
        /// </summary>
        public override void SyncData(IDataStore ds)
        {
            ds.SyncData("Retinues_ClanTroops", ref _clanTroops);
            ds.SyncData("Retinues_KingdomTroops", ref _kingdomTroops);
            ds.SyncData("Retinues_ClanTroopsByFaction", ref _clanTroopsByFaction);
            ds.SyncData("Retinues_KingdomTroopsByFaction", ref _kingdomTroopsByFaction);
            ds.SyncData("Retinues_CultureTroops", ref _cultureTroops);
            ds.SyncData("Retinues_MinorClanTroops", ref _minorClanTroops);

            if (ds.IsSaving)
            {
                // Backup troop data before saving
                TroopImportExport.MakeBackup();
            }
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ //
        //                    Event Registration                  //
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ //

        public override void RegisterEvents()
        {
            // Save roots before saving
            CampaignEvents.OnBeforeSaveEvent.AddNonSerializedListener(this, OnBeforeSave);

            // Load roots after loading
            CampaignEvents.OnGameLoadedEvent.AddNonSerializedListener(this, OnGameLoaded);

            // Possible clan unlock event
            CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, ClanUnlock);

            // Possible kingdom unlock event
            CampaignEvents.KingdomCreatedEvent.AddNonSerializedListener(this, KingdomUnlock);
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ //
        //                         Events                         //
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ //

        /* ━━━━━━━━ Saving ━━━━━━━━ */

        /// <summary>
        /// Collects all defined custom troops before saving.
        /// </summary>
        internal void OnBeforeSave()
        {
            // Collect clan troops: every troop-owning clan, plus any clan whose wrapper still
            // carries troops (e.g. a co-op player's clan while its leader is offline).
            _clanTroopsByFaction = [];
            foreach (var clan in SavedClans())
            {
                var data = new FactionSaveData(WFaction.Get(clan));
                if (TroopOwners.IsOwnerClan(clan) || !data.IsEmpty)
                    _clanTroopsByFaction.Add(data);
            }

            // Collect kingdom troops. Clear stale data when no player leads the kingdom any
            // more (e.g. a rebel kingdom that was absorbed back into another faction);
            // otherwise the defunct faction's troops linger in the save and get re-applied
            // on every load, corrupting clan troops that reuse their stub ids.
            _kingdomTroopsByFaction = [];
            foreach (var kingdom in TroopOwners.Kingdoms)
                _kingdomTroopsByFaction.Add(new FactionSaveData(WFaction.Get(kingdom)));

            // Legacy slots for the main player.
            _clanTroops = Find(_clanTroopsByFaction, Clan.PlayerClan?.StringId);
            _kingdomTroops = Find(_kingdomTroopsByFaction, Clan.PlayerClan?.Kingdom?.StringId);

            if (ResetCultureTroops)
            {
                ResetCultureTroops = false;
                _cultureTroops = null;
                _minorClanTroops = null;

                // Dropping the saved data is not enough on its own: the culture trees are still
                // flagged as edited in memory, so the very next save re-captured them and the
                // reset appeared to do nothing. Clear those marks too, but keep the ones that
                // belong to the player's own clan/kingdom roots so their troops are never
                // dropped (the dialog promises they are left untouched).
                var keep = new HashSet<string>(StringComparer.Ordinal);
                foreach (var clan in TroopOwners.Clans)
                    CollectVanillaRootIds(WFaction.Get(clan), keep);
                foreach (var kingdom in TroopOwners.Kingdoms)
                    CollectVanillaRootIds(WFaction.Get(kingdom), keep);

                WCharacter.EditedVanillaRootIds.RemoveWhere(id => !keep.Contains(id));
                return;
            }

            if (Config.EnableGlobalEditor == true)
            {
                // Collect all base cultures
                var cultures =
                    MBObjectManager.Instance.GetObjectTypeList<CultureObject>()?.ToList() ?? [];

                // Initialize culture troops
                _cultureTroops = [];

                // Save each culture's troop data
                foreach (var culture in cultures)
                    _cultureTroops.Add(new FactionSaveData(new WCulture(culture)));

                // Initialize minor clan troops
                _minorClanTroops = [];

                // Save each minor clan's troop data
                foreach (var clan in Clan.All)
                    if (clan.IsMinorFaction)
                        _minorClanTroops.Add(new FactionSaveData(new WClan(clan)));
            }
        }

        /* ━━━━━━━━ Loading ━━━━━━━ */

        /// <summary>
        /// Loads and rebuilds all custom troop trees after a save is loaded.
        /// </summary>
        private void OnGameLoaded(CampaignGameStarter _)
        {
            // Support diagnostic: the effective value decides whether culture troop data is
            // captured/applied at all, and reports of "the switch does nothing" are resolved by
            // reading this line from the log.
            Log.Info($"Global editor enabled: {(bool)Config.EnableGlobalEditor}");

            // Restore culture reset flag
            ResetCultureTroops = false;

            // NOTE: do NOT pre-register the save data's custom ids in ActiveStubIds here.
            // It looks like a safe defense (so a failed restore can't leave troops unregistered
            // for the sanitizer to eat), but the allocating WCharacter constructors go through
            // AllocateStub, which only honors a requested id when it is NOT yet active — a
            // pre-registered id would make every root and upgrade child reallocate onto a
            // different free stub, scrambling the whole tree. Per-node registration during
            // Apply plus the per-child/per-node exception isolation in TroopSaveData is the
            // correct protection.

            // Saves made before troops were stored per faction hold the main player's clan
            // and kingdom in the single legacy slots.
            MigrateLegacySlots();

            // Rebuild clan troops
            var loadedClanIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var data in _clanTroopsByFaction ?? [])
            {
                var clan = Clan.All.FirstOrDefault(c => c.StringId == data?.FactionId);
                if (clan is null || clan.IsEliminated)
                {
                    Log.Info($"Skipping troop data for missing clan '{data?.FactionId}'.");
                    continue;
                }

                data.Apply(WFaction.Get(clan));
                loadedClanIds.Add(clan.StringId);
            }

            // Rebuild kingdom troops only for kingdoms still ruled by a clan with troops.
            // Applying with a null faction would fall into the faction-less culture path,
            // which writes stale troop data onto free stubs (without claiming them) and
            // corrupts clan troops that later reuse those stub ids. Drop the data so it is
            // not re-saved either. The ruling-clan test does not depend on who is online, so
            // it gives the same answer on a co-op server before players have joined.
            var keptKingdoms = new List<FactionSaveData>();
            foreach (var data in _kingdomTroopsByFaction ?? [])
            {
                var kingdom = Kingdom.All.FirstOrDefault(k => k.StringId == data?.FactionId);
                if (
                    kingdom is null
                    || kingdom.IsEliminated
                    || kingdom.RulingClan is null
                    || !loadedClanIds.Contains(kingdom.RulingClan.StringId)
                )
                {
                    Log.Info($"Dropping stale troop data for kingdom '{data?.FactionId}'.");
                    continue;
                }

                data.Apply(WFaction.Get(kingdom));
                keptKingdoms.Add(data);
            }
            _kingdomTroopsByFaction = keptKingdoms;

            if (Config.EnableGlobalEditor == true)
            {
                // Rebuild culture troops
                if (_cultureTroops is not null)
                    foreach (FactionSaveData data in _cultureTroops)
                        data.Apply();

                // Rebuild minor clan troops
                if (_minorClanTroops is not null)
                    foreach (FactionSaveData data in _minorClanTroops)
                        data.Apply();
            }
        }

        /// <summary>
        /// Collects the root ids of a faction's vanilla troops, so a culture-wide reset can spare
        /// the marks that belong to the player's own factions.
        /// </summary>
        private static void CollectVanillaRootIds(WFaction faction, HashSet<string> into)
        {
            if (faction is null || into is null)
                return;

            void Add(WCharacter troop)
            {
                if (troop is null || !troop.IsVanilla)
                    return; // Custom troops are always persisted, marks do not apply to them.

                var id = troop.Root?.StringId;
                if (!string.IsNullOrEmpty(id))
                    into.Add(id);
            }

            Add(faction.RetinueElite);
            Add(faction.RetinueBasic);
            Add(faction.RootElite);
            Add(faction.RootBasic);
            Add(faction.MilitiaMelee);
            Add(faction.MilitiaMeleeElite);
            Add(faction.MilitiaRanged);
            Add(faction.MilitiaRangedElite);
            Add(faction.CaravanGuard);
            Add(faction.CaravanMaster);
            Add(faction.Villager);
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ //
        //                       Maintenance                      //
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ //

        /// <summary>
        /// Drops saved kingdom troop data when the player no longer leads a kingdom (e.g. a
        /// rebel kingdom that was absorbed). Returns true if stale data was discarded.
        /// Used by the scrub cheat to clean a corrupted save without waiting for a reload.
        /// </summary>
        public bool ClearStaleKingdomData()
        {
            if (_kingdomTroopsByFaction is null)
                return false;

            var owned = new HashSet<string>(
                TroopOwners.Kingdoms.Select(k => k.StringId),
                StringComparer.Ordinal
            );
            var removed = _kingdomTroopsByFaction.RemoveAll(d => !owned.Contains(d?.FactionId));
            if (removed == 0)
                return false;

            _kingdomTroops = Find(_kingdomTroopsByFaction, Clan.PlayerClan?.Kingdom?.StringId);
            Log.Info($"Scrub: discarded stale troop data for {removed} kingdom(s).");
            return true;
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ //
        //                         Helpers                        //
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ //

        /// <summary>
        /// Moves the legacy single clan/kingdom slots into the per-faction lists, as the
        /// main player's entries. Runs only for saves without per-faction data.
        /// </summary>
        internal void MigrateLegacySlots()
        {
            if (_clanTroopsByFaction is not null || _kingdomTroopsByFaction is not null)
                return; // Already per faction; the legacy slots are only a mirror.

            _clanTroopsByFaction = [];
            _kingdomTroopsByFaction = [];

            var clan = Clan.PlayerClan;
            if (_clanTroops is not null && clan is not null)
            {
                _clanTroops.FactionId = clan.StringId;
                _clanTroopsByFaction.Add(_clanTroops);
                Log.Info($"Migrated legacy clan troop data to clan '{clan.StringId}'.");
            }

            // Upstream applied kingdom data only while the main hero led a kingdom.
            var kingdom = Hero.MainHero?.IsKingdomLeader == true ? clan?.Kingdom : null;
            if (_kingdomTroops is not null && kingdom is not null)
            {
                _kingdomTroops.FactionId = kingdom.StringId;
                _kingdomTroopsByFaction.Add(_kingdomTroops);
                Log.Info($"Migrated legacy kingdom troop data to kingdom '{kingdom.StringId}'.");
            }
        }

        /// <summary>
        /// Clans whose troops are considered for saving: troop owners plus every major clan,
        /// so a clan that still carries troops (e.g. a co-op player who is offline) keeps them.
        /// </summary>
        private static IEnumerable<Clan> SavedClans() =>
            TroopOwners
                .Clans.Concat(Clan.All.Where(c => !c.IsEliminated && !c.IsMinorFaction))
                .Distinct();

        private static FactionSaveData Find(List<FactionSaveData> list, string factionId) =>
            factionId is null ? null : list?.FirstOrDefault(d => d?.FactionId == factionId);

        /* ━━━━━ Fief Acquired ━━━━ */

        void ClanUnlock(
            Settlement s,
            bool _,
            Hero n,
            Hero o,
            Hero __,
            ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail d
        )
        {
            var clan = WFaction.Get(n?.Clan);
            if (clan?.IsPlayerClan != true)
                return; // Not a player clan gaining a fief

            Log.Debug($"Fief acquired: {s.Name}, ensuring troops exist.");
            TroopBuilder.EnsureTroopsExist(clan);
        }

        /* ━━━━━ Kingdom Created ━━━ */

        void KingdomUnlock(Kingdom k)
        {
            var kingdom = WFaction.Get(k);
            if (kingdom?.IsPlayerKingdom != true)
                return; // Not a player kingdom

            Log.Debug($"Kingdom created: {k.Name}, ensuring troops exist.");
            TroopBuilder.EnsureTroopsExist(kingdom);
        }
    }
}
