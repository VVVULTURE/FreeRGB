using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace FreeRGB.Core;

public sealed record SetupStatus(bool OpenRgbInstalled, bool PawnIoInstalled, bool ElevatedTaskRegistered, string OpenRgbExe);

/// <summary>
/// One-click installation of the pieces FreeRGB needs on Windows: a pinned, hash-verified OpenRGB, the PawnIO driver
/// (needed only for RAM / motherboard SMBus devices) and a scheduled task that starts OpenRGB elevated at sign-in
/// without a UAC prompt each time. Nothing is installed without the user pressing the button, and the single
/// elevated step triggers one normal UAC prompt.
///
/// PawnIO is NOT embedded: its site gives no redistribution terms, so FreeRGB downloads the author's official signed
/// installer (hash pinned) or uses winget, exactly as the user could do by hand.
/// </summary>
public sealed class HardwareSetup
{
    public const string OpenRgbVersion = "1.0";
    public const string OpenRgbUrl = "https://codeberg.org/OpenRGB/OpenRGB/releases/download/release_1.0/OpenRGB_1.0_Windows_64_81bbe18.zip";
    public const string OpenRgbSha256 = "182A52A3C97C4C4AE52C80286B4260C9666C51C3447DBC416EE8945F66192E90";
    public const string PawnIoUrl = "https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe";
    public const string PawnIoSha256 = "1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032";
    public const string TaskName = "FreeRGB-OpenRGB";

    private readonly string _dataDir;

    public HardwareSetup(string dataDir) { _dataDir = dataDir; }

    public static bool IsSupported => OperatingSystem.IsWindows();
    public string OpenRgbDir => Path.Combine(_dataDir, "openrgb");
    public string OpenRgbExe => Path.Combine(OpenRgbDir, "OpenRGB.exe");
    public string OpenRgbConfigDir => Path.Combine(_dataDir, "openrgb-config");

    /// <summary>Command-line OpenRGB is started with: headless SDK server (no GUI, so no dialogs), loopback only.</summary>
    public string ServerArguments(int port) => $"--server --server-port {port} --config \"{OpenRgbConfigDir}\"";

