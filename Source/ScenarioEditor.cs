using System;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ModbusEmulatorX {
public sealed class TrafficLogBox : TextBox {
 public event EventHandler Browsing;
 protected override void WndProc(ref Message m) {
  if(m.Msg==0x115||m.Msg==0x114||m.Msg==0x20A||m.Msg==0x20E||m.Msg==0x201||m.Msg==0x100) {
   var handler=Browsing;if(handler!=null)handler(this,EventArgs.Empty);
  }
  base.WndProc(ref m);
 }
}
public class ScenarioPointRow : ScenarioValue { public bool Include {get;set;} }
public class ScenarioDeviceRow : DeviceState { public bool Include {get;set;} }
public sealed class ScenarioDraft {
 readonly Project context;
 readonly string originalName;
 public BindingList<ScenarioPointRow> Points {get;private set;}
 public BindingList<ScenarioDeviceRow> Devices {get;private set;}
 public ScenarioDraft(Project project,Scenario original) {
  context=project.Snapshot();originalName=original==null?null:original.Name;
  Points=new BindingList<ScenarioPointRow>();Devices=new BindingList<ScenarioDeviceRow>();
  foreach(var d in context.Devices) {
   var state=original==null?null:original.Devices.FirstOrDefault(x=>x.Name==d.Name);
   Devices.Add(new ScenarioDeviceRow{Include=state!=null,Name=d.Name,Responding=state==null?d.Responding:state.Responding,DelayMs=state==null?d.DelayMs:state.DelayMs,DropPercent=state==null?d.DropPercent:state.DropPercent,ExceptionCode=state==null?d.ExceptionCode:state.ExceptionCode});
   foreach(var p in d.Points) {
    var v=original==null?null:original.Values.FirstOrDefault(x=>x.Device==d.Name&&x.Point==p.Name);
    Points.Add(new ScenarioPointRow{Include=v!=null,Device=d.Name,Point=p.Name,Value=v==null?p.Value:v.Value,Simulation=v==null?p.Simulation:v.Simulation});
   }
  }
 }
 public Scenario Build(string name) {
  name=(name??"").Trim();
  if(name.Length==0||context.Scenarios.Any(s=>s.Name!=originalName&&s.Name.Equals(name,StringComparison.OrdinalIgnoreCase)))throw new Exception("Use a nonblank, unique scenario name.");
  var result=new Scenario{Name=name};
  foreach(var r in Devices.Where(r=>r.Include))result.Devices.Add(new DeviceState{Name=r.Name,Responding=r.Responding,DelayMs=r.DelayMs,DropPercent=r.DropPercent,ExceptionCode=r.ExceptionCode});
  foreach(var r in Points.Where(r=>r.Include))result.Values.Add(new ScenarioValue{Device=r.Device,Point=r.Point,Value=r.Value,Simulation=r.Simulation});
  if(result.Devices.Count==0&&result.Values.Count==0)throw new Exception("Include at least one device fault or point value.");
  result.Validate(context);
  return result;
 }
}
public sealed class ScenarioEditor : Form {
 readonly ScenarioDraft draft;
 readonly TextBox name=new TextBox();
 readonly DataGridView points;
 readonly DataGridView faults;
 public Scenario Result {get;private set;}
 public ScenarioEditor(Project project,Scenario original) {
  draft=new ScenarioDraft(project,original);
  Text=original==null?"Create scenario / fault preset":"Edit scenario / fault preset";
  Width=1050;Height=700;MinimumSize=new Size(780,500);StartPosition=FormStartPosition.CenterParent;
  Font=new Font("Segoe UI",9);
  var root=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(12),ColumnCount=1,RowCount=4};
  root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));Controls.Add(root);
  root.Controls.Add(new Label{Text="Name",AutoSize=true},0,0);
  name.Text=original==null?"New scenario":original.Name;name.Dock=DockStyle.Top;root.Controls.Add(name,0,1);
  var tabs=new TabControl{Dock=DockStyle.Fill};root.Controls.Add(tabs,0,2);
  points=MakeGrid();Column(points,"Include",true,false);Column(points,"Device",false,true);Column(points,"Point",false,true);Column(points,"Value",false,false);
  points.Columns.Add(new DataGridViewComboBoxColumn{Name="Simulation",DataPropertyName="Simulation",HeaderText="Signal mode",DataSource=Enum.GetValues(typeof(Wave)),Width=130});points.DataSource=draft.Points;
  faults=MakeGrid();Column(faults,"Include",true,false);Column(faults,"Name",false,true);Column(faults,"Responding",true,false);Column(faults,"DelayMs",false,false);Column(faults,"DropPercent",false,false);Column(faults,"ExceptionCode",false,false);faults.DataSource=draft.Devices;
  AddTab(tabs,"Point values & signals",points,"Tick Include for each value to change. Manual holds the value; other modes use the point's configured range and period when Run signals is active.");
  AddTab(tabs,"Device faults",faults,"Tick Include to save a device fault. Responding off = timeout; delay 0–60000 ms; drop 0–100%; exception 0–11 (0 = off). Unticked devices remain unchanged.");
  var actions=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft};
  var cancel=new Button{Text="Cancel",AutoSize=true,DialogResult=DialogResult.Cancel};var save=new Button{Text="Save scenario",AutoSize=true};actions.Controls.Add(cancel);actions.Controls.Add(save);root.Controls.Add(actions,0,3);CancelButton=cancel;
  save.Click+=(s,e)=>{try{if(!points.EndEdit()||!faults.EndEdit())throw new Exception("Correct the highlighted cell before saving.");BindingContext[draft.Points].EndCurrentEdit();BindingContext[draft.Devices].EndCurrentEdit();Result=draft.Build(name.Text);DialogResult=DialogResult.OK;Close();}catch(Exception error){MessageBox.Show(this,error.Message,"Scenario",MessageBoxButtons.OK,MessageBoxIcon.Warning);}};
 }
 static DataGridView MakeGrid(){var g=new LivePointGrid{Dock=DockStyle.Fill,AutoGenerateColumns=false,AllowUserToAddRows=false,AllowUserToDeleteRows=false,RowHeadersVisible=false,BackgroundColor=Color.White};g.DataError+=(s,e)=>{e.ThrowException=false;e.Cancel=true;MessageBox.Show("Enter a valid value for this column.","Scenario");};return g;}
 static void Column(DataGridView g,string field,bool check,bool readOnly){DataGridViewColumn c=check?(DataGridViewColumn)new DataGridViewCheckBoxColumn():new DataGridViewTextBoxColumn();c.Name=field;c.DataPropertyName=field;c.HeaderText=field;c.ReadOnly=readOnly;c.Width=field=="Include"?65:readOnly?210:120;g.Columns.Add(c);}
 static void AddTab(TabControl tabs,string title,Control grid,string hint){var tab=new TabPage(title);tab.Controls.Add(grid);tab.Controls.Add(new Label{Text=hint,Dock=DockStyle.Top,Height=60,Padding=new Padding(6)});tabs.TabPages.Add(tab);}
}
public partial class MainForm {
 void EnsureNoSequence(){if(sequence!=null&&!sequence.Completed)throw new Exception("Cancel the running sequence before changing scenarios or faults.");}
 void EditScenario(bool edit){EnsureNoSequence();var selected=edit?scenarios.SelectedItem as Scenario:null;if(edit&&selected==null)return;using(var form=new ScenarioEditor(project,selected)){if(form.ShowDialog(this)!=DialogResult.OK)return;if(selected==null)project.Scenarios.Add(form.Result);else project.Scenarios[project.Scenarios.IndexOf(selected)]=form.Result;RefreshScenarios();scenarios.SelectedItem=form.Result;Changed();}}
 void DuplicateScenario(){EnsureNoSequence();var selected=scenarios.SelectedItem as Scenario;if(selected==null)return;string name=Ask("Copy scenario name",selected.Name+" copy");if(name==null)return;var copy=new ScenarioDraft(project,selected).Build(name);if(project.Scenarios.Any(s=>s.Name.Equals(copy.Name,StringComparison.OrdinalIgnoreCase)))throw new Exception("Choose a unique scenario name.");project.Scenarios.Add(copy);RefreshScenarios();scenarios.SelectedItem=copy;Changed();}
 void MoveScenario(int direction){EnsureNoSequence();var selected=scenarios.SelectedItem as Scenario;if(selected==null)return;int at=project.Scenarios.IndexOf(selected),next=at+direction;if(next<0||next>=project.Scenarios.Count)return;project.Scenarios.RemoveAt(at);project.Scenarios.Insert(next,selected);RefreshScenarios();scenarios.SelectedItem=selected;Changed();}
}
}
