using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;

namespace ModbusEmulatorX {
    [StructLayout(LayoutKind.Explicit, Size = 80)]
    public struct NativeUnicastRow {
        [FieldOffset(0)] public ushort Family;
        [FieldOffset(2)] public ushort Port;
        [FieldOffset(4)] public uint Address;
        [FieldOffset(32)] public ulong InterfaceLuid;
        [FieldOffset(40)] public uint InterfaceIndex;
        [FieldOffset(44)] public int PrefixOrigin;
        [FieldOffset(48)] public int SuffixOrigin;
        [FieldOffset(52)] public uint ValidLifetime;
        [FieldOffset(56)] public uint PreferredLifetime;
        [FieldOffset(60)] public byte PrefixLength;
        [FieldOffset(61)] public byte SkipAsSource;
        [FieldOffset(64)] public int DadState;
        [FieldOffset(68)] public uint ScopeId;
        [FieldOffset(72)] public long CreatedAt;
    }

    public sealed class WindowsAliasPlatform : IAliasPlatform {
        [DllImport("iphlpapi.dll", ExactSpelling = true)] static extern void InitializeUnicastIpAddressEntry(out NativeUnicastRow row);
        [DllImport("iphlpapi.dll", ExactSpelling = true)] static extern uint GetUnicastIpAddressEntry(ref NativeUnicastRow row);
        [DllImport("iphlpapi.dll", ExactSpelling = true)] static extern uint CreateUnicastIpAddressEntry(ref NativeUnicastRow row);
        [DllImport("iphlpapi.dll", ExactSpelling = true)] static extern uint DeleteUnicastIpAddressEntry(ref NativeUnicastRow row);
        [DllImport("iphlpapi.dll", ExactSpelling = true)] static extern uint SendARP(uint destination, uint source, byte[] hardwareAddress, ref uint length);

        static uint Ip(string address) { return BitConverter.ToUInt32(IPAddress.Parse(NetworkPlanning.Canonical(address)).GetAddressBytes(), 0); }
        static NativeUnicastRow Row(string address, int index) {
            NativeUnicastRow row; InitializeUnicastIpAddressEntry(out row);
            row.Family = 2; row.Address = Ip(address); row.InterfaceIndex = checked((uint)index); row.InterfaceLuid = 0;
            return row;
        }
        static NetworkAdapterInfo Adapter(string id) {
            var adapter = NetworkPlanning.Adapters().SingleOrDefault(a => a.Id == id);
            if (adapter == null || adapter.Loopback || !adapter.Active) throw new Exception("The selected network adapter is unavailable or disconnected. Choose an active adapter in Network Setup.");
            return adapter;
        }
        public void ValidateAdapter(string id) { Adapter(id); }
        public bool IsLocal(string address) {
            return NetworkPlanning.SpecialLocal(address) || NetworkPlanning.Adapters().Any(a => a.Addresses.Any(ip => ip.Address == address));
        }
        public bool Conflict(string adapterId, string address) {
            var adapter = Adapter(adapterId);
            if (IsLocal(address)) return false;
            try { using (var ping = new Ping()) if (ping.Send(address, 500).Status == IPStatus.Success) return true; }
            catch (PingException) { }
            var source = adapter.Addresses.FirstOrDefault(a => NetworkPlanning.SameSubnet(a.Address, address, a.Mask));
            byte[] mac = new byte[6]; uint length = 6;
            uint result = SendARP(Ip(address), source == null ? 0 : Ip(source.Address), mac, ref length);
            return result == 0 && length > 0 && mac.Any(b => b != 0);
        }
        public AliasRecord Add(string adapterId, string address, int prefix) {
            NetworkPlanning.ValidateAlias(address, prefix);
            var adapter = Adapter(adapterId);
            var row = Row(address, adapter.Index);
            row.PrefixOrigin = 1; row.SuffixOrigin = 1;
            row.PrefixLength = checked((byte)prefix); row.SkipAsSource = 1;
            row.ValidLifetime = uint.MaxValue; row.PreferredLifetime = uint.MaxValue;
            uint result = CreateUnicastIpAddressEntry(ref row);
            if (result != 0) throw new Win32Exception((int)result, "Could not add " + address + ": " + new Win32Exception((int)result).Message);
            var query = Row(address, adapter.Index);
            result = GetUnicastIpAddressEntry(ref query);
            if (result != 0) {
                DeleteUnicastIpAddressEntry(ref row);
                throw new Win32Exception((int)result, "Could not read back the new alias " + address + ".");
            }
            return new AliasRecord { Address = address, AdapterId = adapterId, InterfaceLuid = query.InterfaceLuid, PrefixLength = prefix, CreatedAt = query.CreatedAt };
        }
        static bool Query(AliasRecord record, out NativeUnicastRow row) {
            row = Row(record.Address, 0); row.InterfaceLuid = record.InterfaceLuid;
            uint result = GetUnicastIpAddressEntry(ref row);
            if (result == 1168 || result == 2 || result == 87) return false;
            if (result != 0) throw new Win32Exception((int)result);
            return true;
        }
        public bool Matches(AliasRecord record) {
            NetworkPlanning.ValidateAlias(record.Address, record.PrefixLength);
            NativeUnicastRow current;
            return Query(record, out current) && Owns(record, current);
        }
        public static bool Owns(AliasRecord record, NativeUnicastRow current) {
            return record.CreatedAt != 0 && current.Family == 2 && current.Address == Ip(record.Address) &&
                current.InterfaceLuid == record.InterfaceLuid && current.CreatedAt == record.CreatedAt &&
                current.SkipAsSource != 0 && current.PrefixOrigin == 1 && current.SuffixOrigin == 1 && current.PrefixLength == record.PrefixLength;
        }
        public void WaitReady(IList<AliasRecord> records) {
            DateTime deadline = DateTime.UtcNow.AddSeconds(15);
            while (true) {
                bool ready = true;
                foreach (var record in records) {
                    NativeUnicastRow current;
                    if (!Query(record, out current)) throw new Exception("The new IP alias disappeared: " + record.Address);
                    if (current.DadState == 2) throw new Exception("Windows detected a duplicate address: " + record.Address);
                    if (current.DadState != 4) ready = false;
                }
                if (ready) return;
                if (DateTime.UtcNow >= deadline) throw new Exception("Windows did not finish checking the new IP addresses. The new aliases have been rolled back.");
                Thread.Sleep(150);
            }
        }
        public void Remove(AliasRecord record) {
            if (!Matches(record)) return;
            NativeUnicastRow row; if (!Query(record, out row)) return;
            uint result = DeleteUnicastIpAddressEntry(ref row);
            if (result != 0 && result != 1168) throw new Win32Exception((int)result, "Could not remove " + record.Address + ".");
        }
        public static NativeUnicastRow Inspect(string address, int interfaceIndex) {
            var row = Row(address, interfaceIndex);
            uint result = GetUnicastIpAddressEntry(ref row);
            if (result != 0) throw new Win32Exception((int)result);
            return row;
        }
    }

