using System;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Retinues.Game;
using Retinues.Utils;
using TaleWorlds.CampaignSystem;

namespace Retinues.Mods.Coop
{
    /// <summary>
    /// Reads Bannerlord Coop's state by reflection, so the main assembly never compiles against Coop:
    /// - GameInterface.ContainerProvider._active (Coop 0.1.7) or _lifetimeScope (0.1.6) is set while a session runs.
    /// - Common.ModInformation.IsServer tells the dedicated server from a player's game (the server's bin folder is
    ///   a backup, since IsServer can be set late).
    /// - GameInterface.Services.Heroes.Extensions.HeroExtensions.IsPlayerHero(Hero) answers "is this a player?".
    /// If a future Coop renames the session state, this assumes a session on a player's game, the safe side: that
    /// game then changes nothing on its own.
    /// </summary>
    [SafeClass]
    public static class CoopState
    {
        private static bool _modulesLoaded;
        private static FieldInfo _scope;
        private static PropertyInfo _isServer;
        private static MethodInfo _isPlayerHero;

        /// <summary>True once it is known whether Coop is loaded.</summary>
        public static bool Resolved { get; private set; }

        /// <summary>True when Coop is loaded in this game.</summary>
        public static bool Loaded { get; private set; }

        /// <summary>True while a co-op session runs.</summary>
        public static bool InSession
        {
            get
            {
                Resolve();
                if (!Loaded)
                    return false;
                if (_scope == null)
                    return true;
                try
                {
                    return _scope.GetValue(null) != null;
                }
                catch
                {
                    return true;
                }
            }
        }

        /// <summary>True on the dedicated server (in or out of a session).</summary>
        public static bool IsServerProcess
        {
            get
            {
                Resolve();
                try
                {
                    if (
                        Directory
                            .GetCurrentDirectory()
                            .EndsWith("Win64_Shipping_Server", StringComparison.OrdinalIgnoreCase)
                    )
                        return true;
                    return _isServer != null && (bool)_isServer.GetValue(null, null);
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>A player's game in a co-op session: it shows the campaign but changes nothing itself.</summary>
        public static bool IsClient => InSession && !IsServerProcess;

        /// <summary>The server of a co-op session: it runs Retinues for everyone.</summary>
        public static bool IsServer => InSession && IsServerProcess;

        /// <summary>Called once modules are loaded (main menu, or the server's first tick).</summary>
        public static void ModulesLoaded()
        {
            _modulesLoaded = true;
            Resolve();
        }

        /// <summary>True if Coop says a human player controls the hero.</summary>
        public static bool IsPlayerHero(Hero hero)
        {
            Resolve();
            if (hero == null || _isPlayerHero == null)
                return false;
            try
            {
                return (bool)_isPlayerHero.Invoke(null, [hero]);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Keeps TroopOwners in step with the session: while one runs, every player hero's clan and kingdom own
        /// troops; otherwise single-player rules apply. Called every application tick.
        /// </summary>
        public static void Tick()
        {
            var want = InSession && _isPlayerHero != null;
            var hooked = TroopOwners.IsPlayerHeroOverride == (Func<Hero, bool>)OwnerHook;
            if (want == hooked)
                return;

            TroopOwners.IsPlayerHeroOverride = want ? OwnerHook : null;
            Log.Info(
                want
                    ? "Coop session: every player's clan and kingdom own their own troops."
                    : "No Coop session: single-player troop ownership."
            );
        }

        // Cached delegate so the hook can be recognised by reference.
        private static readonly Func<Hero, bool> OwnerHook = IsPlayerHeroOrMain;

        // The main hero stays an owner: on a player's game it is that player, and on the server it is the save's
        // original hero, whose troops a single-player save brought along.
        private static bool IsPlayerHeroOrMain(Hero hero) =>
            hero != null && (hero == Hero.MainHero || IsPlayerHero(hero));

        private static void Resolve()
        {
            if (Resolved)
                return;

            Type container = null,
                info = null,
                heroes = null;
            var coop = false;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                string name;
                try
                {
                    name = assembly.GetName().Name ?? "";
                }
                catch
                {
                    continue;
                }

                if (name == "GameInterface")
                {
                    coop = true;
                    container ??= assembly.GetType("GameInterface.ContainerProvider", false);
                    heroes ??= assembly.GetType(
                        "GameInterface.Services.Heroes.Extensions.HeroExtensions",
                        false
                    );
                }
                else if (
                    name == "Coop.Core"
                    || (name == "Coop" && assembly.GetType("Coop.CoopMod", false) != null)
                )
                {
                    coop = true;
                }

                info ??= assembly.GetType("Common.ModInformation", false);
            }

            if (!coop)
            {
                // Before the main menu Coop may not be loaded yet; look again later. The dedicated server runs the
                // setup on its first tick, which can come before Coop has loaded, so "no Coop" is final only once a
                // campaign exists.
                if (_modulesLoaded && Campaign.Current != null)
                    Resolved = true;
                return;
            }

            Loaded = true;
            _scope =
                container == null
                    ? null
                    : AccessTools.Field(container, "_active")
                        ?? AccessTools.Field(container, "_lifetimeScope");
            _isServer = info == null ? null : AccessTools.Property(info, "IsServer");
            _isPlayerHero =
                heroes == null ? null : AccessTools.Method(heroes, "IsPlayerHero", [typeof(Hero)]);

            if (_scope == null || _isServer == null)
                Log.Warn(
                    "Could not read Coop's session state; assuming a player's game in a session."
                );
            if (_isPlayerHero == null)
                Log.Warn("Could not read Coop's player list; only the main hero owns troops.");
            Resolved = true;
        }
    }
}
