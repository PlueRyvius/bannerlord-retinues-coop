using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Retinues.Configuration;
using Retinues.Game;
using Retinues.Game.Wrappers;
using Retinues.Troops;
using Retinues.Utils;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;

namespace Retinues.Mods.Coop
{
    /// <summary>
    /// Co-op server: Recruit Anywhere. In single player Retinues swaps a foreign settlement's
    /// volunteers to the player's clan troops while the player looks at them. In Coop the
    /// volunteers belong to everyone's world, and Coop hires on the server by checking each recruit
    /// against the notable's own volunteer slot, so the screen keeps showing culture troops. Instead,
    /// right after Coop hires for a player, the culture troops just hired outside that player's own
    /// fiefs become their clan's matching troops, under the same settings single player uses.
    /// Coop's hire handler is internal, so it is found by name; if a Coop update renames it, the
    /// hook logs that and recruits stay culture troops.
    /// </summary>
    [SafeClass]
    public static class CoopRecruits
    {
        private const string HandlerType =
            "GameInterface.Services.TroopRosters.Interfaces.TroopRosterInterface";
        private const string HandlerMethod = "HandleOnRecruitmentDone";

        private static readonly Random Rng = new();
        private static bool _tried;

        /// <summary>Server: hooks Coop's hire handler once.</summary>
        public static void EnsurePatched()
        {
            if (_tried)
                return;
            _tried = true;
            try
            {
                var method = AccessTools.Method(AccessTools.TypeByName(HandlerType), HandlerMethod);
                if (method == null)
                {
                    Log.Warn(
                        $"Co-op: Coop's recruitment handler ({HandlerType}.{HandlerMethod}) was not found; players recruit culture troops outside their fiefs."
                    );
                    return;
                }
                new Harmony("Retinues.CoopRecruits").Patch(
                    method,
                    prefix: new HarmonyMethod(typeof(CoopRecruits), nameof(Before)),
                    postfix: new HarmonyMethod(typeof(CoopRecruits), nameof(After))
                );
                Log.Info("Co-op: Recruit Anywhere hooked into Coop's recruitment.");
            }
            catch (Exception e)
            {
                Log.Exception(e, "Co-op: hooking Coop's recruitment failed.");
            }
        }

        private static void Before(
            object __instance,
            string mobilePartyId,
            out Dictionary<CharacterObject, int> __state
        )
        {
            __state = null;
            try
            {
                var party = Find(__instance, mobilePartyId);
                Log.Debug(
                    $"Co-op: recruitment for '{mobilePartyId}' (party found: {party != null})."
                );
                if (party != null)
                    __state = Counts(party.MemberRoster);
            }
            catch (Exception e)
            {
                Log.Exception(e, "Co-op: reading a party before recruitment failed.");
            }
        }

        private static void After(
            object __instance,
            string mobilePartyId,
            Dictionary<CharacterObject, int> __state
        )
        {
            if (__state == null)
                return;
            try
            {
                var party = Find(__instance, mobilePartyId);
                if (party == null)
                {
                    Log.Debug(
                        $"Co-op: recruits for unknown party '{mobilePartyId}' left as hired."
                    );
                    return;
                }
                var hired = new Dictionary<CharacterObject, int>();
                foreach (var kv in Counts(party.MemberRoster))
                {
                    __state.TryGetValue(kv.Key, out var before);
                    if (kv.Value > before)
                        hired[kv.Key] = kv.Value - before;
                }
                if (hired.Count > 0)
                    ConvertRecruits(party, hired);
            }
            catch (Exception e)
            {
                Log.Exception(e, "Co-op: converting recruits failed.");
            }
        }