    public sealed class ProtectedAliasStore : IAliasLedgerStore {
        readonly string root;
        public ProtectedAliasStore() {
            root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ModbusEmulatorX", "NetworkAliases");
            string parent = Path.GetDirectoryName(root);
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(true, false);
            foreach (var sid in new[] { WellKnownSidType.BuiltinAdministratorsSid, WellKnownSidType.LocalSystemSid })
                security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.ReadAndExecute, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            security.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
            foreach (string directory in new[] { parent, root }) {
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory, security);
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new Exception("The network ownership folder cannot be a link.");
                var owner = (SecurityIdentifier)Directory.GetAccessControl(directory).GetOwner(typeof(SecurityIdentifier));
                if (!owner.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid) && !owner.IsWellKnown(WellKnownSidType.LocalSystemSid)) throw new Exception("The network ownership folder is not owned by the administrator helper.");
                Directory.SetAccessControl(directory, security);
            }
        }
        string FilePath(string sessionId) {
            Guid guid; if (!Guid.TryParse(sessionId, out guid)) throw new Exception("Invalid network session.");
            return Path.Combine(root, guid.ToString("D") + ".xml");
        }
        public AliasLedger Load(string sessionId) {
            string path = FilePath(sessionId);
            if (!File.Exists(path)) return new AliasLedger { SessionId = sessionId };
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new Exception("Alias ownership records cannot be links.");
            var owner = (SecurityIdentifier)File.GetAccessControl(path).GetOwner(typeof(SecurityIdentifier));
            if (!owner.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid) && !owner.IsWellKnown(WellKnownSidType.LocalSystemSid)) throw new Exception("The alias ownership record is not owned by the administrator helper.");
            var ledger = NetworkXml.Read<AliasLedger>(path);
            if (ledger.Records == null || ledger.Records.Count > 4096 || ledger.SessionId != sessionId) throw new Exception("Invalid network ownership data.");
            foreach (var record in ledger.Records) {
                if (record == null || record.InterfaceLuid == 0) throw new Exception("Invalid alias ownership entry.");
                NetworkPlanning.ValidateAlias(record.Address, record.PrefixLength);
            }
            return ledger;
        }
        public void Save(AliasLedger ledger) {
            string path = FilePath(ledger.SessionId);
            NetworkXml.Write(path, ledger);
            var security = File.GetAccessControl(path);
            security.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
            File.SetAccessControl(path, security);
        }
        public static int CountOwned(string sessionId) {
            Guid id; if (!Guid.TryParse(sessionId, out id)) return 0;
            string file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ModbusEmulatorX", "NetworkAliases", id.ToString("D") + ".xml");
            if (!File.Exists(file)) return 0;
            return NetworkXml.Read<AliasLedger>(file).Records.Count;
        }
    }
}

