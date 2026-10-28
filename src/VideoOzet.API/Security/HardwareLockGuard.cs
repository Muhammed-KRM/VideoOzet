using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace VideoOzet.API.Security;

public static class HardwareLockGuard
{
    private const string InstallTicketFileName = ".install_ticket";
    private const string ExpectedTicketToken = "VZ-INSTALL-AUTH-2026-9F8E7D6C";
    private const string LocalLockFileName = "device.lock";
    private const string HmacSecret = "VideoOzet-HWID-MasterSecret-2026-!#9876543210-AcademicEdition";

    public static bool EnsureHardwareAuthorized(string[] args)
    {
        var baseDir = AppContext.BaseDirectory;
        var ticketPath = Path.Combine(baseDir, InstallTicketFileName);
        var localLockPath = Path.Combine(baseDir, LocalLockFileName);
        var systemSealPath = GetSystemSealPath();

        bool isSealCommand = args != null && Array.Exists(args, a => string.Equals(a, "--seal-hardware", StringComparison.OrdinalIgnoreCase));

        // 1. Eğer kurulum bileti (.install_ticket) mevcutsa veya --seal-hardware parametresiyle kurulum yapılıyorsa cihazı mühürle
        if (File.Exists(ticketPath))
        {
            try
            {
                var ticketContent = File.ReadAllText(ticketPath).Trim();
                if (string.Equals(ticketContent, ExpectedTicketToken, StringComparison.Ordinal))
                {
                    var signature = ComputeCurrentMachineSignature();
                    File.WriteAllText(localLockPath, signature, Encoding.UTF8);

                    var sysDir = Path.GetDirectoryName(systemSealPath);
                    if (!string.IsNullOrEmpty(sysDir))
                    {
                        Directory.CreateDirectory(sysDir);
                    }
                    File.WriteAllText(systemSealPath, signature, Encoding.UTF8);

                    // Tek kullanımlık kurulum biletini anında imha et
                    File.Delete(ticketPath);

                    if (isSealCommand)
                    {
                        Console.WriteLine("Donanım kilidi başarıyla mühürlendi.");
                        Environment.Exit(0);
                    }
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Donanım kilidi oluşturulurken hata: {ex.Message}");
            }
            finally
            {
                try { if (File.Exists(ticketPath)) File.Delete(ticketPath); } catch { }
            }
        }

        // 2. Normal açılışta hem uygulama içindeki device.lock hem de sistem mührü (.vz_hw_seal) mevcut ve bu bilgisayarla eşleşiyor olmalı
        if (!File.Exists(localLockPath) || !File.Exists(systemSealPath))
        {
            PrintUnauthorizedAndWait();
            return false;
        }

        try
        {
            var localSig = File.ReadAllText(localLockPath).Trim();
            var systemSig = File.ReadAllText(systemSealPath).Trim();
            var expectedSig = ComputeCurrentMachineSignature();

            if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(localSig), Encoding.UTF8.GetBytes(expectedSig)) ||
                !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(systemSig), Encoding.UTF8.GetBytes(expectedSig)))
            {
                PrintUnauthorizedAndWait();
                return false;
            }

            if (isSealCommand)
            {
                Environment.Exit(0);
            }

            return true;
        }
        catch
        {
            PrintUnauthorizedAndWait();
            return false;
        }
    }

    private static void PrintUnauthorizedAndWait()
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("==========================================================================");
        Console.WriteLine(" GÜVENLİK ENGELİ: YETKİSİZ BİLGİSAYAR VEYA KOPYALANMIŞ UYGULAMA TESPİT EDİLDİ!");
        Console.WriteLine(" Bu yazılım yalnızca ilk kurulduğu bilgisayarda çalışacak şekilde");
        Console.WriteLine(" donanım kilidi (HWID) ile mühürlenmiştir.");
        Console.WriteLine(" Başka bir bilgisayara kopyalanarak çalıştırılamaz.");
        Console.WriteLine("==========================================================================");
        Console.ResetColor();
    }

    private static string GetSystemSealPath()
    {
        if (OperatingSystem.IsMacOS())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "Library", "Application Support", ".com.videoozet.runtime", ".vz_hw_seal");
        }
        else if (OperatingSystem.IsWindows())
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "Microsoft", "CLR_v10_Cache", ".vz_hw_seal");
        }
        else
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".config", ".videoozet_hw_seal");
        }
    }

    private static string ComputeCurrentMachineSignature()
    {
        var rawHwid = GetRawHardwareFingerprint();
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(HmacSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(rawHwid));
        return Convert.ToHexString(hash);
    }

    private static string GetRawHardwareFingerprint()
    {
        var sb = new StringBuilder();
        sb.Append("OS:").Append(OperatingSystem.IsWindows() ? "WIN" : OperatingSystem.IsMacOS() ? "MAC" : "LINUX").Append('|');
        sb.Append("CPU:").Append(Environment.ProcessorCount).Append('|');

        if (OperatingSystem.IsWindows())
        {
            var machineGuid = ReadWindowsMachineGuid();
            sb.Append("MGUID:").Append(machineGuid).Append('|');
            sb.Append("HOST:").Append(Environment.MachineName.ToUpperInvariant());
        }
        else if (OperatingSystem.IsMacOS())
        {
            var macUuid = ReadMacPlatformUuid();
            sb.Append("MACUUID:").Append(macUuid);
        }
        else
        {
            var linuxId = ReadLinuxMachineId();
            sb.Append("LINUXID:").Append(linuxId);
        }

        return sb.ToString();
    }

    private static string ReadWindowsMachineGuid()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "reg",
                Arguments = @"query ""HKLM\SOFTWARE\Microsoft\Cryptography"" /v MachineGuid",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(3000);
                foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (line.Contains("MachineGuid", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = line.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 3)
                        {
                            return parts[^1].Trim().ToUpperInvariant();
                        }
                    }
                }
            }
        }
        catch { }

        return Environment.MachineName.ToUpperInvariant() + "_" + Environment.UserName.ToUpperInvariant();
    }

    private static string ReadMacPlatformUuid()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "/usr/sbin/ioreg",
                Arguments = "-rd1 -c IOPlatformExpertDevice",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(3000);
                foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (line.Contains("IOPlatformUUID", StringComparison.OrdinalIgnoreCase))
                    {
                        var idx = line.IndexOf('=');
                        if (idx > 0)
                        {
                            return line.Substring(idx + 1).Trim().Trim('"').ToUpperInvariant();
                        }
                    }
                }
            }
        }
        catch { }

        return Environment.MachineName.ToUpperInvariant() + "_" + Environment.UserName.ToUpperInvariant();
    }

    private static string ReadLinuxMachineId()
    {
        try
        {
            if (File.Exists("/etc/machine-id"))
                return File.ReadAllText("/etc/machine-id").Trim();
        }
        catch { }
        return Environment.MachineName.ToUpperInvariant();
    }
}
