using System;
using System.Collections.Generic;
using System.Linq;
using Retinues.Utils;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;

namespace Retinues.Game
{
    /// <summary>
    /// Decides which clans and kingdoms own custom troops.
    /// In single player that is the main hero's clan and, when the main hero rules one, their
    /// kingdom. A multiplayer integration (e.g. Bannerlord Coop) sets <see cref="IsPlayerHeroOverride"/>
    /// so every human player's clan and kingdom owns its own troops.
    /// </summary>
    [SafeClass]
    public static class TroopOwners
    {
        /// <summary>
        /// Optional hook: returns true for every hero controlled by a human player.
        /// Null means single player, where only the main hero counts.
        /// </summary>
        public static Func<Hero, bool> IsPlayerHeroOverride;

        /// <summary>
        /// True if the hero is controlled by a human player.
        /// </summary>
        public static bool IsPlayerHero(Hero hero)
        {
            if (hero == null)
                return false;

            var hook = IsPlayerHeroOverride;
            return hook != null ? hook(hero) : hero == Hero.MainHero;
        }

        /// <summary>
        /// True if the party is a human player's own party (the main party in single player).
        /// </summary>
        public static bool IsPlayerParty(MobileParty party)
        {
            if (party == null)
                return false;

            if (party.IsMainParty)
                return true;

            return IsPlayerHeroOverride != null
                && party.LeaderHero != null
                && party.LeaderHero.PartyBelongedTo == party
                && IsPlayerHero(party.LeaderHero);
        }

        /// <summary>
        /// The player hero a player party belongs to, or null if it is not a player party.
        /// </summary>
        public static Hero PlayerOf(MobileParty party)
        {
            if (!IsPlayerParty(party))
                return null;

            return party.IsMainParty ? Hero.MainHero : party.LeaderHero;
        }

        /// <summary>
        /// True if the clan owns custom clan troops.
        /// </summary>
        public static bool IsOwnerClan(Clan clan)
        {
            if (clan == null)
                return false;

            // Single player keeps the exact upstream rule.
            if (IsPlayerHeroOverride == null)
                return clan == Clan.PlayerClan;

            return !clan.IsEliminated && IsPlayerHero(clan.Leader);
        }

        /// <summary>
        /// True if the kingdom owns custom kingdom troops (its ruler is a player).
        /// </summary>
        public static bool IsOwnerKingdom(Kingdom kingdom)
        {
            if (kingdom == null || kingdom.IsEliminated)
                return false;

            return IsPlayerHero(kingdom.Leader);
        }

        /// <summary>
        /// The troop-owning clan a clan answers to: the clan itself if it owns troops,
        /// otherwise the ruling clan of its kingdom when a player rules that kingdom.
        /// </summary>
        public static Clan PlayerClanOver(Clan clan)
        {
            if (clan == null)
                return null;

            if (IsOwnerClan(clan))
                return clan;

            var kingdom = clan.Kingdom;
            return IsOwnerKingdom(kingdom) ? kingdom.RulingClan : null;
        }

        /// <summary>
        /// True if the hero may edit the troops of the faction: their own clan, or the
        /// kingdom they rule.
        /// </summary>
        public static bool CanEdit(Hero hero, IFaction faction)
        {
            if (hero?.Clan == null || faction == null)
                return false;

            return faction switch
            {
                Clan clan => clan == hero.Clan && IsOwnerClan(clan),
                Kingdom kingdom => kingdom.Leader == hero && IsOwnerKingdom(kingdom),
                _ => false,
            };
        }

        /// <summary>
        /// True if the faction (clan or kingdom) owns custom troops.
        /// </summary>
        public static bool IsOwner(IFaction faction) =>
            faction switch
            {
                Clan clan => IsOwnerClan(clan),
                Kingdom kingdom => IsOwnerKingdom(kingdom),
                _ => false,
            };

        /// <summary>
        /// Every clan that currently owns custom troops.
        /// </summary>
        public static IEnumerable<Clan> Clans
        {
            get
            {
                if (IsPlayerHeroOverride == null)
                    return Clan.PlayerClan != null ? [Clan.PlayerClan] : [];

                return Clan.All.Where(IsOwnerClan).ToList();
            }
        }

        /// <summary>
        /// Every kingdom that currently owns custom troops.
        /// </summary>
        public static IEnumerable<Kingdom> Kingdoms => Kingdom.All.Where(IsOwnerKingdom).ToList();
    }
}
