CanvasApp.Common.dll is locked by multiple running CanvasApp.Client processes.

```
Get-Process CanvasApp.Client -ErrorAction SilentlyContinue | Stop-Process -Force
```