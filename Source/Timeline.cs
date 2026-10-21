using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Serialization;

namespace ModbusEmulatorX {
public enum TimelineAction { SetValue, RampValue, SetResponse, SetDelay, SetDrop, SetException, RestoreDevice }
public sealed class TimelineStep {
 public double AtSeconds {get;set;}
 public TimelineAction Action {get;set;}
 public string Device {get;set;}
 public string Point {get;set;}
 public double Value {get;set;}
 public double DurationSeconds {get;set;}
 public string Note {get;set;}
 public TimelineStep(){Device="";Point="";Note="";}
 public TimelineStep Copy(){return (TimelineStep)MemberwiseClone();}
 [XmlIgnore] public bool IsPoint {get{return Action==TimelineAction.SetValue||Action==TimelineAction.RampValue;}}
 [XmlIgnore] public string Target {get{return Device+(IsPoint?" / "+Point:"");}}
 [XmlIgnore] public string Description {get{switch(Action){case TimelineAction.SetValue:return "Set value to "+Value;case TimelineAction.RampValue:return "Ramp to "+Value+" over "+DurationSeconds+" s";case TimelineAction.SetResponse:return Value==0?"Stop responding (timeout)":"Respond normally";case TimelineAction.SetDelay:return "Response delay: "+Value+" ms";case TimelineAction.SetDrop:return "Drop "+Value+"% of requests";case TimelineAction.SetException:return "Exception code: "+Value;default:return "Clear all device faults";}}}
}
public sealed class Timeline {
 public string Name {get;set;}
 public double DurationSeconds {get;set;}
 public int RepeatCount {get;set;}
 public bool RestoreOnFinish {get;set;}
 public List<TimelineStep> Steps {get;set;}
 public Timeline(){Name="New timeline";DurationSeconds=30;RepeatCount=1;RestoreOnFinish=true;Steps=new List<TimelineStep>();}
 public override string ToString(){return Name;}
 public Timeline Copy(){return new Timeline{Name=Name,DurationSeconds=DurationSeconds,RepeatCount=RepeatCount,RestoreOnFinish=RestoreOnFinish,Steps=Steps.Select(s=>s.Copy()).ToList()};}
 public void Validate(Project p){
  if(string.IsNullOrWhiteSpace(Name)||!Finite(DurationSeconds)||DurationSeconds<=0||DurationSeconds>86400||RepeatCount<1||RepeatCount>10000||Steps==null||Steps.Count>10000||(long)Steps.Count*RepeatCount>100000)throw new Exception("Use a name, duration 0–86400 seconds (greater than zero), 1–10000 repeats and at most 100000 total step executions.");
  foreach(var s in Steps){
   if(s==null||!Enum.IsDefined(typeof(TimelineAction),s.Action)||!Finite(s.AtSeconds)||s.AtSeconds<0||s.AtSeconds>=DurationSeconds||!Finite(s.DurationSeconds)||s.DurationSeconds<0||s.AtSeconds+s.DurationSeconds>DurationSeconds||!Finite(s.Value))throw new Exception("Each step must start before the timeline ends, finish within it and use finite values.");
   var d=p.Devices.FirstOrDefault(x=>x.Name==s.Device);if(d==null)throw new Exception("Step device not found: "+s.Device);
   if(s.IsPoint){var pt=d.Points.FirstOrDefault(x=>x.Name==s.Point);if(pt==null)throw new Exception("Step point not found: "+s.Target);if(s.Action==TimelineAction.RampValue&&(s.DurationSeconds<=0||pt.Type==ValueType.Bool))throw new Exception("Ramps require a numeric point and a positive duration.");if(s.Action==TimelineAction.SetValue&&s.DurationSeconds!=0)throw new Exception("Set value is instantaneous; use Ramp value for a duration.");lock(d.Sync){double before=pt.Value;try{pt.Value=s.Value;pt.Encode();}finally{pt.Value=before;}}}
   else if(s.Action==TimelineAction.RestoreDevice){if(s.DurationSeconds!=0)throw new Exception("Clear faults is instantaneous.");}
   else {double max=s.Action==TimelineAction.SetResponse?1:s.Action==TimelineAction.SetDelay?60000:s.Action==TimelineAction.SetDrop?100:11;if(s.Value<0||s.Value>max||s.Value!=Math.Truncate(s.Value))throw new Exception(s.Description+": use a whole number from 0 to "+max+".");}
  }
  var ordered=Steps.OrderBy(s=>s.AtSeconds).ToList();
  for(int i=0;i<ordered.Count;i++)for(int j=i+1;j<ordered.Count;j++){
   var a=ordered[i];var b=ordered[j];if(b.AtSeconds>a.AtSeconds+a.DurationSeconds)break;
   bool same=a.Device==b.Device&&(a.IsPoint&&b.IsPoint?a.Point==b.Point:!a.IsPoint&&!b.IsPoint&&(a.Action==b.Action||a.Action==TimelineAction.RestoreDevice||b.Action==TimelineAction.RestoreDevice));
   if(same&&(a.AtSeconds==b.AtSeconds||b.AtSeconds<a.AtSeconds+a.DurationSeconds))throw new Exception("Conflicting steps for "+a.Target+" at "+b.AtSeconds+" s. End the earlier action before changing the same value or fault.");
  }
 }
 static bool Finite(double n){return !double.IsNaN(n)&&!double.IsInfinity(n);}
}
public sealed class TimelineRunner {
 sealed class Active {public TimelineStep Step;public double Before;}
 readonly Project project;
 readonly Timeline plan;
 readonly Scenario baseline;
 readonly List<TimelineStep> ordered;
 readonly List<Active> active=new List<Active>();
 readonly List<string> evidence=new List<string>{"ElapsedSeconds,Cycle,Event,Target,Detail"};
 int next;
 int cycle;
 public bool Paused {get;private set;}
 public bool Completed {get;private set;}
 public double Elapsed {get;private set;}
 public string State {get;private set;}
 public string Name {get{return plan.Name;}}
 public int Cycle {get{return Math.Min(cycle+1,plan.RepeatCount);}}
 public double TotalSeconds {get{return plan.DurationSeconds*plan.RepeatCount;}}
 public string NextStep {get{return next<ordered.Count?ordered[next].AtSeconds+" s: "+ordered[next].Description:"Waiting for cycle end";}}
 public string[] Evidence {get{return evidence.ToArray();}}
 public event Action<string> Log;
 public TimelineRunner(Project p,Timeline timeline){p.Validate();timeline.Validate(p);if(timeline.Steps.Count==0)throw new Exception("Add at least one step before running.");project=p;plan=timeline.Copy();baseline=Scenario.Capture(p,"Timeline start");ordered=plan.Steps.OrderBy(s=>s.AtSeconds).ToList();State="Running";}
 public void Pause(){if(!Completed){Paused=true;State="Paused";}}
 public void Resume(){if(!Completed){Paused=false;State="Running";}}
 public void Stop(){if(Completed)return;baseline.Apply(project);active.Clear();Completed=true;Paused=false;State="Stopped — starting state restored";Record(Elapsed,"Stopped","",State);}
 public void Advance(double seconds){
  if(double.IsNaN(seconds)||double.IsInfinity(seconds)||seconds<0)throw new ArgumentException("Elapsed time must be finite and nonnegative.");
  if(Completed||Paused)return;
  double target=Math.Min(TotalSeconds,Elapsed+seconds);
  try{while(!Completed){double start=cycle*plan.DurationSeconds;double local=Math.Min(plan.DurationSeconds,target-start);Update(local,start);Elapsed=start+local;if(local<plan.DurationSeconds)break;
   cycle++;active.Clear();next=0;
   if(cycle>=plan.RepeatCount){if(plan.RestoreOnFinish)baseline.Apply(project);Completed=true;State=plan.RestoreOnFinish?"Completed — starting state restored":"Completed — final state kept";Record(Elapsed,"Completed","",State);}
   else{baseline.Apply(project);Record(Elapsed,"Repeat","","Starting state restored");}
  }}catch{baseline.Apply(project);Completed=true;Paused=false;State="Failed — starting state restored";Record(Elapsed,"Failed","",State);throw;}
 }
 void Update(double until,double start){
  while(true){double at=next<ordered.Count?ordered[next].AtSeconds:double.PositiveInfinity;double end=active.Count==0?double.PositiveInfinity:active.Min(a=>a.Step.AtSeconds+a.Step.DurationSeconds);double time=Math.Min(at,end);if(time>until)break;
   UpdateRamps(time);
   foreach(var a in active.Where(a=>a.Step.AtSeconds+a.Step.DurationSeconds<=time).ToList()){if(!a.Step.IsPoint)SetFault(a.Step,a.Before);active.Remove(a);Record(start+time,"Ended",a.Step.Target,a.Step.IsPoint?"Ramp reached target":"Previous fault setting restored");}
   while(next<ordered.Count&&ordered[next].AtSeconds==time){var s=ordered[next++];var d=project.Devices.First(x=>x.Name==s.Device);double before=0;
    lock(d.Sync){if(s.IsPoint){var point=d.Points.First(x=>x.Name==s.Point);before=point.Value;point.Simulation=Wave.Manual;if(s.Action==TimelineAction.SetValue)point.Value=s.Value;}
     else if(s.Action==TimelineAction.RestoreDevice){d.Responding=true;d.DelayMs=0;d.DropPercent=0;d.ExceptionCode=0;}
     else{before=ReadFault(s,d);SetFault(s,s.Value);}}
    if(s.DurationSeconds>0)active.Add(new Active{Step=s,Before=before});Record(start+time,"Started",s.Target,s.Description);
   }
  }
  UpdateRamps(until);
 }
 void UpdateRamps(double time){foreach(var a in active.Where(x=>x.Step.Action==TimelineAction.RampValue)){var s=a.Step;var d=project.Devices.First(x=>x.Name==s.Device);lock(d.Sync){var p=d.Points.First(x=>x.Name==s.Point);double fraction=Math.Max(0,Math.Min(1,(time-s.AtSeconds)/s.DurationSeconds));p.Value=a.Before+(s.Value-a.Before)*fraction;}}}
 static double ReadFault(TimelineStep s,Device d){return s.Action==TimelineAction.SetResponse?(d.Responding?1:0):s.Action==TimelineAction.SetDelay?d.DelayMs:s.Action==TimelineAction.SetDrop?d.DropPercent:d.ExceptionCode;}
 void SetFault(TimelineStep s,double v){var d=project.Devices.First(x=>x.Name==s.Device);lock(d.Sync){switch(s.Action){case TimelineAction.SetResponse:d.Responding=v!=0;break;case TimelineAction.SetDelay:d.DelayMs=(int)v;break;case TimelineAction.SetDrop:d.DropPercent=(int)v;break;case TimelineAction.SetException:d.ExceptionCode=(int)v;break;}}}
 void Record(double at,string kind,string target,string detail){evidence.Add(string.Join(",",new[]{at.ToString("0.###",CultureInfo.InvariantCulture),Math.Min(cycle+1,plan.RepeatCount).ToString(CultureInfo.InvariantCulture),kind,target,detail}.Select(Quote)));var handler=Log;if(handler!=null)handler(kind+" · "+target+" · "+detail);}
 static string Quote(string value){if(value.StartsWith("=")||value.StartsWith("+")||value.StartsWith("-")||value.StartsWith("@")||value.StartsWith("\t")||value.StartsWith("\r"))value="'"+value;return "\""+value.Replace("\"","\"\"")+"\"";}
}
}
