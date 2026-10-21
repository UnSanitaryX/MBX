using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ModbusEmulatorX;

public static class NetworkTests {
    const string AdapterId = "11111111-1111-1111-1111-111111111111";
    const string SecondId = "22222222-2222-2222-2222-222222222222";
    const string SessionId = "33333333-3333-3333-3333-333333333333";
    static void Check(bool value, string message = "Network assertion failed") { if (!value) throw new Exception(message); }
    static void Reject(Action action) { bool rejected = false; try { action(); } catch { rejected = true; } Check(rejected, "Expected rejection"); }
    static NetworkAdapterInfo Adapter(string id = AdapterId, string ip = "192.168.100.10", string mask = "255.255.255.0") {
        return new NetworkAdapterInfo { Id = id, Name = "Test adapter", Index = id == AdapterId ? 10 : 11, Active = true, Addresses = new List<AdapterAddress> { new AdapterAddress { Address = ip, Mask = mask } } };
    }
    static Project ProjectWith(params string[] addresses) {
        var p = new Project(); int unit = 1;
        foreach (string address in addresses) p.Devices.Add(new Device { Name = "Device " + unit, BindAddress = address, UnitId = unit++ });
        return p;
    }
    static AliasRequest Request(params string[] addresses) { return new AliasRequest { Operation = "prepare", SessionId = SessionId, AdapterId = AdapterId, PrefixLength = 24, CheckConflicts = true, Addresses = addresses.ToList() }; }
    static AliasRequest Cleanup() { return new AliasRequest { Operation = "cleanup", SessionId = SessionId }; }
    public static void Run(Action<string, Action> test, string scratch) {
        test("Network: same subnet does not imply exact-address ownership", () => {
            var plan = NetworkPlanning.Build(ProjectWith("192.168.100.101"), new NetworkOptions(), new[] { Adapter() });
            Check(!plan.Ready && plan.Rows.Single().Missing && plan.Adapter.Id == AdapterId);
        });
        test("Network: an existing exact IP requires no alias", () => Check(NetworkPlanning.Build(ProjectWith("192.168.100.10"), new NetworkOptions(), new[] { Adapter() }).Ready));
        test("Network: loopback and wildcard bindings need no adapter changes", () => Check(NetworkPlanning.Build(ProjectWith("127.0.0.77", "0.0.0.0"), new NetworkOptions(), new NetworkAdapterInfo[0]).Ready));
        test("Network: disabled and RTU devices do not request aliases", () => { var p = ProjectWith("192.168.100.101", "192.168.100.102"); p.Devices[0].Enabled = false; p.Devices[1].Transport = Transport.Rtu; Check(NetworkPlanning.Build(p, new NetworkOptions(), new[] { Adapter() }).Rows.Count == 0); });
        test("Network: multiple unit IDs share one address plan entry", () => Check(NetworkPlanning.Build(ProjectWith("192.168.100.101", "192.168.100.101"), new NetworkOptions(), new[] { Adapter() }).Rows.Count == 1));
        test("Network: automatic adapter selection follows the matching subnet", () => Check(NetworkPlanning.Build(ProjectWith("192.168.100.101"), new NetworkOptions(), new[] { Adapter(SecondId, "10.0.0.10"), Adapter() }).Adapter.Id == AdapterId));
        test("Network: ambiguous matching adapters require an explicit choice", () => Check(NetworkPlanning.Build(ProjectWith("192.168.100.101"), new NetworkOptions(), new[] { Adapter(), Adapter(SecondId, "192.168.100.20") }).Adapter == null));
        test("Network: explicit adapter selection overrides inference", () => Check(NetworkPlanning.Build(ProjectWith("192.168.100.101"), new NetworkOptions { AdapterId = SecondId }, new[] { Adapter(), Adapter(SecondId, "10.0.0.10") }).Adapter.Id == SecondId));
        test("Network: stale adapter IDs can be reselected on another PC", () => Check(NetworkPlanning.Build(ProjectWith("192.168.100.101"), new NetworkOptions { AdapterId = SecondId }, new[] { Adapter() }).Adapter.Id == AdapterId));
        test("Network: mask and prefix round trips", () => { for (int i = 1; i <= 32; i++) Check(NetworkPlanning.Prefix(NetworkPlanning.Mask(i)) == i); });
        test("Network: noncontiguous masks are rejected", () => { Reject(() => NetworkPlanning.Prefix("255.0.255.0")); Reject(() => NetworkPlanning.Prefix("0.0.0.0")); });
        test("Network: invalid prefix limits are rejected", () => { Reject(() => NetworkPlanning.Mask(0)); Reject(() => NetworkPlanning.Mask(33)); });
        test("Network: network and broadcast aliases are rejected", () => { Reject(() => NetworkPlanning.ValidateAlias("192.168.100.0", 24)); Reject(() => NetworkPlanning.ValidateAlias("192.168.100.255", 24)); });
        test("Network: unicast /31 and /32 aliases are supported", () => { NetworkPlanning.ValidateAlias("192.168.100.0", 31); NetworkPlanning.ValidateAlias("192.168.100.255", 32); });
        test("Network: wildcard, loopback and multicast aliases cannot be created", () => { foreach (string ip in new[] { "0.0.0.0", "127.0.0.1", "224.0.0.1", "255.255.255.255" }) Reject(() => NetworkPlanning.ValidateAlias(ip, 24)); });
        test("Network: helper rejects invalid operation, session and adapter", () => { var r = Request("192.168.100.101"); r.SessionId = "../unsafe"; Reject(r.Validate); r = Request(); r.Operation = "run-command"; Reject(r.Validate); r = Request(); r.AdapterId = "adapter;command"; Reject(r.Validate); });
        test("Network: helper rejects oversized requests and supplied cleanup addresses", () => { var r = Request(); r.Addresses = Enumerable.Repeat("192.168.100.101", 257).ToList(); Reject(r.Validate); r = Cleanup(); r.Addresses.Add("192.168.100.10"); Reject(r.Validate); });
        test("Network: per-PC options roundtrip independently of projects", () => { string file = Path.Combine(scratch, "network-options.xml"); var o = new NetworkOptions { AdapterId = AdapterId, PrefixLength = 16, CheckConflicts = false, RemoveOnStop = false, PrepareOnStart = false }; o.Save(file); var read = NetworkOptions.Load(file); Check(read.AdapterId == AdapterId && read.PrefixLength == 16 && !read.CheckConflicts && !read.RemoveOnStop && !read.PrepareOnStart && read.SessionId == o.SessionId); });
        test("Network: external XML entities in options are rejected", () => { string file = Path.Combine(scratch, "network-external.xml"); File.WriteAllText(file, "<!DOCTYPE x [<!ENTITY x SYSTEM 'file:///missing'>]><NetworkOptions><AdapterId>&x;</AdapterId></NetworkOptions>"); Reject(() => NetworkOptions.Load(file)); });
        test("Network: prepare adds only missing addresses", () => { var p = new FakePlatform(); p.Existing.Add("192.168.100.10"); var s = new MemoryStore(); var result = new AliasManager(p, s).Execute(Request("192.168.100.10", "192.168.100.101")); Check(result.Success && result.ManagedCount == 1 && p.Added.SequenceEqual(new[] { "192.168.100.101" })); Check(p.Existing.Contains("192.168.100.10")); });
        test("Network: duplicate requests create one alias", () => { var p = new FakePlatform(); var result = new AliasManager(p, new MemoryStore()).Execute(Request("192.168.100.101", "192.168.100.101")); Check(result.Success && p.Added.Count == 1); });
        test("Network: responsive conflicts stop the batch before any changes", () => { var p = new FakePlatform(); p.Conflicts.Add("192.168.100.102"); var result = new AliasManager(p, new MemoryStore()).Execute(Request("192.168.100.101", "192.168.100.102")); Check(!result.Success && p.Added.Count == 0 && result.Error.Contains("192.168.100.102")); });
        test("Network: conflict checks are configurable, readiness checks remain", () => { var p = new FakePlatform(); p.Conflicts.Add("192.168.100.101"); var request = Request("192.168.100.101"); request.CheckConflicts = false; var result = new AliasManager(p, new MemoryStore()).Execute(request); Check(result.Success && p.Probes == 0 && p.ReadinessChecks == 1); });
        test("Network: partial add failure rolls back newly added aliases", () => { var p = new FakePlatform { FailAddress = "192.168.100.103" }; var result = new AliasManager(p, new MemoryStore()).Execute(Request("192.168.100.101", "192.168.100.102", "192.168.100.103")); Check(!result.Success && result.ManagedCount == 0); Check(p.Removed.SequenceEqual(new[] { "192.168.100.102", "192.168.100.101" })); });
        test("Network: failed duplicate-address detection rolls back the batch", () => { var p = new FakePlatform { FailReady = true }; var result = new AliasManager(p, new MemoryStore()).Execute(Request("192.168.100.101", "192.168.100.102")); Check(!result.Success && result.ManagedCount == 0 && p.Removed.Count == 2); });
        test("Network: failed ownership persistence rolls back the new alias", () => { var p = new FakePlatform(); var s = new MemoryStore { FailNextSave = true }; var result = new AliasManager(p, s).Execute(Request("192.168.100.101")); Check(!result.Success && result.ManagedCount == 0 && p.Removed.Count == 1); });
        test("Network: rollback preserves pre-existing owned aliases", () => { var p = new FakePlatform(); var s = new MemoryStore(); var manager = new AliasManager(p, s); Check(manager.Execute(Request("192.168.100.101")).Success); p.FailAddress = "192.168.100.103"; var result = manager.Execute(Request("192.168.100.102", "192.168.100.103")); Check(!result.Success && result.ManagedCount == 1 && p.IsLocal("192.168.100.101")); });
        test("Network: cleanup removes only aliases in this session ledger", () => { var p = new FakePlatform(); p.Existing.Add("192.168.100.10"); var s = new MemoryStore(); var manager = new AliasManager(p, s); Check(manager.Execute(Request("192.168.100.101")).Success); var result = manager.Execute(Cleanup()); Check(result.Success && result.ManagedCount == 0 && p.Removed.SequenceEqual(new[] { "192.168.100.101" }) && p.IsLocal("192.168.100.10")); });
        test("Network: changed ownership timestamps prevent deletion", () => { var p = new FakePlatform(); var s = new MemoryStore(); var manager = new AliasManager(p, s); manager.Execute(Request("192.168.100.101")); p.Owned["192.168.100.101"].CreatedAt++; var result = manager.Execute(Cleanup()); Check(result.Success && p.Removed.Count == 0 && p.IsLocal("192.168.100.101")); });
        test("Network: changed alias prefixes prevent deletion", () => { var p = new FakePlatform(); var s = new MemoryStore(); var manager = new AliasManager(p, s); manager.Execute(Request("192.168.100.101")); p.Owned["192.168.100.101"].PrefixLength = 16; Check(manager.Execute(Cleanup()).Success && p.Removed.Count == 0); });
        test("Network: cleanup failure retains ownership for a retry", () => { var p = new FakePlatform(); var s = new MemoryStore(); var manager = new AliasManager(p, s); manager.Execute(Request("192.168.100.101")); p.FailRemove = true; var result = manager.Execute(Cleanup()); Check(!result.Success && result.ManagedCount == 1); p.FailRemove = false; Check(manager.Execute(Cleanup()).Success && s.Ledger.Records.Count == 0); });
        test("Network: repeated preparation is idempotent", () => { var p = new FakePlatform(); var manager = new AliasManager(p, new MemoryStore()); Check(manager.Execute(Request("192.168.100.101")).Success); Check(manager.Execute(Request("192.168.100.101")).Success && p.Added.Count == 1); });
        test("Network: a missing adapter causes no mutations", () => { var p = new FakePlatform { FailAdapter = true }; var result = new AliasManager(p, new MemoryStore()).Execute(Request("192.168.100.101")); Check(!result.Success && p.Added.Count == 0 && p.Probes == 0); });
        test("Network: malformed helper arguments are rejected without elevation", () => { Check(NetworkElevation.HelperMain(new[] { "--network-helper", "arbitrary-pipe", "payload" }) == 2); Check(NetworkElevation.HelperMain(new string[0]) == 2); });
        test("Network: elevation uses the exact executable and an encoded request", () => { string app = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ModbusEmulatorX.exe"); string channel = "ModbusEmulatorXNet_" + new string('a',32); var request = Request("192.168.100.101"); var info = NetworkElevation.LaunchInfo(app,request,channel); Check(info.FileName==app && info.UseShellExecute && info.Verb=="runas" && info.WindowStyle==System.Diagnostics.ProcessWindowStyle.Hidden); var parts=info.Arguments.Split(' '); Check(parts.Length==3&&parts[0]=="--network-helper"&&parts[1]==channel); using(var stream=new MemoryStream(Convert.FromBase64String(parts[2]))){var decoded=NetworkXml.Read<AliasRequest>(stream);Check(decoded.SessionId==SessionId&&decoded.Addresses.Single()=="192.168.100.101");} });
        test("Network: launch rejects arbitrary channels and executable paths",()=>{Reject(()=>NetworkElevation.LaunchInfo("missing.exe",Request(),"anything"));Reject(()=>NetworkElevation.LaunchInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ModbusEmulatorX.exe"),Request(),"pipe;command"));});
        test("Network: native row layout matches the Windows ABI", () => { Check(Marshal.SizeOf(typeof(NativeUnicastRow)) == 80); Check((int)Marshal.OffsetOf(typeof(NativeUnicastRow), "InterfaceLuid") == 32 && (int)Marshal.OffsetOf(typeof(NativeUnicastRow), "CreatedAt") == 72); });
        test("Network: native ownership requires the exact alias identity",()=>{var record=new AliasRecord{Address="192.168.100.101",InterfaceLuid=10,CreatedAt=42,PrefixLength=24};var row=new NativeUnicastRow{Family=2,Address=BitConverter.ToUInt32(System.Net.IPAddress.Parse(record.Address).GetAddressBytes(),0),InterfaceLuid=10,CreatedAt=42,PrefixLength=24,SkipAsSource=1,PrefixOrigin=1,SuffixOrigin=1};Check(WindowsAliasPlatform.Owns(record,row));row.CreatedAt=43;Check(!WindowsAliasPlatform.Owns(record,row));row.CreatedAt=42;row.InterfaceLuid=11;Check(!WindowsAliasPlatform.Owns(record,row));});
        test("Network: primary addresses and unverifiable timestamps are never owned",()=>{var record=new AliasRecord{Address="192.168.100.101",InterfaceLuid=10,CreatedAt=42,PrefixLength=24};var row=new NativeUnicastRow{Family=2,Address=BitConverter.ToUInt32(System.Net.IPAddress.Parse(record.Address).GetAddressBytes(),0),InterfaceLuid=10,CreatedAt=42,PrefixLength=24,SkipAsSource=0,PrefixOrigin=1,SuffixOrigin=1};Check(!WindowsAliasPlatform.Owns(record,row));row.SkipAsSource=1;row.CreatedAt=0;record.CreatedAt=0;Check(!WindowsAliasPlatform.Owns(record,row));});
        test("Network: read-only Windows API query validates the native binding", () => { var adapter = NetworkPlanning.Adapters().First(a => a.Addresses.Count > 0); var ip = adapter.Addresses[0]; var row = WindowsAliasPlatform.Inspect(ip.Address, adapter.Index); Check(row.Family == 2 && row.InterfaceIndex == adapter.Index && row.InterfaceLuid != 0); Check(row.Address == BitConverter.ToUInt32(System.Net.IPAddress.Parse(ip.Address).GetAddressBytes(), 0)); });
        test("Network: setup form can be constructed without showing a window", () => { using (var f = new NetworkSetupForm(ProjectWith("127.0.0.1"), new NetworkOptions(), false, message => { })) { Check(!f.IsHandleCreated); Check(f.Controls.Find("NetworkAdapter", true).Length == 1 && f.Controls.Find("NetworkSubnetMask", true).Length == 1 && f.Controls.Find("PrepareOnStart", true).Length == 1); } });
        test("Network: running servers lock address changes but allow cleanup preferences",()=>{using(var f=new NetworkSetupForm(ProjectWith("192.168.100.101"),new NetworkOptions(),false,message=>{},true)){Check(!f.Controls.Find("NetworkAdapter",true).Single().Enabled);Check(!f.Controls.Find("PrepareAddresses",true).Single().Enabled);Check(!f.Controls.Find("RemoveOwnedAliases",true).Single().Enabled);Check(f.Controls.Find("RemoveAliasesOnStop",true).Single().Enabled);}});
        test("Network: startup errors identify missing addresses and the remedy", () => { var p = ProjectWith("192.0.2.243"); var plan = NetworkPlanning.Build(p, new NetworkOptions(), NetworkPlanning.Adapters()); if (plan.Ready) return; using (var engine = new Engine()) { try { engine.Start(p); throw new Exception("Expected missing-address error"); } catch (Exception error) { Check(error.Message.Contains("192.0.2.243") && error.Message.Contains("Network Setup")); Check(!engine.Running); } } });
    }
    sealed class MemoryStore : IAliasLedgerStore {
        public AliasLedger Ledger = new AliasLedger { SessionId = SessionId };
        public bool FailNextSave;
        public AliasLedger Load(string sessionId) { return Copy(Ledger); }
        public void Save(AliasLedger ledger) { if (FailNextSave) { FailNextSave = false; throw new IOException("Simulated write failure"); } Ledger = Copy(ledger); }
        static AliasLedger Copy(AliasLedger ledger) { return new AliasLedger { SessionId = ledger.SessionId, Records = ledger.Records.Select(Clone).ToList() }; }
    }
    static AliasRecord Clone(AliasRecord r) { return new AliasRecord { Address = r.Address, AdapterId = r.AdapterId, PrefixLength = r.PrefixLength, InterfaceLuid = r.InterfaceLuid, CreatedAt = r.CreatedAt }; }
    sealed class FakePlatform : IAliasPlatform {
        public readonly HashSet<string> Existing = new HashSet<string>();
        public readonly HashSet<string> Conflicts = new HashSet<string>();
        public readonly Dictionary<string, AliasRecord> Owned = new Dictionary<string, AliasRecord>();
        public readonly List<string> Added = new List<string>();
        public readonly List<string> Removed = new List<string>();
        public string FailAddress;
        public bool FailReady, FailRemove, FailAdapter;
        public int Probes, ReadinessChecks;
        public bool IsLocal(string address) { return Existing.Contains(address) || Owned.ContainsKey(address); }
        public void ValidateAdapter(string id) { if (FailAdapter) throw new Exception("Adapter disconnected"); }
        public bool Conflict(string adapter, string address) { System.Threading.Interlocked.Increment(ref Probes); return Conflicts.Contains(address); }
        public AliasRecord Add(string adapter, string address, int prefix) { if (address == FailAddress) throw new Exception("Simulated add failure"); var r = new AliasRecord { Address = address, AdapterId = adapter, PrefixLength = prefix, InterfaceLuid = 10, CreatedAt = Added.Count + 1 }; Owned.Add(address, Clone(r)); Added.Add(address); return r; }
        public void WaitReady(IList<AliasRecord> records) { ReadinessChecks++; if (FailReady) throw new Exception("Duplicate address detected"); }
        public bool Matches(AliasRecord record) { AliasRecord own; return Owned.TryGetValue(record.Address, out own) && own.CreatedAt == record.CreatedAt && own.PrefixLength == record.PrefixLength && own.InterfaceLuid == record.InterfaceLuid; }
        public void Remove(AliasRecord record) { if (FailRemove) throw new Exception("Simulated remove failure"); Removed.Add(record.Address); Owned.Remove(record.Address); }
    }
}
