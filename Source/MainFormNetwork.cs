using System;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ModbusEmulatorX {
    public partial class MainForm {
        NetworkOptions networkOptions;
        bool networkBusy;
        bool closingAfterCleanup;

        NetworkOptions LoadNetworkOptions() {
            try { return NetworkOptions.Load(NetworkOptions.SettingsPath); }
            catch (Exception error) { pending.Enqueue("NETWORK Saved settings could not be loaded: " + error.Message); return new NetworkOptions(); }
        }
        void OpenNetworkSetup() {
            if (networkBusy) throw new Exception("Wait for the current network operation before opening Network Setup.");
            using (var setup = new NetworkSetupForm(project, networkOptions, false, message => pending.Enqueue(message), engine.Running)) setup.ShowDialog(this);
        }
        async void Start() {
            if (engine.Running) return;
            if (networkBusy) { MessageBox.Show(this, "Wait for network preparation or cleanup to finish.", "Network Setup"); return; }
            string failure = null;
            try {
                Commit(); project.Validate();
                var plan = NetworkPlanning.Build(project, networkOptions, NetworkPlanning.Adapters());
                if (!plan.Ready) {
                    if (!networkOptions.PrepareOnStart) throw new Exception(NetworkPlanning.MissingMessage(plan));
                    using (var setup = new NetworkSetupForm(project, networkOptions, true, message => pending.Enqueue(message))) {
                        if (setup.ShowDialog(this) != DialogResult.OK) return;
                    }
                }
                engine.Start(project); SetConfiguration(false); pending.Enqueue("Project started: " + project.Name);
            } catch (Exception error) {
                engine.Stop(); SetConfiguration(true);
                failure = error.GetBaseException().Message;
            }
            if (failure == null) return;
            try { await CleanupOwnedAliases(); } catch (Exception cleanup) { failure += "\r\nNetwork cleanup: " + cleanup.GetBaseException().Message; }
            MessageBox.Show(this, failure, "Modbus Emulator X", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        async void StopServersCommand() {
            if (networkBusy) return;
            try { Stop(); await CleanupOwnedAliases(); }
            catch (Exception error) { MessageBox.Show(this, error.GetBaseException().Message, "Modbus Emulator X", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
        async Task CleanupOwnedAliases() {
            if (!networkOptions.RemoveOnStop || ProtectedAliasStore.CountOwned(networkOptions.SessionId) == 0) return;
            if (networkBusy) throw new Exception("Another network operation is still running.");
            networkBusy = true; SetConfiguration(false);
            try {
                pending.Enqueue("NETWORK Removing aliases created by this app...");
                AliasResult result = await Task.Run(() => NetworkElevation.Run(Application.ExecutablePath, NetworkElevation.CleanupRequest(networkOptions)));
                foreach (string message in result.Messages) pending.Enqueue("NETWORK " + message);
                if (!result.Success) throw new Exception(result.Error ?? "Some managed aliases could not be removed. Use Network Setup > Remove my aliases to retry.");
            } finally { networkBusy = false; SetConfiguration(!engine.Running && !simulate); }
        }
        async void ClosingWithNetworkCleanup(object sender, FormClosingEventArgs args) {
            if (closingAfterCleanup) return;
            args.Cancel = true;
            if (networkBusy) { MessageBox.Show(this, "Wait for the current network operation to finish before closing.", "Network Setup"); return; }
            if (!ConfirmDiscard()) return;
            timer.Stop();
            try { Stop(); await CleanupOwnedAliases(); }
            catch (Exception error) { MessageBox.Show(this, "Servers have stopped. " + error.GetBaseException().Message + "\r\nAny retained temporary aliases can be removed in Network Setup or will disappear when Windows restarts.", "Network cleanup", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            closingAfterCleanup = true; Close();
        }
        void ShowHelp() {
            MessageBox.Show(this, "Configure devices and points, then start servers. Import .xlsx/.csv projects or open .mbx projects.\r\n\r\nNetwork Setup manages the project's exact local IP addresses. Select the test-network adapter and prefix, then prepare missing addresses. Windows administrator approval is required. Existing adapter settings and pre-existing addresses are preserved.\r\n\r\nChoose automatic preparation prompts on Start, conflict checks and removal of app-created aliases on Stop. Settings are saved per PC, separately from portable projects.\r\n\r\nScenarios & faults: create a timeline, add timed value changes, ramps or device faults, then start servers and run it. Pause freezes timeline time while current faults remain active. Stop restores starting values and faults. Timelines are retained in .MBX and .XLSX; CSV contains devices and points only.\r\n\r\nWilliam Keay · 2026", "Modbus Emulator X", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
