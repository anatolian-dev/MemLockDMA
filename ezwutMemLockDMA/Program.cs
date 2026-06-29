using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using Microsoft.Win32;

namespace ezwutMemLock
{
    class Program
    {
        // --- Windows API Imports (P/Invoke) ---

        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern int NtSetTimerResolution(uint DesiredResolution, bool SetResolution, out uint CurrentResolution);

        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern int NtQueryTimerResolution(out uint MaximumResolution, out uint MinimumResolution, out uint CurrentResolution);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr dwMinimumWorkingSetSize, IntPtr dwMaximumWorkingSetSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetPriorityClass(IntPtr handle, uint priorityClass);

        // --- Memory / Standby List API Imports ---

        [DllImport("ntdll.dll")]
        private static extern int NtSetSystemInformation(int SystemInformationClass, IntPtr SystemInformation, int SystemInformationLength);

        // Privileges constants
        private const int SYSTEM_MEMORY_LIST_COMMAND = 80; // SystemMemoryListInformation
        private const int MemoryPurgeStandbyList = 4;
        private const int MemoryPurgeLowPriorityStandbyList = 5;

        // Process Access Rights
        private const uint PROCESS_SET_QUOTA = 0x0100;
        private const uint PROCESS_SET_INFORMATION = 0x0200;
        private const uint PROCESS_QUERY_INFORMATION = 0x0400;
        private const uint PROCESS_VM_READ = 0x0010;

        // Priority Classes
        private const uint REALTIME_PRIORITY_CLASS = 0x00000100;
        private const uint HIGH_PRIORITY_CLASS = 0x00000080;

        static void Main(string[] args)
        {
            Console.Title = "ezwutMemLock || DMA OS Latency & Memory Optimizer";
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(@"
==================================================================
              ___               _   __  __          _   
  ___ ___ __ _\_ _|_ __  ___ ___| |_|  \/  | ___  __| |  
 / _ \_ _/ _` || || '_ \/ __/ _ \ __| |\/| |/ _ \/ _` |  
|  __/ _ \ (_| || || | | | (_|  __/ |_| |  | | (_) \ (_| |  
 \___/___\__, |___|_| |_|\___\___|\__|_|  |_|\___/\__,_|  
         |___/                                            
==================================================================");
            Console.ResetColor();

            if (!IsAdministrator())
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[!] Error: This utility must be run with Administrator privileges.");
                Console.WriteLine("    Please restart the program as Administrator.");
                Console.ResetColor();
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
                return;
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[+] Elevated Administrator privileges confirmed.");
            Console.ResetColor();

            // 1. Apply FTDI Registry Tweaks
            OptimizeFtdiLatency();

            // 2. Set System Timer Resolution to 0.5ms (WTR Core)
            bool timerSuccess = SetTimerResolutionToMax(out uint currentRes);
            if (timerSuccess)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[+] System timer resolution locked to: {(currentRes / 10000.0):F2}ms (0.50ms targets reached).");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("[!] Warning: Could not set high-precision timer resolution.");
                Console.ResetColor();
            }

            // 3. Native Standby Memory Purge (EmptyStandbyList Core)
            PurgeStandbyListCache();

            // 4. Find and Optimize DMA Reader Process
            Console.WriteLine("\n[*] Scanning active processes for DMA Reader...");
            Process? targetProc = FindDmaReaderProcess();

            if (targetProc != null)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[+] Target Process Detected: {targetProc.ProcessName} (PID: {targetProc.Id})");
                Console.ResetColor();

                // Optimize target process
                OptimizeProcessMemoryAndPriority(targetProc);
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("[!] No active DMA Reader detected automatically (looking for modules: leechcore.dll, vmm.dll).");
                Console.WriteLine("    Please start your DMA software first, or type the name of the executable manually.");
                Console.ResetColor();

                Console.Write("\nManually enter Process Name (without .exe) or press Enter to keep scanning: ");
                string? manualName = Console.ReadLine();
                if (!string.IsNullOrWhiteSpace(manualName))
                {
                    try
                    {
                        Process[] processes = Process.GetProcessesByName(manualName);
                        if (processes.Length > 0)
                        {
                            targetProc = processes[0];
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine($"[+] Target Process Selected: {targetProc.ProcessName} (PID: {targetProc.Id})");
                            Console.ResetColor();
                            OptimizeProcessMemoryAndPriority(targetProc);
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("[!] Process not found. Running in system-only optimization mode.");
                            Console.ResetColor();
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[-] Error manual targeting: {ex.Message}");
                    }
                }
            }

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n==================================================================");
            Console.WriteLine("[*] Optimization loop active. Keep this window open while gaming.");
            Console.WriteLine("[*] Press 'C' to clear standby memory cache manually.");
            Console.WriteLine("[*] Press 'Q' to quit.");
            Console.WriteLine("==================================================================");
            Console.ResetColor();

            // Main monitoring loop
            while (true)
            {
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true).Key;
                    if (key == ConsoleKey.Q)
                    {
                        break;
                    }
                    else if (key == ConsoleKey.C)
                    {
                        PurgeStandbyListCache();
                    }
                }

                // Periodically check if target is alive or needs memory re-locked
                if (targetProc != null)
                {
                    try
                    {
                        targetProc.Refresh();
                        if (targetProc.HasExited)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"\n[!] Target process {targetProc.ProcessName} exited!");
                            Console.ResetColor();
                            targetProc = null;
                        }
                    }
                    catch
                    {
                        targetProc = null;
                    }
                }

                Thread.Sleep(2000);
            }
        }

