using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Railroader.Modding;

namespace CollegeEducatedAutoEngineer
{
    /// <summary>
    /// Entry point for the game's own mod loader: Info.json lists this assembly under "assemblies", and the game
    /// creates this class and calls OnModLoad once at start-up. It must stay public with a parameterless constructor.
    /// </summary>
    public sealed class ModEntryPoint : IModEntryPoint
    {
        public void OnModLoad(IModContext context)
        {
            // 0Harmony.dll ships beside this assembly. Hook the resolver before any method that uses Harmony is
            // compiled, so it is found even if the runtime does not probe the mod folder by itself.
            string dir = context.ModDirectory;
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                string file = Path.Combine(dir, new AssemblyName(args.Name).Name + ".dll");
                return File.Exists(file) ? Assembly.LoadFrom(file) : null;
            };
            Main.Load(context);
        }

        public void OnModUnload()
        {
            Main.Shutdown();
        }
    }

    /// <summary>Logger with the method names the rest of the mod was written against (UMM's).</summary>
    internal sealed class ModLogger
    {
        private readonly IModLogger _log;

        internal ModLogger(IModLogger log)
        {
            _log = log;
        }

        public void Log(string message) => _log.Info(message);
        public void Warning(string message) => _log.Warning(message);
        public void Error(string message) => _log.Error(message);
        public void LogException(Exception e) => _log.Error(e, e.Message);
    }

    internal sealed class ModInfo
    {
        public string Id;
        public string Version;
    }

    /// <summary>What the mod knows about itself: log, version (from Info.json) and folder.</summary>
    internal sealed class ModHandle
    {
        public ModLogger Logger;
        public ModInfo Info;
        public string Path;
    }

    public static class Main
    {
        internal static ModHandle Mod;
        internal static Settings Settings;

        /// <summary>True while the mod is running. The fix and the samplers check this every tick.</summary>
        internal static bool Active;

        internal static string SettingsPath => Path.Combine(Mod.Path, "Settings.xml");

        private static HarmonyLib.Harmony _harmony;
        internal static HarmonyLib.Harmony Harmony => _harmony;
        private static UnityEngine.GameObject _hotkey;
        private static UnityEngine.GameObject _legacyLimits;

        internal static void Load(IModContext context)
        {
            Mod = new ModHandle
            {
                Logger = new ModLogger(context.Log),
                Info = new ModInfo { Id = context.ModIdentifier, Version = ReadVersion(context.ModDirectory) },
                Path = context.ModDirectory,
            };

            Settings = Settings.Load(SettingsPath, out string problem);
            if (problem != null) Mod.Logger.Warning("Settings: " + problem);

            _harmony = new HarmonyLib.Harmony(context.ModIdentifier);
            Active = SelfCheck.Run(context, _harmony);
            if (!Active) return;

            try
            {
                _hotkey = new UnityEngine.GameObject("CollegeEducatedAutoEngineer.Hotkey");
                UnityEngine.Object.DontDestroyOnLoad(_hotkey);
                _hotkey.AddComponent<SettingsHotkey>();
            }
            catch (Exception e)
            {
                Mod.Logger.Warning("Settings window hotkey could not be set up: " + e.Message);
            }

            // Puts this mod's settings page into the Mod Settings Hub when that is installed (found by reflection,
            // in either load order); without it the mod opens its own window as before.
            HubLink.Register(context.ModIdentifier, "College Educated Auto Engineer", Mod.Info.Version, SettingsUi.BuildPage);

            try
            {
                // Does nothing until the setting is on (the self-check switches the setting off if the game changed).
                _legacyLimits = new UnityEngine.GameObject("CollegeEducatedAutoEngineer.LegacySpeedLimits");
                UnityEngine.Object.DontDestroyOnLoad(_legacyLimits);
                _legacyLimits.AddComponent<LegacySpeedLimits>();
            }
            catch (Exception e)
            {
                Mod.Logger.Warning("Legacy speed limits could not be set up: " + e.Message);
            }

            // Engineers that already exist (none at start-up, normally) get their helpers now.
            foreach (var engineer in UnityEngine.Object.FindObjectsByType<Model.AI.AutoEngineer>(UnityEngine.FindObjectsSortMode.None))
            {
                AutoEngineer_OnEnable_Patch.Attach(engineer);
            }
        }

        internal static void Shutdown()
        {
            Active = false;
            HubLink.Unregister();
            if (_harmony != null)
            {
                _harmony.UnpatchAll(_harmony.Id);
                _harmony = null;
            }

            foreach (var sampler in UnityEngine.Object.FindObjectsByType<AutoEngineerSampler>(UnityEngine.FindObjectsSortMode.None))
            {
                UnityEngine.Object.Destroy(sampler);
            }
            foreach (var glider in UnityEngine.Object.FindObjectsByType<ThrottleGlider>(UnityEngine.FindObjectsSortMode.None))
            {
                UnityEngine.Object.Destroy(glider);
            }
            foreach (var stop in UnityEngine.Object.FindObjectsByType<WaterStop>(UnityEngine.FindObjectsSortMode.None))
            {
                UnityEngine.Object.Destroy(stop);
            }

            if (_legacyLimits != null)
            {
                UnityEngine.Object.Destroy(_legacyLimits);   // puts the cleared limits back (OnDestroy)
                _legacyLimits = null;
            }

            if (_hotkey != null)
            {
                UnityEngine.Object.Destroy(_hotkey);
                _hotkey = null;
            }

            LogWriter.Close();
            SignalLog.Close();
            FuelSummary.Close();
        }

        private static string ReadVersion(string modDirectory)
        {
            try
            {
                string json = File.ReadAllText(Path.Combine(modDirectory, "Info.json"));
                Match m = Regex.Match(json, "\"version\"\\s*:\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase);
                if (m.Success) return m.Groups[1].Value;
            }
            catch (Exception)
            {
            }
            return "?";
        }
    }
}
