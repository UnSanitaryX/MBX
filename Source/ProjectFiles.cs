using System;
using System.IO;
namespace ModbusEmulatorX {
public static class ProjectFiles {
 public static Project Load(string path){switch(Path.GetExtension(path).ToLowerInvariant()){case ".xlsx":return ExcelProject.Import(path);case ".csv":return Project.ImportCsv(path);case ".mbx":return Project.Load(path);default:throw new Exception("Use .mbx, .xlsx or .csv project files.");}}
 public static void Save(Project p,string path){switch(Path.GetExtension(path).ToLowerInvariant()){case ".xlsx":ExcelProject.Export(p,path);break;case ".csv":p.ExportCsv(path);break;case ".mbx":p.Save(path);break;default:throw new Exception("Use .mbx, .xlsx or .csv project files.");}}
}
}
