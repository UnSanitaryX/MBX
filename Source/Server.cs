using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.IO.Ports;

namespace ModbusEmulatorX {
public class Engine:IDisposable {
 readonly List<TcpListener> listeners=new List<TcpListener>(); readonly List<TcpClient> clients=new List<TcpClient>();
 readonly List<SerialPort> serialPorts=new List<SerialPort>(); readonly List<Thread> workers=new List<Thread>(); int generation;
 volatile bool stopping=true; readonly Random random=new Random();
 public event Action<string> Log; public long Requests;public long Errors;public long Writes;
 public bool Running {get{return !stopping;}} public int Connections {get{lock(clients)return clients.Count;}}
 public void Start(Project project){if(Running)return;project.Validate();var network=NetworkPlanning.Build(project,new NetworkOptions(),NetworkPlanning.Adapters());if(!network.Ready)throw new Exception(NetworkPlanning.MissingMessage(network));stopping=false;int run=Interlocked.Increment(ref generation);try{
  foreach(var group in project.Devices.Where(d=>d.Enabled&&d.Transport==Transport.Tcp).GroupBy(d=>d.BindAddress+":"+d.Port)){
   Device first=group.First();var listener=new TcpListener(IPAddress.Parse(first.BindAddress),first.Port);listener.Start(64);listeners.Add(listener);
   List<Device> devices=group.ToList();var thread=new Thread(()=>Accept(listener,devices,run));thread.IsBackground=true;workers.Add(thread);thread.Start();Emit("LISTEN "+group.Key+" units "+string.Join(",",devices.Select(d=>d.UnitId.ToString())));
  }
  foreach(var group in project.Devices.Where(d=>d.Enabled&&d.Transport==Transport.Rtu).GroupBy(d=>d.SerialPort,StringComparer.OrdinalIgnoreCase)){
   Device d=group.First();var port=new SerialPort(d.SerialPort,d.BaudRate,d.Parity,d.DataBits,d.StopBits){ReadTimeout=Math.Max(4,(int)Math.Ceiling(38500.0/d.BaudRate)),WriteTimeout=3000};port.Open();serialPorts.Add(port);var devices=group.ToList();var thread=new Thread(()=>ServeSerial(port,devices,run));thread.IsBackground=true;workers.Add(thread);thread.Start();Emit("RTU "+d.SerialPort+" "+d.BaudRate+" baud");
  }
  if(listeners.Count+serialPorts.Count==0)throw new Exception("Enable at least one device.");
 }catch{Stop();throw;}}
 void Accept(TcpListener listener,List<Device> devices,int run){while(!stopping&&run==generation){try{TcpClient c=listener.AcceptTcpClient();lock(clients){if(stopping||run!=generation||clients.Count>=128){c.Close();continue;}clients.Add(c);}ThreadPool.QueueUserWorkItem(_=>Serve(c,devices,run));}catch(SocketException){if(!stopping&&run==generation)Emit("Listener socket error.");}catch(ObjectDisposedException){break;}catch(Exception e){if(!stopping&&run==generation)Emit(e.Message);}}}
 static byte[] Read(Stream s,int n){byte[] b=new byte[n];int at=0;while(at<n){int r=s.Read(b,at,n-at);if(r==0)throw new EndOfStreamException();at+=r;}return b;}
 void Serve(TcpClient c,List<Device> devices,int run){try{c.NoDelay=true;c.ReceiveTimeout=30000;c.SendTimeout=5000;string remote=c.Client.RemoteEndPoint.ToString();Emit("CONNECT "+remote);using(c)using(NetworkStream stream=c.GetStream())while(!stopping&&run==generation){byte[] h=Read(stream,7);int length=U(h,4);if(U(h,2)!=0||length<2||length>254)break;byte[] request=Read(stream,length-1);Device d=devices.FirstOrDefault(x=>x.UnitId==h[6]);Interlocked.Increment(ref Requests);
   if(d==null){Emit("IGNORE unknown unit "+h[6]);continue;}if(!d.Responding)continue;
   int drop;lock(random)drop=random.Next(100);if(drop<d.DropPercent)continue;
   if(d.DelayMs>0){int delay=d.DelayMs;while(delay>0&&!stopping&&run==generation){Thread.Sleep(Math.Min(100,delay));delay-=100;}}if(stopping||run!=generation)break;
   byte[] response=d.ExceptionCode>0?Ex(request[0],(byte)d.ExceptionCode):Process(d,request);
   if(response[0]>=128)Interlocked.Increment(ref Errors);else if(request[0]==5||request[0]==6||request[0]==15||request[0]==16||request[0]==22||request[0]==23)Interlocked.Increment(ref Writes);
   byte[] packet=new byte[7+response.Length];packet[0]=h[0];packet[1]=h[1];Put(packet,4,response.Length+1);packet[6]=h[6];Array.Copy(response,0,packet,7,response.Length);stream.Write(packet,0,packet.Length);
   Emit(d.Name+" FC"+request[0]+" RX "+BitConverter.ToString(request)+" TX "+BitConverter.ToString(response));
  }}catch(IOException){}catch(SocketException){}catch(ObjectDisposedException){}catch(Exception e){if(!stopping)Emit("Client: "+e.Message);}finally{lock(clients)clients.Remove(c);try{c.Close();}catch{}}}
 public static byte[] Process(Device d,byte[] p){if(p==null||p.Length==0)return null;byte f=p[0];try{lock(d.Sync){
  if(f>=1&&f<=4){if(p.Length!=5)return Ex(f,3);int start=U(p,1),count=U(p,3);if(count<1||count>(f<3?2000:125))return Ex(f,3);if(start+count>65536)return Ex(f,2);Area area=f==1?Area.Coil:f==2?Area.DiscreteInput:f==3?Area.HoldingRegister:Area.InputRegister;ushort[] v=d.Read(area,start,count);byte[] r=new byte[2+(f<3?(count+7)/8:count*2)];r[0]=f;r[1]=(byte)(r.Length-2);for(int i=0;i<count;i++)if(f<3){if(v[i]!=0)r[2+i/8]|=(byte)(1<<(i%8));}else Put(r,2+i*2,v[i]);return r;}
  if(f==5||f==6){if(!d.AllowWrites)return Ex(f,1);if(p.Length!=5)return Ex(f,3);int value=U(p,3);if(f==5&&value!=0&&value!=65280)return Ex(f,3);d.Write(f==5?Area.Coil:Area.HoldingRegister,U(p,1),new ushort[]{(ushort)(f==5?(value==0?0:1):value)});return (byte[])p.Clone();}
  if(f==15||f==16){if(!d.AllowWrites)return Ex(f,1);if(p.Length<6)return Ex(f,3);int start=U(p,1),count=U(p,3),size=f==15?(count+7)/8:count*2;if(count<1||count>(f==15?1968:123)||p[5]!=size||p.Length!=6+size)return Ex(f,3);if(start+count>65536)return Ex(f,2);ushort[] v=new ushort[count];for(int i=0;i<count;i++)v[i]=f==15?(ushort)((p[6+i/8]>>(i%8))&1):(ushort)U(p,6+i*2);d.Write(f==15?Area.Coil:Area.HoldingRegister,start,v);return p.Take(5).ToArray();}
  if(f==22){if(!d.AllowWrites)return Ex(f,1);if(p.Length!=7)return Ex(f,3);int address=U(p,1),and=U(p,3),or=U(p,5);ushort old=d.Read(Area.HoldingRegister,address,1)[0];d.Write(Area.HoldingRegister,address,new ushort[]{(ushort)((old&and)|(or&~and))});return (byte[])p.Clone();}
  if(f==23){if(!d.AllowWrites)return Ex(f,1);if(p.Length<10)return Ex(f,3);int rs=U(p,1),rc=U(p,3),ws=U(p,5),wc=U(p,7);if(rc<1||rc>125||wc<1||wc>121||p[9]!=wc*2||p.Length!=10+wc*2)return Ex(f,3);if(rs+rc>65536||ws+wc>65536)return Ex(f,2);d.Read(Area.HoldingRegister,rs,rc);ushort[] v=new ushort[wc];for(int i=0;i<wc;i++)v[i]=(ushort)U(p,10+i*2);d.Write(Area.HoldingRegister,ws,v);ushort[] values=d.Read(Area.HoldingRegister,rs,rc);byte[] r=new byte[2+rc*2];r[0]=f;r[1]=(byte)(rc*2);for(int i=0;i<rc;i++)Put(r,2+i*2,values[i]);return r;}
  if(f==8){if(p.Length!=5)return Ex(f,3);if(U(p,1)!=0)return Ex(f,1);return (byte[])p.Clone();}
  if(f==43){if(p.Length!=4||p[1]!=14)return Ex(f,1);int code=p[2],start=p[3];if(code!=1&&code!=4)return Ex(f,3);if(start>2)return Ex(f,2);string[] texts={"Modbus Emulator X",d.Name,"1.3.0"};var bytes=new List<byte>{43,14,(byte)code,0x81,0,0,0};for(int i=start;i<3;i++){byte[] text=Encoding.ASCII.GetBytes(texts[i]);if(text.Length>80)text=text.Take(80).ToArray();bytes.Add((byte)i);bytes.Add((byte)text.Length);bytes.AddRange(text);bytes[6]++;if(code==4)break;}return bytes.ToArray();}
  return Ex(f,1);
 }}catch(AddressException){return Ex(f,2);}catch(OverflowException){return Ex(f,4);}catch(ArgumentException){return Ex(f,3);}catch{return Ex(f,4);}}
 static byte[] Ex(byte f,byte code){return new byte[]{(byte)(f|128),code};}public static int U(byte[] p,int at){return (p[at]<<8)|p[at+1];}public static void Put(byte[] p,int at,int value){p[at]=(byte)(value>>8);p[at+1]=(byte)value;}
 void Emit(string message){var handler=Log;if(handler!=null)handler(DateTime.Now.ToString("HH:mm:ss.fff")+"  "+message);}
 public void Stop(){stopping=true;Interlocked.Increment(ref generation);foreach(var l in listeners)l.Stop();listeners.Clear();foreach(var p in serialPorts)try{p.Close();p.Dispose();}catch{}serialPorts.Clear();lock(clients){foreach(var c in clients)try{c.Close();}catch{}clients.Clear();}foreach(var worker in workers)if(worker!=Thread.CurrentThread)worker.Join(1000);workers.Clear();Emit("STOPPED");}
 void ServeSerial(SerialPort port,List<Device> devices,int run){var frame=new List<byte>();while(!stopping&&run==generation){try{int b=port.ReadByte();if(b<0)continue;frame.Add((byte)b);int expected=Rtu.ExpectedLength(frame);if(expected>256||frame.Count>256){frame.Clear();continue;}if(expected>0&&frame.Count==expected){ProcessSerial(port,devices,frame.ToArray(),run);frame.Clear();}}catch(TimeoutException){if(frame.Count>=4)ProcessSerial(port,devices,frame.ToArray(),run);frame.Clear();}catch(Exception e){if(!stopping&&run==generation)Emit("RTU port error: "+e.Message);break;}}}
 void ProcessSerial(SerialPort port,List<Device> devices,byte[] frame,int run){try{if(!Rtu.Valid(frame)){Interlocked.Increment(ref Errors);Emit("RTU CRC/length rejected");return;}int unit=frame[0];byte[] pdu=frame.Skip(1).Take(frame.Length-3).ToArray();Interlocked.Increment(ref Requests);bool broadcast=unit==0;bool write=pdu[0]==5||pdu[0]==6||pdu[0]==15||pdu[0]==16||pdu[0]==22;
  if(broadcast&&!write)return;foreach(Device d in devices.Where(x=>broadcast||x.UnitId==unit)){if(!d.Responding)continue;int sample;lock(random)sample=random.Next(100);if(sample<d.DropPercent)continue;
   int delay=d.DelayMs;while(delay>0&&!stopping&&run==generation){Thread.Sleep(Math.Min(100,delay));delay-=100;}if(stopping||run!=generation)return;
   byte[] response=d.ExceptionCode>0?Ex(pdu[0],(byte)d.ExceptionCode):Process(d,pdu);if(response[0]>=128)Interlocked.Increment(ref Errors);else if(write||pdu[0]==23)Interlocked.Increment(ref Writes);
   Emit(d.Name+" RTU RX "+BitConverter.ToString(frame)+" TX "+(broadcast?"broadcast/no reply":BitConverter.ToString(response)));if(!broadcast){byte[] packet=Rtu.Frame((byte)unit,response);port.Write(packet,0,packet.Length);}
  }
 }catch(Exception e){if(!stopping&&run==generation)Emit("RTU frame error: "+e.Message);}}
 public void Dispose(){Stop();}
}
public static class Rtu {
 public static ushort Crc(byte[] b,int count){ushort crc=0xffff;for(int i=0;i<count;i++){crc^=b[i];for(int j=0;j<8;j++)crc=(ushort)(((crc&1)!=0)?(crc>>1)^0xa001:crc>>1);}return crc;}
 public static bool Valid(byte[] b){return b!=null&&b.Length>=4&&b.Length<=256&&Crc(b,b.Length-2)==(ushort)(b[b.Length-2]|(b[b.Length-1]<<8));}
 public static byte[] Frame(byte unit,byte[] pdu){byte[] b=new byte[pdu.Length+3];b[0]=unit;Array.Copy(pdu,0,b,1,pdu.Length);ushort crc=Crc(b,b.Length-2);b[b.Length-2]=(byte)crc;b[b.Length-1]=(byte)(crc>>8);return b;}
 public static int ExpectedLength(IList<byte> b){if(b.Count<2)return 0;switch(b[1]){case 1:case 2:case 3:case 4:case 5:case 6:case 8:return 8;case 15:case 16:return b.Count<7?0:9+b[6];case 22:return 10;case 23:return b.Count<11?0:13+b[10];case 43:return 7;default:return 0;}}
}
}


