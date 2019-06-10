<#
.SYNOPSIS
Configures remote machine debugging for Visual Studio Code and Visual Studio 2017+.

.DESCRIPTION
Will ensure that all the required software is available or it will install the prerequisites, creates any necessary SSH keys and files on the local and remote machine, validates the connection to the remote host, and finally configures remote debugging for IoT Edge modules.

.PARAMETER RemoteHost
The DNS name or IP address of the remote host. If not suppllied, the script will prompt the user.

.PARAMETER User
The user name for the remote host connection. If not suppllied, the script will prompt the user.

.PARAMETER ModuleName
The name of the module (the container name on the remote host) to debug. If not suppllied, the script will prompt the user.

.PARAMETER Path
The path to write the configuration files. The default is the current directory.

.PARAMETER KeyFileName
The RSA key file name. It defaults to 'id_rsa'.

.PARAMETER SkipChecks
Skips the checks for the required software and SSH files. 

.PARAMETER NoVsCode
Does not update the .vscode/launch.json file

.PARAMETER NoVsXml
Does not write the Visual Studio 2017+ XML options file. The XML file is passed to the Debug.MIDebugLaunch command in Visual Studio command window. 

.EXAMPLE 
C:\> .\Configure-RemoteDebug.ps1

Will install any missing software and prompt the user for any missing information.  

.EXAMPLE 
C:\> .\Configure-RemoteDebug.ps1 -SkipChecks

Skips the checks for any prerequisites and fails is anything is missing without any corrective action.

.EXAMPLE 
C:\> .\Configure-RemoteDebug.ps1 -RemoteHost iotedge -User iot -ModuleName edgeModule

Connects to the remote host and looks for a docker container with the name 'edgeModule". If the module exists, it will write the configuration files relative to the current directory

.EXAMPLE 
C:\> .\Configure-RemoteDebug.ps1 -SkipChecks -RemoteHost iotedge -User iot -ModuleName edgeModule

Skips the checks for any prerequisites, Connects to the remote host and looks for a docker container with the name 'edgeModule". If the module exists, it will write the configuration files relative to the current directory

.EXAMPLE 
C:\> .\Configure-RemoteDebug.ps1 -RemoteHost iotedge -User iot -ModuleName edgeModule -Path SomeProject

Connects to the remote host and looks for a docker container with the name 'edgeModule". If the module exists, it will write the configuration files in the subdirectory 'SomeProject'

#>
Param(
    [Parameter(Mandatory = $false)] [string] $RemoteHost,
    [Parameter(Mandatory = $false)] [string] $User,
    [Parameter(Mandatory = $false)] [string] $ModuleName,
    [ValidateScript( {Test-Path $_ -PathType 'Container'})] 
    [Parameter(Mandatory = $false)] [string] $Path = "$PWD",
    [Parameter(Mandatory = $false)] [string] $KeyFileName = 'id_rsa',
    [switch] $SkipChecks,
    [switch] $NoVsCode,
    [switch] $NoVsXml
)
$ErrorActionPreference = "Stop"

if ((Get-ExecutionPolicy) -eq 'Restricted') {
    Set-ExecutionPolicy Bypass
}

