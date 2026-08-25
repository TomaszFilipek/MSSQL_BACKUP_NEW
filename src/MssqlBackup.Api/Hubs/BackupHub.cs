using Microsoft.AspNetCore.SignalR;

namespace MssqlBackup.Api.Hubs;

public class BackupHub : Hub
{
    public async Task SendBackupCreated(object record)
    {
        await Clients.All.SendAsync("BackupCreated", record);
    }
}