        /// <summary>
        /// Turns culture troops a player just hired outside their own fiefs into their clan's
        /// matching troops (or, mixed in as in single player, their kingdom's).
        /// </summary>
        internal static void ConvertRecruits(
            MobileParty party,
            Dictionary<CharacterObject, int> hired
        )
        {
            if (Config.RestrictToOwnedSettlements)
            {
                Log.Debug("Co-op: Recruit Anywhere is off; recruits left as hired.");
                return;
            }
            var hero = TroopOwners.PlayerOf(party);
            var settlement = party.CurrentSettlement;
            if (hero == null || settlement == null)
            {
                Log.Debug(
                    $"Co-op: recruits of {party.Name} left as hired (player hero: {hero?.Name}, in: {settlement?.Name})."
                );
                return;
            }

            using (Player.ActAs(hero))
            {
                var clan = Player.Clan;
                var kingdom = Player.Kingdom;
                if (clan == null)
                    return;

                // In the player's own fiefs volunteers are already their troops (daily swap).
                var where = new WSettlement(settlement);
                if (
                    where.PlayerFaction == clan
                    || (kingdom != null && where.PlayerFaction == kingdom)
                )
                {
                    Log.Debug(
                        $"Co-op: {settlement.Name} is {hero.Name}'s own; recruits left as hired."
                    );
                    return;
                }
                if (
                    Config.RestrictToSameCultureSettlements
                    && clan.Culture?.StringId != where.Culture?.StringId
                )
                    return;

                int converted = 0;
                foreach (var kv in hired)
                {
                    var troop = new WCharacter(kv.Key);
                    if (troop.IsHero || troop.IsCustom)
                        continue;
                    for (int i = 0; i < kv.Value; i++)
                    {
                        if (Rng.NextDouble() >= Config.CustomVolunteersProportion)
                            continue; // stays a culture troop, as in the single-player mix
                        var target =
                            kingdom != null
                            && Rng.NextDouble() < Config.KingdomVolunteersInClanFiefsProportion
                                ? kingdom
                                : clan;
                        var replacement = TroopMatcher.PickBestFromFaction(
                            target,
                            troop,
                            sameTierOnly: false
                        );
                        if (replacement?.Base == null)
                        {
                            Log.Debug($"Co-op: no troop of {target.Name} matches {troop.Name}.");
                            continue;
                        }
                        party.MemberRoster.AddToCounts(kv.Key, -1);
                        party.MemberRoster.AddToCounts(replacement.Base, 1);
                        converted++;
                    }
                }
                if (converted > 0)
                    Log.Info(
                        $"Co-op: {converted} recruit(s) of {hero.Name} in {settlement.Name} joined as {clan.Name}'s troops."
                    );
            }
        }

        // Coop names parties by its own ids (the player's party is "MobileParty_Player"), so the
        // handler's own object manager resolves them, as Coop does.
        private static MobileParty Find(object handler, string id)
        {
            if (id == null)
                return null;
            var objects =
                handler == null
                    ? null
                    : AccessTools.Field(handler.GetType(), "objectManager")?.GetValue(handler);
            var lookup = objects
                ?.GetType()
                .GetInterfaces()
                .Concat([objects.GetType()])
                .SelectMany(t => t.GetMethods())
                .FirstOrDefault(m =>
                    m.Name == "TryGetObject"
                    && m.IsGenericMethodDefinition
                    && m.GetParameters().Length == 2
                    && m.GetParameters()[0].ParameterType == typeof(string)
                );
            if (lookup != null)
            {
                var args = new object[] { id, null };
                if ((bool)lookup.MakeGenericMethod(typeof(MobileParty)).Invoke(objects, args))
                    return args[1] as MobileParty;
            }
            return MobileParty.All.FirstOrDefault(p => p.StringId == id);
        }

        private static Dictionary<CharacterObject, int> Counts(TroopRoster roster)
        {
            var counts = new Dictionary<CharacterObject, int>();
            if (roster == null)
                return counts;
            for (int i = 0; i < roster.Count; i++)
            {
                var e = roster.GetElementCopyAtIndex(i);
                if (e.Character == null)
                    continue;
                counts[e.Character] = counts.TryGetValue(e.Character, out var c)
                    ? c + e.Number
                    : e.Number;
            }
            return counts;
        }
    }
}
