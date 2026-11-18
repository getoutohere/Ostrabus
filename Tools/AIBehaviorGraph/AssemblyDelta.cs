using System.Text.Json;

// Compare all normalized members with the same metadata/IL normalizer used by
// graph invalidation. No native game code is loaded or executed.
static class AssemblyDelta
{
    internal static void Run(string[] args)
    {
        if (args.Length != 4)
            throw new ArgumentException("assembly-diff OLD-ASSEMBLY NEW-ASSEMBLY OUTPUT.json");
        using var before = new AssemblyFingerprint(Path.GetFullPath(args[1]));
        using var after = new AssemblyFingerprint(Path.GetFullPath(args[2]));
        var changes = before.Members.Keys.Union(after.Members.Keys, StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .Select(id =>
            {
                before.Members.TryGetValue(id, out var oldMember);
                after.Members.TryGetValue(id, out var newMember);
                return new
                {
                    id,
                    status = oldMember == null ? "added" : newMember == null ? "removed" :
                        oldMember.Hash == newMember.Hash ? "unchanged" : "changed",
                    beforeHash = oldMember?.Hash,
                    afterHash = newMember?.Hash
                };
            }).Where(row => row.status != "unchanged").ToArray();
        var references = before.ReferenceHashes.Keys.Union(after.ReferenceHashes.Keys, StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .Where(id => before.ReferenceHashes.GetValueOrDefault(id) != after.ReferenceHashes.GetValueOrDefault(id))
            .Select(id => new
            {
                id,
                beforeHash = before.ReferenceHashes.GetValueOrDefault(id),
                afterHash = after.ReferenceHashes.GetValueOrDefault(id)
            }).ToArray();
        string output = Path.GetFullPath(args[3]);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(new
        {
            scope = "Normalized in-module member bodies/contracts and directly referenced assembly bytes. Not runtime, transitive-external, asset or compatibility approval.",
            normalizer = "cecil-metadata-il-v1",
            beforeAssembly = Path.GetFullPath(args[1]),
            afterAssembly = Path.GetFullPath(args[2]),
            beforeAssemblyHash = before.BinaryHash,
            afterAssemblyHash = after.BinaryHash,
            beforeMemberCount = before.Members.Count,
            afterMemberCount = after.Members.Count,
            changes,
            changedReferences = references
        }, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            report = output,
            changedMembers = changes.Count(row => row.status == "changed"),
            addedMembers = changes.Count(row => row.status == "added"),
            removedMembers = changes.Count(row => row.status == "removed"),
            changedMethods = changes.Count(row => row.id.StartsWith("M:", StringComparison.Ordinal)),
            changedReferencedAssemblies = references.Length
        }));
    }
}