    public SetupStatus GetStatus()
    {
        bool pawn = false, task = false;
        if (OperatingSystem.IsWindows())
        {
            try { using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO"); pawn = k is not null; }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException) { }
            task = RunQuiet("schtasks", $"/query /tn {TaskName}") == 0;
        }
        return new(File.Exists(OpenRgbExe), pawn, task, OpenRgbExe);
    }

    // ---- step 1: OpenRGB (no elevation) --------------------------------------------------------
    public async Task InstallOpenRgbAsync(IProgress<string> log, CancellationToken ct = default)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "freergb-openrgb-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var zip = Path.Combine(tmp, "openrgb.zip");
            log.Report($"Downloading OpenRGB {OpenRgbVersion}…");
            await DownloadVerifiedAsync(OpenRgbUrl, zip, OpenRgbSha256, ct);
            log.Report("Hash verified. Extracting…");
            var extract = Path.Combine(tmp, "x");
            ZipFile.ExtractToDirectory(zip, extract);                // rejects path traversal entries
            var exe = Directory.EnumerateFiles(extract, "OpenRGB.exe", SearchOption.AllDirectories).FirstOrDefault()
                      ?? throw new InvalidDataException("OpenRGB.exe not found in the downloaded archive.");
            var src = Path.GetDirectoryName(exe)!;
            if (Directory.Exists(OpenRgbDir)) Directory.Delete(OpenRgbDir, true);
            Directory.CreateDirectory(Path.GetDirectoryName(OpenRgbDir)!);
            Directory.Move(src, OpenRgbDir);
            Directory.CreateDirectory(OpenRgbConfigDir);
            log.Report("OpenRGB installed.");
        }
        finally { try { Directory.Delete(tmp, true); } catch (IOException) { } }
    }

    public static async Task DownloadVerifiedAsync(string url, string destination, string expectedSha256, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("FreeRGB-setup");
        await using (var s = await http.GetStreamAsync(url, ct))
        await using (var f = File.Create(destination))
            await s.CopyToAsync(f, ct);
        await using var r = File.OpenRead(destination);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(r, ct));
        if (!actual.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(destination);
            throw new InvalidDataException($"Downloaded file hash mismatch (expected {expectedSha256}, got {actual}). Nothing was installed.");
        }
    }

    // ---- step 2: PawnIO + elevated start task (one UAC prompt) -----------------------------------
    /// <summary>Runs the elevated part. Returns the exit code (0 = success); -1 if the user declined the UAC prompt.</summary>
    public async Task<int> InstallElevatedPartsAsync(int port, IProgress<string> log, CancellationToken ct = default)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if (!File.Exists(OpenRgbExe)) throw new InvalidOperationException("Install OpenRGB first.");
        var script = Path.Combine(Path.GetTempPath(), "freergb-setup-" + Guid.NewGuid().ToString("N") + ".ps1");
        await File.WriteAllTextAsync(script, BuildElevatedScript(port), new UTF8Encoding(true), ct);
        log.Report("Requesting administrator permission (Windows will ask once)…");
        try
        {
            var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\"")
            { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Normal };
            using var p = Process.Start(psi) ?? throw new InvalidOperationException("Could not start the setup script.");
            await p.WaitForExitAsync(ct);
            log.Report(p.ExitCode == 0 ? "Driver and start-up task installed." : $"Setup script failed (exit code {p.ExitCode}).");
            return p.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) // UAC declined
        {
            log.Report("Administrator permission was declined; nothing was changed.");
            return -1;
        }
        finally { try { File.Delete(script); } catch (IOException) { } }
    }

    private static string Q(string s) => s.Replace("'", "''");

    private string BuildElevatedScript(int port)
    {
        var user = Environment.UserDomainName + "\\" + Environment.UserName;
        return $$"""
$ErrorActionPreference = 'Stop'
try {
  Write-Host '== FreeRGB hardware setup =='
  if (-not (Get-Service -Name PawnIO -ErrorAction SilentlyContinue)) {
    if (Get-Command winget -ErrorAction SilentlyContinue) {
      Write-Host 'Installing PawnIO with winget (namazso.PawnIO)...'
      winget install --id namazso.PawnIO -e --silent --accept-package-agreements --accept-source-agreements
      if ($LASTEXITCODE -ne 0) { throw "winget failed with exit code $LASTEXITCODE" }
    } else {
      Write-Host 'winget not found: downloading the official PawnIO installer...'
      $f = Join-Path $env:TEMP 'PawnIO_setup.exe'
      Invoke-WebRequest '{{PawnIoUrl}}' -OutFile $f
      $h = (Get-FileHash $f -Algorithm SHA256).Hash
      if ($h -ne '{{PawnIoSha256}}') { Remove-Item $f; throw "PawnIO installer hash mismatch ($h). Nothing was installed." }
      $sig = Get-AuthenticodeSignature $f
      if ($sig.Status -ne 'Valid') { Remove-Item $f; throw "PawnIO installer signature is not valid ($($sig.Status))." }
      Start-Process $f -Wait
    }
  } else { Write-Host 'PawnIO already installed.' }

  Write-Host 'Registering the OpenRGB start-up task (runs elevated at sign-in, no prompt each time)...'
  $exe = '{{Q(OpenRgbExe)}}'
  $action = New-ScheduledTaskAction -Execute $exe -Argument '{{Q(ServerArguments(port))}}' -WorkingDirectory '{{Q(OpenRgbDir)}}'
  $trigger = New-ScheduledTaskTrigger -AtLogOn -User '{{Q(user)}}'
  $principal = New-ScheduledTaskPrincipal -UserId '{{Q(user)}}' -LogonType Interactive -RunLevel Highest
  $settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
  Register-ScheduledTask -TaskName '{{TaskName}}' -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null
  Write-Host 'Done.'
  Start-Sleep 2
  exit 0
} catch {
  Write-Host "FAILED: $_" -ForegroundColor Red
  Start-Sleep 8
  exit 1
}
""";
    }

    // ---- starting OpenRGB --------------------------------------------------------------------------
    /// <summary>Starts OpenRGB headless: via the elevated task when registered, otherwise as a normal process.</summary>
    public Process? StartOpenRgb(int port)
    {
        if (!File.Exists(OpenRgbExe)) return null;
        if (OperatingSystem.IsWindows() && RunQuiet("schtasks", $"/run /tn {TaskName}") == 0) return null; // started by the task
        return OpenRgbLauncher.Launch(OpenRgbExe, port, OpenRgbConfigDir);
    }

    private static int RunQuiet(string file, string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true });
            if (p is null) return -1;
            p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd();
            p.WaitForExit(5000);
            return p.HasExited ? p.ExitCode : -1;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { return -1; }
    }
}
