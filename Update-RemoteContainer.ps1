<#
.SYNOPSIS
Stops a container on an IoT Edge host, removes the container image, and waits for the module to be restarted by the IoT Edge Agent

.DESCRIPTION
This allows developers to remotely stop containers on a remote IoT Edge host, remove the image on that host, and waits for the 
IoT Edge Agent to pull the image and restart the container. This is useful when a edge deployment has not changed but the image 
at the repository has.

.PARAMETER RemoteHost
The DNS name or IP address of the remote host. If not suppllied, the script will prompt the user.

.PARAMETER User
The user name for the remote host connection. If not suppllied, the script will prompt the user.

.PARAMETER ModuleName
The name of the module (the container name on the remote host) to debug. If not suppllied, the script will prompt the user.

.PARAMETER Timeout
The amount of time in seconds to wait for the module to return. The default is 30 seconds.

.PARAMETER KeyFileName
The RSA key file name. Defaults to 'id_rsa'.

.EXAMPLE 
C:\> .\Update-RemoteContainer.ps1

Will prompt for name of remote host and user, display a list of running containers for the user to select a single one, stops the 
container on the remote host, remove the image on the remote host, and wait for the container to restart.

.EXAMPLE 
C:\> .\Update-RemoteContainer.ps1 -RemoteHost iotedge -User iot -ModuleName edgeModule

Connects to the remote host and looks for a docker container with the name 'edgeModule". If the module exists, it stops the 
container on the remote host, remove the image on the remote host, and wait for the container to restart.

#>
Param(
    [Parameter(Mandatory = $false)] [string] $RemoteHost,
    [Parameter(Mandatory = $false)] [string] $User,
    [Parameter(Mandatory = $false)] [string] $ModuleName,
    [ValidateScript({ if ($_ -le 0) { throw "Timeout must be greater than zero"} else { $true }})]
    [Parameter(Mandatory = $false)] [int]    $Timeout = 30,
    [Parameter(Mandatory = $false)] [string] $KeyFileName = 'id_rsa'
)
$ErrorActionPreference = "Stop"

$SshDir = Join-Path $env:USERPROFILE '.ssh'
$RsaKeyFile = Join-Path $SshDir $KeyFileName
$RsaPubKeyFile = "$RsaKeyFile.pub"
$PpkFile = "$RsaKeyFile.ppk"
try {
    trap {"Missing required file: $_"}
    Write-Host -BackgroundColor White -ForegroundColor Black "`r`Checking ssh keys..."
    $RsaKeyFile = Resolve-Path $RsaKeyFile
    Write-Host "Using existing RSA key file of $RsaKeyFile"
    $RsaPubKeyFile = Resolve-Path $RsaPubKeyFile
    Write-Host "Using existing RSA pub file of $RsaPubKeyFile"
    $PpkFile = Resolve-Path $PpkFile
    Write-Host "Using existing ppk key file of $PpkFile"
}
catch {
    Write-Error -Message "Can not continue without the proper SSH files"
    exit
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

# If the module name was not provided on the command line
if ([string]::IsNullOrEmpty($ModuleName)) {

    # Get the modules from Docker on the host
    $Modules = @(& plink -i $PpkFile -batch -t -l $User $RemoteHost 'docker ps -a --format """table {{.Names}},{{.Image}},{{.Status}},{{.RunningFor}}"""')

    $SelectedModule = $Modules | ConvertFrom-Csv | Where-Object {($_.NAMES -notlike 'edgeAgent') -and ($_.NAMES -notlike 'edgeHub')} | Out-GridView  -Title 'Select the container to restart' -PassThru | Select-Object -First 1 
    if ($null -eq $SelectedModule) {
        Write-Host -ForegroundColor Yellow "No module selected. Exiting"
        exit
    }
}
else {
    # Check that the specified container, $ModuleName, exists on remote machine
    $SelectedModule = @(& plink -i $PpkFile -batch -t -l $User $RemoteHost 'docker ps -a --format """table {{.Names}},{{.Image}}""" --filter """name='$ModuleName'"""') | ConvertFrom-Csv | Where-Object 'NAMES' -EQ $ModuleName
    if ($null -eq $SelectedModule) {
        Write-Error -Message "Error from ${RemoteHost}: No container found matching name `"$ModuleName`""
        exit
    }
}

#Get the container name and image from the selected module
$ModuleName = $SelectedModule | Select-Object -ExpandProperty 'NAMES'
$ImageName = $SelectedModule | Select-Object -ExpandProperty 'IMAGE'

Write-Host -ForegroundColor Yellow "Stopping `"$ModuleName`" and removing the `"$ImageName`" image on $RemoteHost"
& plink -i $PpkFile -batch -t -l $User $RemoteHost "docker stop $ModuleName && docker rmi -f $ImageName"

#Loop until timeout expires or until the container returns
Write-Host -ForegroundColor Yellow "Waiting for $ModuleName to return on $RemoteHost..."
do {
    $Instance = @(& plink -i $PpkFile -batch -t -l $User $RemoteHost 'docker ps --format """{{.Names}}""" --filter """name='$ModuleName'"""')
    if ($Instance -contains $ModuleName) {
        Write-Host "$ModuleName back up on $RemoteHost"
        exit
    }
    Write-Progress -Activity "Waiting for $ModuleName to return on $RemoteHost..." -SecondsRemaining $Timeout
    Start-Sleep 1
} while ($Timeout-- -gt 1)
Write-Error -Message "Error: $ModuleName never restarted on $RemoteHost" -Category LimitsExceeded
Pause
