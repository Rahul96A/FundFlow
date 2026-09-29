using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FundFlow.Api.IntegrationTests.Infrastructure;

/// <summary>RFC 7807 body as the API returns it (extension members such as <c>code</c> sit at the root).</summary>
public sealed record Problem(
    string? Type,
    string? Title,
    int Status,
    string? Detail,
    string? Code,
    Dictionary<string, string[]>? Errors,
    string? TraceId,
    string? CorrelationId,
    string? Instance);

/// <summary>
/// One browser-like client: its own cookie jar (so the HttpOnly refresh cookie behaves as in production) and its own
/// access token. Requests run through the real pipeline: routing, auth, tenant middleware, rate limiter, handlers.
/// </summary>
public sealed class ApiSession(HttpClient http) : IDisposable
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public HttpClient Http { get; } = http;

    public string? AccessToken { get; set; }

    public Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string url,
        object? body = null,
        bool authenticated = true,
        bool webClient = false,
        IDictionary<string, string>? headers = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (authenticated && AccessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
        }

        if (webClient)
        {
            request.Headers.Add("X-FundFlow-Client", "web");
        }

        if (headers is not null)
        {
            foreach (var (name, value) in headers)
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: Json);
        }

        return Http.SendAsync(request);
    }

    public Task<HttpResponseMessage> GetAsync(string url, bool authenticated = true) => SendAsync(HttpMethod.Get, url, authenticated: authenticated);

    public Task<HttpResponseMessage> PostAsync(string url, object? body = null, bool authenticated = true, bool webClient = false) =>
        SendAsync(HttpMethod.Post, url, body ?? new { }, authenticated, webClient);

    public Task<HttpResponseMessage> PutAsync(string url, object body) => SendAsync(HttpMethod.Put, url, body);

    public Task<HttpResponseMessage> DeleteAsync(string url) => SendAsync(HttpMethod.Delete, url);

    public async Task<T> GetJsonAsync<T>(string url)
    {
        using var response = await GetAsync(url);
        await response.ShouldBeAsync(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    public void Dispose() => Http.Dispose();
}

public static class HttpResponseExtensions
{
    /// <summary>Asserts the status and, when it fails, shows the response body (the actual reason).</summary>
    public static async Task ShouldBeAsync(this HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(expected, $"the response body was: {body}");
    }

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        try
        {
            return JsonSerializer.Deserialize<T>(body, ApiSession.Json)!;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Could not read {typeof(T).Name} from ({(int)response.StatusCode}): {body}", ex);
        }
    }

    public static async Task<T> ExpectAsync<T>(this HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        await response.ShouldBeAsync(expected);
        return await response.ReadAsync<T>();
    }

    /// <summary>Asserts an RFC 7807 problem with the given status and (optionally) machine-readable code.</summary>
    public static async Task<Problem> ShouldBeProblemAsync(this HttpResponseMessage response, HttpStatusCode status, string? code = null)
    {
        await response.ShouldBeAsync(status);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var problem = await response.ReadAsync<Problem>();
        problem.Status.Should().Be((int)status);
        problem.Type.Should().StartWith("https://api.fundflow.com/errors/");
        problem.TraceId.Should().NotBeNullOrEmpty();
        if (code is not null)
        {
            problem.Code.Should().Be(code);
        }

        return problem;
    }
}
