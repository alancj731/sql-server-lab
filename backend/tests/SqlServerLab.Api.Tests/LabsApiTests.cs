using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SqlServerLab.Application.Jobs;
using SqlServerLab.Application.Labs;
using SqlServerLab.Domain.Jobs;
using SqlServerLab.Domain.Labs;

namespace SqlServerLab.Api.Tests;

public sealed class LabsApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Requests_without_identity_are_unauthorized()
    {
        var client = factory.ClientFor(null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/labs")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/labs", new { name = "x", region = "eastus", ttlHours = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/v1/jobs/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Invalid_development_identity_is_rejected()
    {
        var client = factory.ClientFor("bad user<script>");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/labs")).StatusCode);
    }

    [Fact]
    public async Task Create_returns_202_with_job_resource()
    {
        var client = factory.ClientFor(NewUser());
        var response = await client.PostAsJsonAsync("/api/v1/labs", new CreateLabRequest("api-lab", "eastus", 2));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.ReadAsync<CreateLabResponse>();
        Assert.Equal(LabJobStatus.Queued, body.Job.Status);
        Assert.Equal(0, body.Job.Progress);
        Assert.Equal($"/api/v1/jobs/{body.Job.JobId}", body.Job.StatusUrl);
        Assert.Equal(body.Job.StatusUrl, response.Headers.Location?.OriginalString);
        Assert.Equal(LabState.Requested, body.Lab.State);

        var job = await (await client.GetAsync(body.Job.StatusUrl)).ReadAsync<JobDto>();
        Assert.Equal(body.Job.JobId, job.JobId);

        var raw = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"type\":\"ProvisionLab\"", raw);
        Assert.Contains("\"jobId\"", raw);
    }

    [Fact]
    public async Task Validation_errors_are_problem_details()
    {
        var client = factory.ClientFor(NewUser());
        var response = await client.PostAsJsonAsync("/api/v1/labs", new CreateLabRequest("BAD", "mars", 0));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = doc.RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty("Name", out _));
        Assert.True(errors.TryGetProperty("Region", out _));
        Assert.True(errors.TryGetProperty("TtlHours", out _));
        Assert.True(doc.RootElement.TryGetProperty("correlationId", out _));
    }

    [Fact]
    public async Task Malformed_json_is_a_bad_request()
    {
        var client = factory.ClientFor(NewUser());
        var response = await client.PostAsync("/api/v1/labs", new StringContent("{nope", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Other_users_cannot_see_or_act_on_a_lab()
    {
        var owner = factory.ClientFor(NewUser());
        var created = await (await owner.PostAsJsonAsync("/api/v1/labs", new CreateLabRequest("private", "eastus", 1))).ReadAsync<CreateLabResponse>();
        var intruder = factory.ClientFor(NewUser());

        Assert.Equal(HttpStatusCode.NotFound, (await intruder.GetAsync($"/api/v1/labs/{created.Lab.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.PostAsync($"/api/v1/labs/{created.Lab.Id}/start", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.GetAsync(created.Job.StatusUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.GetAsync($"/api/v1/labs/{created.Lab.Id}/audit")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.PostAsync($"/api/v1/jobs/{created.Job.JobId}/cancel", null)).StatusCode);
        Assert.DoesNotContain(await (await intruder.GetAsync("/api/v1/labs")).ReadAsync<List<LabDto>>(), l => l.Id == created.Lab.Id);

        var admin = factory.ClientFor(NewUser(), admin: true);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/v1/labs/{created.Lab.Id}")).StatusCode);
    }

    [Fact]
    public async Task Conflicting_operations_are_rejected_with_409()
    {
        var client = factory.ClientFor(NewUser());
        var created = await (await client.PostAsJsonAsync("/api/v1/labs", new CreateLabRequest("conflict", "eastus", 1))).ReadAsync<CreateLabResponse>();

        var start = await client.PostAsync($"/api/v1/labs/{created.Lab.Id}/start", null);
        Assert.Equal(HttpStatusCode.Conflict, start.StatusCode);
        Assert.Equal("application/problem+json", start.Content.Headers.ContentType?.MediaType);

        await factory.SetLabStateAsync(created.Lab.Id, LabState.Ready);
        var first = await client.PostAsync($"/api/v1/labs/{created.Lab.Id}/deallocate", null);
        var repeat = await client.PostAsync($"/api/v1/labs/{created.Lab.Id}/deallocate", null);
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal((await first.ReadAsync<JobDto>()).JobId, (await repeat.ReadAsync<JobDto>()).JobId);

        var delete = await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/labs/{created.Lab.Id}")
        {
            Content = JsonContent.Create(new DeleteLabRequest("conflict")),
        });
        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
    }

    [Fact]
    public async Task Delete_requires_typed_confirmation()
    {
        var client = factory.ClientFor(NewUser());
        var created = await (await client.PostAsJsonAsync("/api/v1/labs", new CreateLabRequest("to-delete", "eastus", 1))).ReadAsync<CreateLabResponse>();
        await factory.SetLabStateAsync(created.Lab.Id, LabState.Ready);

        var wrong = await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/labs/{created.Lab.Id}")
        {
            Content = JsonContent.Create(new DeleteLabRequest("to-delete-x")),
        });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);

        var missing = await client.DeleteAsync($"/api/v1/labs/{created.Lab.Id}");
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);

        var right = await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/labs/{created.Lab.Id}")
        {
            Content = JsonContent.Create(new DeleteLabRequest("to-delete")),
        });
        Assert.Equal(HttpStatusCode.Accepted, right.StatusCode);
        Assert.Equal(LabJobType.DeleteLab, (await right.ReadAsync<JobDto>()).Type);
    }

    [Fact]
    public async Task Cancel_extend_jobs_and_audit_endpoints_work()
    {
        var client = factory.ClientFor(NewUser());
        var created = await (await client.PostAsJsonAsync("/api/v1/labs", new CreateLabRequest("misc", "eastus", 1))).ReadAsync<CreateLabResponse>();

        var cancel = await client.PostAsync($"/api/v1/jobs/{created.Job.JobId}/cancel", null);
        Assert.Equal(HttpStatusCode.Accepted, cancel.StatusCode);
        Assert.Equal(LabJobStatus.Cancelled, (await cancel.ReadAsync<JobDto>()).Status);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/v1/jobs/{created.Job.JobId}/cancel", null)).StatusCode);

        var extended = await client.PostAsJsonAsync($"/api/v1/labs/{created.Lab.Id}/extend-expiration", new ExtendExpirationRequest(2));
        Assert.Equal(HttpStatusCode.OK, extended.StatusCode);
        Assert.True((await extended.ReadAsync<LabDto>()).ExpiresAt > created.Lab.ExpiresAt);

        Assert.Single(await (await client.GetAsync($"/api/v1/labs/{created.Lab.Id}/jobs")).ReadAsync<List<JobDto>>());
        var audit = await client.GetStringAsync($"/api/v1/labs/{created.Lab.Id}/audit");
        Assert.Contains("job.cancelled", audit);
        Assert.Contains("lab.expiration-extended", audit);
    }

    [Fact]
    public async Task Environment_reports_simulation()
    {
        var env = await (await factory.ClientFor(NewUser()).GetAsync("/api/v1/environment")).ReadAsync<EnvironmentDto>();
        Assert.True(env.IsSimulated);
        Assert.Equal("Local", env.InfrastructureMode);
        Assert.Contains("eastus", env.Regions);
    }

    [Fact]
    public async Task Health_correlation_and_security_headers()
    {
        var client = factory.ClientFor(null);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/labs");
        request.Headers.Add("X-Correlation-ID", "abc12345-test");
        request.Headers.Add("X-Dev-User", NewUser());
        var response = await client.SendAsync(request);
        Assert.Equal("abc12345-test", response.Headers.GetValues("X-Correlation-ID").Single());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
    }

    [Fact]
    public async Task Responses_never_contain_secrets_or_connection_strings()
    {
        var client = factory.ClientFor(NewUser());
        var created = await (await client.PostAsJsonAsync("/api/v1/labs", new CreateLabRequest("secrets", "eastus", 1))).ReadAsync<CreateLabResponse>();
        foreach (var url in new[] { "/api/v1/labs", $"/api/v1/labs/{created.Lab.Id}", "/api/v1/environment", $"/api/v1/labs/{created.Lab.Id}/audit" })
        {
            var body = await client.GetStringAsync(url);
            Assert.DoesNotContain("Data Source", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("password", body, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Client_config_is_public_and_contains_no_secrets()
    {
        var response = await factory.ClientFor(null).GetAsync("/config.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"authMode\":\"Development\"", body);
        Assert.DoesNotContain("secret", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unknown_api_routes_are_404_not_the_spa()
    {
        var response = await factory.ClientFor(NewUser()).GetAsync("/api/v1/nope");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static string NewUser() => $"user-{Guid.NewGuid():N}"[..20];
}
