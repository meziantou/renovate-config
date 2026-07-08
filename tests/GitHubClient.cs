using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meziantou.RenovateConfig.Tests;

internal enum PullRequestState
{
    Open,
    Closed,
    All,
}

internal sealed class GitHubClient
{
    private const string UserAgent = "meziantou-renovate-config-tests";
    private const int PageSize = 100;
    private static readonly Uri BaseAddress = new("https://api.github.com/");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AuthenticationHeaderValue _authenticationHeader;

    public GitHubClient(string token)
    {
        _authenticationHeader = new AuthenticationHeaderValue("Bearer", token);
    }

    public Task<IReadOnlyList<PullRequest>> GetPullRequestsAsync(string owner, string repository, string baseBranch, PullRequestState state, CancellationToken cancellationToken)
    {
        return GetPaginatedAsync<PullRequest>(
            BuildUri($"repos/{Escape(owner)}/{Escape(repository)}/pulls", [
                KeyValuePair.Create<string, string?>("base", baseBranch),
                KeyValuePair.Create<string, string?>("state", state.ToString().ToLowerInvariant()),
                KeyValuePair.Create<string, string?>("per_page", PageSize.ToString(CultureInfo.InvariantCulture)),
            ]),
            cancellationToken);
    }

    public Task<IReadOnlyList<PullRequestCommit>> GetPullRequestCommitsAsync(string owner, string repository, int pullRequestNumber, CancellationToken cancellationToken)
    {
        return GetPaginatedAsync<PullRequestCommit>(
            BuildUri($"repos/{Escape(owner)}/{Escape(repository)}/pulls/{pullRequestNumber.ToString(CultureInfo.InvariantCulture)}/commits", [
                KeyValuePair.Create<string, string?>("per_page", PageSize.ToString(CultureInfo.InvariantCulture)),
            ]),
            cancellationToken);
    }

    public async Task<bool> IsPullRequestMergedAsync(string owner, string repository, int pullRequestNumber, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            () => CreateRequest(HttpMethod.Get, BuildUri($"repos/{Escape(owner)}/{Escape(repository)}/pulls/{pullRequestNumber.ToString(CultureInfo.InvariantCulture)}/merge")),
            cancellationToken).ConfigureAwait(false);

        return response.StatusCode switch
        {
            HttpStatusCode.NoContent => true,
            HttpStatusCode.NotFound => false,
            _ => await ThrowForUnexpectedStatusAsync(response, cancellationToken).ConfigureAwait(false),
        };
    }

    public async Task<string> GetFileContentAsync(string owner, string repository, string path, string gitReference, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            () => CreateRequest(HttpMethod.Get, BuildUri($"repos/{Escape(owner)}/{Escape(repository)}/contents/{EscapePath(path)}", [
                KeyValuePair.Create<string, string?>("ref", gitReference),
            ])),
            cancellationToken).ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        var file = await DeserializeAsync<RepositoryContent>(response, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(file.Encoding, "base64", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unexpected GitHub content encoding '{file.Encoding}'.");
        }

        var base64 = file.Content.Replace("\n", string.Empty, StringComparison.Ordinal).Replace("\r", string.Empty, StringComparison.Ordinal);
        return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
    }

    public Task<IReadOnlyList<Branch>> GetBranchesAsync(string owner, string repository, CancellationToken cancellationToken)
    {
        return GetPaginatedAsync<Branch>(
            BuildUri($"repos/{Escape(owner)}/{Escape(repository)}/branches", [
                KeyValuePair.Create<string, string?>("per_page", PageSize.ToString(CultureInfo.InvariantCulture)),
            ]),
            cancellationToken);
    }

    public async Task ClosePullRequestAsync(string owner, string repository, int pullRequestNumber, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            () => CreateRequest(HttpMethod.Patch, BuildUri($"repos/{Escape(owner)}/{Escape(repository)}/pulls/{pullRequestNumber.ToString(CultureInfo.InvariantCulture)}"), JsonContent.Create(new { state = "closed" })),
            cancellationToken).ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteBranchAsync(string owner, string repository, string branchName, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            () => CreateRequest(HttpMethod.Delete, BuildUri($"repos/{Escape(owner)}/{Escape(repository)}/git/refs/heads/{EscapePath(branchName)}")),
            cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return;
        }

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<T>> GetPaginatedAsync<T>(Uri uri, CancellationToken cancellationToken)
    {
        List<T>? results = null;

        for (var page = 1; ; page++)
        {
            using var response = await SendAsync(
                () => CreateRequest(HttpMethod.Get, AppendQuery(uri, "page", page.ToString(CultureInfo.InvariantCulture))),
                cancellationToken).ConfigureAwait(false);

            await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
            var items = await DeserializeAsync<List<T>>(response, cancellationToken).ConfigureAwait(false);
            results ??= new List<T>(items.Count);
            results.AddRange(items);

            if (items.Count < PageSize)
            {
                return results;
            }
        }
    }

    private async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> requestFactory, CancellationToken cancellationToken)
    {
        return await SharedHttpClient.SendAsync(requestFactory, cancellationToken).ConfigureAwait(false);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, Uri uri, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, uri)
        {
            Content = content,
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Authorization = _authenticationHeader;
        request.Headers.UserAgent.ParseAdd(UserAgent);
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        return request;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        await ThrowForUnexpectedStatusAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<T> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"GitHub API response for '{response.RequestMessage?.RequestUri}' was empty.");
    }

    private static async Task<bool> ThrowForUnexpectedStatusAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        throw new HttpRequestException(
            $"GitHub API request to '{response.RequestMessage?.RequestUri}' failed with status code {(int)response.StatusCode} ({response.StatusCode}). Body: {body}",
            inner: null,
            response.StatusCode);
    }

    private static Uri BuildUri(string path, params IReadOnlyList<KeyValuePair<string, string?>> queryParameters)
    {
        var builder = new UriBuilder(new Uri(BaseAddress, path));
        if (queryParameters.Count > 0)
        {
            builder.Query = string.Join("&", queryParameters.Where(static pair => pair.Value is not null).Select(static pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value!)}"));
        }

        return builder.Uri;
    }

    private static Uri AppendQuery(Uri uri, string key, string value)
    {
        var builder = new UriBuilder(uri);
        builder.Query = string.IsNullOrEmpty(builder.Query)
            ? $"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}"
            : builder.Query.TrimStart('?') + "&" + $"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}";
        return builder.Uri;
    }

    private static string Escape(string value) => Uri.EscapeDataString(value);

    private static string EscapePath(string value) => string.Join("/", value.Split('/').Select(Escape));

    internal sealed record PullRequest(
        [property: JsonPropertyName("number")] int Number,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("body")] string? Body,
        [property: JsonPropertyName("labels")] IReadOnlyList<Label> Labels);

    internal sealed record Label([property: JsonPropertyName("name")] string Name);

    internal sealed record PullRequestCommit([property: JsonPropertyName("commit")] CommitDetails Commit);

    internal sealed record CommitDetails([property: JsonPropertyName("message")] string Message);

    internal sealed record Branch([property: JsonPropertyName("name")] string Name);

    internal sealed record RepositoryContent(
        [property: JsonPropertyName("content")] string Content,
        [property: JsonPropertyName("encoding")] string Encoding);
}
