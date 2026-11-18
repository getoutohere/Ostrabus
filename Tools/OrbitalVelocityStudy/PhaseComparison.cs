using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

internal static class PhaseComparison
{
    sealed class Sample
    {
        internal string Kind; internal BodyOrbit Body; internal double Epoch, X, Y; internal bool Correct;
    }
    static readonly List<Sample> Samples = new List<Sample>();
    internal static void Capture(JObject fixture)
    {
        Samples.Clear();
        foreach (var s in (JArray)fixture["samples"])
        {
            foreach (var property in ((JObject)s["bodies"]).Properties())
            {
                if (property.Name != "Venus" && property.Name != "VORB" && property.Name != "VORB|Aux")
                    continue;
                var b = Program.Model(property.Value, (JObject)s["bodies"]);
                Add("captured-" + property.Name, b, Program.D(property.Value, "positionCalculationEpoch"));
            }
        }
        foreach (double eccentricity in new[] { .0067, .455, .9 })
            foreach (double phase in new[] { .00004, .00014, .01, .4, 1d, 2d, 3d, 4d, 5d, 6d })
            {
                var b = new BodyOrbit(new JsonBodyOrbitSave
                {
                    strName = "synthetic",
                    fPerh = .72 * (1 - eccentricity),
                    fAph = .72 * (1 + eccentricity),
                    fAxis1 = 1.44,
                    fAxis2 = 1.44 * Math.Sqrt(1 - eccentricity * eccentricity),
                    fEcc = eccentricity,
                    fPeriod = 19407509,
                    nOrbitDirection = 1
                });
                Add("synthetic-e" + eccentricity, b, phase * b.fPeriod / (2 * Math.PI));
                Add("uncorrected-e" + eccentricity, b, phase * b.fPeriod / (2 * Math.PI), correct: false);
            }
    }
    static void Add(string kind, BodyOrbit b, double t, bool correct = true)
    {
        b.dTimeCalcLast = double.NegativeInfinity;
        b.UpdateTime(t, correct, false);
        Samples.Add(new Sample { Kind = kind, Body = b, Epoch = t, X = b.dXReal, Y = b.dYReal, Correct = correct });
    }
    static BodyOrbit Clone(BodyOrbit source)
    {
        var b = new BodyOrbit(source.GetJSONSave()) { boParent = source.boParent == null ? null : Clone(source.boParent) };
        b.dTimeCalcLast = b.dVelCalcLast = double.NegativeInfinity;
        return b;
    }
    internal static object Compare()
    {
        var differences = Samples.Select(s =>
        {
            var b = Clone(s.Body);
            b.UpdateTime(s.Epoch, s.Correct, false);
            return new
            {
                kind = s.Kind,
                epoch = s.Epoch,
                positionChangeM = Program.Mag(b.dXReal - s.X, b.dYReal - s.Y) * Program.Meters
            };
        }).ToArray();
        return new
        {
            kind = "precision-position-change",
            capturedMaximumM = differences.Where(d => d.kind.StartsWith("captured")).Max(d => d.positionChangeM),
            capturedByBody = differences.Where(d => d.kind.StartsWith("captured")).GroupBy(d => d.kind).Select(g => new { body = g.Key, maximumM = g.Max(v => v.positionChangeM) }).ToArray(),
            syntheticMaximumM = differences.Where(d => d.kind.StartsWith("synthetic")).Max(d => d.positionChangeM),
            largest = differences.OrderByDescending(d => d.positionChangeM).Take(6).ToArray(),
            note = "Changing solver precision changes derived positions. This is not a deployable load-transition policy."
        };
    }

    internal static double MaximumPositionChange()
    {
        double largest = 0;
        foreach (var s in Samples)
        {
            var b = Clone(s.Body);
            b.UpdateTime(s.Epoch, s.Correct, false);
            largest = Math.Max(largest, Program.Mag(b.dXReal - s.X, b.dYReal - s.Y) * Program.Meters);
        }
        return largest;
    }
}