function Get-EnvironmentVariableNames([System.EnvironmentVariableTarget] $Scope) {
    <#
    .SYNOPSIS
    Gets all environment variable names.
    .DESCRIPTION
    Provides a list of environment variable names based on the scope. This can be used to loop through the list and generate names.
    .NOTES
    Process dumps the current environment variable names in memory session. The other scopes refer to the registry values.
    .INPUTS
    None
    .OUTPUTS
    A list of environment variables names.
    .PARAMETER Scope
    The environemnt variable target scope. This is `Process`, `User`, or
    `Machine`.
    .EXAMPLE
    Get-EnvironmentVariableNames -Scope Machine
    #>
    
    # HKCU:\Environment may not exist in all Windows OSes (such as Server Core).
    switch ($Scope) {
        'User' { Get-Item 'HKCU:\Environment' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Property }
        'Machine' { Get-Item 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Environment' | Select-Object -ExpandProperty Property }
        'Process' { Get-ChildItem Env:\ | Select-Object -ExpandProperty Key }
        default { throw "Unsupported environment scope: $Scope" }
    }
}

function Get-EnvironmentVariable {
    <#
    .SYNOPSIS
    Gets an Environment Variable.
    .DESCRIPTION
    This will will get an environment variable based on the variable name and scope while accounting whether to expand the variable or not
    (e.g.: `%TEMP%`-> `C:\User\Username\AppData\Local\Temp`).
    .NOTES
    This helper reduces the number of lines one would have to write to get environment variables, mainly when not expanding the variables is a must.
    .PARAMETER Name
    The environemnt variable you want to get the value from.
    .PARAMETER Scope
    The environemnt variable target scope. This is `Process`, `User`, or `Machine`.
    .PARAMETER PreserveVariables
    A switch parameter stating whether you want to expand the variables or not. Defaults to false.
    .EXAMPLE
    Get-EnvironmentVariable -Name 'TEMP' -Scope User -PreserveVariables
    .EXAMPLE
    Get-EnvironmentVariable -Name 'PATH' -Scope Machine
    #>
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory = $true)][string] $Name,
        [Parameter(Mandatory = $true)][System.EnvironmentVariableTarget] $Scope,
        [Parameter(Mandatory = $false)][switch] $PreserveVariables = $false
    )
    
    [string] $MACHINE_ENVIRONMENT_REGISTRY_KEY_NAME = 'SYSTEM\CurrentControlSet\Control\Session Manager\Environment\';
    [Microsoft.Win32.RegistryKey] $win32RegistryKey = [Microsoft.Win32.Registry]::LocalMachine.OpenSubKey($MACHINE_ENVIRONMENT_REGISTRY_KEY_NAME)
    if ($Scope -eq [System.EnvironmentVariableTarget]::User) {
        [string] $USER_ENVIRONMENT_REGISTRY_KEY_NAME = 'Environment';
        [Microsoft.Win32.RegistryKey] $win32RegistryKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($USER_ENVIRONMENT_REGISTRY_KEY_NAME)
    }
    elseif ($Scope -eq [System.EnvironmentVariableTarget]::Process) {
        return [Environment]::GetEnvironmentVariable($Name, $Scope)
    }
    
    [Microsoft.Win32.RegistryValueOptions] $registryValueOptions = [Microsoft.Win32.RegistryValueOptions]::None
    
    if ($PreserveVariables) {
        Write-Verbose "Choosing not to expand environment names"
        $registryValueOptions = [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames
    }
    
    [string] $environmentVariableValue = [string]::Empty
    
    try {
        #Write-Verbose "Getting environment variable $Name"
        if ($null -ne $win32RegistryKey) {
            # Some versions of Windows do not have HKCU:\Environment
            $environmentVariableValue = $win32RegistryKey.GetValue($Name, [string]::Empty, $registryValueOptions)
        }
    }
    catch {
        Write-Debug "Unable to retrieve the $Name environment variable. Details: $_"
    }
    finally {
        if ($null -ne $win32RegistryKey) {
            $win32RegistryKey.Close()
        }
    }
    
    if ($null -eq $environmentVariableValue -or $environmentVariableValue -eq '') {
        $environmentVariableValue = [Environment]::GetEnvironmentVariable($Name, $Scope)
    }
    
    return $environmentVariableValue
}

$SshDir = Join-Path $env:USERPROFILE '.ssh'

