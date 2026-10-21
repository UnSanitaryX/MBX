using System;
using System.Diagnostics;
using System.Threading;

namespace ModbusEmulatorX {
public static class Cli {
 static volatile bool stopping;
 public static int Main(string[] args){try{
  if(args.Length>0&&args[0]=="network")return NetworkCli.Execute(System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Skip(args,1)));
  if(args.Length<2){Console.WriteLine("Modbus Emulator X\r\n  validate project.mbx|xlsx|csv\r\n  convert input.mbx|xlsx|csv output.mbx|xlsx|csv\r\n  serve project.mbx|xlsx|csv [--signals] [--seconds N] [--scenario NAME]\r\n  demo output.mbx|xlsx|csv\r\n  network adapters\r\n  network status project.mbx|xlsx|csv\r\n  network prepare project.mbx|xlsx|csv --adapter INDEX [--mask 255.255.255.0]\r\n  network cleanup");return args.Length==0?0:1;}
  if(args[0]=="demo"){ProjectFiles.Save(Project.Demo(),args[1]);Console.WriteLine("Created "+args[1]);return 0;}
  Project p=ProjectFiles.Load(args[1]);
  if(args[0]=="validate"){p.Validate();Console.WriteLine("VALID: "+p.Name+"; "+p.Devices.Count+" devices");return 0;}
  if(args[0]=="convert"&&args.Length==3){ProjectFiles.Save(p,args[2]);Console.WriteLine("Saved "+args[2]);return 0;}
  if(args[0]!="serve")throw new Exception("Unknown command or arguments.");
  bool signals=false;double seconds=double.PositiveInfinity;
  for(int i=2;i<args.Length;i++){if(args[i]=="--signals")signals=true;else if(args[i]=="--seconds"&&i+1<args.Length){seconds=double.Parse(args[++i],System.Globalization.CultureInfo.InvariantCulture);if(seconds<=0||double.IsNaN(seconds)||double.IsInfinity(seconds))throw new Exception("Seconds must be positive and finite.");}else if(args[i]=="--scenario"&&i+1<args.Length){string name=args[++i];var sc=p.Scenarios.Find(x=>x.Name==name);if(sc==null)throw new Exception("Scenario not found: "+name);sc.Apply(p);}else throw new Exception("Unknown serve option: "+args[i]);}
  stopping=false;ConsoleCancelEventHandler cancel=(s,e)=>{e.Cancel=true;stopping=true;};Console.CancelKeyPress+=cancel;
  try{using(var engine=new Engine()){engine.Log+=Console.WriteLine;engine.Start(p);var clock=Stopwatch.StartNew();var random=new Random();while(!stopping&&clock.Elapsed.TotalSeconds<seconds){if(signals)Signals.Update(p,clock.Elapsed.TotalSeconds,random);Thread.Sleep(50);}engine.Stop();Console.WriteLine("Requests="+engine.Requests+" Writes="+engine.Writes+" Exceptions="+engine.Errors);}}finally{Console.CancelKeyPress-=cancel;}return 0;
 }catch(Exception e){Console.Error.WriteLine("ERROR: "+e.Message);return 1;}}
}
}
