using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace ModbusEmulatorX {
public sealed class ScenarioSequence {
 readonly Project project;
 readonly Scenario baseline;
 readonly List<Scenario> scenarios;
 readonly double interval;
 readonly string output;
 readonly List<string> evidence=new List<string>{"ElapsedSeconds,Scenario,Device,Point,EngineeringValue,Responding,DelayMs,DropPercent,ExceptionCode"};
 int index;
 public bool Completed {get;private set;}
 public string Current {get;private set;}
 public ScenarioSequence(Project project,double intervalSeconds,string evidencePath){project.Validate();if(project.Scenarios.Count==0)throw new Exception("Capture at least one scenario first.");if(intervalSeconds<=0||double.IsNaN(intervalSeconds)||double.IsInfinity(intervalSeconds))throw new Exception("Scenario interval must be positive and finite.");this.project=project;baseline=Scenario.Capture(project,"Sequence recovery");scenarios=project.Snapshot().Scenarios;interval=intervalSeconds;output=evidencePath;Current="Ready";}
 public void Advance(double elapsed){if(Completed)return;try{
  while(index<scenarios.Count&&elapsed>=index*interval){baseline.Apply(project);Scenario s=scenarios[index++];s.Apply(project);Current=s.Name;Record(elapsed,s.Name);}
  if(index==scenarios.Count&&elapsed>=index*interval)Finish(elapsed,"Completed");
 }catch{Finish(elapsed,"Failed");throw;}}
 public void Cancel(double elapsed){if(!Completed)Finish(elapsed,"Cancelled");}
 void Finish(double elapsed,string outcome){try{baseline.Apply(project);Current=outcome;Record(elapsed,"["+outcome+" / baseline restored]");if(!string.IsNullOrEmpty(output))File.WriteAllLines(output,evidence,new UTF8Encoding(true));}finally{Completed=true;}}
 void Record(double elapsed,string name){foreach(Device d in project.Devices)lock(d.Sync)foreach(Point p in d.Points)evidence.Add(string.Join(",",new[]{elapsed.ToString("0.000",CultureInfo.InvariantCulture),name,d.Name,p.Name,p.Value.ToString("R",CultureInfo.InvariantCulture),d.Responding.ToString(),d.DelayMs.ToString(CultureInfo.InvariantCulture),d.DropPercent.ToString(CultureInfo.InvariantCulture),d.ExceptionCode.ToString(CultureInfo.InvariantCulture)}.Select(Quote)));}
 static string Quote(string text){if(text.StartsWith("=")||text.StartsWith("+")||text.StartsWith("@"))text="'"+text;return "\""+text.Replace("\"","\"\"")+"\"";}
}
}
