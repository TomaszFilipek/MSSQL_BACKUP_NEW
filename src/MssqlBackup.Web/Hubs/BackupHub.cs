using Microsoft.AspNetCore.SignalR;
using MssqlBackup.Web.Models;

namespace MssqlBackup.Web.Hubs;

public class BackupHub : Hub
{
    public async Task SendBackupCreated(BackupRecordDto record)
    {
        await Clients.All.SendAsync("BackupCreated", record);
    }
}
