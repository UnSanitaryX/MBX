param([switch]$RunTests)
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if(-not(Test-Path -LiteralPath $compiler)){$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'}
if(-not(Test-Path -LiteralPath $compiler)){throw 'The .NET Framework C# compiler is required.'}
$source=Join-Path $root 'Source'
$references=@('/reference:System.dll','/reference:System.Core.dll','/reference:System.Xml.dll','/reference:System.Xml.Linq.dll','/reference:System.IO.Compression.dll','/reference:System.IO.Compression.FileSystem.dll')
$core=@('Model.cs','Server.cs','ExcelProject.cs','ProjectFiles.cs','Signals.cs','ScenarioSequence.cs','Timeline.cs','NetworkModel.cs','NetworkNative.cs','NetworkElevation.cs') | ForEach-Object {Join-Path $source $_}
& $compiler /nologo /target:library /optimize+ /warnaserror+ "/out:$root\ModbusEmulatorX.Core.dll" @references @core
if($LASTEXITCODE -ne 0){throw 'Core build failed.'}
& $compiler /nologo /target:winexe /optimize+ /warnaserror+ "/out:$root\ModbusEmulatorX.exe" @references /reference:System.Drawing.dll /reference:System.Windows.Forms.dll "/reference:$root\ModbusEmulatorX.Core.dll" "$source\AssemblyInfo.cs" "$source\DisplayControls.cs" "$source\ScenarioEditor.cs" "$source\TimelinePage.cs" "$source\NetworkSetupForm.cs" "$source\MainFormNetwork.cs" "$source\MainForm.cs"
if($LASTEXITCODE -ne 0){throw 'Application build failed.'}
& $compiler /nologo /target:exe /optimize+ /warnaserror+ "/out:$root\ModbusEmulatorX.Cli.exe" @references "/reference:$root\ModbusEmulatorX.Core.dll" "$source\Cli.cs" "$source\NetworkCli.cs"
if($LASTEXITCODE -ne 0){throw 'Command-line build failed.'}
& $compiler /nologo /target:exe /optimize+ /warnaserror+ "/out:$root\ModbusEmulatorX.Tests.exe" @references /reference:System.Drawing.dll /reference:System.Windows.Forms.dll "/reference:$root\ModbusEmulatorX.Core.dll" "/reference:$root\ModbusEmulatorX.exe" "$source\Tests.cs" "$source\NetworkTests.cs" "$source\TimelineTests.cs"
if($LASTEXITCODE -ne 0){throw 'Test build failed.'}
foreach($name in @('ModbusEmulatorX','ModbusEmulatorX.Cli','ModbusEmulatorX.Tests')){Copy-Item -LiteralPath "$root\App.config" -Destination "$root\$name.exe.config" -Force}
if($RunTests){& "$root\ModbusEmulatorX.Tests.exe";if($LASTEXITCODE -ne 0){throw 'Tests failed.'}}
Write-Output 'Modbus Emulator X build completed.'