if (-not $SkipChecks) {
    If (-NOT ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
 
        $ArgumentList = @('-File "{0}"' -f $MyInvocation.MyCommand.Path)
        $ArgumentList += '-Path "{0}"' -f $PWD
        $MyInvocation.BoundParameters.GetEnumerator() | ForEach-Object {
            if (($_.Value.GetType() -eq [System.Management.Automation.SwitchParameter]) -and $_.Value.IsPresent) {
                $ArgumentList += '-{0}' -f $_.Key
            }
            else {
                $ArgumentList += '-{0} "{1}"' -f $_.Key, $_.Value
            }
        }
        # Relaunch as an elevated process:
        Start-Process powershell.exe -ArgumentList $ArgumentList -Verb RunAs
        exit
    }
    
    Write-Host -BackgroundColor White -ForegroundColor Black "`r`nChecking prerequisites..."
    # Install chocolatey
    if (-not (Test-Path ( Join-Path -Path $ENV:ProgramData -ChildPath '\chocolatey\bin\choco.exe'))) {
        Write-Host -ForegroundColor Yellow 'Installing Chocolatey'
        New-Variable installchocolatey -Value ((New-Object System.Net.WebClient).DownloadString('https://chocolatey.org/install.ps1'))
        Invoke-Expression $installchocolatey
    }

    $packages = @('putty', 'winscp')
    if ($null -eq $(Get-Command ssh -ErrorAction Ignore)) {
        $packages += 'openssh'
    }

    # Install or upgrade packages via chocolatey
    foreach ($package in $packages) {
        if ($null -eq (choco list -l -r $package)) {
            & choco install --yes $package
        }
        else {
            & choco upgrade --yes $package
        }
    }

    Write-Host 'Refreshing Environment variables...'
    $UserName = $env:USERNAME
    $Architecture = $env:PROCESSOR_ARCHITECTURE
    $PsModulePath = $env:PSModulePath
  
    #ordering is important here, $user comes after so we can override $machine
    'Process', 'Machine', 'User' | ForEach-Object {
        $Scope = $_
        Get-EnvironmentVariableNames -Scope $Scope | ForEach-Object {
            Set-Item "Env:$($_)" -Value (Get-EnvironmentVariable -Scope $Scope -Name $_)
        }
    }
  
    #Path gets special treatment b/c it munges the two together
    $paths = 'Machine', 'User' | ForEach-Object {
        (Get-EnvironmentVariable -Name 'PATH' -Scope $_) -split ';'
    } | Select-Object -Unique
    $Env:PATH = $paths -join ';'
  
    # PSModulePath is almost always updated by process, so we want to preserve it.
    $env:PSModulePath = $PsModulePath
  
    # reset user and architecture
    if ($UserName) { $env:USERNAME = $UserName; }
    if ($Architecture) { $env:PROCESSOR_ARCHITECTURE = $Architecture; }

    Write-Host -BackgroundColor White -ForegroundColor Black "`r`Checking ssh keys..."
    # Create the ssh directory if it does not exist
    if (! (Test-Path -Path $SshDir -PathType Container)) {
        New-Item -ItemType Directory -Path $SshDir
    }
    $RsaKeyFile = Join-Path $SshDir $KeyFileName
    $RsaPubKeyFile = $RsaKeyFile + '.pub'
    $PpkFile = $RsaKeyFile + '.ppk'

    # Create the RSA key file if it does not exist
    if (!(Test-Path -Path $RsaKeyFile -PathType Leaf)) {
        Write-Host -ForegroundColor Yellow "Creating new RSA key file of $RsaKeyFile"
        # Remove any old pub or ppk files that might exist
        if (Test-Path -Path $RsaPubKeyFile -PathType Leaf ) {
            Remove-Item -Path $RsaPubKeyFile -Force
        }
        if (Test-Path -Path $PpkFile -PathType Leaf ) {
            Remove-Item -Path $PpkFile -Force
        }
        # Generate new key
        & ssh-keygen.exe -t rsa -f $RsaKeyFile
        if ($LASTEXITCODE -ne 0) {
            Write-Error -Message 'Could not generate the RSA Key file.'
            exit
        }
    }
    else {
        Write-Host "Using existing RSA key file of $RsaKeyFile"
    }
    # if the private key exists but we are missing the public key
    if (!(Test-Path -Path $RsaPubKeyFile -PathType Leaf)) {
        Write-Host -ForegroundColor Yellow "Creating new RSA pub file of $RsaPubKeyFile from $RsaKeyFile"
        & ssh-keygen.exe -y -f $RsaKeyFile | Out-File $RsaPubKeyFile
    }
    else {
        Write-Host "Using existing RSA pub file of $RsaPubKeyFile"
    }

    # Create the PPK file from the RSA file if it does not exist
    if (! (Test-Path -Path $PpkFile -PathType Leaf)) {
        trap {"Could not create ppk file: $_"}
        & winscp.exe /keygen $RsaKeyFile /output=$PpkFile
        #There is not a good exit code from winscp, so let's loop until the file shows up or not
        $RetryCount = 0
        while ($true) {
            if (Test-Path $PpkFile) {
                Write-Host -ForegroundColor Yellow "Created new ppk key file of $PpkFile from $RsaKeyFile"
                break
            }
            if ($RetryCount -gt 3) {
                Write-Error -Message "Could not find the $PpkFile file."
                exit
            }
            $RetryCount = $RetryCount + 1
            Start-Sleep -Seconds 1
        }
    }
    else {
        Write-Host "Using existing ppk key file of $PpkFile"
    }
}
else {
    try {
        trap {"Missing required file: $_"}
        Write-Host -BackgroundColor White -ForegroundColor Black "`r`Checking ssh keys..."
        $RsaKeyFile = Resolve-Path $(Join-Path $SshDir $KeyFileName)
        Write-Host "Using existing RSA key file of $RsaKeyFile"
        $RsaPubKeyFile = Resolve-Path "$RsaKeyFile.pub"
        Write-Host "Using existing RSA pub file of $RsaPubKeyFile"
        $PpkFile = Resolve-Path "$RsaKeyFile.ppk"
        Write-Host "Using existing ppk key file of $PpkFile"
    }
    catch {
        Write-Error -Message "Can not continue without the proper SSH files"
        exit
    }
}

