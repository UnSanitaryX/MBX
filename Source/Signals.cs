using System;
namespace ModbusEmulatorX {
public static class Signals {
 public static void Update(Project project,double elapsedSeconds,Random random){foreach(Device d in project.Devices)lock(d.Sync)foreach(Point p in d.Points){if(p.Simulation==Wave.Manual)continue;double phase=elapsedSeconds/p.PeriodSeconds;double fraction=p.Simulation==Wave.Sine?(Math.Sin(phase*2*Math.PI)+1)/2:p.Simulation==Wave.Ramp?phase-Math.Floor(phase):p.Simulation==Wave.Toggle?(Math.Floor(phase)%2):random.NextDouble();double value=p.Minimum+(p.Maximum-p.Minimum)*fraction;if(p.Type==ValueType.Bool)value=value>=0.5?1:0;p.Value=value;}}
}
}
