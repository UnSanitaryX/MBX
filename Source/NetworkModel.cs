using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Xml;
using System.Xml.Serialization;

namespace ModbusEmulatorX {
    public sealed class NetworkOptions {
        public bool PrepareOnStart { get; set; }
        public string AdapterId { get; set; }
        public int PrefixLength { get; set; }
        public bool CheckConflicts { get; set; }
        public bool RemoveOnStop { get; set; }
        public string SessionId { get; set; }
        public NetworkOptions() {
            PrepareOnStart = true; PrefixLength = 24; CheckConflicts = true;
            RemoveOnStop = true; AdapterId = ""; SessionId = Guid.NewGuid().ToString("D");
        }
        public static string SettingsPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ModbusEmulatorX", "NetworkSettings.xml"); } }
        public static NetworkOptions Load(string path) {
            if (!File.Exists(path)) return new NetworkOptions();
            var result = NetworkXml.Read<NetworkOptions>(path);
            Guid id;
            if (!Guid.TryParse(result.SessionId, out id) || result.PrefixLength < 1 || result.PrefixLength > 32)
                throw new Exception("Invalid saved network settings.");
            return result;
        }
        public void Save(string path) { NetworkXml.Write(path, this); }
    }

    public static class NetworkXml {
        public static T Read<T>(string path) { using (var stream = File.OpenRead(path)) return Read<T>(stream); }
        public static T Read<T>(Stream stream) {
            using (var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1000000, CloseInput = false }))
                return (T)new XmlSerializer(typeof(T)).Deserialize(reader);
        }
        public static void Write<T>(string path, T value) {
            string parent = Path.GetDirectoryName(Path.GetFullPath(path));
            Directory.CreateDirectory(parent);
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                using (var stream = File.Create(temp)) new XmlSerializer(typeof(T)).Serialize(stream, value);
                if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
            } finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }

    public sealed class AdapterAddress {
        public string Address { get; set; }
        public string Mask { get; set; }
    }
    public sealed class NetworkAdapterInfo {
        public string Id { get; set; }
        public string Name { get; set; }
        public int Index { get; set; }
        public bool Active { get; set; }
        public bool Loopback { get; set; }
        public List<AdapterAddress> Addresses { get; set; }
        public NetworkAdapterInfo() { Addresses = new List<AdapterAddress>(); }
        public override string ToString() {
            return Name + " — " + string.Join(", ", Addresses.Select(a => a.Address)) + (Active ? "" : " [disconnected]");
        }
    }
    public sealed class AddressPlanRow {
        public string Address { get; set; }
        public string Devices { get; set; }
        public string Ports { get; set; }
        public string Status { get; set; }
        public bool Missing { get; set; }
    }
    public sealed class NetworkPlan {
        public List<AddressPlanRow> Rows { get; set; }
        public NetworkAdapterInfo Adapter { get; set; }
        public bool Ready { get { return Rows.All(r => !r.Missing); } }
        public NetworkPlan() { Rows = new List<AddressPlanRow>(); }
    }

    public static class NetworkPlanning {
        public static List<NetworkAdapterInfo> Adapters() {
            var result = new List<NetworkAdapterInfo>();
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces()) {
                try {
                if (!nic.Supports(NetworkInterfaceComponent.IPv4)) continue;
                var properties = nic.GetIPProperties();
                var ipv4 = properties.GetIPv4Properties();
                if (ipv4 == null) continue;
                var adapter = new NetworkAdapterInfo { Id = nic.Id, Name = nic.Name, Index = ipv4.Index, Active = nic.OperationalStatus == OperationalStatus.Up, Loopback = nic.NetworkInterfaceType == NetworkInterfaceType.Loopback || nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel };
                foreach (var address in properties.UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)) {
                    if (address.DuplicateAddressDetectionState != DuplicateAddressDetectionState.Preferred && address.DuplicateAddressDetectionState != DuplicateAddressDetectionState.Deprecated) continue;
                    adapter.Addresses.Add(new AdapterAddress { Address = address.Address.ToString(), Mask = address.IPv4Mask.ToString() });
                }
                result.Add(adapter);
                } catch (NetworkInformationException) { }
            }
            return result;
        }
        public static bool SpecialLocal(string address) {
            IPAddress ip;
            return IPAddress.TryParse(address, out ip) && (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any));
        }
        public static string Canonical(string address) {
            IPAddress ip;
            if (!IPAddress.TryParse(address, out ip) || ip.AddressFamily != AddressFamily.InterNetwork)
                throw new Exception("An IPv4 address is required: " + address);
            return ip.ToString();
        }
        public static bool SameSubnet(string first, string second, string mask) {
            byte[] a = IPAddress.Parse(first).GetAddressBytes(), b = IPAddress.Parse(second).GetAddressBytes(), m = IPAddress.Parse(mask).GetAddressBytes();
            return a.Length == 4 && b.Length == 4 && m.Length == 4 && Enumerable.Range(0, 4).All(i => (a[i] & m[i]) == (b[i] & m[i]));
        }
        public static string Mask(int prefix) {
            if (prefix < 1 || prefix > 32) throw new Exception("Prefix length must be 1..32.");
            uint value = uint.MaxValue << (32 - prefix);
            return new IPAddress(new[] { (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value }).ToString();
        }
        public static int Prefix(string mask) {
            byte[] bytes = IPAddress.Parse(Canonical(mask)).GetAddressBytes();
            int result = 0; bool zero = false;
            foreach (byte value in bytes) for (int bit = 7; bit >= 0; bit--) {
                bool set = (value & (1 << bit)) != 0;
                if (set && zero) throw new Exception("The subnet mask must contain consecutive leading ones.");
                if (set) result++; else zero = true;
            }
            if (result == 0) throw new Exception("Use a subnet mask with prefix 1..32.");
            return result;
        }
        public static void ValidateAlias(string address, int prefix) {
            string canonical = Canonical(address);
            byte[] bytes = IPAddress.Parse(canonical).GetAddressBytes();
            if (bytes[0] == 0 || bytes[0] == 127 || bytes[0] >= 224 || canonical == "255.255.255.255")
                throw new Exception("Only unicast, non-loopback project addresses can be added: " + canonical);
            Mask(prefix);
            if (prefix <= 30) {
                uint value = ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
                uint hosts = uint.MaxValue >> prefix;
                if ((value & hosts) == 0 || (value & hosts) == hosts)
                    throw new Exception(canonical + " is a network or broadcast address with /" + prefix + ".");
            }
        }
        public static NetworkPlan Build(Project project, NetworkOptions options, IList<NetworkAdapterInfo> adapters) {
            var plan = new NetworkPlan();
            var assigned = new HashSet<string>(adapters.SelectMany(a => a.Addresses).Select(a => Canonical(a.Address)));
            foreach (var group in project.Devices.Where(d => d.Enabled && d.Transport == Transport.Tcp).GroupBy(d => Canonical(d.BindAddress))) {
                bool local = SpecialLocal(group.Key) || assigned.Contains(group.Key);
                plan.Rows.Add(new AddressPlanRow { Address = group.Key, Devices = string.Join(", ", group.Select(d => d.Name)), Ports = string.Join(", ", group.Select(d => d.Port).Distinct()), Status = local ? "Already available on this PC" : "Needs a local IP alias", Missing = !local });
            }
            if (!string.IsNullOrEmpty(options.AdapterId)) plan.Adapter = adapters.FirstOrDefault(a => a.Id == options.AdapterId);
            if (plan.Adapter == null) {
                var candidates = adapters.Where(a => a.Active && !a.Loopback && a.Addresses.Count > 0).Select(a => new { Adapter = a, Score = plan.Rows.Count(r => a.Addresses.Any(ip => SameSubnet(r.Address, ip.Address, ip.Mask))) }).ToList();
                if (candidates.Count == 1) plan.Adapter = candidates[0].Adapter;
                else if (candidates.Count > 1) {
                    int maximum = candidates.Max(a => a.Score);
                    var best = candidates.Where(a => a.Score == maximum && maximum > 0).ToList();
                    if (best.Count == 1) plan.Adapter = best[0].Adapter;
                }
            }
            return plan;
        }
        public static string MissingMessage(NetworkPlan plan) {
            return "These project IPs are not assigned to this PC: " + string.Join(", ", plan.Rows.Where(r => r.Missing).Select(r => r.Address)) + ". Open Network Setup to prepare their IP aliases, or change the devices' bind addresses.";
        }
    }

    public sealed class AliasRequest {
        public string Operation { get; set; }
        public string SessionId { get; set; }
        public string AdapterId { get; set; }
        public int PrefixLength { get; set; }
        public bool CheckConflicts { get; set; }
        public List<string> Addresses { get; set; }
        public AliasRequest() { Addresses = new List<string>(); }
        public void Validate() {
            Guid id;
            if (!Guid.TryParse(SessionId, out id)) throw new Exception("Invalid network session.");
            SessionId = id.ToString("D");
            if (Operation != "prepare" && Operation != "cleanup") throw new Exception("Invalid network operation.");
            if (Addresses == null || Addresses.Count > 256) throw new Exception("At most 256 project aliases can be prepared at once.");
            if (Operation == "prepare") {
                if (!Guid.TryParse(AdapterId, out id)) throw new Exception("Select a network adapter.");
                foreach (string address in Addresses) NetworkPlanning.ValidateAlias(address, PrefixLength);
                Addresses = Addresses.Select(NetworkPlanning.Canonical).Distinct().ToList();
            } else if (Addresses.Count != 0) throw new Exception("Cleanup uses the ownership ledger, not a supplied address list.");
        }
    }
    public sealed class AliasRecord {
        public string Address { get; set; }
        public string AdapterId { get; set; }
        public int PrefixLength { get; set; }
        public ulong InterfaceLuid { get; set; }
        public long CreatedAt { get; set; }
    }
    public sealed class AliasLedger {
        public string SessionId { get; set; }
        public List<AliasRecord> Records { get; set; }
        public AliasLedger() { Records = new List<AliasRecord>(); }
    }
    public sealed class AliasResult {
        public bool Success { get; set; }
        public string Error { get; set; }
        public List<string> Messages { get; set; }
        public int ManagedCount { get; set; }
        public AliasResult() { Messages = new List<string>(); }
    }
    public interface IAliasPlatform {
        bool IsLocal(string address);
        void ValidateAdapter(string adapterId);
        bool Conflict(string adapterId, string address);
        AliasRecord Add(string adapterId, string address, int prefix);
        void WaitReady(IList<AliasRecord> records);
        bool Matches(AliasRecord record);
        void Remove(AliasRecord record);
    }
    public interface IAliasLedgerStore {
        AliasLedger Load(string sessionId);
        void Save(AliasLedger ledger);
    }
    public sealed class AliasManager {
        readonly IAliasPlatform platform;
        readonly IAliasLedgerStore store;
        public AliasManager(IAliasPlatform platform, IAliasLedgerStore store) { this.platform = platform; this.store = store; }
        public AliasResult Execute(AliasRequest request) {
            var result = new AliasResult();
            var created = new List<AliasRecord>();
            AliasLedger ledger = null;
            try {
                request.Validate(); ledger = store.Load(request.SessionId);
                if (ledger.SessionId != request.SessionId || ledger.Records == null) throw new Exception("Invalid alias ownership ledger.");
                if (request.Operation == "cleanup") {
                    foreach (var record in ledger.Records.ToList()) {
                        if (platform.Matches(record)) { platform.Remove(record); result.Messages.Add("Removed " + record.Address); }
                        else result.Messages.Add("Left unchanged: " + record.Address + " is no longer the alias created by this session.");
                        ledger.Records.Remove(record); store.Save(ledger);
                    }
                } else {
                    platform.ValidateAdapter(request.AdapterId);
                    var missing = request.Addresses.Where(a => !platform.IsLocal(a)).ToList();
                    if (request.CheckConflicts) {
                        var conflicts = new List<string>();
                        System.Threading.Tasks.Parallel.ForEach(missing, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = 8 }, address => {
                            if (platform.Conflict(request.AdapterId, address)) lock (conflicts) conflicts.Add(address);
                        });
                        if (conflicts.Count > 0) throw new Exception("Addresses already respond on the network: " + string.Join(", ", conflicts) + ". Disconnect the real devices or choose different project IPs.");
                    }
                    foreach (string address in missing) {
                        if (platform.IsLocal(address)) { result.Messages.Add("Already local: " + address); continue; }
                        AliasRecord record = platform.Add(request.AdapterId, address, request.PrefixLength);
                        created.Add(record); ledger.Records.Add(record); store.Save(ledger);
                        result.Messages.Add("Added " + address + "/" + request.PrefixLength);
                    }
                    platform.WaitReady(created);
                    foreach (string address in request.Addresses) if (!platform.IsLocal(address)) throw new Exception("Address is not ready: " + address);
                }
                result.Success = true;
            } catch (Exception error) {
                result.Error = error.GetBaseException().Message;
                foreach (var record in created.AsEnumerable().Reverse()) {
                    try {
                        if (platform.Matches(record)) platform.Remove(record);
                        if (ledger != null) ledger.Records.Remove(record);
                        result.Messages.Add("Rolled back " + record.Address);
                    } catch (Exception rollback) { result.Messages.Add("Cleanup still needed for " + record.Address + ": " + rollback.Message); }
                }
                if (ledger != null) try { store.Save(ledger); } catch (Exception save) { result.Messages.Add("Ownership record could not be saved: " + save.Message); }
            }
            result.ManagedCount = ledger == null ? 0 : ledger.Records.Count;
            return result;
        }
    }
}
