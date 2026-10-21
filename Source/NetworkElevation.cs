using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml.Serialization;

namespace ModbusEmulatorX {
    public static class NetworkElevation {
        public static bool IsAdministrator() {
            using (var identity = WindowsIdentity.GetCurrent()) return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        static string Encode(AliasRequest request) {
            using (var stream = new MemoryStream()) {
                new XmlSerializer(typeof(AliasRequest)).Serialize(stream, request);
                return Convert.ToBase64String(stream.ToArray());
            }
        }
        public static ProcessStartInfo LaunchInfo(string executable, AliasRequest request, string pipeName) {
            request.Validate();
            if (!Path.IsPathRooted(executable) || !File.Exists(executable)) throw new Exception("The installed emulator executable could not be found.");
            if (!Regex.IsMatch(pipeName, "^ModbusEmulatorXNet_[0-9a-f]{32}$")) throw new Exception("Invalid network helper channel.");
            return new ProcessStartInfo(executable, "--network-helper " + pipeName + " " + Encode(request)) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
        }
        public static AliasResult Run(string executable, AliasRequest request) {
            request.Validate();
            string pipeName = "ModbusEmulatorXNet_" + Guid.NewGuid().ToString("N");
            var security = new PipeSecurity();
            using (var identity = WindowsIdentity.GetCurrent()) security.AddAccessRule(new PipeAccessRule(identity.User, PipeAccessRights.FullControl, AccessControlType.Allow));
            foreach (var sid in new[] { WellKnownSidType.BuiltinAdministratorsSid, WellKnownSidType.LocalSystemSid })
                security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(sid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
            using (var pipe = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, security)) {
                IAsyncResult waiting = pipe.BeginWaitForConnection(null, null);
                try {
                    var start = LaunchInfo(executable, request, pipeName);
                    using (var process = Process.Start(start)) {
                        if (process == null) throw new Exception("Windows did not start the network helper.");
                        DateTime deadline = DateTime.UtcNow.AddSeconds(180);
                        while (!waiting.AsyncWaitHandle.WaitOne(100)) {
                            if (process.HasExited && !waiting.AsyncWaitHandle.WaitOne(200)) throw new Exception("The network helper exited without a response (code " + process.ExitCode + ").");
                            if (DateTime.UtcNow >= deadline) throw new Exception("The network helper is still running. Wait for it to finish before trying again; its ownership ledger retains any created aliases.");
                        }
                        pipe.EndWaitForConnection(waiting);
                        var reading = System.Threading.Tasks.Task.Run(() => NetworkXml.Read<AliasResult>(pipe));
                        if (!reading.Wait(30000)) throw new Exception("The network helper response timed out. Check Network Setup before trying again.");
                        if (!process.WaitForExit(5000)) throw new Exception("The network helper did not finish cleanly.");
                        return reading.Result;
                    }
                } catch (Win32Exception error) {
                    if (error.NativeErrorCode == 1223) return new AliasResult { Error = "Windows administrator approval was cancelled. No network changes were requested." };
                    throw;
                } finally { waiting.AsyncWaitHandle.Close(); }
            }
        }
        public static int HelperMain(string[] args) {
            if (args.Length != 3 || args[0] != "--network-helper" || !Regex.IsMatch(args[1], "^ModbusEmulatorXNet_[0-9a-f]{32}$") || args[2].Length > 60000) return 2;
            AliasResult result;
            try {
                if (!IsAdministrator()) throw new Exception("Administrator privileges are required to add or remove IP aliases.");
                AliasRequest request;
                using (var stream = new MemoryStream(Convert.FromBase64String(args[2]))) request = NetworkXml.Read<AliasRequest>(stream);
                request.Validate();
                using (var gate = new Mutex(false, "Global\\ModbusEmulatorX_NetworkAliasManager")) {
                    bool acquired = false;
                    try {
                        try { acquired = gate.WaitOne(30000); } catch (AbandonedMutexException) { acquired = true; }
                        if (!acquired) throw new Exception("Another network setup operation is running. Try again after it completes.");
                        result = new AliasManager(new WindowsAliasPlatform(), new ProtectedAliasStore()).Execute(request);
                    } finally { if (acquired) gate.ReleaseMutex(); }
                }
            } catch (Exception error) { result = new AliasResult { Error = error.GetBaseException().Message }; }
            try {
                using (var pipe = new NamedPipeClientStream(".", args[1], PipeDirection.Out)) {
                    pipe.Connect(5000);
                    new XmlSerializer(typeof(AliasResult)).Serialize(pipe, result);
                    pipe.Flush();
                }
            } catch { return 3; }
            return result.Success ? 0 : 1;
        }
        public static AliasRequest PrepareRequest(Project project, NetworkOptions options, NetworkPlan plan) {
            if (plan.Adapter == null) throw new Exception("Select a network adapter in Network Setup.");
            return new AliasRequest { Operation = "prepare", SessionId = options.SessionId, AdapterId = plan.Adapter.Id, PrefixLength = options.PrefixLength, CheckConflicts = options.CheckConflicts, Addresses = plan.Rows.Where(r => r.Missing).Select(r => r.Address).ToList() };
        }
        public static AliasRequest CleanupRequest(NetworkOptions options) { return new AliasRequest { Operation = "cleanup", SessionId = options.SessionId }; }
    }
}
