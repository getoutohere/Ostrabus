using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Cecil normally writes the physical temporary PDB filename into CodeView.
// Keep its GUID/age and symbol contents, but advertise the final adjacent file.
sealed class PortableSymbols : ISymbolWriterProvider {
    readonly PortablePdbWriterProvider provider = new();

    public ISymbolWriter GetSymbolWriter(ModuleDefinition module, string fileName) =>
        new Writer(provider.GetSymbolWriter(module, fileName));

    public ISymbolWriter GetSymbolWriter(ModuleDefinition module, Stream symbolStream) =>
        new Writer(provider.GetSymbolWriter(module, symbolStream));

    sealed class Writer(ISymbolWriter inner) : ISymbolWriter {
        public void Dispose() => inner.Dispose();
        public ISymbolReaderProvider GetReaderProvider() => inner.GetReaderProvider();
        public void Write(MethodDebugInformation information) => inner.Write(information);

        public ImageDebugHeader GetDebugHeader() {
            var header = inner.GetDebugHeader();
            var entries = header.Entries.Select(entry => {
                if (entry.Directory.Type != ImageDebugType.CodeView) return entry;
                Files.Need(entry.Data.Length >= 24 && Encoding.ASCII.GetString(entry.Data, 0, 4) == "RSDS",
                    "Unexpected portable PDB debug header.");
                var name = Encoding.UTF8.GetBytes("Ostrabus.pdb\0");
                var data = new byte[24 + name.Length];
                Array.Copy(entry.Data, data, 24);
                Array.Copy(name, 0, data, 24, name.Length);
                var directory = entry.Directory;
                directory.SizeOfData = data.Length;
                return new ImageDebugHeaderEntry(directory, data);
            }).ToArray();
            return new ImageDebugHeader(entries);
        }
    }
}
