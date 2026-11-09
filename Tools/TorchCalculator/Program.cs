using Ostrabus.TorchCalculator;

ModelChecks.Run();
if (args.Length == 2 && args[0] == "--browser-checks") BrowserChecks.Export(args[1]);
else if (args.Length > 0 && args[0] != "--check") throw new ArgumentException("Usage: --check, or --browser-checks <output.html>");
else Console.WriteLine("Open Tools/TorchCalculator/calculator.html directly in a browser. No server is required.");
