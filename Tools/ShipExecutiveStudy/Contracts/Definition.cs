using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MissionContracts;

public sealed class Limits
{
    public int SuspendedMissions;
    public int PendingWorldEffects;
}

public sealed class Transition
{
    public string Id, From, Event, To, WhenArrival;
    public string[] Operations = Array.Empty<string>();
    public string[] Effects = Array.Empty<string>();
}

public sealed class Profile
{
    public string Kind, WithoutGoal, WithGoal;
}

public sealed class Definition
{
    public int SchemaVersion;
    public string Id, InitialPhase;
    public string Purpose;
    public string[] LifecycleOperations;
    public JArray Contracts;
    public JObject ImplementationScope;
    public Limits Limits;
    public string[] Phases, Events, Operations, Effects;
    public Transition[] Transitions;
    public Profile[] Profiles;
    [JsonIgnore] public string Fingerprint;
    readonly Dictionary<string, List<Transition>> table = new Dictionary<string, List<Transition>>();

    public static Definition Load(string path) => Parse(File.ReadAllText(path));

    public static Definition Parse(string json)
    {
        var model = JsonConvert.DeserializeObject<Definition>(json, new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Error, TypeNameHandling = TypeNameHandling.None });
        model.Validate();
        using (var sha = SHA256.Create())
            model.Fingerprint = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(json))).Replace("-", "").ToLowerInvariant();
        return model;
    }

    public void Validate()
    {
        if (SchemaVersion != 1 || string.IsNullOrWhiteSpace(Id) || Limits == null || Limits.PendingWorldEffects != 1 || Limits.SuspendedMissions < 1 || Limits.SuspendedMissions > 16)
            throw new InvalidOperationException("Unsupported model identity or bounds");
        if (Phases == null || Events == null || Operations == null || Effects == null || Transitions == null || !Phases.Contains(InitialPhase))
            throw new InvalidOperationException("Incomplete model vocabulary");
        foreach (var vocabulary in new[] { Phases, Events, Operations, Effects })
            if (vocabulary.Distinct().Count() != vocabulary.Length || vocabulary.Any(string.IsNullOrWhiteSpace))
                throw new InvalidOperationException("Duplicate or empty model symbol");
        string[] entryPhases = { "Selecting", "Revalidating", "Reacting", "Engaging", "Operating" };
        if (Profiles == null || Profiles.Any(p => string.IsNullOrWhiteSpace(p.Kind) || !Phases.Contains(p.WithoutGoal) || !Phases.Contains(p.WithGoal) || !entryPhases.Contains(p.WithoutGoal) || !entryPhases.Contains(p.WithGoal)) || Profiles.Select(p => p.Kind).Distinct().Count() != Profiles.Length)
            throw new InvalidOperationException("Unbound mission profile");
        string[] supportedOperations = { "setGoal", "clearGoal", "prepareArrival", "clearArrival", "complete", "observeDisposition" };
        if (Operations.Except(supportedOperations).Any())
            throw new InvalidOperationException("Unknown state operation");
        table.Clear();
        var ids = new HashSet<string>();
        foreach (var edge in Transitions)
        {
            if (string.IsNullOrWhiteSpace(edge.Id) || !ids.Add(edge.Id) || !Phases.Contains(edge.From) || !Phases.Contains(edge.To) || !Events.Contains(edge.Event) || edge.Operations.Except(Operations).Any() || edge.Effects.Except(Effects).Any())
                throw new InvalidOperationException("Unbound or duplicate model edge: " + edge.Id);
            string key = edge.From + "/" + edge.Event;
            if (edge.WhenArrival != null && edge.WhenArrival != "Dock" && edge.WhenArrival != "None")
                throw new InvalidOperationException("Unknown arrival guard");
            if (!table.TryGetValue(key, out var branches))
                table.Add(key, branches = new List<Transition>());
            if (branches.Any(b => b.WhenArrival == null || edge.WhenArrival == null || b.WhenArrival == edge.WhenArrival))
                throw new InvalidOperationException("Ambiguous model transition: " + key);
            branches.Add(edge);
        }
        if (Transitions.Any(t => t.Event == "ApproachFailed" && t.To != "AwaitingPolicy"))
            throw new InvalidOperationException("Failed approach cannot imply successful arrival");
    }

    public Transition Find(string phase, string input, string arrival) => table.TryGetValue(phase + "/" + input, out var branches)
        ? branches.SingleOrDefault(b => b.WhenArrival == null || b.WhenArrival == arrival) : null;
}
