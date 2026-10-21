using System;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ModbusEmulatorX {
    public sealed class NetworkSetupForm : Form {
        readonly Project project;
        readonly NetworkOptions options;
        readonly bool startRequested;
        readonly bool serversRunning;
        readonly Action<string> log;
        readonly ComboBox adapters = new ComboBox();
        readonly NumericUpDown prefix = new NumericUpDown();
        readonly CheckBox automatic = new CheckBox();
        readonly CheckBox conflicts = new CheckBox();
        readonly CheckBox cleanup = new CheckBox();
        readonly TextBox mask = new TextBox();
        readonly Label summary = new Label();
        readonly LivePointGrid grid = new LivePointGrid();
        readonly Button prepare = new Button();
        readonly Button remove = new Button();
        readonly Button save = new Button();
        readonly Button existing = new Button();
        bool busy;
        NetworkPlan plan;

        public NetworkSetupForm(Project project, NetworkOptions options, bool startRequested, Action<string> log, bool serversRunning = false) {
            this.project = project; this.options = options; this.startRequested = startRequested; this.log = log;
            this.serversRunning = serversRunning;
            Text = "Network Setup — project IP addresses"; Name = "NetworkSetup";
            Width = 1000; Height = 680; MinimumSize = new Size(820, 580);
            StartPosition = FormStartPosition.CenterParent; Font = new Font("Segoe UI", 9);
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 7 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            Controls.Add(root);
            root.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "The PC must own each IP address used by a simulated TCP device. Choose the test-network adapter and review the addresses below. Preparation adds temporary secondary addresses; existing primary addresses, gateway, DNS and DHCP settings are retained.", AutoSize = false }, 0, 0);
            var connection = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            connection.Controls.Add(new Label { Text = "Adapter", AutoSize = true, Margin = new Padding(0, 8, 8, 0) });
            adapters.Name = "NetworkAdapter"; adapters.DropDownStyle = ComboBoxStyle.DropDownList; adapters.Width = 350; adapters.DropDownWidth = 750;
            connection.Controls.Add(adapters);
            connection.Controls.Add(new Label { Text = "Prefix /", AutoSize = true, Margin = new Padding(12, 8, 2, 0) });
            prefix.Name = "NetworkPrefix"; prefix.Minimum = 1; prefix.Maximum = 32; prefix.Width = 48; prefix.Value = options.PrefixLength;
            connection.Controls.Add(prefix); mask.Name = "NetworkSubnetMask"; mask.Width = 125; mask.Margin = new Padding(8, 3, 0, 0); connection.Controls.Add(mask);
            var refresh = new Button { Text = "Refresh", AutoSize = true, Margin = new Padding(8, 3, 0, 0) }; connection.Controls.Add(refresh);
            refresh.Click += (sender, args) => { if (!busy) try { RefreshAdapters(); } catch (Exception error) { ShowError(error); } };
            root.Controls.Add(connection, 0, 1);
            var choices = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, Padding = new Padding(0, 0, 0, 8), FlowDirection = FlowDirection.TopDown, WrapContents = false };
            automatic.Name = "PrepareOnStart"; automatic.Text = "Offer to prepare missing project addresses when starting servers"; automatic.AutoSize = true; automatic.Checked = options.PrepareOnStart;
            conflicts.Name = "CheckAddressConflicts"; conflicts.Text = "Check for responding devices before adding addresses"; conflicts.AutoSize = true; conflicts.Checked = options.CheckConflicts;
            cleanup.Name = "RemoveAliasesOnStop"; cleanup.Text = "Remove this app's created aliases when servers stop or the app closes"; cleanup.AutoSize = true; cleanup.Checked = options.RemoveOnStop;
            choices.Controls.AddRange(new Control[] { automatic, conflicts, cleanup }); root.Controls.Add(choices, 0, 2);
            grid.Name = "ProjectNetworkAddresses"; grid.Dock = DockStyle.Fill; grid.AutoGenerateColumns = false; grid.AllowUserToAddRows = false; grid.AllowUserToDeleteRows = false; grid.ReadOnly = true; grid.RowHeadersVisible = false; grid.BackgroundColor = Color.White; grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            foreach (string name in new[] { "Address", "Ports", "Devices", "Status" }) grid.Columns.Add(new DataGridViewTextBoxColumn { Name = name, HeaderText = name, DataPropertyName = name, FillWeight = name == "Devices" ? 150 : name == "Ports" ? 50 : 100 });
            root.Controls.Add(grid, 0, 3); summary.Dock = DockStyle.Fill; summary.Padding = new Padding(0, 8, 0, 0); root.Controls.Add(summary, 0, 4);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            prepare.Text = startRequested ? "Prepare addresses & start" : "Prepare addresses"; prepare.Name = "PrepareAddresses"; prepare.AutoSize = true; prepare.Height = 32;
            remove.Text = "Remove my aliases"; remove.Name = "RemoveOwnedAliases"; remove.AutoSize = true; remove.Height = 32;
            existing.Text = "Start with current addresses"; existing.AutoSize = true; existing.Height = 32; existing.Visible = startRequested;
            save.Text = "Save settings & close"; save.AutoSize = true; save.Height = 32;
            actions.Controls.AddRange(new Control[] { prepare, remove, existing, save }); root.Controls.Add(actions, 0, 5);
            root.Controls.Add(new Label { Text = "Administrator approval required. Use an isolated test network; disconnect the real devices.", Dock = DockStyle.Fill, ForeColor = Color.DimGray }, 0, 6);
            var found = NetworkPlanning.Adapters();
            var initial = NetworkPlanning.Build(project, options, found);
            foreach (var adapter in found.Where(a => !a.Loopback)) adapters.Items.Add(adapter);
            if (initial.Adapter != null) adapters.SelectedItem = adapters.Items.Cast<NetworkAdapterInfo>().FirstOrDefault(a => a.Id == initial.Adapter.Id);
            if (initial.Adapter != null && string.IsNullOrEmpty(options.AdapterId)) {
                var matching = initial.Adapter.Addresses.FirstOrDefault(a => initial.Rows.Any(r => NetworkPlanning.SameSubnet(r.Address, a.Address, a.Mask)));
                if (matching != null) prefix.Value = NetworkPlanning.Prefix(matching.Mask);
            }
            adapters.SelectedIndexChanged += (sender, args) => RefreshPlan(); prefix.ValueChanged += (sender, args) => RefreshPlan();
            mask.Leave += (sender, args) => { try { prefix.Value = NetworkPlanning.Prefix(mask.Text); RefreshPlan(); } catch (Exception error) { ShowError(error); mask.Text = NetworkPlanning.Mask((int)prefix.Value); } };
            prepare.Click += async (sender, args) => await ChangeAddresses(false);
            remove.Click += async (sender, args) => await ChangeAddresses(true);
            save.Click += (sender, args) => { try { SaveOptions(); DialogResult = DialogResult.Cancel; Close(); } catch (Exception error) { ShowError(error); } };
            existing.Click += (sender, args) => { try { SaveOptions(); RefreshPlan(); if (!plan.Ready) throw new Exception(NetworkPlanning.MissingMessage(plan)); DialogResult = DialogResult.OK; Close(); } catch (Exception error) { ShowError(error); } };
            FormClosing += (sender, args) => { if (busy) args.Cancel = true; };
            adapters.Enabled = !serversRunning; prefix.Enabled = !serversRunning; mask.Enabled = !serversRunning;
            RefreshPlan();
        }
        void SaveOptions() {
            options.AdapterId = adapters.SelectedItem is NetworkAdapterInfo ? ((NetworkAdapterInfo)adapters.SelectedItem).Id : "";
            options.PrefixLength = (int)prefix.Value; options.PrepareOnStart = automatic.Checked;
            options.CheckConflicts = conflicts.Checked; options.RemoveOnStop = cleanup.Checked;
            options.Save(NetworkOptions.SettingsPath);
        }
        void RefreshPlan() {
            var selected = adapters.SelectedItem as NetworkAdapterInfo;
            var chosen = new NetworkOptions { AdapterId = selected == null ? "" : selected.Id, PrefixLength = (int)prefix.Value };
            plan = NetworkPlanning.Build(project, chosen, NetworkPlanning.Adapters());
            if (selected == null) plan.Adapter = null;
            grid.DataSource = plan.Rows;
            mask.Text = NetworkPlanning.Mask((int)prefix.Value);
            int owned = ProtectedAliasStore.CountOwned(options.SessionId);
            summary.Text = (plan.Ready ? "All configured TCP addresses are already available on this PC." : plan.Rows.Count(r => r.Missing) + " missing project addresses. Review the adapter and mask, then prepare them.") + "  Managed aliases: " + owned + "." + (serversRunning ? " Stop servers before changing addresses. Cleanup preferences can be saved now." : "");
            prepare.Enabled = !busy && !serversRunning && !plan.Ready && plan.Adapter != null && plan.Adapter.Active;
            remove.Enabled = !busy && !serversRunning && owned > 0; existing.Enabled = !busy && plan.Ready;
        }
        void RefreshAdapters() {
            string selectedId = adapters.SelectedItem is NetworkAdapterInfo ? ((NetworkAdapterInfo)adapters.SelectedItem).Id : options.AdapterId;
            var found = NetworkPlanning.Adapters();
            var suggested = NetworkPlanning.Build(project, new NetworkOptions { AdapterId = selectedId }, found);
            adapters.BeginUpdate();
            try {
                adapters.Items.Clear();
                foreach (var adapter in found.Where(a => !a.Loopback)) adapters.Items.Add(adapter);
                if (suggested.Adapter != null) adapters.SelectedItem = adapters.Items.Cast<NetworkAdapterInfo>().FirstOrDefault(a => a.Id == suggested.Adapter.Id);
            } finally { adapters.EndUpdate(); }
            RefreshPlan();
        }
        async Task ChangeAddresses(bool removing) {
            try {
                if (serversRunning) throw new Exception("Stop servers before adding or removing IP addresses.");
                SaveOptions();
                if (!removing) {
                    RefreshPlan();
                    if (plan.Ready) return;
                    if (plan.Adapter == null) throw new Exception("Choose an active network adapter.");
                }
                AliasRequest request = removing ? NetworkElevation.CleanupRequest(options) : NetworkElevation.PrepareRequest(project, options, plan);
                request.Validate(); SetBusy(true);
                summary.Text = removing ? "Removing only aliases recorded as created by this app..." : "Waiting for Windows approval, checking conflicts and preparing temporary addresses...";
                AliasResult result = await Task.Run(() => NetworkElevation.Run(Application.ExecutablePath, request));
                foreach (string message in result.Messages) log("NETWORK " + message);
                if (!result.Success) throw new Exception(result.Error ?? "The network operation did not complete.");
                SetBusy(false); RefreshPlan();
                if (!removing && startRequested) {
                    if (!plan.Ready) throw new Exception(NetworkPlanning.MissingMessage(plan));
                    DialogResult = DialogResult.OK; Close();
                }
            } catch (Exception error) { SetBusy(false); RefreshPlan(); ShowError(error); }
        }
        void SetBusy(bool value) {
            busy = value; adapters.Enabled = !value && !serversRunning; prefix.Enabled = !value && !serversRunning; mask.Enabled = !value && !serversRunning; automatic.Enabled = !value;
            conflicts.Enabled = !value; cleanup.Enabled = !value; save.Enabled = !value;
            prepare.Enabled = !value && !serversRunning && !plan.Ready; remove.Enabled = !value && !serversRunning; existing.Enabled = !value && plan.Ready;
            UseWaitCursor = value;
        }
        void ShowError(Exception error) { MessageBox.Show(this, error.GetBaseException().Message, "Network Setup", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }
}
