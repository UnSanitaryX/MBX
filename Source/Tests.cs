using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using ModbusEmulatorX;
using VT=ModbusEmulatorX.ValueType;

public static class Tests {
 static int passed,failed; static string scratch;
 static void Test(string name,Action action){try{action();passed++;Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e);}}
 static void Assert(bool value,string message="Assertion failed"){if(!value)throw new Exception(message);}
 static void Equal(byte[] actual,params byte[] expected){Assert(actual.SequenceEqual(expected),"Expected "+BitConverter.ToString(expected)+"; got "+BitConverter.ToString(actual));}
 static void Near(double actual,double expected,double tolerance=0.00001){Assert(Math.Abs(actual-expected)<=tolerance,"Expected "+expected+"; got "+actual);}
 static void Reject(Action action){bool rejected=false;try{action();}catch{rejected=true;}Assert(rejected,"Expected rejection.");}
 static Device Map(){var d=new Device{Name="Test",AllowWrites=true};for(int i=0;i<20;i++){d.Points.Add(new Point{Name="H"+i,Address=i,Value=i,Default=i});d.Points.Add(new Point{Name="I"+i,Area=Area.InputRegister,Address=i,Value=100+i,Default=100+i});d.Points.Add(new Point{Name="C"+i,Area=Area.Coil,Type=VT.Bool,Address=i,Value=i%2,Default=i%2});d.Points.Add(new Point{Name="D"+i,Area=Area.DiscreteInput,Type=VT.Bool,Address=i,Value=(i+1)%2,Default=(i+1)%2});}return d;}
 static Project MapProject(){var p=new Project{Name="Tests"};p.Devices.Add(Map());return p;}
 static byte[] Process(params byte[] p){return Engine.Process(Map(),p);}
 [STAThread]public static int Main(string[] args){scratch=Path.Combine(args.Length==2&&args[0]=="--scratch"?args[1]:Path.GetTempPath(),"ModbusEmulatorX-tests-"+Guid.NewGuid());Directory.CreateDirectory(scratch);try{
  Test("Demo validates",()=>Project.Demo().Validate());
  Test("Scenario rejects an unencodable signal range without changing live state",()=>{var p=Project.Demo();var d=new ScenarioDraft(p,null);var row=d.Points.First(x=>x.Point=="Motor run");row.Include=true;row.Simulation=Wave.Toggle;Reject(()=>d.Build("Invalid range"));row.Simulation=Wave.Manual;d.Build("Valid");Assert(p.Devices[0].Points[2].Simulation==Wave.Manual);});
  Test("Network checkboxes fit their autosized row at multiple scales",()=>{foreach(float scale in new[]{1f,1.5f,2f})using(var f=new NetworkSetupForm(Project.Demo(),new NetworkOptions(),false,message=>{})){f.Scale(new System.Drawing.SizeF(scale,scale));f.PerformLayout();var cleanup=f.Controls.Find("RemoveAliasesOnStop",true).Single();var choices=cleanup.Parent;choices.PerformLayout();var root=(System.Windows.Forms.TableLayoutPanel)choices.Parent;root.PerformLayout();Assert(root.RowStyles[2].SizeType==System.Windows.Forms.SizeType.AutoSize);Assert(cleanup.Bottom<=choices.ClientSize.Height,"Cleanup checkbox exceeds its row at "+scale);Assert(!f.Visible);}});

  Test("Scenario draft isolates edits and supports fault-only presets",()=>{var p=Project.Demo();var draft=new ScenarioDraft(p,null);draft.Devices[0].Include=true;draft.Devices[0].Responding=false;draft.Devices[0].DelayMs=450;draft.Devices[0].DropPercent=25;draft.Devices[0].ExceptionCode=4;var sc=draft.Build("Fault");Assert(p.Devices[0].Responding&&p.Devices[0].DelayMs==0);Assert(sc.Values.Count==0);sc.Apply(p);Assert(!p.Devices[0].Responding&&p.Devices[0].DelayMs==450&&p.Devices[0].DropPercent==25&&p.Devices[0].ExceptionCode==4);});
  Test("Scenario draft validates bad faults, empty selection and duplicate names",()=>{var p=Project.Demo();p.Scenarios.Add(Scenario.Capture(p,"Existing"));var d=new ScenarioDraft(p,null);Reject(()=>d.Build("Empty"));d.Devices[0].Include=true;Reject(()=>d.Build("existing"));d.Devices[0].DropPercent=101;Reject(()=>d.Build("Invalid"));Assert(p.Devices[0].DropPercent==0);});
  Test("Scenario draft validates point encoding and preserves unselected values",()=>{var p=Project.Demo();var d=new ScenarioDraft(p,null);var row=d.Points.First(x=>x.Point=="Motor run");row.Include=true;row.Value=2;Reject(()=>d.Build("Bad bit"));row.Value=1;var sc=d.Build("Start motor");Assert(sc.Values.Count==1&&sc.Devices.Count==0);sc.Apply(p);Near(p.Devices[0].Points[0].Value,22.5);Near(p.Devices[0].Points[2].Value,1);});
  Test("Scenario draft editing roundtrips both project formats",()=>{var p=Project.Demo();var old=Scenario.Capture(p,"Initial");p.Scenarios.Add(old);var d=new ScenarioDraft(p,old);d.Points[0].Value=75;d.Points[0].Simulation=Wave.Manual;var sc=d.Build("Edited");Near(old.Values[0].Value,22.5);p.Scenarios[0]=sc;foreach(string ext in new[]{"mbx","xlsx"}){string file=Path.Combine(scratch,"edited."+ext);ProjectFiles.Save(p,file);var loaded=ProjectFiles.Load(file);loaded.Scenarios[0].Apply(loaded);Near(loaded.Devices[0].Points[0].Value,75);Assert(loaded.Devices[0].Points[0].Simulation==Wave.Manual);}});
  Test("Traffic browsing pauses on wheel, scrollbar and navigation without showing a window",()=>{using(var t=new TrafficLogBox()){int n=0;t.Browsing+=(a,b)=>n++;var method=typeof(TrafficLogBox).GetMethod("WndProc",BindingFlags.NonPublic|BindingFlags.Instance);foreach(int msg in new[]{0x115,0x114,0x20A,0x20E,0x201,0x100}){var m=System.Windows.Forms.Message.Create(IntPtr.Zero,msg,IntPtr.Zero,IntPtr.Zero);method.Invoke(t,new object[]{m});}Assert(n==6);}});
  Test("Scenario editor constructs without showing a window",()=>{using(var f=new ScenarioEditor(Project.Demo(),null)){Assert(f.Width>=780&&f.Result==null);}});

  foreach(VT type in Enum.GetValues(typeof(VT)))foreach(bool words in new[]{false,true})foreach(bool bytes in new[]{false,true}){VT t=type;bool sw=words,sb=bytes;Test("Value roundtrip "+t+" words="+sw+" bytes="+sb,()=>{var pt=new Point{Type=t,Value=t==VT.Bool?1:t==VT.Int16?-1234:t==VT.Int32?-1234567:t==VT.Float32?12.375:12345,SwapWords=sw,SwapBytes=sb};Near(pt.Decode(pt.Encode()),pt.Value);});}
  Test("Float32 standard words",()=>{var p=new Point{Type=VT.Float32,Value=1};Assert(p.Encode().SequenceEqual(new ushort[]{0x3f80,0}));});
  Test("Scaling offset",()=>{var p=new Point{Scale=0.01,Offset=-10,Value=6.25};Assert(p.Encode()[0]==1625);Near(p.Decode(new ushort[]{1625}),6.25);});
  Test("Reject overflow",()=>Reject(()=>new Point{Type=VT.UInt16,Value=65536}.Encode()));
  Test("Reject Float32 overflow",()=>Reject(()=>new Point{Type=VT.Float32,Value=1e100}.Encode()));
  Test("Reject nonboolean bit",()=>Reject(()=>new Point{Type=VT.Bool,Value=2}.Encode()));
  Test("FC01 packed coils",()=>Equal(Process(1,0,0,0,10),1,2,0xaa,2));
  Test("FC02 independent discrete inputs",()=>Equal(Process(2,0,0,0,10),2,2,0x55,1));
  Test("FC03 holding registers",()=>Equal(Process(3,0,1,0,2),3,4,0,1,0,2));
  Test("FC04 independent input registers",()=>Equal(Process(4,0,1,0,2),4,4,0,101,0,102));
  Test("FC05 single coil",()=>{var d=Map();Equal(Engine.Process(d,new byte[]{5,0,0,255,0}),5,0,0,255,0);Assert(d.Read(Area.Coil,0,1)[0]==1);});
  Test("FC05 invalid coil encoding",()=>Equal(Process(5,0,0,0,1),133,3));
  Test("FC06 single register",()=>{var d=Map();Equal(Engine.Process(d,new byte[]{6,0,0,0x12,0x34}),6,0,0,0x12,0x34);Assert(d.Read(Area.HoldingRegister,0,1)[0]==0x1234);});
  Test("FC15 multiple coils",()=>{var d=Map();Equal(Engine.Process(d,new byte[]{15,0,0,0,10,2,0x55,1}),15,0,0,0,10);Assert(d.Read(Area.Coil,0,10).SequenceEqual(new ushort[]{1,0,1,0,1,0,1,0,1,0}));});
  Test("FC16 multiple registers",()=>{var d=Map();Equal(Engine.Process(d,new byte[]{16,0,0,0,2,4,0x12,0x34,0x56,0x78}),16,0,0,0,2);Assert(d.Read(Area.HoldingRegister,0,2).SequenceEqual(new ushort[]{0x1234,0x5678}));});
  Test("FC22 mask register",()=>{var d=Map();d.Write(Area.HoldingRegister,0,new ushort[]{0x1234});Equal(Engine.Process(d,new byte[]{22,0,0,0xff,0,0,0xab}),22,0,0,0xff,0,0,0xab);Assert(d.Read(Area.HoldingRegister,0,1)[0]==0x12ab);});
  Test("FC23 write before read",()=>{var d=Map();Equal(Engine.Process(d,new byte[]{23,0,0,0,2,0,0,0,2,4,0,40,0,41}),23,4,0,40,0,41);});
  Test("FC23 invalid read leaves write unchanged",()=>{var d=Map();Equal(Engine.Process(d,new byte[]{23,0,99,0,1,0,0,0,1,2,0,40}),151,2);Assert(d.Read(Area.HoldingRegister,0,1)[0]==0);});
  Test("FC43 basic identification",()=>{var r=Process(43,14,1,0);Assert(r[0]==43&&r[1]==14&&r[6]==3);});
  Test("FC08 echo diagnostics",()=>Equal(Process(8,0,0,0x12,0x34),8,0,0,0x12,0x34));
  Test("FC08 unsupported subfunction",()=>Equal(Process(8,0,1,0,0),136,1));
  Test("FC43 individual object",()=>{var r=Process(43,14,4,1);Assert(r[6]==1&&r[7]==1);});
  Test("FC43 unsupported object",()=>Equal(Process(43,14,1,4),171,2));
  Test("Unsupported function",()=>Equal(Process(99,0),227,1));
  Test("Read malformed payload",()=>Equal(Process(3,0,0),131,3));
  Test("Read zero quantity",()=>Equal(Process(3,0,0,0,0),131,3));
  Test("Read quantity above maximum",()=>Equal(Process(3,0,0,0,126),131,3));
  Test("Read address overflow",()=>Equal(Process(3,255,255,0,2),131,2));
  Test("Bit address overflow",()=>Equal(Process(1,255,255,0,2),129,2));
  Test("Undefined address",()=>Equal(Process(3,0,40,0,1),131,2));
  Test("Global write protection",()=>{var d=Map();d.AllowWrites=false;foreach(byte f in new byte[]{5,6,15,16,22,23}){byte[] r=Engine.Process(d,new byte[]{f});Assert(r[0]==(f|128)&&r[1]==1);}});
  Test("Point write protection is atomic",()=>{var d=Map();d.Points.First(p=>p.Name=="H1").Writable=false;Equal(Engine.Process(d,new byte[]{16,0,0,0,2,4,0,99,0,99}),144,2);Assert(d.Read(Area.HoldingRegister,0,1)[0]==0);});
  Test("FC16 byte count validation",()=>Equal(Process(16,0,0,0,2,2,0,99),144,3));
  Test("FC16 boundary overflow",()=>Equal(Process(16,255,255,0,2,4,0,1,0,2),144,2));
  Test("Partial 32-bit register write",()=>{var d=new Device{AllowWrites=true};d.Points.Add(new Point{Name="wide",Type=VT.UInt32,Value=0x12345678});d.Write(Area.HoldingRegister,1,new ushort[]{0xabcd});Near(d.Points[0].Value,0x1234abcd);});
  Test("Reject NaN write atomically",()=>{var d=new Device{AllowWrites=true};d.Points.Add(new Point{Name="float",Type=VT.Float32,Value=1});Equal(Engine.Process(d,new byte[]{16,0,0,0,2,4,0x7f,0xc0,0,0}),144,3);Near(d.Points[0].Value,1);});
  Test("Duplicate register rejected",()=>{var p=MapProject();p.Devices[0].Points.Add(new Point{Address=0});Reject(p.Validate);});
  Test("Wide register overlap rejected",()=>{var p=MapProject();p.Devices[0].Points.First(x=>x.Name=="H0").Type=VT.Float32;Reject(p.Validate);});
  Test("Duplicate unit rejected",()=>{var p=MapProject();p.Devices.Add(new Device{Name="Duplicate"});Reject(p.Validate);});
  Test("Distinct unit IDs share listener",()=>{var p=MapProject();p.Devices.Add(new Device{Name="Other unit",UnitId=2});p.Validate();});
  Test("Wildcard listener conflicts",()=>{var p=MapProject();p.Devices.Add(new Device{Name="Wildcard",BindAddress="0.0.0.0",UnitId=2});Reject(p.Validate);});
  Test("Scale zero rejected",()=>{var p=MapProject();p.Devices[0].Points[0].Scale=0;Reject(p.Validate);});
  Test("Invalid simulation period rejected",()=>{var p=MapProject();p.Devices[0].Points[0].PeriodSeconds=0;Reject(p.Validate);});
  Test("Unknown enums rejected",()=>{var p=MapProject();p.Devices[0].Points[0].Area=(Area)99;Reject(p.Validate);});
  Test("RTU unit and serial settings validation",()=>{var p=MapProject();var d=p.Devices[0];d.Transport=Transport.Rtu;d.UnitId=0;Reject(p.Validate);d.UnitId=247;p.Validate();d.DataBits=7;Reject(p.Validate);});
  Test("RTU shared serial settings conflict",()=>{var p=MapProject();p.Devices[0].Transport=Transport.Rtu;p.Devices.Add(new Device{Name="RTU2",Transport=Transport.Rtu,UnitId=2,BaudRate=19200});Reject(p.Validate);});
  Test("RTU published CRC vector",()=>{byte[] frame={1,3,0,0,0,10,0xc5,0xcd};Assert(Rtu.Valid(frame));Equal(Rtu.Frame(1,new byte[]{3,0,0,0,10}),frame);frame[3]=1;Assert(!Rtu.Valid(frame));});
  Test("RTU variable frame length",()=>Assert(Rtu.ExpectedLength(new byte[]{1,16,0,0,0,2,4})==13));
  Test("Native project roundtrip including scenario",()=>{var p=Project.Demo();p.Scenarios.Add(Scenario.Capture(p,"Baseline"));string f=Path.Combine(scratch,"project.mbx");p.Save(f);p.Save(f);var loaded=Project.Load(f);Assert(loaded.Scenarios.Count==1&&loaded.Devices[0].Points.Count==5);Near(loaded.Devices[0].Points[0].Value,22.5);});
  Test("Native rejects external XML entities",()=>{string f=Path.Combine(scratch,"bad.mbx");File.WriteAllText(f,"<!DOCTYPE Project [<!ENTITY x SYSTEM 'file:///missing'>]><Project><Name>&x;</Name></Project>");Reject(()=>Project.Load(f));});
  Test("CSV project roundtrip / quoting / Unicode",()=>{var p=MapProject();p.Name="Project, \"X\"\r\nTest";p.Devices[0].Points[0].Name="=point";p.Devices[0].Points[0].Notes="comma, semicolon; \"quote\"\r\nUnicode \u00b0\u03a9";string f=Path.Combine(scratch,"project.csv");p.ExportCsv(f);var loaded=Project.ImportCsv(f);Assert(loaded.Name==p.Name);Assert(loaded.Devices[0].Points[0].Name=="=point");Assert(loaded.Devices[0].Points[0].Notes==p.Devices[0].Points[0].Notes);});
  Test("CSV apostrophe roundtrip",()=>{var p=MapProject();p.Devices[0].Points[0].Name="'=name";Assert(Project.ImportCsvText(p.CsvText()).Devices[0].Points[0].Name=="'=name");});
  Test("CSV malformed numeric leaves source untouched",()=>{var p=MapProject();string csv=p.CsvText().Replace("\"1502\"","\"oops\"");Reject(()=>Project.ImportCsvText(csv));Assert(p.Devices[0].Port==1502);});
  Test("CSV semicolon regional separator",()=>{var p=MapProject();var rows=Csv.Parse(p.CsvText());string csv=string.Join("\r\n",rows.Select(r=>string.Join(";",r.Select(x=>"\""+x.Replace("\"","\"\"")+"\""))));Assert(Project.ImportCsvText(csv).Devices[0].Points.Count==80);});
  Test("CSV inconsistent device settings rejected",()=>{var p=MapProject();string csv=p.CsvText();int index=csv.LastIndexOf("\"1502\"",StringComparison.Ordinal);csv=csv.Substring(0,index)+csv.Substring(index).Replace("\"1502\"","\"1602\"");Reject(()=>Project.ImportCsvText(csv));});
  Test("CSV unmatched quote rejected",()=>Reject(()=>Csv.Parse("a,b\r\n\"oops")));
  Test("CSV empty project roundtrip",()=>{var p=new Project{Name="Empty"};Assert(Project.ImportCsvText(p.CsvText()).Devices.Count==0);});
  Test("CSV device with no points",()=>{var p=new Project();p.Devices.Add(new Device());Assert(Project.ImportCsvText(p.CsvText()).Devices.Count==1);});
  Test("Excel roundtrip / scenarios / serial settings",()=>{var p=Project.Demo();p.Name="Excel \u03a9";p.Devices.Add(new Device{Name="Serial",Transport=Transport.Rtu,SerialPort="COM99",UnitId=4,Enabled=false});p.Scenarios.Add(Scenario.Capture(p,"All healthy"));string f=Path.Combine(scratch,"project.xlsx");ExcelProject.Export(p,f);ExcelProject.Export(p,f);var loaded=ExcelProject.Import(f);Assert(loaded.Name==p.Name&&loaded.Devices.Count==2&&loaded.Scenarios.Count==1);Assert(loaded.Devices[1].SerialPort=="COM99");loaded.Scenarios[0].Apply(loaded);});
  Test("Excel empty project roundtrip",()=>{string f=Path.Combine(scratch,"empty.xlsx");ExcelProject.Export(new Project(),f);Assert(ExcelProject.Import(f).Devices.Count==0);});
  Test("Excel edited numeric cell reimports",()=>{string f=Path.Combine(scratch,"edit.xlsx");ExcelProject.Export(Project.Demo(),f);EditSheet(f,"xl/worksheets/sheet3.xml",doc=>{XNamespace n="http://schemas.openxmlformats.org/spreadsheetml/2006/main";doc.Descendants(n+"c").First(c=>(string)c.Attribute("r")=="H2").Element(n+"v").Value="47.5";});Near(ExcelProject.Import(f).Devices[0].Points[0].Value,47.5);});
  Test("Excel formula without cache rejected",()=>{string f=Path.Combine(scratch,"formula.xlsx");ExcelProject.Export(Project.Demo(),f);EditSheet(f,"xl/worksheets/sheet3.xml",doc=>{XNamespace n="http://schemas.openxmlformats.org/spreadsheetml/2006/main";var c=doc.Descendants(n+"c").First(x=>(string)x.Attribute("r")=="H2");c.Element(n+"v").Remove();c.Add(new XElement(n+"f","1+1"));});Reject(()=>ExcelProject.Import(f));});
  Test("Excel missing sheet rejected",()=>{string f=Path.Combine(scratch,"missing.xlsx");ExcelProject.Export(Project.Demo(),f);using(var s=File.Open(f,FileMode.Open))using(var z=new ZipArchive(s,ZipArchiveMode.Update))z.GetEntry("xl/worksheets/sheet2.xml").Delete();Reject(()=>ExcelProject.Import(f));});
  Test("Excel cached formula result",()=>{string f=Path.Combine(scratch,"shared.xlsx");ExcelProject.Export(Project.Demo(),f);EditSheet(f,"xl/worksheets/sheet3.xml",doc=>{XNamespace n="http://schemas.openxmlformats.org/spreadsheetml/2006/main";var c=doc.Descendants(n+"c").First(x=>(string)x.Attribute("r")=="H2");c.Element(n+"v").Value="36.5";c.Add(new XElement(n+"f","30+6.5"));});Near(ExcelProject.Import(f).Devices[0].Points[0].Value,36.5);});
  Test("Excel shared-string cell",()=>{string f=Path.Combine(scratch,"strings.xlsx");ExcelProject.Export(Project.Demo(),f);EditSheet(f,"xl/worksheets/sheet3.xml",doc=>{XNamespace n="http://schemas.openxmlformats.org/spreadsheetml/2006/main";var c=doc.Descendants(n+"c").First(x=>(string)x.Attribute("r")=="B2");c.RemoveNodes();c.SetAttributeValue("t","s");c.Add(new XElement(n+"v",0));});using(var s=File.Open(f,FileMode.Open))using(var z=new ZipArchive(s,ZipArchiveMode.Update))using(var w=new StreamWriter(z.CreateEntry("xl/sharedStrings.xml").Open()))w.Write("<sst xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main' count='1' uniqueCount='1'><si><t>Temperature</t></si></sst>");Assert(ExcelProject.Import(f).Devices[0].Points[0].Name=="Temperature");});
  Test("Last valid register and bit addresses",()=>{var d=new Device{AllowWrites=true};d.Points.Add(new Point{Name="Last",Address=65535,Value=7});d.Points.Add(new Point{Name="Last coil",Area=Area.Coil,Type=VT.Bool,Address=65535,Value=1});Equal(Engine.Process(d,new byte[]{3,255,255,0,1}),3,2,0,7);Equal(Engine.Process(d,new byte[]{1,255,255,0,1}),1,1,1);Equal(Engine.Process(d,new byte[]{6,255,255,0,9}),6,255,255,0,9);});
  Test("Scenario capture / apply",()=>{var p=MapProject();var sc=Scenario.Capture(p,"Initial");p.Devices[0].Points[0].Value=42;p.Devices[0].Responding=false;sc.Apply(p);Near(p.Devices[0].Points[0].Value,0);Assert(p.Devices[0].Responding);});
  Test("Invalid scenario applies no partial changes",()=>{var p=MapProject();var sc=Scenario.Capture(p,"Bad");sc.Values[0].Value=100;sc.Values[1].Point="missing";Reject(()=>sc.Apply(p));Near(p.Devices[0].Points[0].Value,0);});
  Test("Scenario NaN rejected",()=>{var p=MapProject();var sc=Scenario.Capture(p,"Bad");sc.Values[0].Value=double.NaN;Reject(()=>sc.Apply(p));});
  Test("Signals sine / ramp / toggle / manual",()=>{var p=Project.Demo();Point pt=p.Devices[0].Points[0];pt.Minimum=0;pt.Maximum=100;pt.PeriodSeconds=10;Signals.Update(p,2.5,new Random(1));Near(pt.Value,100);pt.Simulation=Wave.Ramp;Signals.Update(p,2.5,new Random(1));Near(pt.Value,25);pt.Simulation=Wave.Toggle;Signals.Update(p,10,new Random(1));Near(pt.Value,100);pt.Simulation=Wave.Manual;Signals.Update(p,20,new Random(1));Near(pt.Value,100);});
  Test("Signals boolean 0..1",()=>{var p=MapProject();Point pt=p.Devices[0].Points.First(x=>x.Name=="C0");pt.Simulation=Wave.Toggle;pt.Minimum=0;pt.Maximum=1;pt.PeriodSeconds=1;Signals.Update(p,0,new Random());Near(pt.Value,0);Signals.Update(p,1,new Random());Near(pt.Value,1);});
  Test("Sequence restores baseline between scenarios",()=>{var p=MapProject();var sc=new Scenario{Name="Stimulus"};sc.Values.Add(new ScenarioValue{Device="Test",Point="H0",Value=70});p.Scenarios.Add(sc);p.Scenarios.Add(new Scenario{Name="Second"});string file=Path.Combine(scratch,"evidence.csv");var sequence=new ScenarioSequence(p,2,file);sequence.Advance(0);Near(p.Devices[0].Points[0].Value,70);Assert(!sequence.Completed);sequence.Advance(2);Near(p.Devices[0].Points[0].Value,0);sequence.Advance(4);Assert(sequence.Completed&&File.ReadAllText(file).Contains("Completed"));});
  Test("Sequence cancellation restores initial values",()=>{var p=MapProject();var sc=new Scenario{Name="Stimulus"};sc.Values.Add(new ScenarioValue{Device="Test",Point="H0",Value=70});p.Scenarios.Add(sc);var sequence=new ScenarioSequence(p,2,null);sequence.Advance(0);sequence.Cancel(0.5);Near(p.Devices[0].Points[0].Value,0);Assert(sequence.Completed);});
  Test("Sequence interval validation",()=>{var p=MapProject();p.Scenarios.Add(Scenario.Capture(p,"Start"));Reject(()=>new ScenarioSequence(p,0,null));});
  Test("Command-line convert and validate",()=>{string exe=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ModbusEmulatorX.Cli.exe");string file=Path.Combine(scratch,"cli.xlsx");RunCli(exe,"demo \""+file+"\"");RunCli(exe,"validate \""+file+"\"");string native=Path.Combine(scratch,"cli.mbx");RunCli(exe,"convert \""+file+"\" \""+native+"\"");Assert(Project.Load(native).Devices.Count==1);});
  Test("Command-line bounded server run",()=>{var p=MapProject();p.Devices[0].Port=FreePort();string file=Path.Combine(scratch,"serve.mbx");p.Save(file);RunCli(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ModbusEmulatorX.Cli.exe"),"serve \""+file+"\" --seconds 0.1 --signals");});
  TimelineTests.Run(Test,scratch);
  NetworkTests.Run(Test,scratch);
  Test("Headless form construction and layout (never shown)",()=>{using(var f=new MainForm()){Assert(f.Text.StartsWith("Modbus Emulator X"));Assert(f.Width>=1050);}});
  Test("Author credit remains at the right of the status footer",()=>{using(var f=new MainForm()){f.PerformLayout();var credit=f.Controls.Find("AuthorCredit",true).Single() as System.Windows.Forms.Label;Assert(credit!=null&&credit.Text=="William Keay · 2026");Assert(credit.TextAlign==System.Drawing.ContentAlignment.MiddleRight);var footer=credit.Parent as System.Windows.Forms.TableLayoutPanel;Assert(footer!=null&&footer.GetColumn(credit)==1&&footer.GetRow(credit)==0);Assert(footer.ColumnStyles[0].SizeType==System.Windows.Forms.SizeType.Percent&&footer.ColumnStyles[1].SizeType==System.Windows.Forms.SizeType.AutoSize);}});
  Test("Table and trend enable buffered drawing",()=>{using(var grid=new LivePointGrid())using(var trend=new BufferedTrendPanel()){var getStyle=typeof(System.Windows.Forms.Control).GetMethod("GetStyle",BindingFlags.Instance|BindingFlags.NonPublic);foreach(var control in new System.Windows.Forms.Control[]{grid,trend}){Assert((bool)getStyle.Invoke(control,new object[]{System.Windows.Forms.ControlStyles.OptimizedDoubleBuffer}));Assert((bool)getStyle.Invoke(control,new object[]{System.Windows.Forms.ControlStyles.AllPaintingInWmPaint}));}}});
  Test("Unchanged live data requests no repeated cell redraws",()=>{var tracker=new LiveCellTracker();var point=new Point{Value=23.5,Simulation=Wave.Manual};Assert(tracker.Observe(point,true,true)==(LiveCellChanges.Value|LiveCellChanges.Simulation));for(int i=0;i<1000;i++)Assert(tracker.Observe(point,true,true)==LiveCellChanges.None);});
  Test("A changed live value redraws only its value cell",()=>{var tracker=new LiveCellTracker();var point=new Point{Value=1};tracker.Observe(point,true,true);point.Value=2;Assert(tracker.Observe(point,true,true)==LiveCellChanges.Value);Assert(tracker.Observe(point,true,true)==LiveCellChanges.None);});
  Test("A changed signal mode redraws only its mode cell",()=>{var tracker=new LiveCellTracker();var point=new Point{Value=1};tracker.Observe(point,true,true);point.Simulation=Wave.Ramp;Assert(tracker.Observe(point,true,true)==LiveCellChanges.Simulation);});
  Test("Offscreen value changes survive horizontal scrolling",()=>{var tracker=new LiveCellTracker();var point=new Point{Value=1};tracker.Observe(point,true,true);point.Value=50;Assert(tracker.Observe(point,false,true)==LiveCellChanges.None);Assert(tracker.Observe(point,false,false)==LiveCellChanges.None);Assert(tracker.Observe(point,true,true)==LiveCellChanges.Value);});
  Test("Offscreen mode changes are not consumed by value refreshes",()=>{var tracker=new LiveCellTracker();var point=new Point{Value=1};tracker.Observe(point,true,true);point.Simulation=Wave.Sine;Assert(tracker.Observe(point,true,false)==LiveCellChanges.None);Assert(tracker.Observe(point,true,true)==LiveCellChanges.Simulation);});
  Test("Live rows are tracked independently after scrolling",()=>{var tracker=new LiveCellTracker();var first=new Point{Value=1};var second=new Point{Value=1};tracker.Observe(first,true,true);Assert(tracker.Observe(second,true,true)==(LiveCellChanges.Value|LiveCellChanges.Simulation));first.Value=42;Assert(tracker.Observe(second,true,true)==LiveCellChanges.None);Assert(tracker.Observe(first,true,true)==LiveCellChanges.Value);tracker.Clear();Assert(tracker.Observe(second,true,true)==(LiveCellChanges.Value|LiveCellChanges.Simulation));});
  Test("A Modbus write is detected without resetting the table",()=>{var d=Map();var point=d.Points.Single(p=>p.Name=="H0");var tracker=new LiveCellTracker();tracker.Observe(point,true,true);Equal(Engine.Process(d,new byte[]{6,0,0,0,99}),6,0,0,0,99);Assert(tracker.Observe(point,true,true)==LiveCellChanges.Value);Near(point.Value,99);});
  Test("Hidden tables skip periodic redraw and do not create a window",()=>{using(var grid=new LivePointGrid()){Assert(!grid.IsHandleCreated);Assert(grid.RefreshLiveCells()==0);Assert(!grid.IsHandleCreated);}});
  Test("Actual TCP transactions, unit routing, fragmentation, stop/restart",Network);
  Test("TCP timeout and 100-percent packet loss",NetworkSilence);
  Test("TCP malformed header closes connection",NetworkBadHeader);
  Test("Random malformed requests do not escape protocol handler",()=>{var d=Map();var rng=new Random(7);for(int i=0;i<2000;i++){byte[] req=new byte[rng.Next(1,260)];rng.NextBytes(req);byte[] res=Engine.Process(d,req);Assert(res!=null&&res.Length>=2);}});
 }finally{try{Directory.Delete(scratch,true);}catch{}}
 Console.WriteLine("RESULT: "+passed+" passed, "+failed+" failed.");return failed==0?0:1;}
 static void EditSheet(string path,string name,Action<XDocument> edit){using(var s=File.Open(path,FileMode.Open))using(var z=new ZipArchive(s,ZipArchiveMode.Update)){var entry=z.GetEntry(name);XDocument doc;using(var stream=entry.Open())doc=XDocument.Load(stream);edit(doc);entry.Delete();using(var stream=z.CreateEntry(name).Open())doc.Save(stream);}}
 static void RunCli(string exe,string args){using(var p=System.Diagnostics.Process.Start(new ProcessStartInfo(exe,args){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true})){string result=p.StandardOutput.ReadToEnd();string errors=p.StandardError.ReadToEnd();Assert(p.WaitForExit(5000),"CLI did not exit.");Assert(p.ExitCode==0,result+errors);}}
 static byte[] ReadExact(Stream s,int n){byte[] b=new byte[n];int at=0;while(at<n){int count=s.Read(b,at,n-at);if(count==0)throw new EndOfStreamException();at+=count;}return b;}
 static byte[] Transaction(NetworkStream stream,byte unit,byte[] pdu,bool split=false){byte[] req=new byte[7+pdu.Length];req[0]=0x12;req[1]=0x34;req[6]=unit;Engine.Put(req,4,pdu.Length+1);Array.Copy(pdu,0,req,7,pdu.Length);if(split){stream.Write(req,0,3);Thread.Sleep(10);stream.Write(req,3,req.Length-3);}else stream.Write(req,0,req.Length);byte[] header=ReadExact(stream,7);Assert(header[0]==0x12&&header[1]==0x34&&header[6]==unit);return ReadExact(stream,Engine.U(header,4)-1);}
 static void Network(){var reserve=new TcpListener(IPAddress.Loopback,0);reserve.Start();int port=((IPEndPoint)reserve.LocalEndpoint).Port;reserve.Stop();var p=MapProject();p.Devices[0].Port=port;var second=Map();second.Name="Unit 2";second.UnitId=2;second.Port=port;second.Points.First(x=>x.Name=="H0").Value=999;p.Devices.Add(second);
  using(var engine=new Engine()){engine.Start(p);using(var client=new TcpClient("127.0.0.1",port)){client.ReceiveTimeout=2000;var s=client.GetStream();Equal(Transaction(s,1,new byte[]{3,0,0,0,1},true),3,2,0,0);Equal(Transaction(s,2,new byte[]{3,0,0,0,1}),3,2,3,231);Equal(Transaction(s,1,new byte[]{6,0,0,0,77}),6,0,0,0,77);Equal(Transaction(s,1,new byte[]{3,0,0,0,1}),3,2,0,77);
   p.Devices[0].ExceptionCode=6;Equal(Transaction(s,1,new byte[]{3,0,0,0,1}),131,6);p.Devices[0].ExceptionCode=0;
   p.Devices[0].DelayMs=100;var clock=Stopwatch.StartNew();Transaction(s,1,new byte[]{3,0,0,0,1});Assert(clock.ElapsedMilliseconds>=80);p.Devices[0].DelayMs=0;
   engine.Stop();Assert(!engine.Running);engine.Start(p);
  }using(var client=new TcpClient("127.0.0.1",port)){client.ReceiveTimeout=2000;Equal(Transaction(client.GetStream(),2,new byte[]{3,0,0,0,1}),3,2,3,231);}engine.Stop();Assert(engine.Requests>=7&&engine.Writes==1&&engine.Errors>=1);
  }
 }
 static int FreePort(){var l=new TcpListener(IPAddress.Loopback,0);l.Start();int port=((IPEndPoint)l.LocalEndpoint).Port;l.Stop();return port;}
 static void NetworkSilence(){var p=MapProject();p.Devices[0].Port=FreePort();using(var engine=new Engine()){engine.Start(p);foreach(int mode in new[]{0,1,2}){p.Devices[0].Responding=mode!=0;p.Devices[0].DropPercent=mode==1?100:0;using(var c=new TcpClient("127.0.0.1",p.Devices[0].Port)){c.ReceiveTimeout=150;bool timeout=false;try{Transaction(c.GetStream(),(byte)(mode==2?99:1),new byte[]{3,0,0,0,1});}catch(IOException){timeout=true;}Assert(timeout,"Expected no reply for offline/loss/unknown unit.");}}}}
 static void NetworkBadHeader(){var p=MapProject();p.Devices[0].Port=FreePort();using(var engine=new Engine()){engine.Start(p);using(var c=new TcpClient("127.0.0.1",p.Devices[0].Port)){c.ReceiveTimeout=2000;var s=c.GetStream();byte[] request={0,1,0,1,0,6,1,3,0,0,0,1};s.Write(request,0,request.Length);bool closed=false;try{closed=s.ReadByte()==-1;}catch(IOException){closed=true;}Assert(closed);}}}
}

