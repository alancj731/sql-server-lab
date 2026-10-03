using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SqlServerLab.Domain.Jobs;
using SqlServerLab.Domain.Labs;
using SqlServerLab.Infrastructure.Persistence;

namespace SqlServerLab.Api.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"sqllab-api-{Guid.NewGuid():N}.db");

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseDefaultServiceProvider(o =>
        {
            o.ValidateScopes = true;
            o.ValidateOnBuild = true;
        });
        builder.UseSetting("ControlDb:Provider", "Sqlite");
        builder.UseSetting("ControlDb:ConnectionString", $"Data Source={_path};Pooling=False");
        builder.UseSetting("ControlDb:MigrateOnStartup", "true");
        builder.UseSetting("Authentication:Mode", "Development");
        builder.UseSetting("Authentication:DefaultDevUser", "");
        builder.UseSetting("RateLimiting:MutationsPerMinute", "1000");
        builder.UseSetting("LabLimits:MaxActiveLabsPerUser", "5");
    }

    public HttpClient ClientFor(string? user, bool admin = false)
    {
        var client = CreateClient();
        if (user is not null)
        {
            client.DefaultRequestHeaders.Add("X-Dev-User", user);
        }

        if (admin)
        {
            client.DefaultRequestHeaders.Add("X-Dev-Roles", "admin");
        }

        return client;
    }

    public async Task SetLabStateAsync(Guid labId, LabState state)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        await db.Jobs.Where(j => j.LabId == labId && (j.Status == LabJobStatus.Queued || j.Status == LabJobStatus.Running))
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, LabJobStatus.Succeeded).SetProperty(j => j.MutexKey, (string?)null));
        var lab = await db.Labs.SingleAsync(l => l.Id == labId);
        lab.State = state;
        lab.Touch(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            File.Delete(_path + suffix);
        }
    }
}

public static class HttpExtensions
{
    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(ApiFactory.Json))!;
}
