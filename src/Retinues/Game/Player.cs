using System;
using System.Collections.Generic;
using System.Linq;
using Retinues.Game.Wrappers;
using Retinues.Utils;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Library;

namespace Retinues.Game
{
    /// <summary>
    /// Static helpers for accessing player-related game state and attributes.
    /// Provides wrappers for clan, kingdom, character, party, and resources.
    /// </summary>
    [SafeClass]
    public static class Player
    {
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ //
        //                          Reset                         //
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ //

        /// <summary>
        /// Resets cached player state (clan, culture, character, kingdom).
        /// </summary>
        public static void Reset()
        {
            _culture = null;
            _character = null;
            _acting = null;
            WFaction.ClearRegistry();
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ //
        //                      Acting Hero                       //
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ //

        private static Hero _acting;

        /// <summary>
        /// The hero "the player" refers to. This is the main hero, unless a multiplayer
        /// integration runs an action on behalf of another player's hero (see <see cref="ActAs"/>).
        /// </summary>
        public static Hero ActingHero => _acting ?? Hero.MainHero;

        /// <summary>
        /// True while an action runs on behalf of a hero other than the main hero.
        /// </summary>
        public static bool IsActingForOther => _acting != null && _acting != Hero.MainHero;

        /// <summary>
        /// Runs the code inside the returned scope as the given hero: every Player member
        /// (clan, kingdom, party, gold, influence...) resolves to that hero until disposed.
        /// </summary>
        public static IDisposable ActAs(Hero hero)
        {
            var previous = _acting;
            _acting = hero;
            return new ActingScope(previous);
        }

        private sealed class ActingScope(Hero previous) : IDisposable
        {
            private bool _disposed;

            public void Dispose()
            {
                if (_disposed)
                    return;
                _disposed = true;
                _acting = previous;
            }
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ //
        //                       Components                       //
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ //

        /// <summary>
        /// Returns the player's clan as a WFaction wrapper.
        /// </summary>
        public static WFaction Clan => WFaction.Get(ActingHero?.Clan);

        private static WCulture _culture;

        /// <summary>
        /// Returns the player's culture as a WCulture wrapper.
        /// </summary>
        public static WCulture Culture
        {
            get
            {
                // Cached for the main hero only; acting heroes are resolved each time.
                if (IsActingForOther)
                    return new WCulture(ActingHero.Culture);
                _culture ??= new WCulture(Hero.MainHero.Culture);
                return _culture;
            }
        }

        private static WCharacter _character;

        /// <summary>
        /// Returns the player's character as a WCharacter wrapper.
        /// </summary>
        public static WCharacter Character
        {
            get
            {
                if (IsActingForOther)
                    return new WCharacter(ActingHero.CharacterObject);
                _character ??= new WCharacter(Hero.MainHero.CharacterObject);
                return _character;
            }
        }

        /// <summary>
        /// Returns the player's main party as a WParty wrapper.
        /// </summary>
        public static WParty Party =>
            new(IsActingForOther ? ActingHero.PartyBelongedTo : MobileParty.MainParty);

        /// <summary>
        /// Returns the player's map faction.
        /// </summary>
        public static IFaction MapFaction => ActingHero.MapFaction;

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ //
        //                       Attributes                       //
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ //

        /// <summary>
        /// Returns the player's name.
        /// </summary>
        public static string Name => ActingHero.Name.ToString();

        /// <summary>
        /// Returns true if the player is female.
        /// </summary>
        public static bool IsFemale => ActingHero.IsFemale;

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ //
        //                         Renown                         //
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ //

        /// <summary>
        /// Returns the player's clan renown.
        /// </summary>
        public static float Renown => ActingHero.Clan.Renown;

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ //
        //                          Gold                          //
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ //

        /// <summary>
        /// Returns the player's gold.
        /// </summary>
        public static int Gold => ActingHero.Gold;

        /// <summary>
        /// Changes the player's gold by the given amount.
        /// </summary>
        public static void ChangeGold(int amount)
        {
            ActingHero.ChangeHeroGold(amount);
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ //
        //                        Influence                       //
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ //

        /// <summary>
        /// Returns the player's clan influence.
        /// </summary>
        public static int Influence
        {
            get
            {
                var clan = ActingHero?.Clan;
                if (clan == null)
                    return 0;
                return (int)clan.Influence;
            }
        }

        /// <summary>
        /// Changes the player's clan influence by the given amount.
        /// </summary>
        public static void ChangeInfluence(int amount)
        {
            var clan = ActingHero?.Clan;
            if (clan == null)
                return;
            clan.Influence = Math.Max(0f, Influence + amount);
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ //
        //                         Kingdom                        //
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ //

        /// <summary>
        /// Returns true if the player is a kingdom leader.
        /// </summary>
        public static bool IsKingdomLeader => ActingHero?.IsKingdomLeader == true;

        /// <summary>
        /// Returns the player's kingdom as a WFaction wrapper, if leader.
        /// </summary>
        public static WFaction Kingdom =>
            IsKingdomLeader ? WFaction.Get(ActingHero.Clan.Kingdom) : null;

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ //
        //                         Troops                         //
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ //

        /// <summary>
        /// Enumerates all custom troops for the player's clan and kingdom.
        /// </summary>
        public static IEnumerable<WCharacter> Troops
        {
            get
            {
                if (Clan != null)
                    foreach (var troop in Clan.Troops)
                        yield return troop;

                if (Kingdom != null)
                    foreach (var troop in Kingdom.Troops)
                        yield return troop;
            }
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ //
        //                          Army                          //
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ //

        /// <summary>
        /// Returns true if the player is the leader of their army.
        /// </summary>
        public static bool IsArmyLeader => Party.Army?.LeaderParty?.StringId == Party.StringId;

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ //
        //                       Settlement                       //
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ //

        public static WSettlement CurrentSettlement =>
            Party?.Base?.CurrentSettlement != null
                ? new WSettlement(Party.Base.CurrentSettlement)
                : null;

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ //
        //                        Commands                        //
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ //

        /// <summary>
        /// Lists the IDs of all player troops. Usage: retinues.list_custom_troops
        /// </summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("list_custom_troops", "retinues")]
        public static string ListCustomTroops(List<string> args)
        {
            var list = Troops.Select(t => $"{t.StringId}: {t.Name}").ToList();
            if (list.Count == 0)
                return "No active custom troops found.";

            return string.Join("\n", list);
        }
    }
}