Write-Host -BackgroundColor White -ForegroundColor Black "`r`nConnecting to IoT Edge host..."
while ($true) {
    while ([string]::IsNullOrEmpty($RemoteHost)) {
        $RemoteHost = Read-Host -Prompt 'Enter the name of the IoT Edge remote host'
    }
    if (-not (Test-Connection $RemoteHost -Quiet)) {
        Write-Host -ForegroundColor Red "Can not reach the remote host '$RemoteHost'"
        $RemoteHost = $null
    }
    else {
        Write-Host "Successfully pinged '$RemoteHost'"
        break
    }
}

while ([string]::IsNullOrEmpty($User)) {
    $User = Read-Host -Prompt "Enter the username for $RemoteHost"
}

# Test that the PPK file works to connect to host
$Success = "Successfully connected to $RemoteHost as $User"
$Result = & PLINK.EXE -i $PpkFile -t -batch -l $User $RemoteHost echo $Success
if ($Result -ne $Success) {
    Write-Host -ForegroundColor Yellow "Attempting to add our public key to the authorized keys on $RemoteHost for $User "
    & PLINK.EXE -l $User $RemoteHost "mkdir -p .ssh && echo `'$(Get-Content $RsaPubKeyFile)`' >> ~/.ssh/authorized_keys"

    # Test that the PPK file works to connect to host
    $Result = & PLINK.EXE -i $PpkFile -t -batch -l $User $RemoteHost echo $Success
    Write-Host $Result
    if ($Result -ne $Success) {
        Write-Error -Message "Can not connect to $RemoteHost as $User."
        exit
    }
}
else {
    Write-Host $Result
}

if ([string]::IsNullOrEmpty($ModuleName)) {
    # Get the modules from Docker on the host
    $Modules = @(& plink -i $PpkFile -batch -t -l $User $RemoteHost 'docker ps --format """table {{.Names}},{{.Image}},{{.Status}},{{.RunningFor}}"""')
    # Cannot connect to the Docker daemon at unix:///var/run/docker.sock. Is the docker daemon running?
    if ($Modules[0] -like 'Cannot connect to the Docker daemon*') {
        Write-Error -Message "Error from ${RemoteHost}: " + $Modules[0]
        exit
    }
    # Got permission denied while trying to connect to the Docker daemon socket at unix:///var/run/docker.sock: Get http://%2Fvar%2Frun%2Fdocker.sock/v1.40/containers/json: dial unix /var/run/docker.sock: connect: permission denied
    if ($Modules[0] -like 'Got permission denied while trying to connect*') {
        Write-Host -ForegroundColor Yellow "Attempting to add $User on $RemoteHost to the docker group"
        & plink -i $PpkFile -l $User $RemoteHost -t sudo usermod -aG docker `$USER
        $Modules = @(& plink -i $PpkFile -l $User $RemoteHost -batch -t 'docker ps --format """table {{.Names}},{{.Image}},{{.Status}},{{.RunningFor}}"""')
        if ($Modules[0] -like 'Got permission denied while trying to connect*') {
            Write-Error -Message "Error from ${RemoteHost}: $Modules[0]"
            exit
        }
    }

    # Select the module to configure
    $SelectedModule = $Modules | ConvertFrom-Csv | Where-Object {($_.NAMES -notlike 'edgeAgent') -and ($_.NAMES -notlike 'edgeHub')} | Out-GridView  -Title 'Select the container to debug' -PassThru | Select-Object -First 1 
    if ($null -eq $SelectedModule) {
        Write-Host -ForegroundColor Yellow "No module selected. Exiting"
        exit
    }
    #Get the module name from the selected module
    $ModuleName = $SelectedModule | Select-Object -ExpandProperty 'NAMES'
}
else {
    #Check that the specified container exists on remote machine
    $Modules = @(& plink -i $PpkFile -batch -t -l $User $RemoteHost 'docker ps --format """{{.Names}}""" --filter """name='$ModuleName'"""')
    if ($Modules -notcontains $ModuleName) {
        Write-Error -Message "Error from ${RemoteHost}: No container found matching name `"$ModuleName`""
        exit
    }
}

