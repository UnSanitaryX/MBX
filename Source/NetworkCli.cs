using System;
using System.IO;
using System.Linq;

namespace ModbusEmulatorX {
    public static class NetworkCli {
        public static int Execute(string[] args) {
            if (args.Length == 0) throw new Exception("Use network adapters, network status PROJECT, network prepare PROJECT --adapter INDEX [--mask MASK], or network cleanup.");
            var adapters = NetworkPlanning.Adapters();
            if (args[0] == "adapters" && args.Length == 1) {
                foreach (var adapter in adapters.Where(a => !a.Loopback)) Console.WriteLine(adapter.Index + "  " + adapter);
                return 0;
            }
            NetworkOptions options = NetworkOptions.Load(NetworkOptions.SettingsPath);
            if (args[0] == "cleanup" && args.Length == 1) return Apply(NetworkElevation.CleanupRequest(options));
            if ((args[0] != "status" && args[0] != "prepare") || args.Length < 2) throw new Exception("Invalid network command.");
            var project = ProjectFiles.Load(args[1]);
            for (int i = 2; i < args.Length; i++) {
                if (args[i] == "--adapter" && i + 1 < args.Length) {
                    int index = int.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
                    var selected = adapters.SingleOrDefault(a => a.Index == index && !a.Loopback);
                    if (selected == null) throw new Exception("Adapter index not found. Use network adapters.");
                    options.AdapterId = selected.Id;
                } else if (args[i] == "--mask" && i + 1 < args.Length) options.PrefixLength = NetworkPlanning.Prefix(args[++i]);
                else if (args[i] == "--prefix" && i + 1 < args.Length) options.PrefixLength = int.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
                else if (args[i] == "--skip-conflict-check") options.CheckConflicts = false;
                else throw new Exception("Unknown network option: " + args[i]);
            }
            var plan = NetworkPlanning.Build(project, options, adapters);
            foreach (var row in plan.Rows) Console.WriteLine(row.Address + "  ports " + row.Ports + "  " + row.Status);
            if (args[0] == "status") return plan.Ready ? 0 : 2;
            if (plan.Ready) { Console.WriteLine("All project addresses are already local."); return 0; }
            var request = NetworkElevation.PrepareRequest(project, options, plan);
            request.Validate(); options.AdapterId = plan.Adapter.Id; options.Save(NetworkOptions.SettingsPath);
            return Apply(request);
        }
        static int Apply(AliasRequest request) {
            string app = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ModbusEmulatorX.exe");
            AliasResult result = NetworkElevation.Run(app, request);
            foreach (string message in result.Messages) Console.WriteLine(message);
            if (!result.Success) throw new Exception(result.Error ?? "Network operation failed.");
            Console.WriteLine("Managed aliases: " + result.ManagedCount);
            return 0;
        }
    }
}