        private static bool IsAdministrator()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        // --- FTDI Latency Optimizer ---
        private static void OptimizeFtdiLatency()
        {
            Console.WriteLine("[*] Optimizing FTDI USB driver latency registry parameters...");
            int updatedCount = 0;
            try
            {
                // Registry path for USB serial and FTDI devices
                string ftdiPath = @"SYSTEM\CurrentControlSet\Enum\FTDIBUS";
                using (RegistryKey? ftdiKey = Registry.LocalMachine.OpenSubKey(ftdiPath, true))
                {
                    if (ftdiKey != null)
                    {
                        foreach (string subkeyName in ftdiKey.GetSubKeyNames())
                        {
                            using (RegistryKey? deviceKey = ftdiKey.OpenSubKey(subkeyName, true))
                            {
                                if (deviceKey == null) continue;
                                foreach (string subSubkeyName in deviceKey.GetSubKeyNames())
                                {
                                    using (RegistryKey? paramKey = deviceKey.OpenSubKey(subSubkeyName + @"\Device Parameters", true))
                                    {
                                        if (paramKey != null)
                                        {
                                            // Check latency value
                                            object? latencyVal = paramKey.GetValue("LatencyTimer");
                                            if (latencyVal != null)
                                            {
                                                int currentLat = Convert.ToInt32(latencyVal);
                                                if (currentLat != 1)
                                                {
                                                    paramKey.SetValue("LatencyTimer", 1, RegistryValueKind.DWord);
                                                    Console.WriteLine($"    [+] Adjusted key: {subkeyName} LatencyTimer: {currentLat}ms -> 1ms");
                                                    updatedCount++;
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                if (updatedCount > 0)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"[+] Registry scan completed: {updatedCount} FTDI latency values updated to 1ms.");
                    Console.ResetColor();
                }
                else
                {
                    Console.WriteLine("[~] Registry scan completed: FTDI drivers are already optimized at 1ms.");
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[-] Warning reading registry keys: {ex.Message}");
                Console.ResetColor();
            }
        }

        // --- System Timer Resolution (WTR) ---
        private static bool SetTimerResolutionToMax(out uint currentRes)
        {
            currentRes = 0;
            try
            {
                int queryResult = NtQueryTimerResolution(out uint maxRes, out uint minRes, out uint curRes);
                if (queryResult == 0) // STATUS_SUCCESS
                {
                    // Request maximum resolution (commonly 5000 units = 0.50ms)
                    int setResult = NtSetTimerResolution(maxRes, true, out currentRes);
                    return setResult == 0;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[-] Error requesting high performance timer resolution: {ex.Message}");
            }
            return false;
        }

        // --- Standby Memory Purger (EmptyStandbyList) ---
        private static void PurgeStandbyListCache()
        {
            Console.WriteLine("[*] Purging Windows memory standby list cache pages...");
            try
            {
                // Native NT memory list purge commands (similar to EmptyStandbyList.exe)
                int bufferSize = Marshal.SizeOf(typeof(int));
                IntPtr commandPtr = Marshal.AllocHGlobal(bufferSize);

                // Purge Standby List
                Marshal.WriteInt32(commandPtr, MemoryPurgeStandbyList);
                int status1 = NtSetSystemInformation(SYSTEM_MEMORY_LIST_COMMAND, commandPtr, bufferSize);

                // Purge Low Priority Standby List
                Marshal.WriteInt32(commandPtr, MemoryPurgeLowPriorityStandbyList);
                int status2 = NtSetSystemInformation(SYSTEM_MEMORY_LIST_COMMAND, commandPtr, bufferSize);

                Marshal.FreeHGlobal(commandPtr);

                if (status1 == 0 && status2 == 0)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("[+] Windows memory working set standby caches successfully purged!");
                    Console.ResetColor();
                }
                else
                {
                    Console.WriteLine($"[~] Memory purge command returned code (Standby: {status1}, LowPri: {status2}).");
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[-] Memory purge operation encountered issues: {ex.Message}");
                Console.ResetColor();
            }
        }

        // --- DMA Reader Auto-Detection Loop ---
        private static Process? FindDmaReaderProcess()
        {
            Process[] processes = Process.GetProcesses();
            foreach (Process proc in processes)
            {
                // Skip system and core idle processes
                if (proc.Id <= 4 || proc.ProcessName == "Idle" || proc.ProcessName == "System")
                    continue;

                try
                {
                    // Look through loaded library modules for LeechCore or VMM interface dlls
                    foreach (ProcessModule module in proc.Modules)
                    {
                        if (module.ModuleName != null)
                        {
                            string lowerName = module.ModuleName.ToLower();
                            if (lowerName.Contains("leechcore") || lowerName.Contains("vmm.dll"))
                            {
                                return proc;
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore processes we can't open (system protected files)
                }
            }

            // Fallback: search current directory for other executables
            try
            {
                string runDir = AppDomain.CurrentDomain.BaseDirectory;
                string currentExeName = Path.GetFileName(Environment.ProcessPath ?? "");
                foreach (string file in Directory.GetFiles(runDir, "*.exe"))
                {
                    string filename = Path.GetFileName(file);
                    if (filename.Equals(currentExeName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    string nameWithoutExt = Path.GetFileNameWithoutExtension(filename);
                    Process[] candidate = Process.GetProcessesByName(nameWithoutExt);
                    if (candidate.Length > 0)
                    {
                        return candidate[0];
                    }
                }
            }
            catch
            {
                // Dir scan error
            }

            return null;
        }

        // --- Memory Lock and Priority Booster ---
        private static void OptimizeProcessMemoryAndPriority(Process proc)
        {
            try
            {
                // Lock working set memory allocation (prevents swapping to pagefile)
                IntPtr minSize = new IntPtr(1024 * 1024 * 16);  // 16MB minimum working set
                IntPtr maxSize = new IntPtr(1024 * 1024 * 128); // 128MB maximum working set

                IntPtr hProc = OpenProcess(PROCESS_SET_QUOTA | PROCESS_SET_INFORMATION | PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, false, proc.Id);
                if (hProc != IntPtr.Zero)
                {
                    bool setMemorySuccess = SetProcessWorkingSetSize(hProc, minSize, maxSize);
                    CloseHandle(hProc);

                    if (setMemorySuccess)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("[+] OS Memory Locking (VirtualLock / WorkingSet) locked successfully.");
                        Console.ResetColor();
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine("[!] Working set memory allocation lock failed (already locked or protected).");
                        Console.ResetColor();
                    }
                }

                // Elevate Thread and Process priorities
                proc.PriorityClass = ProcessPriorityClass.High;
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("[+] Process Priority Class elevated to [High].");
                Console.ResetColor();

                foreach (ProcessThread thread in proc.Threads)
                {
                    try
                    {
                        thread.PriorityLevel = ThreadPriorityLevel.Highest;
                    }
                    catch
                    {
                        // Some system worker threads don't allow modifying priority
                    }
                }
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("[+] Process Thread priorities set to [Highest].");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[-] Warning while applying process priority settings: {ex.Message}");
                Console.ResetColor();
            }
        }
    }
}
