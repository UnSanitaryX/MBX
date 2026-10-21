using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Serialization;

namespace ModbusEmulatorX {
public enum Area { Coil, DiscreteInput, HoldingRegister, InputRegister }
public enum ValueType { Bool, UInt16, Int16, UInt32, Int32, Float32 }
public enum Wave { Manual, Sine, Ramp, Random, Toggle }
public enum Transport { Tcp, Rtu }
public class Point {
 public string Name {get;set;} public Area Area {get;set;} public int Address {get;set;}
 public ValueType Type {get;set;} public bool SwapWords {get;set;} public bool SwapBytes {get;set;}
 public double Value {get;set;} public double Default {get;set;} public double Scale {get;set;} public double Offset {get;set;}
 public string Units {get;set;} public Wave Simulation {get;set;} public double Minimum {get;set;} public double Maximum {get;set;} public double PeriodSeconds {get;set;}
 public bool Writable {get;set;} public string Notes {get;set;}
 [XmlIgnore, Browsable(false)] public int Width {get {return Type==ValueType.UInt32||Type==ValueType.Int32||Type==ValueType.Float32?2:1;}}
 public Point(){Name="Point"; Scale=1; Maximum=100; PeriodSeconds=10; Writable=true; Units=""; Notes=""; Type=ValueType.UInt16; Area=Area.HoldingRegister;}
 public ushort[] Encode(){
  double raw=(Value-Offset)/Scale; byte[] bytes;
  switch(Type){
   case ValueType.Bool: if(Value!=0&&Value!=1)throw new ArgumentException("Bool value must be 0 or 1.");return new ushort[]{(ushort)Value};
   case ValueType.UInt16: bytes=BitConverter.GetBytes(checked((ushort)Math.Round(raw)));break;
   case ValueType.Int16: bytes=BitConverter.GetBytes(checked((short)Math.Round(raw)));break;
   case ValueType.UInt32: bytes=BitConverter.GetBytes(checked((uint)Math.Round(raw)));break;
   case ValueType.Int32: bytes=BitConverter.GetBytes(checked((int)Math.Round(raw)));break;
   default: float f=(float)raw;if(float.IsNaN(f)||float.IsInfinity(f))throw new OverflowException("Value exceeds Float32 range.");bytes=BitConverter.GetBytes(f);break;
  }
  if(BitConverter.IsLittleEndian)Array.Reverse(bytes);
  ushort[] words=new ushort[bytes.Length/2]; for(int i=0;i<words.Length;i++) words[i]=(ushort)((bytes[i*2]<<8)|bytes[i*2+1]);
  if(SwapBytes)for(int i=0;i<words.Length;i++)words[i]=(ushort)((words[i]<<8)|(words[i]>>8));
  if(SwapWords)Array.Reverse(words); return words;
 }
 public double Decode(ushort[] input){
  ushort[] w=(ushort[])input.Clone(); if(SwapWords)Array.Reverse(w);
  if(SwapBytes)for(int i=0;i<w.Length;i++)w[i]=(ushort)((w[i]<<8)|(w[i]>>8));
  byte[] b=new byte[w.Length*2]; for(int i=0;i<w.Length;i++){b[i*2]=(byte)(w[i]>>8);b[i*2+1]=(byte)w[i];}
  if(BitConverter.IsLittleEndian)Array.Reverse(b); double r;
  switch(Type){case ValueType.Bool:return w[0]==0?0:1;case ValueType.UInt16:r=BitConverter.ToUInt16(b,0);break;case ValueType.Int16:r=BitConverter.ToInt16(b,0);break;case ValueType.UInt32:r=BitConverter.ToUInt32(b,0);break;case ValueType.Int32:r=BitConverter.ToInt32(b,0);break;default:r=BitConverter.ToSingle(b,0);break;}
  return r*Scale+Offset;
 }
}
public class Device {
 public Transport Transport {get;set;}
 public string SerialPort {get;set;} public int BaudRate {get;set;} public int DataBits {get;set;}
 public System.IO.Ports.Parity Parity {get;set;} public System.IO.Ports.StopBits StopBits {get;set;}
 public string Name {get;set;} public string BindAddress {get;set;} public int Port {get;set;} public int UnitId {get;set;}
 public bool Enabled {get;set;} public bool AllowWrites {get;set;} public bool Responding {get;set;}
 public int DelayMs {get;set;} public int DropPercent {get;set;} public int ExceptionCode {get;set;}
 [Browsable(false)] public BindingList<Point> Points {get;set;}
 [XmlIgnore, Browsable(false)] public object Sync=new object();
 public Device(){Name="Device";BindAddress="127.0.0.1";Port=1502;UnitId=1;Enabled=true;Responding=true;Points=new BindingList<Point>();SerialPort="COM1";BaudRate=9600;DataBits=8;StopBits=System.IO.Ports.StopBits.One;Parity=System.IO.Ports.Parity.Even;}
 public override string ToString(){return Name+"  |  "+(Transport==Transport.Tcp?BindAddress+":"+Port:SerialPort+" RTU")+" / "+UnitId;}
 public ushort[] Read(Area area,int start,int count){lock(Sync){ushort[] data=new ushort[count];bool[] found=new bool[count];foreach(Point p in Points.Where(p=>p.Area==area&&p.Address<start+count&&p.Address+p.Width>start)){ushort[] w=p.Encode();for(int i=0;i<w.Length;i++){int ix=p.Address+i-start;if(ix>=0&&ix<count){data[ix]=w[i];found[ix]=true;}}}if(found.Any(x=>!x))throw new AddressException();return data;}}
 public void Write(Area area,int start,ushort[] values){lock(Sync){Read(area,start,values.Length); var affected=Points.Where(p=>p.Area==area&&p.Address<start+values.Length&&p.Address+p.Width>start).ToList();if(affected.Any(p=>!p.Writable))throw new AddressException();var changes=new List<KeyValuePair<Point,double>>();foreach(Point p in affected){ushort[] w=p.Encode();for(int i=0;i<w.Length;i++){int ix=p.Address+i-start;if(ix>=0&&ix<values.Length)w[i]=values[ix];}double v=p.Decode(w);if(double.IsNaN(v)||double.IsInfinity(v))throw new ArgumentException("Non-finite value");changes.Add(new KeyValuePair<Point,double>(p,v));}foreach(var change in changes)change.Key.Value=change.Value;}}
}
public class AddressException:Exception{}
public class Project {
 public List<Scenario> Scenarios {get;set;}
 public List<Timeline> Timelines {get;set;}
 public int Version {get;set;} public string Name {get;set;} public BindingList<Device> Devices {get;set;}
 public Project(){Version=1;Name="Untitled project";Devices=new BindingList<Device>();Scenarios=new List<Scenario>();Timelines=new List<Timeline>();}
 public static Project Demo(){var p=new Project{Name="Modbus Emulator X demo"};var d=new Device{Name="Process simulator"};d.Points.Add(new Point{Name="Temperature",Type=ValueType.Float32,Value=22.5,Default=22.5,Units="deg C",Minimum=15,Maximum=35,Simulation=Wave.Sine});d.Points.Add(new Point{Name="Pressure",Address=2,Scale=0.01,Value=6.2,Default=6.2,Units="bar",Maximum=10});d.Points.Add(new Point{Name="Motor run",Area=Area.Coil,Type=ValueType.Bool});d.Points.Add(new Point{Name="Healthy",Area=Area.DiscreteInput,Type=ValueType.Bool,Value=1,Default=1});d.Points.Add(new Point{Name="Supply",Area=Area.InputRegister,Value=240,Default=240});p.Devices.Add(d);p.Timelines.Add(new Timeline{Name="Demo: ramp, fault and recovery",DurationSeconds=15,Steps=new List<TimelineStep>{new TimelineStep{Device=d.Name,Point="Temperature",Action=TimelineAction.SetValue,Value=22.5},new TimelineStep{AtSeconds=2,Device=d.Name,Point="Temperature",Action=TimelineAction.RampValue,Value=35,DurationSeconds=5},new TimelineStep{AtSeconds=4,Device=d.Name,Action=TimelineAction.SetDelay,Value=150,DurationSeconds=3},new TimelineStep{AtSeconds=9,Device=d.Name,Action=TimelineAction.SetResponse,Value=0,DurationSeconds=2},new TimelineStep{AtSeconds=12,Device=d.Name,Action=TimelineAction.RestoreDevice}}});return p;}
 public T WithLocks<T>(Func<T> action){return LockAt(0,action);}
 T LockAt<T>(int at,Func<T> action){if(Devices==null||at==Devices.Count)return action();if(Devices[at]==null)return LockAt(at+1,action);lock(Devices[at].Sync)return LockAt(at+1,action);}
 public void Validate(){WithLocks(()=>{ValidateCore();return true;});}
 void ValidateCore(){
  if(Version!=1)throw new Exception("Unsupported project version."); if(string.IsNullOrWhiteSpace(Name))throw new Exception("Project name is required.");if(Devices==null||Scenarios==null||Timelines==null)throw new Exception("Missing project collections.");
  var ids=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  foreach(Device d in Devices){
   if(d==null||d.Points==null)throw new Exception("Invalid device.");
   if(!Enum.IsDefined(typeof(Transport),d.Transport))throw new Exception("Unknown transport.");
   System.Net.IPAddress ip;if(d.Transport==Transport.Tcp&&(!System.Net.IPAddress.TryParse(d.BindAddress,out ip)||ip.AddressFamily!=System.Net.Sockets.AddressFamily.InterNetwork))throw new Exception(d.Name+": use an IPv4 bind address.");
   if(string.IsNullOrWhiteSpace(d.Name)||!names.Add(d.Name))throw new Exception("Device names must be nonblank and unique.");
   if(d.Port<1||d.Port>65535||d.UnitId<0||d.UnitId>255)throw new Exception(d.Name+": invalid port or unit ID.");
   if(d.Transport==Transport.Rtu&&(string.IsNullOrWhiteSpace(d.SerialPort)||d.UnitId<1||d.UnitId>247||d.BaudRate<300||d.BaudRate>4000000||d.DataBits!=8||!Enum.IsDefined(typeof(System.IO.Ports.Parity),d.Parity)||(d.StopBits!=System.IO.Ports.StopBits.One&&d.StopBits!=System.IO.Ports.StopBits.Two)))throw new Exception(d.Name+": RTU needs a port, unit 1..247, 8 data bits, valid baud/parity and 1 or 2 stop bits.");
   if(d.Enabled&&!ids.Add(d.Transport+":"+(d.Transport==Transport.Tcp?d.BindAddress+":"+d.Port:d.SerialPort.ToUpperInvariant())+":"+d.UnitId))throw new Exception("Duplicate endpoint and unit ID.");
   if(d.DelayMs<0||d.DelayMs>60000||d.DropPercent<0||d.DropPercent>100||d.ExceptionCode<0||d.ExceptionCode>11)throw new Exception(d.Name+": invalid fault settings.");
   var occupied=new HashSet<string>(); var pointNames=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   foreach(Point p in d.Points){try{
    if(string.IsNullOrWhiteSpace(p.Name)||!pointNames.Add(p.Name))throw new Exception("Point names must be unique and nonblank.");
    if(!Enum.IsDefined(typeof(Area),p.Area)||!Enum.IsDefined(typeof(ValueType),p.Type)||!Enum.IsDefined(typeof(Wave),p.Simulation))throw new Exception("Unknown enum value.");
    if(p.Address<0||p.Address+p.Width>65536)throw new Exception("Address must fit 0..65535.");
    if((p.Area==Area.Coil||p.Area==Area.DiscreteInput)!=(p.Type==ValueType.Bool))throw new Exception("Bit tables require Bool; register tables require a numeric type.");
    if(p.Scale==0||!Finite(p.Scale)||!Finite(p.Offset)||!Finite(p.Value)||!Finite(p.Default)||!Finite(p.Minimum)||!Finite(p.Maximum)||!Finite(p.PeriodSeconds))throw new Exception("Values must be finite and scale nonzero.");
    if(p.PeriodSeconds<=0||p.Minimum>p.Maximum)throw new Exception("Invalid simulation range or period.");
    p.Encode();double current=p.Value;try{p.Value=p.Default;p.Encode();if(p.Simulation!=Wave.Manual){p.Value=p.Minimum;p.Encode();p.Value=p.Maximum;p.Encode();}}finally{p.Value=current;}
    for(int i=0;i<p.Width;i++)if(!occupied.Add(p.Area+":"+(p.Address+i)))throw new Exception("Overlapping address.");
   }catch(Exception e){throw new Exception(d.Name+" / "+p.Name+": "+e.Message);}}
  }
  foreach(Device d in Devices.Where(d=>d.Enabled&&d.Transport==Transport.Tcp&&d.BindAddress=="0.0.0.0"))if(Devices.Any(x=>x.Enabled&&x.Transport==Transport.Tcp&&x.Port==d.Port&&x.BindAddress!="0.0.0.0"))throw new Exception("Wildcard bind overlaps another listener.");
  foreach(var group in Devices.Where(d=>d.Enabled&&d.Transport==Transport.Rtu).GroupBy(d=>d.SerialPort,StringComparer.OrdinalIgnoreCase)){Device first=group.First();if(group.Any(d=>d.BaudRate!=first.BaudRate||d.DataBits!=first.DataBits||d.Parity!=first.Parity||d.StopBits!=first.StopBits))throw new Exception("Devices sharing a serial port must use the same serial settings.");}
  var timelineNames=new HashSet<string>(StringComparer.OrdinalIgnoreCase);foreach(var timeline in Timelines){if(timeline==null||!timelineNames.Add(timeline.Name))throw new Exception("Timeline names must be unique.");timeline.Validate(this);}
  var scenarioNames=new HashSet<string>(StringComparer.OrdinalIgnoreCase);foreach(Scenario scenario in Scenarios){if(scenario==null||string.IsNullOrWhiteSpace(scenario.Name)||!scenarioNames.Add(scenario.Name))throw new Exception("Scenario names must be nonblank and unique.");scenario.Validate(this);}
 }
 static bool Finite(double v){return !double.IsNaN(v)&&!double.IsInfinity(v);}
 public void Save(string path){WithLocks(()=>{Validate();Atomic(path,s=>new XmlSerializer(typeof(Project)).Serialize(s,this));return true;});}
 public Project Snapshot(){return WithLocks(()=>{var serializer=new XmlSerializer(typeof(Project));using(var ms=new MemoryStream()){serializer.Serialize(ms,this);ms.Position=0;return (Project)serializer.Deserialize(ms);}});}
 public static Project Load(string path){Project p;using(var s=File.OpenRead(path))using(var reader=System.Xml.XmlReader.Create(s,new System.Xml.XmlReaderSettings{DtdProcessing=System.Xml.DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=50000000}))p=(Project)new XmlSerializer(typeof(Project)).Deserialize(reader);p.Validate();return p;}
 static void Atomic(string path,Action<Stream> write){string tmp=path+"."+Guid.NewGuid()+".tmp";try{using(var s=File.Create(tmp))write(s);if(File.Exists(path))File.Replace(tmp,path,null);else File.Move(tmp,path);}finally{if(File.Exists(tmp))File.Delete(tmp);}}
 static string[] Headers={"Schema","Project","Device","BindAddress","Port","UnitId","Enabled","AllowWrites","Responding","DelayMs","DropPercent","ExceptionCode","Point","Area","Address","Type","SwapWords","SwapBytes","Value","Default","Scale","Offset","Units","Simulation","Minimum","Maximum","PeriodSeconds","Writable","Notes","Transport","SerialPort","BaudRate","DataBits","Parity","StopBits"};
 static string S(object v){return Convert.ToString(v,CultureInfo.InvariantCulture)??"";}
 static string Q(string s){if(s.StartsWith("'")||s.StartsWith("=")||s.StartsWith("+")||s.StartsWith("@")||s.StartsWith("\t")||s.StartsWith("\r")||(s.StartsWith("-")&&!IsNumber(s)))s="'"+s;return "\""+s.Replace("\"","\"\"")+"\"";}
 static bool IsNumber(string s){double n;return double.TryParse(s,NumberStyles.Float,CultureInfo.InvariantCulture,out n);}
 public string CsvText(){Validate();var w=new StringBuilder();w.AppendLine(string.Join(",",Headers));foreach(Device d in Devices){IEnumerable<Point> pts=d.Points.Count==0?(IEnumerable<Point>)new Point[]{null}:d.Points;foreach(Point p in pts){var r=new List<object>{Version,Name,d.Name,d.BindAddress,d.Port,d.UnitId,d.Enabled,d.AllowWrites,d.Responding,d.DelayMs,d.DropPercent,d.ExceptionCode};if(p!=null)r.AddRange(new object[]{p.Name,p.Area,p.Address,p.Type,p.SwapWords,p.SwapBytes,p.Value,p.Default,p.Scale,p.Offset,p.Units,p.Simulation,p.Minimum,p.Maximum,p.PeriodSeconds,p.Writable,p.Notes});while(r.Count<29)r.Add("");r.AddRange(new object[]{d.Transport,d.SerialPort,d.BaudRate,d.DataBits,d.Parity,d.StopBits});w.AppendLine(string.Join(",",r.Select(x=>Q(S(x)))));}}if(Devices.Count==0){var r=new string[Headers.Length];r[0]="1";r[1]=Name;w.AppendLine(string.Join(",",r.Select(x=>Q(x??"")))) ;}return w.ToString();}
 public void ExportCsv(string path){byte[] bytes=new UTF8Encoding(true).GetBytes("\ufeff"+WithLocks(()=>CsvText()));Atomic(path,stream=>stream.Write(bytes,0,bytes.Length));}
 public static Project ImportCsv(string path){return ImportCsvText(File.ReadAllText(path,Encoding.UTF8));}
 public static Project ImportCsvText(string text){
  var rows=Csv.Parse(text);if(rows.Count==0)throw new Exception("Empty CSV.");
  var h=rows[0]; if(h.Length!=Headers.Length||!h.SequenceEqual(Headers))throw new Exception("CSV headers must match the exported template (schema 1).");
  Project result=new Project();var devices=new Dictionary<string,Device>();var signatures=new Dictionary<string,string>(); bool first=true;
  for(int i=1;i<rows.Count;i++){string[] r=rows[i];if(r.All(string.IsNullOrWhiteSpace))continue;try{
   if(r.Length!=Headers.Length)throw new Exception("Expected "+Headers.Length+" columns.");
   if(first){result.Version=I(r[0]);result.Name=r[1];first=false;}else if(result.Version!=I(r[0])||result.Name!=r[1])throw new Exception("Project fields differ between rows.");
   if(r[2]==""){if(r.Skip(2).Any(x=>x!=""))throw new Exception("Device name is missing.");continue;}
   string signature=string.Join("\u001f",r.Take(12).Concat(r.Skip(29)));Device d;
   if(!devices.TryGetValue(r[2],out d)){d=new Device{Name=r[2],BindAddress=r[3],Port=I(r[4]),UnitId=I(r[5]),Enabled=B(r[6]),AllowWrites=B(r[7]),Responding=B(r[8]),DelayMs=I(r[9]),DropPercent=I(r[10]),ExceptionCode=I(r[11]),Transport=E<Transport>(r[29]),SerialPort=r[30],BaudRate=I(r[31]),DataBits=I(r[32]),Parity=E<System.IO.Ports.Parity>(r[33]),StopBits=E<System.IO.Ports.StopBits>(r[34])};devices.Add(d.Name,d);signatures.Add(d.Name,signature);result.Devices.Add(d);}else if(signatures[d.Name]!=signature)throw new Exception("Repeated device settings differ.");
   if(r[12]!="")d.Points.Add(new Point{Name=r[12],Area=E<Area>(r[13]),Address=I(r[14]),Type=E<ValueType>(r[15]),SwapWords=B(r[16]),SwapBytes=B(r[17]),Value=D(r[18]),Default=D(r[19]),Scale=D(r[20]),Offset=D(r[21]),Units=r[22],Simulation=E<Wave>(r[23]),Minimum=D(r[24]),Maximum=D(r[25]),PeriodSeconds=D(r[26]),Writable=B(r[27]),Notes=r[28]});
  }catch(Exception e){throw new Exception("CSV row "+(i+1)+": "+e.Message);}}
  if(first)throw new Exception("CSV contains no project rows.");result.Validate();return result;
 }
 static int I(string s){return int.Parse(s,NumberStyles.Integer,CultureInfo.InvariantCulture);}static double D(string s){return double.Parse(s,NumberStyles.Float,CultureInfo.InvariantCulture);}static bool B(string s){if(s=="1")return true;if(s=="0")return false;return bool.Parse(s);}static T E<T>(string s){return (T)Enum.Parse(typeof(T),s,true);}
}
public class Scenario {
 public string Name {get;set;} public List<ScenarioValue> Values {get;set;} public List<DeviceState> Devices {get;set;}
 public Scenario(){Values=new List<ScenarioValue>();Devices=new List<DeviceState>();}
 public override string ToString(){return Name;}
 public static Scenario Capture(Project p,string name){var s=new Scenario{Name=name};foreach(Device d in p.Devices)lock(d.Sync){s.Devices.Add(new DeviceState{Name=d.Name,Responding=d.Responding,DelayMs=d.DelayMs,DropPercent=d.DropPercent,ExceptionCode=d.ExceptionCode});foreach(Point pt in d.Points)s.Values.Add(new ScenarioValue{Device=d.Name,Point=pt.Name,Value=pt.Value,Simulation=pt.Simulation});}return s;}
 public void Validate(Project p){if(Values==null||Devices==null)throw new Exception("Scenario collections missing.");var seen=new HashSet<string>();foreach(var state in Devices){if(state==null||!seen.Add(state.Name)||!p.Devices.Any(d=>d.Name==state.Name))throw new Exception("Invalid, duplicate or missing scenario device.");if(state.DelayMs<0||state.DelayMs>60000||state.DropPercent<0||state.DropPercent>100||state.ExceptionCode<0||state.ExceptionCode>11)throw new Exception("Invalid scenario faults.");}seen.Clear();foreach(var v in Values){if(v==null||!seen.Add(v.Device+"\u001f"+v.Point)||!Enum.IsDefined(typeof(Wave),v.Simulation)||double.IsNaN(v.Value)||double.IsInfinity(v.Value))throw new Exception("Invalid scenario point.");Device d=p.Devices.FirstOrDefault(x=>x.Name==v.Device);Point pt=d==null?null:d.Points.FirstOrDefault(x=>x.Name==v.Point);if(pt==null)throw new Exception("Scenario point missing: "+v.Device+" / "+v.Point);lock(d.Sync){double old=pt.Value;try{pt.Value=v.Value;pt.Encode();if(v.Simulation!=Wave.Manual){pt.Value=pt.Minimum;pt.Encode();pt.Value=pt.Maximum;pt.Encode();}}finally{pt.Value=old;}}}}
 public void Apply(Project p){Validate(p);foreach(var state in Devices){Device d=p.Devices.First(x=>x.Name==state.Name);d.Responding=state.Responding;d.DelayMs=state.DelayMs;d.DropPercent=state.DropPercent;d.ExceptionCode=state.ExceptionCode;}foreach(var v in Values){Device d=p.Devices.First(x=>x.Name==v.Device);lock(d.Sync){Point pt=d.Points.First(x=>x.Name==v.Point);pt.Value=v.Value;pt.Simulation=v.Simulation;}}}
}
public class ScenarioValue{public string Device {get;set;}public string Point {get;set;}public double Value {get;set;}public Wave Simulation {get;set;}}
public class DeviceState{public string Name {get;set;}public bool Responding {get;set;}public int DelayMs {get;set;}public int DropPercent {get;set;}public int ExceptionCode {get;set;}}
public static class Csv {
 public static char Separator(string s){string first=s.Split('\n')[0];return first.Count(c=>c==';')>first.Count(c=>c==',')?';':',';}
 public static List<string[]> Parse(string s){char separator=Separator(s);var rows=new List<string[]>();var row=new List<string>();var cell=new StringBuilder();bool quoted=false;bool begin=true;
  for(int i=0;i<s.Length;i++){char c=s[i];if(i==0&&c=='\ufeff')continue;if(c=='"'){if(quoted&&i+1<s.Length&&s[i+1]=='"'){cell.Append('"');i++;}else if(quoted||begin)quoted=!quoted;else throw new Exception("Unexpected quote in CSV.");}else if(!quoted&&(c==separator||c=='\n'||c=='\r')){row.Add(Unescape(cell.ToString()));cell.Length=0;begin=true;if(c!=separator){rows.Add(row.ToArray());row.Clear();if(c=='\r'&&i+1<s.Length&&s[i+1]=='\n')i++;}continue;}else cell.Append(c);begin=false;}
  if(quoted)throw new Exception("Unclosed quoted CSV field.");if(cell.Length>0||row.Count>0){row.Add(Unescape(cell.ToString()));rows.Add(row.ToArray());}return rows;
 }
 static string Unescape(string s){if(s.StartsWith("'")&&s.Length>1&&"'=+@-\t\r".IndexOf(s[1])>=0)return s.Substring(1);return s;}
}
}