Write-Host -BackgroundColor White -ForegroundColor Black "`r`nWriting configuration files..."
if (-not $NoVsCode) {
    $LaunchJsonPath = Join-Path (Resolve-Path $Path) '.vscode/launch.json'
    trap { "Error creating/updating the $LaunchJsonPath file : $_" }
    #Create/Update the .vscode launch.json
    if (-not (Test-Path -Path $LaunchJsonPath)) {
        [ordered]@{
            version        = "0.2.0";
            configurations = @()
        } | ConvertTo-Json | Out-File -FilePath $(New-Item -Path $LaunchJsonPath -Force).FullName
    }
    $LaunchJsonPath = Resolve-Path $LaunchJsonPath

    $LaunchJson = Get-Content -Path $LaunchJsonPath | Where-Object {-not $_.Trim().Contains('//')} | ConvertFrom-Json 

    $ConfigName = "Remote debug $ModuleName module on $RemoteHost";
    $ConfigValue = [ordered]@{
        name          = $ConfigName;
        type          = "coreclr";
        request       = "attach";
        processName   = "dotnet";
        pipeTransport = [ordered]@{
            pipeProgram  = "plink";
            pipeArgs     = @(
                "-batch"
                "-i $PpkFile",
                "-l $User",
                "$RemoteHost",
                "docker",
                "exec",
                "-i",
                "$ModuleName"
            );
            debuggerPath = "/vsdbg/vsdbg";
            pipeCwd      = "`$`{workspaceFolder`}";
            quoteArgs    = $false;
        };
        sourceFileMap = @{
            "/src" = "`$`{workspaceFolder`}"
        };
        "justMyCode"  = $true
    }

    $LaunchJson.configurations = @($LaunchJson.configurations | Where-Object {$_.Name -ne $ConfigName})
    $LaunchJson.configurations += $ConfigValue

    # Depth must be set otherwise JSON file is not correct
    $LaunchJson | ConvertTo-Json -Depth 10 | Set-Content $LaunchJsonPath
    Write-Host -ForegroundColor Yellow "Updated $LaunchJsonPath"
}
else {
    Write-Host -ForegroundColor Yellow "Skipping VsCode configuration"
}

if (-not $NoVsXml) {
    trap { "Error creating the vs2017 XML file: $_" }
    #Create the vs2017 XML file
    $XmlFilePath = Join-Path (Resolve-Path $Path) "$ModuleName.$RemoteHost.xml"
    if (Test-Path -Path $XmlFilePath) {
        Write-Host -ForegroundColor Yellow "The $XmlFilePath already exists."
        $overwrite = Read-Host "Are you sure want to overwrite it?"
        if ($overwrite -ne 'y') {
            Write-Host -ForegroundColor Yellow "Leaving existing file unchanged."
            exit
        }
    }

    $PlinkPath = $(Get-Command PLINK.EXE).Source
    $DebugXml = [xml] @"
<?xml version="1.0" encoding="utf-8"?>
<PipeLaunchOptions xmlns="http://schemas.microsoft.com/vstudio/MDDDebuggerOptions/2014"
                   TargetArchitecture="x64"
                   MIMode="clrdbg"
                   PipePath="$PlinkPath"
                   PipeArguments="-batch -t -i $PpkFile -l $User $RemoteHost docker exec -i $ModuleName /vsdbg/vsdbg --interpreter=mi --attach --name dotnet">
  <LaunchCompleteCommand>None</LaunchCompleteCommand>
</PipeLaunchOptions>
"@
    $DebugXml.Save($XmlFilePath)
    Write-Host -ForegroundColor Yellow "Wrote $XmlFilePath"
}
else {
    Write-Host -ForegroundColor Yellow "Skipping Visual Studio configuration"
}
