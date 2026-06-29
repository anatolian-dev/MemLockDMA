# ezwutMemLock || DMA OS Latency & Memory Optimizer

C# system utility designed to optimize CPU priority, thread scheduling latency, and physical RAM caching for Direct Memory Access (DMA) client applications running on a second PC.

DMA gaming setups run packet reading loops that are highly sensitive to latency stutters. On secondary computers (with single channel RAM), Windows background caching policies, core parking, and standard scheduling ticks (usually 15.6ms resolution) cause micro-stutters.

**ezwutMemLock** solves this by enforcing direct real-time overrides of Windows kernel scheduling, hardware driver buffering, and process page swapping.

### Prerequisites
* Windows 10 / 11
* .NET 8.0 SDK (to build)

