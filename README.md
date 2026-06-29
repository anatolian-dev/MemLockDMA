# ezwutMemLock || DMA OS Latency & Memory Optimizer

A high-performance C# system utility designed to optimize CPU priority, thread scheduling latency, and physical RAM caching for Direct Memory Access (DMA) client applications (like CS2 radars, ESP, etc.) running on a second PC.

## Why this exists

DMA gaming setups run packet reading loops that are highly sensitive to latency stutters. On secondary computers (especially older multi-core systems, e.g. 4-core Intel i5 processors with 8GB RAM), Windows background caching policies, core parking, and standard scheduling ticks (usually 15.6ms resolution) cause micro-stutters.

**ezwutMemLock** solves this by enforcing direct real-time overrides of Windows kernel scheduling, hardware driver buffering, and process page swapping.

## Features

1. **Smart DMA Auto-Detection (DLL Scan):** Automatically scans active processes to detect which process has loaded the DMA hardware driver communication interface libraries (`leechcore.dll` or `vmm.dll`). No configuration needed.
2. **FTDI USB Driver Latency Optimizer:** Auto-scans the Windows Registry for active FTDI USB serial controller devices (which DMA cards run on) and locks their hardware buffer polling latency timer down to **1ms** (default Windows driver configurations default to 16ms).
3. **High-Precision Scheduler Lock:** Overrides the system clock frequency to **0.50ms (500μs)** using native NT timer routines (`NtSetTimerResolution` in `ntdll.dll`), reducing CPU scheduling delays.
4. **Physical RAM Buffer Lock:** Locks the DMA reader process working set limits in physical RAM using `SetProcessWorkingSetSize` APIs, instructing Windows not to page it to the disk pagefile.
5. **Standby Cache Memory Purge:** Flushes standard and low-priority Windows standby list caching pages (similar to the famous *EmptyStandbyList* utilities) to free up physical RAM blocks for packet processing.
6. **Thread/Process Elevation:** Promotes the process scheduling class to `High` and targets process threads to run at `Highest` or `TimeCritical` priority levels.

---

## How to Compile & Run

### Prerequisites
* Windows 10 / 11
* .NET 8.0 SDK (to build)

### Build Instructions
Open your terminal inside the project directory and build the release configuration:
```bash
dotnet build -c Release
```

### Execution
Run the compiled executable as Administrator:
```powershell
# Run the release version
.\bin\Release\net8.0\ezwutMemLock.exe
```

*Note: The executable automatically checks for and requests Administrator privileges.*

## How it works (Under the Hood)
* **High-Precision Timers:** Standard Windows threads wake up every 15.6 milliseconds. Calling `NtSetTimerResolution` pushes this down to 0.5ms, which means the DMA polling loop can process memory updates 30x faster.
* **Working Set Tuning:** When memory limits are locked, Windows' Virtual Memory Manager will not swap the DMA reader's active pages to hard drive caching sectors, preventing structural micro-stutters during heavy gaming sessions.
* **Standby List Flashing:** Purging standby pages forces Windows to allocate contiguous physical memory blocks to active execution threads, resolving latency spikes on systems with limited RAM (e.g. 8GB).
