using Microsoft.EntityFrameworkCore;
using MssqlBackup.Api.Data;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseCors();

if (app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAuthorization();

app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    var retries = 10;
    for (var i = 0; i < retries; i++)
    {
        try
        {
            dbContext.Database.Migrate();
            logger.LogInformation("Database migration completed successfully");
            break;
        }
        catch (Exception ex)
        {
            if (i == retries - 1)
            {
                logger.LogCritical(ex, "Failed to connect to database after {Retries} attempts", retries);
                throw;
            }
            logger.LogWarning(ex, "Attempt {Attempt}/{Retries} - SQL Server not ready, waiting 5s...", i + 1, retries);
            await Task.Delay(5000);
        }
    }
}

app.Run();
