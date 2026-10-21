using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace ModbusEmulatorX {
public sealed class StepEditor : Form {
 readonly Project project;
 readonly ComboBox action=new ComboBox();
 readonly ComboBox device=new ComboBox();
 readonly ComboBox point=new ComboBox();
 readonly NumericUpDown at=new NumericUpDown();
 readonly NumericUpDown duration=new NumericUpDown();
 readonly TextBox value=new TextBox();
 readonly TextBox note=new TextBox();
 readonly CheckBox response=new CheckBox{Text="Respond to requests"};
 readonly Label valueLabel=new Label();
 readonly Label hint=new Label();
 public TimelineStep Result {get;private set;}
 public StepEditor(Project project,TimelineStep existing=null){
  this.project=project;Text=existing==null?"Add a timed step":"Edit timed step";Width=660;Height=590;MinimumSize=new Size(580,530);StartPosition=FormStartPosition.CenterParent;Font=new Font("Segoe UI",9);
  var root=new TableLayoutPanel{Dock=DockStyle.Fill,AutoScroll=true,Padding=new Padding(16),ColumnCount=2,RowCount=10};root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,160));root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));Controls.Add(root);
  foreach(var c in new[]{action,device,point}){c.DropDownStyle=ComboBoxStyle.DropDownList;c.Dock=DockStyle.Top;}
  action.Items.AddRange(new object[]{"Set a point value","Ramp a point to a value","Stop / resume device replies","Add response delay","Drop requests (%)","Return Modbus exceptions","Clear all device faults"});
  foreach(var d in project.Devices)device.Items.Add(d.Name);
  foreach(var n in new[]{at,duration}){n.DecimalPlaces=3;n.Maximum=86400;n.Increment=1;n.Dock=DockStyle.Top;}
  AddRow(root,0,"1. At time (seconds)",at);AddRow(root,1,"2. Action",action);AddRow(root,2,"3. Device",device);AddRow(root,3,"Point",point);value.Dock=DockStyle.Top;valueLabel.Text="4. Value";valueLabel.AutoSize=true;root.Controls.Add(valueLabel,0,4);root.Controls.Add(value,1,4);root.Controls.Add(response,1,5);AddRow(root,6,"For (seconds)",duration);note.Dock=DockStyle.Top;AddRow(root,7,"Note (optional)",note);
  hint.AutoSize=true;hint.MaximumSize=new Size(560,0);hint.Padding=new Padding(0,12,0,12);root.Controls.Add(hint,0,8);root.SetColumnSpan(hint,2);
  var buttons=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft};var cancel=new Button{Text="Cancel",AutoSize=true,DialogResult=DialogResult.Cancel};var save=new Button{Text="Save step",AutoSize=true};buttons.Controls.Add(cancel);buttons.Controls.Add(save);root.Controls.Add(buttons,0,9);root.SetColumnSpan(buttons,2);CancelButton=cancel;
  device.SelectedIndexChanged+=(s,e)=>RefreshPoints();action.SelectedIndexChanged+=(s,e)=>RefreshFields();
  action.SelectedIndex=existing==null?0:(int)existing.Action;
  device.SelectedItem=existing==null?project.Devices.Select(d=>d.Name).FirstOrDefault():existing.Device;
  if(existing!=null){point.SelectedItem=existing.Point;at.Value=(decimal)existing.AtSeconds;duration.Value=(decimal)existing.DurationSeconds;value.Text=existing.Value.ToString("R",CultureInfo.InvariantCulture);response.Checked=existing.Value!=0;note.Text=existing.Note;}
  else {value.Text="0";response.Checked=false;}
  RefreshFields();
  save.Click+=(s,e)=>{try{var kind=(TimelineAction)action.SelectedIndex;Result=new TimelineStep{AtSeconds=(double)at.Value,Action=kind,Device=Convert.ToString(device.SelectedItem),Point=point.Enabled?Convert.ToString(point.SelectedItem):"",Value=kind==TimelineAction.SetResponse?(response.Checked?1:0):kind==TimelineAction.RestoreDevice?0:double.Parse(value.Text,CultureInfo.InvariantCulture),DurationSeconds=duration.Enabled?(double)duration.Value:0,Note=note.Text};var check=new Timeline{DurationSeconds=Math.Max(1,Result.AtSeconds+Result.DurationSeconds+0.001)};check.Steps.Add(Result);check.Validate(project);DialogResult=DialogResult.OK;Close();}catch(Exception error){Result=null;MessageBox.Show(this,error.Message,"Check step",MessageBoxButtons.OK,MessageBoxIcon.Warning);}};
 }
 static void AddRow(TableLayoutPanel root,int row,string label,Control control){root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.Controls.Add(new Label{Text=label,AutoSize=true,Margin=new Padding(0,8,8,8)},0,row);root.Controls.Add(control,1,row);}
 void RefreshPoints(){string previous=Convert.ToString(point.SelectedItem);point.Items.Clear();var d=project.Devices.FirstOrDefault(x=>x.Name==Convert.ToString(device.SelectedItem));if(d!=null)foreach(var p in d.Points)point.Items.Add(p.Name);if(point.Items.Contains(previous))point.SelectedItem=previous;else if(point.Items.Count>0)point.SelectedIndex=0;}
 void RefreshFields(){var kind=(TimelineAction)Math.Max(0,action.SelectedIndex);point.Enabled=kind==TimelineAction.SetValue||kind==TimelineAction.RampValue;response.Visible=kind==TimelineAction.SetResponse;value.Visible=kind!=TimelineAction.SetResponse&&kind!=TimelineAction.RestoreDevice;valueLabel.Text=kind==TimelineAction.SetDelay?"4. Delay (milliseconds)":kind==TimelineAction.SetDrop?"4. Loss (0–100%)":kind==TimelineAction.SetException?"4. Exception (0–11)":"4. Target value";duration.Enabled=kind!=TimelineAction.SetValue&&kind!=TimelineAction.RestoreDevice;if(kind==TimelineAction.RampValue&&duration.Value==0)duration.Value=5;
  hint.Text=kind==TimelineAction.RampValue?"Ramp smoothly from the value at this step's start to the target, over the chosen duration. Numeric points only.":kind==TimelineAction.SetValue?"Set the engineering value immediately and hold it. This point switches to Manual so signals cannot overwrite it.":kind==TimelineAction.RestoreDevice?"Resume replies and clear delay, request loss and exception faults on this device.":"For = 0 keeps this setting until another step changes it. A positive duration restores its previous setting automatically. Exception 0 disables forced exceptions; 1 = illegal function, 2 = illegal address, 3 = illegal value, 4 = device failure, 6 = busy.";
 }
}
public partial class MainForm {
 readonly ComboBox timelineList=new ComboBox();
 readonly NumericUpDown timelineDuration=new NumericUpDown();
 readonly NumericUpDown timelineRepeat=new NumericUpDown();
 readonly CheckBox timelineRestore=new CheckBox();
 readonly LivePointGrid timelineGrid=new LivePointGrid();
 readonly Label timelineStatus=new Label();
 readonly ProgressBar timelineProgress=new ProgressBar();
 readonly List<Control> timelineEditors=new List<Control>();
 readonly Stopwatch timelineClock=new Stopwatch();
 TimelineRunner timelineRunner;
 double lastTimelineTime;
 bool bindingTimeline;
 Button timelineRun,timelinePause,timelineStop;
 Timeline CurrentTimeline {get{return timelineList.SelectedItem as Timeline;}}
 bool TimelineActive {get{return timelineRunner!=null&&!timelineRunner.Completed;}}
 void BuildTimelinePage(TabPage page){
  var root=new TableLayoutPanel{Name="TimelinePage",Dock=DockStyle.Fill,ColumnCount=1,RowCount=7,Padding=new Padding(12)};for(int i=0;i<7;i++)root.RowStyles.Add(new RowStyle(i==4?SizeType.Percent:SizeType.AutoSize,i==4?100:0));page.Controls.Add(root);
  root.Controls.Add(new Label{Text="1  Choose a timeline     2  Add timed actions     3  Run and watch",AutoSize=true,Font=new Font(Font,FontStyle.Bold),Margin=new Padding(0,0,0,12)},0,0);
  var library=Flow();timelineList.Width=220;timelineList.DropDownStyle=ComboBoxStyle.DropDownList;timelineList.SelectedIndexChanged+=(s,e)=>BindTimeline();library.Controls.Add(timelineList);timelineEditors.Add(timelineList);
  AddTimelineButton(library,"New",NewTimeline);AddTimelineButton(library,"Rename",()=>{var t=CurrentTimeline;if(t==null)return;string name=Ask("Timeline name",t.Name);if(name==null)return;CheckTimelineName(name,t);t.Name=name.Trim();RefreshTimelines(t);Changed();});AddTimelineButton(library,"Duplicate",()=>{var t=CurrentTimeline;if(t==null)return;string name=Ask("Copy timeline name",t.Name+" copy");if(name==null)return;CheckTimelineName(name,null);var copy=t.Copy();copy.Name=name.Trim();project.Timelines.Add(copy);RefreshTimelines(copy);Changed();});AddTimelineButton(library,"Delete",()=>{var t=CurrentTimeline;if(t!=null&&MessageBox.Show(this,"Delete timeline '"+t.Name+"'?","Delete timeline",MessageBoxButtons.YesNo)==DialogResult.Yes){project.Timelines.Remove(t);RefreshTimelines();Changed();}});root.Controls.Add(library,0,1);
  var settings=Flow();settings.Controls.Add(new Label{Text="Cycle length (s)",AutoSize=true,Margin=new Padding(0,9,4,0)});timelineDuration.Minimum=0.001M;timelineDuration.Maximum=86400;timelineDuration.DecimalPlaces=3;timelineDuration.Width=100;timelineDuration.Value=30;settings.Controls.Add(timelineDuration);settings.Controls.Add(new Label{Text="Repeat",AutoSize=true,Margin=new Padding(12,9,4,0)});timelineRepeat.Minimum=1;timelineRepeat.Maximum=10000;timelineRepeat.Width=65;settings.Controls.Add(timelineRepeat);timelineRestore.Text="Restore starting state on finish";timelineRestore.AutoSize=true;timelineRestore.Checked=true;settings.Controls.Add(timelineRestore);timelineEditors.AddRange(new Control[]{timelineDuration,timelineRepeat,timelineRestore});timelineDuration.ValueChanged+=(s,e)=>SaveTimelineSettings();timelineRepeat.ValueChanged+=(s,e)=>SaveTimelineSettings();timelineRestore.CheckedChanged+=(s,e)=>SaveTimelineSettings();root.Controls.Add(settings,0,2);
  var steps=Flow();AddTimelineButton(steps,"+ Add step",()=>EditTimelineStep(false));AddTimelineButton(steps,"Edit step",()=>EditTimelineStep(true));AddTimelineButton(steps,"Duplicate step",DuplicateTimelineStep);AddTimelineButton(steps,"Remove step",()=>{var step=SelectedTimelineStep();if(step!=null){CurrentTimeline.Steps.Remove(step);BindTimeline();Changed();}});AddTimelineButton(steps,"Import older scenarios",ImportOldScenarios);root.Controls.Add(steps,0,3);
  timelineGrid.Name="TimelineSteps";timelineGrid.Dock=DockStyle.Fill;timelineGrid.AutoGenerateColumns=false;timelineGrid.ReadOnly=true;timelineGrid.AllowUserToAddRows=false;timelineGrid.AllowUserToDeleteRows=false;timelineGrid.RowHeadersVisible=false;timelineGrid.SelectionMode=DataGridViewSelectionMode.FullRowSelect;timelineGrid.MultiSelect=false;timelineGrid.BackgroundColor=Color.White;
  foreach(var field in new[]{"AtSeconds","Target","Description","DurationSeconds","Note"})timelineGrid.Columns.Add(new DataGridViewTextBoxColumn{DataPropertyName=field,Name=field,HeaderText=field=="AtSeconds"?"At (s)":field=="DurationSeconds"?"For (s)":field,Width=field=="Description"?260:field=="Target"?200:field=="Note"?200:80,SortMode=DataGridViewColumnSortMode.NotSortable});timelineGrid.CellDoubleClick+=(s,e)=>{if(e.RowIndex>=0)Run(()=>{EnsureTimelineIdle();EditTimelineStep(true);});};root.Controls.Add(timelineGrid,0,4);
  var playback=Flow();timelineRun=Button("Run timeline",StartTimeline);timelinePause=Button("Pause",PauseTimeline);timelineStop=Button("Stop & restore",StopTimeline);playback.Controls.AddRange(new Control[]{timelineRun,timelinePause,timelineStop,Button("Export run log",ExportTimelineLog)});root.Controls.Add(playback,0,5);
  var feedback=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,ColumnCount=1,RowCount=2};timelineStatus.AutoSize=true;timelineStatus.MaximumSize=new Size(650,0);timelineStatus.Padding=new Padding(0,6,0,6);feedback.Controls.Add(timelineStatus);timelineProgress.Dock=DockStyle.Top;timelineProgress.Height=14;feedback.Controls.Add(timelineProgress);root.Controls.Add(feedback,0,6);UpdateTimelineStatus();
 }
 static FlowLayoutPanel Flow(){return new FlowLayoutPanel{AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Dock=DockStyle.Fill,WrapContents=true,Margin=Padding.Empty};}
 void AddTimelineButton(FlowLayoutPanel panel,string text,Action action){var button=Button(text,()=>{EnsureTimelineIdle();action();});panel.Controls.Add(button);timelineEditors.Add(button);}
 void EnsureTimelineIdle(){if(TimelineActive)throw new Exception("Stop the running timeline before editing it.");}
 void CheckTimelineName(string name,Timeline current){if(string.IsNullOrWhiteSpace(name)||project.Timelines.Any(t=>t!=current&&t.Name.Equals(name.Trim(),StringComparison.OrdinalIgnoreCase)))throw new Exception("Choose a nonblank, unique timeline name.");}
 void NewTimeline(){string name=Ask("New timeline name","Timeline "+(project.Timelines.Count+1));if(name==null)return;CheckTimelineName(name,null);var t=new Timeline{Name=name.Trim()};project.Timelines.Add(t);RefreshTimelines(t);Changed();}
 void RefreshTimelines(Timeline selected=null){bindingTimeline=true;timelineList.DataSource=null;timelineList.DataSource=project.Timelines.ToList();if(selected!=null)timelineList.SelectedItem=selected;bindingTimeline=false;BindTimeline();}
 void BindTimeline(){if(bindingTimeline)return;bindingTimeline=true;var t=CurrentTimeline;timelineGrid.DataSource=t==null?null:t.Steps.OrderBy(s=>s.AtSeconds).ToList();if(t!=null){timelineDuration.Value=(decimal)t.DurationSeconds;timelineRepeat.Value=t.RepeatCount;timelineRestore.Checked=t.RestoreOnFinish;}bindingTimeline=false;UpdateTimelineStatus();}
 void SaveTimelineSettings(){if(bindingTimeline||CurrentTimeline==null||TimelineActive)return;CurrentTimeline.DurationSeconds=(double)timelineDuration.Value;CurrentTimeline.RepeatCount=(int)timelineRepeat.Value;CurrentTimeline.RestoreOnFinish=timelineRestore.Checked;Changed();UpdateTimelineStatus();}
 TimelineStep SelectedTimelineStep(){return timelineGrid.CurrentRow==null?null:timelineGrid.CurrentRow.DataBoundItem as TimelineStep;}
 void EditTimelineStep(bool edit){if(CurrentTimeline==null)throw new Exception("Click New to create a timeline first.");if(project.Devices.Count==0)throw new Exception("Add a device and points first.");var old=edit?SelectedTimelineStep():null;if(edit&&old==null)return;using(var form=new StepEditor(project,old)){if(form.ShowDialog(this)!=DialogResult.OK)return;SaveStep(old,form.Result);}}
 void SaveStep(TimelineStep old,TimelineStep replacement){var t=CurrentTimeline;var draft=t.Copy();if(old!=null)draft.Steps.RemoveAt(t.Steps.IndexOf(old));draft.Steps.Add(replacement);draft.DurationSeconds=Math.Max(t.DurationSeconds,replacement.AtSeconds+Math.Max(0.001,replacement.DurationSeconds));draft.Validate(project);if(old!=null)t.Steps.Remove(old);t.Steps.Add(replacement);t.DurationSeconds=draft.DurationSeconds;BindTimeline();Changed();}
 void DuplicateTimelineStep(){var step=SelectedTimelineStep();if(step==null)return;var copy=step.Copy();copy.AtSeconds+=Math.Max(1,step.DurationSeconds);using(var form=new StepEditor(project,copy)){if(form.ShowDialog(this)==DialogResult.OK)SaveStep(null,form.Result);}}
 void StartTimeline(){EnsureTimelineIdle();if(!engine.Running)throw new Exception("Start servers, then click Run timeline.");var t=CurrentTimeline;if(t==null)throw new Exception("Create a timeline and add a step first.");timelineRunner=new TimelineRunner(project,t);simulate=false;CancelSequence();timelineRunner.Log+=message=>pending.Enqueue("TIMELINE "+message);timelineClock.Restart();lastTimelineTime=0;timelineRunner.Advance(0);SetConfiguration(false);Changed();UpdateTimelineStatus();}
 void PauseTimeline(){if(!TimelineActive)return;TickTimeline();if(timelineRunner.Paused){timelineRunner.Resume();timelineClock.Start();}else{timelineRunner.Pause();timelineClock.Stop();}UpdateTimelineStatus();}
 void StopTimeline(){if(TimelineActive){timelineRunner.Stop();timelineClock.Stop();Changed();}UpdateTimelineStatus();SetConfiguration(!engine.Running&&!simulate);}
 void TickTimeline(){if(!TimelineActive)return;double now=timelineClock.Elapsed.TotalSeconds;try{timelineRunner.Advance(Math.Max(0,now-lastTimelineTime));}catch(Exception error){pending.Enqueue("TIMELINE FAILED: "+error.Message);}lastTimelineTime=now;if(timelineRunner.Completed){timelineClock.Stop();SetConfiguration(!engine.Running&&!simulate);properties.Refresh();}UpdateTimelineStatus();}
 void UpdateTimelineStatus(){if(timelineRun==null)return;bool running=TimelineActive;foreach(var c in timelineEditors)c.Enabled=!running;timelineRun.Enabled=!running&&CurrentTimeline!=null;timelinePause.Enabled=running;timelineStop.Enabled=running;timelinePause.Text=running&&timelineRunner.Paused?"Resume":"Pause";if(timelineRunner!=null){timelineStatus.Text=timelineRunner.Name+" — "+timelineRunner.State+"  |  "+timelineRunner.Elapsed.ToString("0.0")+" / "+timelineRunner.TotalSeconds.ToString("0.0")+" s  |  Cycle "+timelineRunner.Cycle+(running?"\r\n"+(timelineRunner.Paused?"Time paused; current faults remain active. ":"")+"Next: "+timelineRunner.NextStep:"");timelineProgress.Value=(int)Math.Min(100,100*timelineRunner.Elapsed/timelineRunner.TotalSeconds);}else{timelineStatus.Text=CurrentTimeline==null?"Create a timeline to begin. Timelines are saved in .MBX and Excel .XLSX projects.":"Ready. Steps run in time order; different devices and points can run together. For = 0 holds a fault until changed. Stop always restores starting values and faults. Repeats start from the original state. Playback updates about 4 times per second.";timelineProgress.Value=0;}if(!running&&CurrentTimeline!=null){try{CurrentTimeline.Validate(project);if(CurrentTimeline.Steps.Count==0){timelineRun.Enabled=false;timelineStatus.Text="Click + Add step to choose a device, action and start time.";}}catch(Exception error){timelineRun.Enabled=false;timelineStatus.Text="Fix timeline: "+error.Message;}}}
 void ExportTimelineLog(){if(timelineRunner==null)throw new Exception("Run a timeline first.");string file=Choose(true,"CSV run log (*.csv)|*.csv","Export timeline run log","Timeline run.csv");if(file!=null)File.WriteAllLines(file,timelineRunner.Evidence,new System.Text.UTF8Encoding(true));}
 void ImportOldScenarios(){if(project.Scenarios.Count==0)throw new Exception("This project has no older snapshot scenarios to import.");int count=0;foreach(var sc in project.Scenarios){string name=sc.Name;int suffix=2;while(project.Timelines.Any(item=>item.Name.Equals(name,StringComparison.OrdinalIgnoreCase)))name=sc.Name+" "+suffix++;var t=new Timeline{Name=name};foreach(var v in sc.Values)t.Steps.Add(new TimelineStep{Device=v.Device,Point=v.Point,Action=TimelineAction.SetValue,Value=v.Value,Note="Imported snapshot value (held in Manual mode)"});foreach(var d in sc.Devices){t.Steps.Add(new TimelineStep{Device=d.Name,Action=TimelineAction.SetResponse,Value=d.Responding?1:0});t.Steps.Add(new TimelineStep{Device=d.Name,Action=TimelineAction.SetDelay,Value=d.DelayMs});t.Steps.Add(new TimelineStep{Device=d.Name,Action=TimelineAction.SetDrop,Value=d.DropPercent});t.Steps.Add(new TimelineStep{Device=d.Name,Action=TimelineAction.SetException,Value=d.ExceptionCode});}t.Validate(project);project.Timelines.Add(t);count++;}RefreshTimelines();Changed();pending.Enqueue("Imported "+count+" older scenarios as timelines.");}
}
}
