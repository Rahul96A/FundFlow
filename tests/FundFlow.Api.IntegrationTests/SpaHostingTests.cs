using System.Net;
using FundFlow.Api.IntegrationTests.Infrastructure;

namespace FundFlow.Api.IntegrationTests;

/// <summary>
/// Single-origin hosting: the API serves the built React app from wwwroot when it is present, without ever letting the
/// app shell shadow the API, health checks or missing files.
/// </summary>
[Collection(ApiCollection.Name)]
public class SpaHostingTests(ApiFixture fixture)
{
    private const string Shell = "<!doctype html><html><body><div id=\"root\">FundFlow app shell</div></body></html>";

    private static string CreateWebRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"fundflow-webroot-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "assets"));
        File.WriteAllText(Path.Combine(root, "index.html"), Shell);
        File.WriteAllText(Path.Combine(root, "assets", "app-3f9a1c.js"), "console.log('fundflow');");
        File.WriteAllText(Path.Combine(root, "favicon.svg"), "<svg xmlns=\"http://www.w3.org/2000/svg\"/>");
        return root;
    }

    private async Task WithHostAsync(Func<HttpClient, Task> test, IDictionary<string, string?>? settings = null)
    {
        var webRoot = CreateWebRoot();
        try
        {
            var overrides = new Dictionary<string, string?> { ["webroot"] = webRoot };
            foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
            {
                overrides[key] = value;
            }

            await using var factory = new ApiFactory(fixture.ServerConnectionString, overrides);
            await factory.InitializeAsync();
            using var client = factory.CreateClient();
            await test(client);
        }
        finally
        {
            Directory.Delete(webRoot, recursive: true);
        }
    }

    [Fact]
    public Task The_root_and_client_side_routes_return_the_app_shell_to_anonymous_visitors() => WithHostAsync(async client =>
    {
        foreach (var path in new[] { "/", "/login", "/settings/users", "/reset-password?token=abc" })
        {
            using var response = await client.GetAsync(path);

            response.StatusCode.Should().Be(HttpStatusCode.OK, $"{path} is a client-side route");
            response.Content.Headers.ContentType?.MediaType.Should().Be("text/html");
            (await response.Content.ReadAsStringAsync()).Should().Contain("FundFlow app shell");
            response.Headers.CacheControl?.NoCache.Should().BeTrue("the shell must always revalidate so a new release is picked up");

            var csp = response.Headers.GetValues("Content-Security-Policy").Single();
            csp.Should().Contain("script-src 'self'").And.Contain("frame-ancestors 'none'").And.NotContain("default-src 'none'");
            response.Headers.GetValues("X-Content-Type-Options").Should().ContainSingle().Which.Should().Be("nosniff");
        }
    });

    [Fact]
    public Task Fingerprinted_assets_are_cached_for_a_year_and_other_files_revalidate() => WithHostAsync(async client =>
    {
        using var asset = await client.GetAsync("/assets/app-3f9a1c.js");
        asset.StatusCode.Should().Be(HttpStatusCode.OK);
        asset.Headers.CacheControl.Should().NotBeNull();
        asset.Headers.CacheControl!.MaxAge.Should().Be(TimeSpan.FromDays(365));
        asset.Headers.CacheControl.Public.Should().BeTrue();
        asset.Headers.GetValues("Cache-Control").Single().Should().Contain("immutable");

        using var icon = await client.GetAsync("/favicon.svg");
        icon.StatusCode.Should().Be(HttpStatusCode.OK);
        icon.Headers.CacheControl?.NoCache.Should().BeTrue();
    });

    [Fact]
    public Task The_app_shell_never_shadows_the_api_health_checks_missing_files_or_other_methods() => WithHostAsync(async client =>
    {
        // Unknown API paths are JSON problems, not HTML, and keep the API's strict headers.
        using var unknownApi = await client.GetAsync("/api/v1/no-such-endpoint");
        unknownApi.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await unknownApi.Content.ReadAsStringAsync()).Should().NotContain("FundFlow app shell");
        unknownApi.Headers.GetValues("Content-Security-Policy").Single().Should().Contain("default-src 'none'");

        using var anonymousApi = await client.GetAsync("/api/v1/auth/me");
        anonymousApi.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        anonymousApi.Headers.CacheControl?.NoStore.Should().BeTrue();

        // Health checks are still health checks.
        using var live = await client.GetAsync("/health/live");
        live.StatusCode.Should().Be(HttpStatusCode.OK);
        (await live.Content.ReadAsStringAsync()).Should().NotContain("FundFlow app shell");

        using var unknownHealth = await client.GetAsync("/health/nope");
        unknownHealth.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // A missing file is never answered with a 200 page that a browser would try to run as a script. (Requests that match no
        // endpoint at all get the API's global "authenticated users only" fallback, so anonymous callers see 401 rather than 404.)
        using var missingAsset = await client.GetAsync("/assets/missing-0000.js");
        missingAsset.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Unauthorized);
        (await missingAsset.Content.ReadAsStringAsync()).Should().NotContain("FundFlow app shell");

        // Only GET/HEAD navigate to the shell.
        using var post = await client.PostAsync("/some/client/route", new StringContent("{}"));
        post.StatusCode.Should().Be(HttpStatusCode.NotFound);
    });

    [Fact]
    public Task Hosting_can_be_switched_off_even_when_a_shell_is_present() => WithHostAsync(
        async client =>
        {
            using var response = await client.GetAsync("/login");

            response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Unauthorized);
            (await response.Content.ReadAsStringAsync()).Should().NotContain("FundFlow app shell");
        },
        new Dictionary<string, string?> { ["Spa:Enabled"] = "false" });
}
