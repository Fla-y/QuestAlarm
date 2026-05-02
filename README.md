# QuestAlarm

Escape Bed!

## Development

Start the local development flow from PowerShell:

```powershell
.\scripts\start-dev.ps1
```

The script publishes the fake challenge client, starts `QuestAlarm.Api` on `http://localhost:5055`, then runs the console host. Shared alarm/session data is stored under `%LOCALAPPDATA%\QuestAlarm`.

Run the desktop shell:

```powershell
dotnet run --project .\QuestAlarm.Desktop\QuestAlarm.Desktop.csproj
```
