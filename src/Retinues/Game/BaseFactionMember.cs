using Retinues.Configuration;
using Retinues.Game.Wrappers;
using Retinues.Utils;

namespace Retinues.Game
{
    /// <summary>
    /// Abstract base for faction-related wrappers, provides access to clan, kingdom, and player faction logic.
    /// </summary>
    [SafeClass]
    public abstract class BaseFactionMember : StringIdentifier
    {
        public abstract WFaction Clan { get; }
        public abstract WFaction Kingdom { get; }

        /// <summary>
        /// Gets the player faction, preferring clan or kingdom based on config.
        /// </summary>
        public WFaction PlayerFaction
        {
            get
            {
                // Clan and Kingdom come from the faction registry, so these are the shared
                // wrappers that carry the troop attributes.
                var clan = Clan;
                clan = clan?.IsPlayerClan == true ? clan : null;
                var kingdom = Kingdom;
                kingdom = kingdom?.IsPlayerKingdom == true ? kingdom : null;

                if (Config.DisableKingdomTroops)
                    // If the settlement is in a player's kingdom but belongs to another clan,
                    // still return the ruling player's clan so volunteers are treated as
                    // player-sphere and swapped to clan troops (not left as vanilla
                    // kingdom/culture troops).
                    return clan ?? RulerClanOf(kingdom);

                return clan ?? kingdom;
            }
        }

        /// <summary>
        /// The ruling clan of a player kingdom, as its registry wrapper.
        /// </summary>
        private static WFaction RulerClanOf(WFaction kingdom) =>
            kingdom?.Base is TaleWorlds.CampaignSystem.Kingdom k
                ? WFaction.Get(k.RulingClan)
                : null;
    }
}
