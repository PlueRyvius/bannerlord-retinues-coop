using System.Linq;
using Retinues.Game;
using Retinues.Game.Wrappers;
using Retinues.Troops.Save;
using TaleWorlds.CampaignSystem;

namespace Retinues.Tests.Cases
{
    /// <summary>
    /// Tests for per-faction troop ownership: single player must resolve exactly as before,
    /// and a multiplayer hook must give other players' clans their own troop owners.
    /// </summary>
    public static class MultiOwnerTests
    {
        /// <summary>
        /// Picks a living major clan that is not the main player's, led by a hero.
        /// </summary>
        private static Clan OtherClan() =>
            Clan.All.FirstOrDefault(c =>
                c != Clan.PlayerClan
                && !c.IsEliminated
                && !c.IsMinorFaction
                && !c.IsBanditFaction
                && c.Leader != null
            );

        [GameTest(
            "RegistrySharesWrapper",
            "owners",
            "WFaction.Get returns the same wrapper as Player.Clan"
        )]
        public static void RegistrySharesWrapper()
        {
            var viaPlayer = Player.Clan;
            var viaRegistry = WFaction.Get(Clan.PlayerClan);

            Tests.AssertTrue(
                ReferenceEquals(viaPlayer, viaRegistry),
                "Player.Clan and WFaction.Get(Clan.PlayerClan) are different instances."
            );
        }

        [GameTest(
            "SinglePlayerOwnersUnchanged",
            "owners",
            "Without a hook only the main player's clan owns troops"
        )]
        public static void SinglePlayerOwnersUnchanged()
        {
            Tests.AssertTrue(
                TroopOwners.IsPlayerHeroOverride == null,
                "A multiplayer hook is set in a single-player test run."
            );

            var owners = TroopOwners.Clans.ToList();
            Tests.AssertTrue(
                owners.Count == 1 && owners[0] == Clan.PlayerClan,
                $"Expected only the player clan as owner, got {owners.Count}."
            );

            var other = OtherClan();
            if (other != null)
                Tests.AssertTrue(
                    !TroopOwners.IsOwnerClan(other),
                    $"AI clan '{other.StringId}' counts as a troop owner."
                );
        }

        [GameTest(
            "HookAddsOwnerClan",
            "owners",
            "A multiplayer hook makes another player's clan a troop owner"
        )]
        public static void HookAddsOwnerClan()
        {
            var other = OtherClan();
            if (other == null)
                return; // No candidate clan in this campaign

            var previous = TroopOwners.IsPlayerHeroOverride;
            TroopOwners.IsPlayerHeroOverride = h => h == Hero.MainHero || h == other.Leader;
            try
            {
                Tests.AssertTrue(TroopOwners.IsOwnerClan(other), "Hooked clan is not an owner.");
                Tests.AssertTrue(
                    TroopOwners.IsOwnerClan(Clan.PlayerClan),
                    "Main player's clan stopped being an owner under the hook."
                );
                Tests.AssertTrue(
                    WFaction.Get(other).IsPlayerClan,
                    "Hooked clan's wrapper does not report IsPlayerClan."
                );
                Tests.AssertTrue(
                    TroopOwners.CanEdit(other.Leader, other)
                        && !TroopOwners.CanEdit(other.Leader, Clan.PlayerClan),
                    "CanEdit does not limit a player to their own clan."
                );

                var fief = other.Settlements.FirstOrDefault(s => s.IsTown || s.IsCastle);
                if (fief != null)
                    Tests.AssertTrue(
                        ReferenceEquals(new WSettlement(fief).PlayerFaction, WFaction.Get(other)),
                        "A hooked clan's fief does not resolve to that clan's troops."
                    );
            }
            finally
            {
                TroopOwners.IsPlayerHeroOverride = previous;
            }
        }

        [GameTest(
            "ActAsResolvesPlayer",
            "owners",
            "Player members follow the acting hero and restore afterwards"
        )]
        public static void ActAsResolvesPlayer()
        {
            var other = OtherClan();
            if (other == null)
                return;

            using (Player.ActAs(other.Leader))
            {
                Tests.AssertTrue(
                    Player.Clan == WFaction.Get(other),
                    "Player.Clan does not follow the acting hero."
                );
                Tests.AssertTrue(
                    Player.Gold == other.Leader.Gold,
                    "Player.Gold does not follow the acting hero."
                );
            }

            Tests.AssertTrue(
                Player.ActingHero == Hero.MainHero && Player.Clan == WFaction.Get(Clan.PlayerClan),
                "Player did not return to the main hero after the scope."
            );
        }

        [GameTest(
            "SaveDataCarriesFactionId",
            "owners",
            "Faction save data records which clan it belongs to"
        )]
        public static void SaveDataCarriesFactionId()
        {
            var data = new FactionSaveData(Player.Clan);
            Tests.AssertTrue(
                data.FactionId == Clan.PlayerClan.StringId,
                $"FactionId is '{data.FactionId}', expected '{Clan.PlayerClan.StringId}'."
            );
        }
    }
}
