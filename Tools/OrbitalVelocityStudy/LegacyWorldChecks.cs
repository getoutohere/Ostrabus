using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using HarmonyLib;
using OrbitalVelocityStudy;

internal static class LegacyWorldChecks
{
    sealed class Sample
    {
        internal BodyOrbit Body;
        internal double Epoch, X, Y, VX, VY;
    }
    static readonly List<Sample> Samples = new List<Sample>();
    static int bodyCount;

    internal static void Capture()
    {
        var fixture = JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/LegacyBodies.json")));
        var saved = fixture["bodies"].ToObject<JsonBodyOrbitSave[]>();
        var bodies = saved.ToDictionary(b => b.strName, b => new BodyOrbit(b));
        foreach (string link in fixture["hierarchy"].Values<string>())
        {
            int separator = link.IndexOf('=');
            string child = link.Substring(0, separator), parent = link.Substring(separator + 1);
            if (!bodies.TryGetValue(child, out var body))
                continue; // Unloaded/deferred environment IDs are not materialized bodies.
            if (!bodies.TryGetValue(parent, out var parentBody))
                throw new Exception("Saved body has an unresolved parent: " + child);
            body.boParent = parentBody;
        }
        Samples.Clear();
        bodyCount = bodies.Count;
        double start = (double)fixture["epoch"];
        foreach (var body in bodies.Values)
            foreach (double offset in new[] { 0d, .25, 21600 })
            {
                double t = start + offset;
                body.dTimeCalcLast = body.dVelCalcLast = double.NegativeInfinity;
                body.UpdateTime(t);
                Samples.Add(new Sample { Body = body, Epoch = t, X = body.dXReal, Y = body.dYReal, VX = body.dVelX, VY = body.dVelY });
            }
    }

    static BodyOrbit Clone(BodyOrbit source)
    {
        var body = new BodyOrbit(source.GetJSONSave()) { boParent = source.boParent == null ? null : Clone(source.boParent) };
        body.dTimeCalcLast = body.dVelCalcLast = double.NegativeInfinity;
        return body;
    }

    internal static object Check(Action<bool, string> check)
    {
        var production = Assembly.LoadFrom(Path.GetFullPath("bin/Debug/netstandard2.1/Ostrabus.dll"));
        var helper = AccessTools.Method(production.GetType("Ostrabus.Orbital.NativeEphemeris", true), "State");
        double maxPosition = 0, maxConventionError = 0, maxFallbackVelocityError = 0;
        int supported = 0, fallback = 0;
        var fallbackNames = new HashSet<string>();
        foreach (var sample in Samples)
        {
            var body = Clone(sample.Body);
            body.UpdateTime(sample.Epoch);
            double positionError = Program.Mag(body.dXReal - sample.X, body.dYReal - sample.Y) * Program.Meters;
            maxPosition = Math.Max(maxPosition, positionError);
            check(positionError == 0, "Saved-world body position changed: " + body.strName + " / " + positionError);
            object[] args = { body, sample.Epoch, null, null };
            bool accepted = (bool)helper.Invoke(null, args);
            if (accepted)
            {
                supported++;
                var vector = args[3];
                double vx = (double)AccessTools.Field(vector.GetType(), "X").GetValue(vector);
                double vy = (double)AccessTools.Field(vector.GetType(), "Y").GetValue(vector);
                double error = Program.Mag(body.dVelX - vx, body.dVelY - vy) * Program.Meters;
                maxConventionError = Math.Max(maxConventionError, error);
                check(error < .00001, "Saved-world native/SFF velocity mismatch: " + body.strName + " / " + error);
            }
            else
            {
                // Five saved retrograde moons use NEGATIVE periods. The existing
                // detached helper does not support that representation. Retain
                // native behavior rather than silently broaden this correction.
                fallback++;
                fallbackNames.Add(body.strName);
                double error = Program.Mag(body.dVelX - sample.VX, body.dVelY - sample.VY) * Program.Meters;
                maxFallbackVelocityError = Math.Max(maxFallbackVelocityError, error);
                check(error == 0, "Unsupported saved body no longer follows unchanged native velocity: " + body.strName);
            }
        }
        return new
        {
            kind = "saved-world-body-grid",
            bodyCount,
            samples = Samples.Count,
            supportedStates = supported,
            nativeFallbackStates = fallback,
            nativeFallbackBodies = fallbackNames.OrderBy(s => s).ToArray(),
            maximumPositionChangeM = maxPosition,
            maximumExistingSffVelocityDifferenceMps = maxConventionError,
            maximumNativeFallbackVelocityChangeMps = maxFallbackVelocityError,
            limitation = "Negative-period representation remains native; this study does not repair its pre-existing detached-helper limitation."
        };
    }
}
