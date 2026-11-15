using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HarmonyLib;
using Ostrabus.Development;
using Ostrabus.NavConsole.Rotation;
using UnityEngine;

static partial class Program {
    static bool readoutInteractive, readoutRaycasts, readoutIgnoresParents;
    static bool SetInteractive(bool __0) { readoutInteractive = __0; return false; }
    static bool SetRaycasts(bool __0) { readoutRaycasts = __0; return false; }
    static bool SetIgnoreParents(bool __0) { readoutIgnoresParents = __0; return false; }

    static void UiInput(Harmony fixture) {
        // Read the actual shipped prefab, not a made-up interactive panel. This
        // metadata-only reader never instantiates Unity objects or writes assets.
        using var file = new UiAssetFile(Path.Combine(InstallationPaths.Game, "Ostranauts_Data", "resources.assets"));
        long root = file.Names.Single(pair => pair.Value == "NavModDuhrt").Key;
        long container = file.Child(root, "Container");
        long readout = file.Child(container, "pnlTxt");
        byte[] group = file.Component(readout, 225);
        Check(group.Length == 24, "Shipped readout CanvasGroup layout changed; revalidate UI integration");
        Check(group[20] == 0 && group[21] == 0 && group[22] == 0,
            "Native DUHRT no longer has the noninteractive readout that reproduced the bug");
        byte[] outer = file.Component(container, 225);
        Check(outer.Length == 24 && outer[20] == 1 && outer[21] == 1,
            "Native outer module input gate changed");
        fixture.Patch(AccessTools.PropertySetter(typeof(CanvasGroup), nameof(CanvasGroup.interactable)),
            prefix: new HarmonyMethod(typeof(Program), nameof(SetInteractive)));
        fixture.Patch(AccessTools.PropertySetter(typeof(CanvasGroup), nameof(CanvasGroup.blocksRaycasts)),
            prefix: new HarmonyMethod(typeof(Program), nameof(SetRaycasts)));
        fixture.Patch(AccessTools.PropertySetter(typeof(CanvasGroup), nameof(CanvasGroup.ignoreParentGroups)),
            prefix: new HarmonyMethod(typeof(Program), nameof(SetIgnoreParents)));
        readoutInteractive = group[20] != 0; readoutRaycasts = group[21] != 0; readoutIgnoresParents = group[22] != 0;
        Check(!readoutInteractive && !readoutRaycasts, "Reproduction failed to block button input");
        RotationPanel.ConfigureInputSurface(Empty<CanvasGroup>());
        Check(readoutInteractive && readoutRaycasts, "Production adaptation left the inherited input blocker enabled");
        Check(!readoutIgnoresParents, "Input adaptation bypasses native layout-edit gating");
        foreach (var padding in new[] { Vector4.zero, new Vector4(12, 4, 0, 20), new Vector4(2, 10, 6, 0) })
        foreach (double angle in new[] { 0.0, Math.PI / 2, Math.PI }) {
            var rotation = new Quaternion(0, 0, (float)Math.Sin(angle / 2), (float)Math.Cos(angle / 2));
            var size = new Vector2(32, 48);
            var bounds = RotationPanel.GlyphBounds(size, padding, rotation);
            var logical = bounds.max - bounds.min;
            var padShift = rotation * new Vector3((padding.x - padding.z) / size.x * logical.x / 2,
                (padding.y - padding.w) / size.y * logical.y / 2, 0);
            var actualCenter = (bounds.min + bounds.max) / 2 + new Vector2(padShift.x, padShift.y);
            Check((actualCenter - new Vector2(.5f, .5f)).magnitude < 1e-6, "Asymmetric sprite trim miscentered an arrow");
            float inkW = logical.x * (size.x - padding.x - padding.z) / size.x;
            float inkH = logical.y * (size.y - padding.y - padding.w) / size.y;
            Check(Math.Abs(Math.Max(inkW, inkH) - .35f) < 1e-6, "Arrow ink did not retain face padding");
        }
        Console.WriteLine("Shipped DUHRT prefab input blocker reproduced; production surface repair enables input while respecting outer Edit-mode gating.");
    }

