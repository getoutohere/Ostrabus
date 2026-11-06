using System;
using System.Collections.Generic;

namespace Ostrabus.AlternativeWork;

internal interface IRouteGraph<T> {
    int Neighbors(T origin, T[] nodes, float[] costs);
}

// One distance field serves every work order. No path objects or per-node collections.
internal sealed class IncrementalRoutes<T> {
    struct Entry { internal T Node; internal float Cost; }
    readonly Dictionary<T, float> distances = new Dictionary<T, float>();
    readonly HashSet<T> settled = new HashSet<T>();
    readonly List<Entry> heap = new List<Entry>();
    readonly T[] neighbors = new T[8];
    readonly float[] costs = new float[8];
    internal bool Complete { get; private set; }
    internal bool Capped { get; private set; }
    internal int Count => settled.Count;
    internal void Reset(T origin) {
        Clear();
        distances[origin] = 0;
        Push(new Entry { Node = origin, Cost = 0 });
    }
    internal void Clear() {
        distances.Clear(); settled.Clear(); heap.Clear();
        Array.Clear(neighbors, 0, neighbors.Length);
        Complete = Capped = false;
    }
    internal bool TryCost(T node, out float cost) {
        cost = 0;
        return settled.Contains(node) && distances.TryGetValue(node, out cost);
    }
    internal void Step(IRouteGraph<T> graph, int nodes = 16, int cap = 8192) {
        if (Complete) return;
        for (int i = 0; i < nodes && heap.Count > 0; i++) {
            Entry entry = Pop();
            if (!settled.Add(entry.Node)) continue;
            if (settled.Count >= cap) { Capped = Complete = true; heap.Clear(); break; }
            int count = graph.Neighbors(entry.Node, neighbors, costs);
            for (int j = 0; j < count; j++) {
                T next = neighbors[j];
                float cost = entry.Cost + costs[j];
                if (settled.Contains(next) || (distances.TryGetValue(next, out float old) && old <= cost)) continue;
                distances[next] = cost;
                Push(new Entry { Node = next, Cost = cost });
            }
        }
        if (heap.Count == 0) Complete = true;
        Array.Clear(neighbors, 0, neighbors.Length);
    }
    void Push(Entry entry) {
        int i = heap.Count;
        heap.Add(entry);
        while (i > 0) {
            int parent = (i - 1) / 2;
            if (heap[parent].Cost <= entry.Cost) break;
            heap[i] = heap[parent]; i = parent;
        }
        heap[i] = entry;
    }
    Entry Pop() {
        Entry result = heap[0], last = heap[heap.Count - 1];
        heap.RemoveAt(heap.Count - 1);
        if (heap.Count == 0) return result;
        int i = 0;
        while (i * 2 + 1 < heap.Count) {
            int child = i * 2 + 1;
            if (child + 1 < heap.Count && heap[child + 1].Cost < heap[child].Cost) child++;
            if (last.Cost <= heap[child].Cost) break;
            heap[i] = heap[child]; i = child;
        }
        heap[i] = last;
        return result;
    }
}
