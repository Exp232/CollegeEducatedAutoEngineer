using System;
using System.Globalization;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Model;
using Model.AI;
using Model.Physics;
using UnityEngine;

namespace CollegeEducatedAutoEngineer
{
    /// <summary>
    /// Writes the live AutoEngineerConfig (including its curves) to a text file once per
    /// game session. The values in the decompiled source are only defaults; the asset can override them.
    /// </summary>
    internal static class ConfigDump
    {
        private static readonly FieldInfo FConfig = AccessTools.Field(typeof(AutoEngineer), "_config");
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static bool _done;

        internal static void DumpOnce(AutoEngineer ae)
        {
            if (_done) return;

            object config = FConfig?.GetValue(ae);
            if (config == null) return;
            _done = true;

            var sb = new StringBuilder();
            sb.AppendLine("CollegeEducatedAutoEngineer config dump");
            sb.AppendLine("Time: " + DateTime.Now.ToString("s"));
            sb.AppendLine("Game version: " + Application.version);
            sb.AppendLine("Mod version: " + Main.Mod.Info.Version);
            sb.AppendLine();

            Dump(sb, config, "AutoEngineerConfig", 0);
            LogWriter.WriteTextFile("config_dump", sb.ToString());
        }

        private static readonly System.Collections.Generic.HashSet<string> EngineDumped = new System.Collections.Generic.HashSet<string>();

        /// <summary>Writes a steam locomotive's engine numbers to their own file, once per locomotive per session.</summary>
        internal static void DumpEngineOnce(AutoEngineer ae)
        {
            var steam = ae.Locomotive as SteamLocomotive;
            if (steam == null || steam.engine == null) return;
            string name = steam.DisplayName ?? "unknown";
            if (!EngineDumped.Add(name)) return;

            var sb = new StringBuilder();
            sb.AppendLine("CollegeEducatedAutoEngineer steam engine dump");
            sb.AppendLine("Time: " + DateTime.Now.ToString("s"));
            sb.AppendLine("Mod version: " + Main.Mod.Info.Version);
            DumpSteamEngine(sb, steam);
            string safe = string.Concat(name.Split(System.IO.Path.GetInvalidFileNameChars())).Replace(' ', '_');
            LogWriter.WriteTextFile("steam_engine_" + safe, sb.ToString());
        }

        /// <summary>The steam engine's numbers (heating surface, pressure, cylinders, grate estimate, consumption rates).</summary>
        private static void DumpSteamEngine(StringBuilder sb, SteamLocomotive steam)
        {
            sb.AppendLine();
            if (steam == null || steam.engine == null)
            {
                sb.AppendLine("SteamEngine: not a steam locomotive");
                return;
            }
            object engine = steam.engine;
            sb.AppendLine("SteamEngine (" + steam.DisplayName + ")");
            foreach (FieldInfo f in engine.GetType().GetFields(Fields))
            {
                if (f.FieldType.IsPrimitive || f.FieldType.IsEnum)
                {
                    sb.AppendLine("  " + f.Name + " = " + Convert.ToString(f.GetValue(engine), CultureInfo.InvariantCulture));
                }
            }
            foreach (PropertyInfo prop in engine.GetType().GetProperties(Fields))
            {
                if (prop.GetIndexParameters().Length != 0 || !prop.CanRead) continue;
                if (!(prop.PropertyType.IsPrimitive || prop.PropertyType.IsEnum)) continue;
                try
                {
                    sb.AppendLine("  [property] " + prop.Name + " = " + Convert.ToString(prop.GetValue(engine, null), CultureInfo.InvariantCulture));
                }
                catch (Exception) { }
            }
        }

        private static void Dump(StringBuilder sb, object obj, string name, int depth)
        {
            string pad = new string(' ', depth * 2);

            if (obj == null)
            {
                sb.AppendLine($"{pad}{name} = null");
                return;
            }

            Type type = obj.GetType();

            if (type.IsPrimitive || type.IsEnum || obj is string || obj is decimal)
            {
                sb.AppendLine($"{pad}{name} = {Convert.ToString(obj, CultureInfo.InvariantCulture)}");
                return;
            }

            if (obj is AnimationCurve curve)
            {
                sb.AppendLine($"{pad}{name} = AnimationCurve [{curve.length} keys]");
                foreach (Keyframe k in curve.keys)
                {
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0}  t={1} v={2} inTan={3} outTan={4}", pad, k.time, k.value, k.inTangent, k.outTangent));
                }
                return;
            }

            if (obj is Array array)
            {
                sb.AppendLine($"{pad}{name} = [{array.Length} items]");
                int i = 0;
                foreach (object element in array)
                {
                    Dump(sb, element, $"{name}[{i++}]", depth + 1);
                }
                return;
            }

            bool unityType = type.Namespace != null && type.Namespace.StartsWith("UnityEngine", StringComparison.Ordinal);
            if (depth >= 3 || unityType)
            {
                sb.AppendLine($"{pad}{name} = {obj}");
                return;
            }

            sb.AppendLine($"{pad}{name} ({type.Name})");
            foreach (FieldInfo field in type.GetFields(Fields))
            {
                Dump(sb, field.GetValue(obj), field.Name, depth + 1);
            }
        }
    }
}