    sealed class UiAssetFile : IDisposable {
        readonly FileStream stream;
        readonly BinaryReader reader;
        readonly Dictionary<long, (long position, int size, int type)> objects = new();
        readonly Dictionary<long, long[]> components = new();
        internal readonly Dictionary<long, string> Names = new();
        void Align() => stream.Position = (stream.Position + 3) & ~3L;
        uint Big32() { var bytes = reader.ReadBytes(4); Array.Reverse(bytes); return BitConverter.ToUInt32(bytes, 0); }
        ulong Big64() { var bytes = reader.ReadBytes(8); Array.Reverse(bytes); return BitConverter.ToUInt64(bytes, 0); }
        string ZeroString() { var chars = new List<byte>(); byte c; while ((c = reader.ReadByte()) != 0) chars.Add(c); return Encoding.UTF8.GetString(chars.ToArray()); }
        string ReadString() { int n = reader.ReadInt32(); if (n < 0 || n > 100000) throw new Exception("Bad native asset string"); string s = Encoding.UTF8.GetString(reader.ReadBytes(n)); Align(); return s; }

        internal UiAssetFile(string path) {
            stream = File.OpenRead(path); reader = new BinaryReader(stream);
            Big32(); Big32(); int version = (int)Big32(); Big32(); int endian = reader.ReadByte(); reader.ReadBytes(3);
            if (version != 22 || endian != 0) throw new Exception("Revalidate UI asset reader for native serialized-file version " + version);
            Big32(); Big64(); long data = (long)Big64(); Big64(); ZeroString(); reader.ReadInt32();
            if (reader.ReadBoolean()) throw new Exception("Revalidate UI reader: native type tree is present");
            int n = reader.ReadInt32(); var types = new int[n];
            for (int i = 0; i < n; i++) {
                types[i] = reader.ReadInt32(); reader.ReadByte(); reader.ReadInt16();
                if (types[i] == 114) reader.ReadBytes(16);
                reader.ReadBytes(16);
            }
            n = reader.ReadInt32();
            for (int i = 0; i < n; i++) {
                Align(); long id = reader.ReadInt64(); long position = reader.ReadInt64() + data;
                int size = reader.ReadInt32(), type = types[reader.ReadInt32()]; objects[id] = (position, size, type);
            }
            foreach (var pair in objects.Where(pair => pair.Value.type == 1)) {
                stream.Position = pair.Value.position; int count = reader.ReadInt32(); var ids = new long[count];
                for (int i = 0; i < count; i++) {
                    if (reader.ReadInt32() != 0) throw new Exception("External prefab component unsupported");
                    ids[i] = reader.ReadInt64();
                }
                components[pair.Key] = ids; reader.ReadUInt32(); Names[pair.Key] = ReadString();
            }
        }
        byte[] Bytes(long id) { var obj = objects[id]; stream.Position = obj.position; return reader.ReadBytes(obj.size); }
        internal byte[] Component(long gameObject, int type) => Bytes(components[gameObject].Single(id => objects[id].type == type));
        internal long Child(long parent, string name) {
            byte[] transform = Component(parent, 224);
            int count = BitConverter.ToInt32(transform, 52);
            if (count < 0 || count > 1000 || 56L + count * 12 + 12 > transform.Length) throw new Exception("Revalidate native RectTransform layout");
            for (int i = 0; i < count; i++) {
                int at = 56 + i * 12;
                if (BitConverter.ToInt32(transform, at) != 0) throw new Exception("External prefab child unsupported");
                byte[] child = Bytes(BitConverter.ToInt64(transform, at + 4));
                long go = BitConverter.ToInt64(child, 4);
                if (Names[go] == name) return go;
            }
            throw new Exception("Native prefab child missing: " + name);
        }
        public void Dispose() { reader.Dispose(); stream.Dispose(); }
    }
}
