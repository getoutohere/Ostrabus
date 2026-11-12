using System.Text;
using System.Text.Json;

// The snapshot is exported by executing shipped constructors in bundled Mono.
// Every native node has a source reference, even when physical leaf outcomes
// remain external inputs to the higher-level travel/lifecycle model.
static class TrafficTrees {
    internal static void Verify(string folder, Model model) {
        using var snapshot = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "control-trees.json")));
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "assembly-dependencies.json")));
        var bindings = manifest.RootElement.GetProperty("bindings").EnumerateObject()
            .SelectMany(group => group.Value.EnumerateArray()).ToArray();
        void Node(JsonElement node) {
            string source = node.GetProperty("source").GetString()!;
            if (!model.Sources.Values.Any(s => s.Origin == "game" && s.Path == source))
                throw new Exception("Native traffic control node lacks a source binding: " + node.GetProperty("path").GetString());
            foreach (var method in node.GetProperty("methods").EnumerateArray()) {
                string type = method.GetProperty("type").GetString()!;
                string name = method.GetProperty("method").GetString()!;
                if (!bindings.Any(binding => binding.GetProperty("type").GetString() == type
                    && binding.GetProperty("methods").EnumerateArray().Any(entry => entry.GetString() == name)))
                    throw new Exception("Native traffic control method lacks an assembly binding: " + type + "." + name);
            }
            foreach (var child in node.GetProperty("children").EnumerateArray()) Node(child);
        }
        foreach (var role in snapshot.RootElement.EnumerateObject()) Node(role.Value);
    }
    internal static string Render(string folder) {
        using var snapshot = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "control-trees.json")));
        var result = new StringBuilder("<!-- Generated from native constructor snapshot. -->\n\n# Native cargo and civilian control trees\n\nChildren are ordered as in the shipped constructors. Priority queues contain dynamic commands. Physical leaf results are not inferred from this diagram.\n");
        foreach (var role in snapshot.RootElement.EnumerateObject()) {
            result.AppendLine("\n## " + role.Name + "\n\n```mermaid\nflowchart TD");
            void Node(JsonElement node) {
                string id = node.GetProperty("path").GetString()!.Replace('/', '_');
                string name = node.GetProperty("name").GetString()!;
                string type = node.GetProperty("type").GetString()!.Split('.').Last();
                result.AppendLine("  " + id + "[\"" + (name.Length == 0 ? type : name + " / " + type) + "\"]");
                int index = 0;
                foreach (var child in node.GetProperty("children").EnumerateArray()) {
                    Node(child);
                    result.AppendLine("  " + id + " -->|\"" + index++ + "\"| " + child.GetProperty("path").GetString()!.Replace('/', '_'));
                }
            }
            Node(role.Value); result.AppendLine("```\n");
        }
        return result.ToString();
    }
}
