using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace ModbusEmulatorX {
                                                                                      
public static class ExcelProject {
 const string Main="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
 const string Rel="http://schemas.openxmlformats.org/officeDocument/2006/relationships";
 static readonly XNamespace ns=Main;
 static readonly string[] DeviceFields={"Name","Transport","BindAddress","Port","UnitId","Enabled","AllowWrites","Responding","DelayMs","DropPercent","ExceptionCode","SerialPort","BaudRate","DataBits","Parity","StopBits"};
 static readonly string[] PointFields={"Name","Area","Address","Type","SwapWords","SwapBytes","Value","Default","Scale","Offset","Units","Simulation","Minimum","Maximum","PeriodSeconds","Writable","Notes"};
 static readonly string[] FaultFields={"Name","Responding","DelayMs","DropPercent","ExceptionCode"};
 static readonly string[] ScenarioPointFields={"Device","Point","Value","Simulation"};
 static readonly string[] TimelineFields={"Name","DurationSeconds","RepeatCount","RestoreOnFinish"};
 static readonly string[] StepFields={"AtSeconds","Action","Device","Point","Value","DurationSeconds","Note"};
 static string S(object o){return Convert.ToString(o,CultureInfo.InvariantCulture)??"";}
 static object[] Props(object obj,string[] fields){return fields.Select(f=>obj.GetType().GetProperty(f).GetValue(obj,null)).ToArray();}
 static object[] Join(object first,object[] rest){return new object[]{first}.Concat(rest).ToArray();}
 static List<object[]> Table(string[] headers){return new List<object[]>{headers.Cast<object>().ToArray()};}

 public static void Export(Project p,string path){
  p=p.Snapshot();p.Validate();var sheets=new Dictionary<string,List<object[]>>();
  sheets.Add("Project",new List<object[]>{new object[]{"Schema","Name"},new object[]{p.Version,p.Name}});
  var devices=Table(DeviceFields);var points=Table(new[]{"Device"}.Concat(PointFields).ToArray());
  foreach(Device d in p.Devices){devices.Add(Props(d,DeviceFields));lock(d.Sync)foreach(Point pt in d.Points)points.Add(Join(d.Name,Props(pt,PointFields)));}
  sheets.Add("Devices",devices);sheets.Add("Points",points);
  var scenarios=Table(new[]{"Name"});var faults=Table(new[]{"Scenario"}.Concat(FaultFields).ToArray());var values=Table(new[]{"Scenario"}.Concat(ScenarioPointFields).ToArray());
  foreach(Scenario sc in p.Scenarios){scenarios.Add(new object[]{sc.Name});foreach(var state in sc.Devices)faults.Add(Join(sc.Name,Props(state,FaultFields)));foreach(var state in sc.Values)values.Add(Join(sc.Name,Props(state,ScenarioPointFields)));}
  sheets.Add("Scenarios",scenarios);sheets.Add("ScenarioDevices",faults);sheets.Add("ScenarioPoints",values);
  var timelines=Table(TimelineFields);var steps=Table(new[]{"Timeline"}.Concat(StepFields).ToArray());
  foreach(var timeline in p.Timelines){timelines.Add(Props(timeline,TimelineFields));foreach(var step in timeline.Steps)steps.Add(Join(timeline.Name,Props(step,StepFields)));}
  sheets.Add("Timelines",timelines);sheets.Add("TimelineSteps",steps);
  sheets.Add("Readme",new List<object[]>{new object[]{"Modbus Emulator X - editable project"},new object[]{"Edit Devices and Points. Addresses are zero-based (0..65535)."},new object[]{"Name columns are keys. Keep references consistent when renaming devices, points or scenarios."},new object[]{"Area: Coil, DiscreteInput, HoldingRegister, InputRegister."},new object[]{"Type: Bool, UInt16, Int16, UInt32, Int32, Float32. 32-bit types use two registers."},new object[]{"Engineering value = raw x Scale + Offset. Scale cannot be zero."},new object[]{"Simulation: Manual, Sine, Ramp, Random, Toggle. PeriodSeconds must be positive."},new object[]{"Transport: Tcp or Rtu. TCP shares endpoint by UnitId; RTU shares serial port by UnitId."},new object[]{"Keep headers and sheet names. Save as .xlsx and import in Modbus Emulator X."},new object[]{"Formulas must have cached results saved by Excel. Blank cells use model defaults."},new object[]{"Default local TCP endpoint is 127.0.0.1:1502; writes are disabled initially."}});
  string tmp=path+"."+Guid.NewGuid()+".tmp";
  try{using(var stream=File.Create(tmp))using(var zip=new ZipArchive(stream,ZipArchiveMode.Create)){
   var types=new XElement(XName.Get("Types","http://schemas.openxmlformats.org/package/2006/content-types"),new XElement(XName.Get("Default","http://schemas.openxmlformats.org/package/2006/content-types"),new XAttribute("Extension","rels"),new XAttribute("ContentType","application/vnd.openxmlformats-package.relationships+xml")),new XElement(XName.Get("Default","http://schemas.openxmlformats.org/package/2006/content-types"),new XAttribute("Extension","xml"),new XAttribute("ContentType","application/xml")));
   Action<string,string> over=(part,type)=>types.Add(new XElement(types.Name.Namespace+"Override",new XAttribute("PartName",part),new XAttribute("ContentType",type)));
   over("/xl/workbook.xml","application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml");over("/xl/styles.xml","application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml");
   var relns=(XNamespace)"http://schemas.openxmlformats.org/package/2006/relationships";
   Write(zip,"_rels/.rels",new XElement(relns+"Relationships",new XElement(relns+"Relationship",new XAttribute("Id","rId1"),new XAttribute("Type",Rel+"/officeDocument"),new XAttribute("Target","xl/workbook.xml"))));
   var workbookSheets=new XElement(ns+"sheets");var relationships=new XElement(relns+"Relationships");int index=0;
   foreach(var sheet in sheets){index++;string name="sheet"+index+".xml";over("/xl/worksheets/"+name,"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");workbookSheets.Add(new XElement(ns+"sheet",new XAttribute("name",sheet.Key),new XAttribute("sheetId",index),new XAttribute((XNamespace)Rel+"id","rId"+index)));relationships.Add(new XElement(relns+"Relationship",new XAttribute("Id","rId"+index),new XAttribute("Type",Rel+"/worksheet"),new XAttribute("Target","worksheets/"+name)));Write(zip,"xl/worksheets/"+name,Worksheet(sheet.Value));}
   relationships.Add(new XElement(relns+"Relationship",new XAttribute("Id","styles"),new XAttribute("Type",Rel+"/styles"),new XAttribute("Target","styles.xml")));
   Write(zip,"xl/workbook.xml",new XElement(ns+"workbook",new XAttribute(XNamespace.Xmlns+"r",Rel),workbookSheets,new XElement(ns+"calcPr",new XAttribute("calcId",191029),new XAttribute("fullCalcOnLoad",1))));Write(zip,"xl/_rels/workbook.xml.rels",relationships);Write(zip,"[Content_Types].xml",types);
   Write(zip,"xl/styles.xml",XElement.Parse("<styleSheet xmlns='"+Main+"'><fonts count='2'><font><sz val='11'/><name val='Calibri'/></font><font><b/><color rgb='FFFFFFFF'/><sz val='11'/><name val='Calibri'/></font></fonts><fills count='3'><fill><patternFill patternType='none'/></fill><fill><patternFill patternType='gray125'/></fill><fill><patternFill patternType='solid'><fgColor rgb='FF173D5D'/><bgColor indexed='64'/></patternFill></fill></fills><borders count='1'><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count='1'><xf numFmtId='0' fontId='0' fillId='0' borderId='0'/></cellStyleXfs><cellXfs count='2'><xf numFmtId='0' fontId='0' fillId='0' borderId='0' xfId='0'/><xf numFmtId='0' fontId='1' fillId='2' borderId='0' xfId='0' applyFont='1' applyFill='1'/></cellXfs><cellStyles count='1'><cellStyle name='Normal' xfId='0' builtinId='0'/></cellStyles></styleSheet>"));
  }if(File.Exists(path))File.Replace(tmp,path,null);else File.Move(tmp,path);}finally{if(File.Exists(tmp))File.Delete(tmp);}
 }
 static XElement Worksheet(List<object[]> rows){var data=new XElement(ns+"sheetData");int cols=rows.Max(r=>r.Length);for(int r=0;r<rows.Count;r++){var row=new XElement(ns+"row",new XAttribute("r",r+1));if(r==0){row.Add(new XAttribute("ht",25));row.Add(new XAttribute("customHeight",1));}for(int c=0;c<rows[r].Length;c++){object value=rows[r][c];var cell=new XElement(ns+"c",new XAttribute("r",Column(c)+(r+1)));if(r==0)cell.Add(new XAttribute("s",1));if(value is bool){cell.Add(new XAttribute("t","b"),new XElement(ns+"v",(bool)value?1:0));}else if(value is double||value is int||value is uint||value is ushort){cell.Add(new XElement(ns+"v",S(value)));}else{cell.Add(new XAttribute("t","inlineStr"),new XElement(ns+"is",new XElement(ns+"t",new XAttribute(XNamespace.Xml+"space","preserve"),S(value))));}row.Add(cell);}data.Add(row);}
  var widths=new XElement(ns+"cols");for(int i=0;i<cols;i++){int max=rows.Take(1000).Max(r=>i<r.Length?S(r[i]).Split('\n').Max(s=>s.Length):0);widths.Add(new XElement(ns+"col",new XAttribute("min",i+1),new XAttribute("max",i+1),new XAttribute("width",Math.Max(12,Math.Min(60,max+3))),new XAttribute("customWidth",1)));}
  var sheet=new XElement(ns+"worksheet",new XElement(ns+"dimension",new XAttribute("ref","A1:"+Column(cols-1)+rows.Count)),new XElement(ns+"sheetViews",new XElement(ns+"sheetView",new XAttribute("workbookViewId",0),new XElement(ns+"pane",new XAttribute("ySplit",1),new XAttribute("topLeftCell","A2"),new XAttribute("activePane","bottomLeft"),new XAttribute("state","frozen")))),new XElement(ns+"sheetFormatPr",new XAttribute("defaultRowHeight",16)),widths,data);if(rows.Count>1)sheet.Add(new XElement(ns+"autoFilter",new XAttribute("ref","A1:"+Column(cols-1)+rows.Count)));return sheet;
 }
 static string Column(int n){string s="";for(n++;n>0;n=(n-1)/26)s=(char)('A'+(n-1)%26)+s;return s;}
 static void Write(ZipArchive zip,string path,XElement element){var entry=zip.CreateEntry(path,CompressionLevel.Optimal);using(var s=entry.Open())using(var w=XmlWriter.Create(s,new XmlWriterSettings{Encoding=new UTF8Encoding(false),Indent=false}))new XDocument(new XDeclaration("1.0","utf-8","yes"),element).Save(w);}
 static XDocument Read(ZipArchive zip,string name){var entry=zip.GetEntry(name);if(entry==null||entry.Length>50000000)throw new Exception("Workbook part missing or too large: "+name);using(var s=entry.Open())using(var r=XmlReader.Create(s,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=50000000}))return XDocument.Load(r);}
 public static Project Import(string path){using(var stream=File.OpenRead(path))using(var zip=new ZipArchive(stream,ZipArchiveMode.Read)){
  var book=Read(zip,"xl/workbook.xml");var relations=Read(zip,"xl/_rels/workbook.xml.rels");var shared=new List<string>();if(zip.GetEntry("xl/sharedStrings.xml")!=null)shared=Read(zip,"xl/sharedStrings.xml").Descendants(ns+"si").Select(e=>string.Concat(e.Descendants(ns+"t").Select(t=>t.Value))).ToList();
  var sheets=new Dictionary<string,List<string[]>>(StringComparer.OrdinalIgnoreCase);
  foreach(var s in book.Descendants(ns+"sheet")){string id=(string)s.Attribute((XNamespace)Rel+"id");var rel=relations.Root.Elements().FirstOrDefault(e=>(string)e.Attribute("Id")==id);if(rel==null||(string)rel.Attribute("TargetMode")=="External")throw new Exception("Unsupported workbook relationship.");string target=(string)rel.Attribute("Target");if(target.StartsWith("/"))target=target.TrimStart('/');else target="xl/"+target;var rows=new List<string[]>();
   foreach(var row in Read(zip,target).Descendants(ns+"row")){var cells=new SortedDictionary<int,string>();foreach(var c in row.Elements(ns+"c")){string reference=(string)c.Attribute("r");if(string.IsNullOrEmpty(reference))throw new Exception("Workbook cell reference is missing.");int col=0;foreach(char ch in reference.TakeWhile(char.IsLetter))col=checked(col*26+char.ToUpperInvariant(ch)-'A'+1);if(col<1||col>100)throw new Exception("Too many project columns.");string type=(string)c.Attribute("t"),value=(string)c.Element(ns+"v")??"";if(c.Element(ns+"f")!=null&&value=="")throw new Exception("Formula has no saved result. Recalculate and save the workbook in Excel.");if(type=="s")value=shared[int.Parse(value,CultureInfo.InvariantCulture)];else if(type=="inlineStr")value=string.Concat(c.Descendants(ns+"t").Select(t=>t.Value));else if(type=="b")value=value=="1"?"True":"False";else if(type=="e")throw new Exception("Workbook contains a formula error at "+reference);cells[col-1]=value;}
    if(cells.Count==0)continue;string[] values=new string[cells.Keys.Max()+1];for(int i=0;i<values.Length;i++)values[i]=cells.ContainsKey(i)?cells[i]:"";if(values.Any(x=>x!=""))rows.Add(values);if(rows.Count>200000)throw new Exception("Too many project rows.");
   }sheets.Add((string)s.Attribute("name"),rows);
  }
  var meta=Rows(sheets,"Project",new[]{"Schema","Name"});if(meta.Count!=1)throw new Exception("Project sheet must have one data row.");var p=new Project{Version=int.Parse(meta[0][0],CultureInfo.InvariantCulture),Name=meta[0][1]};
  foreach(var r in Rows(sheets,"Devices",DeviceFields)){var d=new Device();Set(d,DeviceFields,r,0);p.Devices.Add(d);}
  foreach(var r in Rows(sheets,"Points",new[]{"Device"}.Concat(PointFields).ToArray())){Device d=p.Devices.FirstOrDefault(x=>x.Name==r[0]);if(d==null)throw new Exception("Points: unknown device "+r[0]);var pt=new Point();Set(pt,PointFields,r,1);d.Points.Add(pt);}
  foreach(var r in Rows(sheets,"Scenarios",new[]{"Name"})){if(string.IsNullOrWhiteSpace(r[0])||p.Scenarios.Any(x=>x.Name==r[0]))throw new Exception("Scenario names must be nonblank and unique.");p.Scenarios.Add(new Scenario{Name=r[0]});}
  foreach(var r in Rows(sheets,"ScenarioDevices",new[]{"Scenario"}.Concat(FaultFields).ToArray())){Scenario sc=p.Scenarios.FirstOrDefault(x=>x.Name==r[0]);if(sc==null)throw new Exception("Unknown scenario "+r[0]);var state=new DeviceState();Set(state,FaultFields,r,1);sc.Devices.Add(state);}
  foreach(var r in Rows(sheets,"ScenarioPoints",new[]{"Scenario"}.Concat(ScenarioPointFields).ToArray())){Scenario sc=p.Scenarios.FirstOrDefault(x=>x.Name==r[0]);if(sc==null)throw new Exception("Unknown scenario "+r[0]);var state=new ScenarioValue();Set(state,ScenarioPointFields,r,1);sc.Values.Add(state);}
  if(sheets.ContainsKey("Timelines")||sheets.ContainsKey("TimelineSteps")){
   foreach(var r in Rows(sheets,"Timelines",TimelineFields)){var t=new Timeline();Set(t,TimelineFields,r,0);p.Timelines.Add(t);}
   foreach(var r in Rows(sheets,"TimelineSteps",new[]{"Timeline"}.Concat(StepFields).ToArray())){var t=p.Timelines.FirstOrDefault(x=>x.Name==r[0]);if(t==null)throw new Exception("Unknown timeline "+r[0]);var step=new TimelineStep();Set(step,StepFields,r,1);t.Steps.Add(step);}
  }
  p.Validate();foreach(var sc in p.Scenarios)sc.Validate(p);return p;
 }}
 static List<string[]> Rows(Dictionary<string,List<string[]>> sheets,string name,string[] headers){List<string[]> rows;if(!sheets.TryGetValue(name,out rows)||rows.Count==0||!rows[0].SequenceEqual(headers))throw new Exception(name+": sheet or required headers missing.");return rows.Skip(1).Select((r,i)=>{if(r.Length>headers.Length)throw new Exception(name+" row "+(i+2)+": extra columns.");var padded=new string[headers.Length];for(int c=0;c<padded.Length;c++)padded[c]=c<r.Length?r[c]:"";return padded;}).ToList();}
 static void Set(object obj,string[] fields,string[] row,int start){for(int i=0;i<fields.Length;i++){string s=row[start+i];PropertyInfo prop=obj.GetType().GetProperty(fields[i]);try{if(s==""&&prop.PropertyType!=typeof(string))continue;Type t=prop.PropertyType;object value=t.IsEnum?Enum.Parse(t,s,true):t==typeof(bool)?(object)(s=="1"?true:s=="0"?false:bool.Parse(s)):Convert.ChangeType(s,t,CultureInfo.InvariantCulture);prop.SetValue(obj,value,null);}catch(Exception e){throw new Exception(obj.GetType().Name+" "+fields[i]+": "+e.Message);}}}
}
}
