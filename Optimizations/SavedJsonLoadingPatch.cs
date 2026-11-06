using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using HarmonyLib;
using Ostranauts.Tools;

namespace Ostrabus.Optimizations {
    internal static class SavedJsonLoading {
        internal static bool Enabled;
        static bool warnedUnavailable;
        internal static void ReadArchive<T>(string path, Dictionary<string, T> output, Dictionary<string, byte[]> files) {
            if (!Enabled || (typeof(T) != typeof(JsonShip) && typeof(T) != typeof(JsonGameSave))) {
                DataHandler.JsonToData(path, output, files);
                return;
            }
            // Preserve the vanilla archive loader's dictionary population and error boundary.
            UnityEngine.Debug.Log("#Info# Loading json: " + path + " from byte array.");
            var progress = new StringBuilder(70);
            try {
                byte[] bytes = files[path];
                string json = Encoding.UTF8.GetString(bytes);
                progress.AppendLine("Converting json into Array...");
                T[] values = Deserialize<T[]>(json);
                if (values == null) {
                    progress.AppendLine("null aList found in " + path + "(" + bytes.Length + " bytes) converting to object. Skipping.");
                    return;
                }
                foreach (T value in values) {
                    progress.Append("Getting key: ");
                    PropertyInfo property = value.GetType().GetProperty("strName");
                    if (property == null) JsonLogger.ReportProblem("strName is missing", ReportTypes.FailingString);
                    string key = property.GetValue(value, null).ToString();
                    progress.AppendLine(key);
                    if (output.ContainsKey(key)) {
                        UnityEngine.Debug.Log("Warning: Trying to add " + key + " twice.");
                        output[key] = value;
                    } else output.Add(key, value);
                }
            } catch (Exception ex) {
                JsonLogger.ReportProblem(path, ReportTypes.SourceInfo);
                string context = progress.Length <= 1000 ? progress.ToString() : progress.ToString(progress.Length - 1000, 1000);
                UnityEngine.Debug.LogError(context + "\n" + ex.Message + "\n" + ex.StackTrace);
            }
            if (path.IndexOf("osSGv1", StringComparison.Ordinal) >= 0) UnityEngine.Debug.Log(progress);
        }
        internal static T Deserialize<T>(string json) {
            // Restrict this experiment to archived ship/world DTOs. Keep config/mod data,
            // save metadata, writing and every other JsonMapper caller on vanilla code.
            if (!Enabled || (typeof(T) != typeof(JsonShip[]) && typeof(T) != typeof(JsonGameSave[]))) return LitJson.JsonMapper.ToObject<T>(json);
            bool available;
            try { available = CompatibleJsonDeserializer.Available; }
            catch (Exception ex) {
                available = false;
                if (!warnedUnavailable) {
                    warnedUnavailable = true;
                    Ostrabus.OstrabusPlugin.Logger?.LogWarning("[Optimizations] Save JSON adapter unavailable; using LitJson before parsing: " + ex.Message);
                }
            }
            if (!available) return LitJson.JsonMapper.ToObject<T>(json);
            var sample = LoadProfiling.Enter("Json.NewtonsoftCompatible");
            var timer = Stopwatch.StartNew();
            Exception error = null;
            try { return CompatibleJsonDeserializer.DeserializeNewtonsoft<T>(json); }
            catch (Exception ex) { error = ex; throw; }
            finally {
                LoadProfiling.Exit(sample, error);
                if (timer.Elapsed.TotalSeconds >= 1 || error != null) Ostrabus.OstrabusPlugin.Logger?.LogInfo("[Optimizations] Save JSON " + typeof(T).Name + ": " + timer.Elapsed.TotalMilliseconds.ToString("F0") + " ms" + (error == null ? "." : "; failed: " + error.Message));
            }
            // Do not retry after parsing starts: DTO constructors can consume Unity RNG
            // or touch game databases. A second parse could repeat those side effects.
        }
    }

    [HarmonyPatch]
    internal static class SavedJsonLoadingPatch {
        static IEnumerable<MethodBase> TargetMethods() {
            // Patch concrete callers, never shared generic JsonToData machine code.
            yield return AccessTools.Method(typeof(DataHandler), nameof(DataHandler.LoadSaveFile), new[] { typeof(string), typeof(Dictionary<string, byte[]>) });
            yield return AccessTools.Method(typeof(ShipFileProfilePatch), "ReadMemory");
        }
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
            var original = instructions.ToList();
            var matches = original.Where(instruction => instruction.opcode == OpCodes.Call && instruction.operand is MethodInfo method && method.DeclaringType == typeof(DataHandler) && method.Name == "JsonToData" && method.IsGenericMethod && method.GetParameters().Length == 3 && (method.GetGenericArguments()[0] == typeof(JsonShip) || method.GetGenericArguments()[0] == typeof(JsonGameSave))).ToList();
            if (matches.Count != 1) {
                Ostrabus.OstrabusPlugin.Logger?.LogWarning("[Optimizations] Expected one archive JSON mapper call; found " + matches.Count + ". Original loader retained.");
                return original;
            }
            var call = matches[0];
            call.operand = typeof(SavedJsonLoading).GetMethod(nameof(SavedJsonLoading.ReadArchive), BindingFlags.NonPublic | BindingFlags.Static).MakeGenericMethod(((MethodInfo)call.operand).GetGenericArguments());
            return original;
        }
    }
}
