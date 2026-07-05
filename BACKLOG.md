# WinCheck Backlog

## Processes

| Priority | Task | Description |
|----------|------|-------------|
| P1 | Sort by column | Click column headers (Name, PID, Mem, Thr) to sort process list |
| P1 | CPU usage per process | Show real-time CPU% per process in the tree |
| P2 | Multi-select + batch kill | Select multiple processes with Ctrl/Shift and kill them together |
| P2 | Startup programs | List and enable/disable startup entries (Registry + Task Scheduler) |
| P3 | Process priority | Change process priority (Idle → Realtime) via context menu |
| P3 | Export to CSV | Export current process list filtered by search |

## System Info

| Priority | Task | Description |
|----------|------|-------------|
| P1 | Performance graph | Real-time line chart for CPU/RAM over last 60s |
| P2 | GPU info | Detect GPU(s), show name, VRAM, driver version |
| P2 | Network adapters | List adapters with IP, MAC, connection status, throughput |
| P3 | Battery info | Battery percentage, health, estimated runtime (for laptops) |
| P3 | Temperature sensors | CPU/GPU temperature via WMI |

## Disk

| Priority | Task | Description |
|----------|------|-------------|
| P1 | Open in Explorer | Right-click folder → Open in File Explorer |
| P1 | Search/filter | Filter folder list by name within current directory |
| P2 | File type breakdown | Show extension distribution (e.g., 40% .dll, 25% .exe) |
| P2 | Delete folder | Right-click → Delete folder (move to recycle bin) |
| P3 | Pie chart | Visual chart of folder size distribution |

## Cleanup

| Priority | Task | Description |
|----------|------|-------------|
| P1 | More categories | DNS cache, thumbnail cache, Windows Error Reports, Prefetch |
| P1 | Cleaning progress | Per-category progress bar during cleanup |
| P2 | Restore point | Option to create system restore point before cleaning |
| P2 | Scheduled cleanup | Auto-clean on schedule (daily, weekly, monthly) |
| P3 | Exclude paths | User-defined exclusion list for temp file cleanup |

## General

| Priority | Task | Description |
|----------|------|-------------|
| P1 | Theme toggle | Dark/Light theme switcher in status bar |
| P1 | Minimize to tray | System tray icon, minimize to tray instead of taskbar |
| P2 | Keyboard shortcuts | Ctrl+K (kill), Ctrl+A (select all), Ctrl+1-4 (switch tabs) |
| P2 | Settings persistence | Remember window size, last tab, auto-refresh preference |
| P3 | Auto-start with Windows | Option to launch on system startup |
| P3 | Export/Import config | Save/load scan exclusions and preferences |

## Legend

- **P1**: Next sprint (high impact, low effort)
- **P2**: Upcoming (medium effort)
- **P3**: Future (nice to have)
