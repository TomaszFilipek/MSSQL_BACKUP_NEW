using Microsoft.AspNetCore.ResponseCompression;
using MssqlBackup.Web.Hubs;
using MssqlBackup.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
});

builder.Services.AddHttpClient<BackupApiService>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["ApiSettings:BaseUrl"] ?? "http://localhost:8283");
});

builder.Services.AddSignalR();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<MssqlBackup.Web.Components.App>()
    .AddInteractiveServerRenderMode();

app.MapHub<BackupHub>("/hubs/backup");

app.Run();
