using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Abstractions;

namespace Chatbot.Api.Services.Graph;

public sealed record DriveFile(string DriveId, string ItemId, string Name, long Size, string WebUrl);

public sealed class GraphOptions
{
    public string BaseUrl { get; set; } = "https://graph.microsoft.com/v1.0/";
    public string[] Scopes { get; set; } = ["https://graph.microsoft.com/Files.ReadWrite.All"];

    /// <summary>Drive (document library) for generated documents. Empty = the signed-in user's OneDrive.</summary>
    public string OutputDriveId { get; set; } = "";
    public string OutputFolderPath { get; set; } = "Chatbot";
}

public interface IGraphFiles
{
    Task<DriveFile> GetFileAsync(string driveId, string itemId, CancellationToken cancellationToken);
    Task<byte[]> DownloadAsync(string driveId, string itemId, long maxBytes, CancellationToken cancellationToken);
    Task<DriveFile> UploadAsync(string fileName, byte[] content, string contentType, CancellationToken cancellationToken);
    Task<byte[]> ConvertToPdfAsync(string driveId, string itemId, CancellationToken cancellationToken);
    Task DeleteAsync(string driveId, string itemId, CancellationToken cancellationToken);
}

public sealed class GraphException(string message, HttpStatusCode statusCode) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}

/// <summary>Calls Microsoft Graph on behalf of the signed-in user (OBO), so SharePoint permissions are always the user's own.</summary>
public sealed class GraphFiles(HttpClient http, IAuthorizationHeaderProvider tokens, IOptions<GraphOptions> options) : IGraphFiles
{
    private const int SimpleUploadLimit = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<DriveFile> GetFileAsync(string driveId, string itemId, CancellationToken cancellationToken)
    {
        using var request = await CreateRequestAsync(HttpMethod.Get,
            $"drives/{Segment(driveId)}/items/{Segment(itemId)}?$select=id,name,size,webUrl,file,parentReference", cancellationToken);
        using var response = await SendAsync(request, cancellationToken);
        var item = await response.Content.ReadFromJsonAsync<DriveItem>(Json, cancellationToken)
            ?? throw new GraphException("Graph returned an empty item.", HttpStatusCode.BadGateway);
        if (item.File is null)
        {
            throw new GraphException("The selected item is not a file.", HttpStatusCode.BadRequest);
        }

        return ToDriveFile(item, driveId);
    }

    public async Task<byte[]> DownloadAsync(string driveId, string itemId, long maxBytes, CancellationToken cancellationToken)
    {
        using var request = await CreateRequestAsync(HttpMethod.Get, $"drives/{Segment(driveId)}/items/{Segment(itemId)}/content", cancellationToken);
        using var response = await SendAsync(request, cancellationToken, HttpCompletionOption.ResponseHeadersRead);
        return await ReadLimitedAsync(response, maxBytes, cancellationToken);
    }

    public async Task<DriveFile> UploadAsync(string fileName, byte[] content, string contentType, CancellationToken cancellationToken)
    {
        if (content.Length > SimpleUploadLimit)
        {
            throw new GraphException("Generated documents are limited to 4 MB.", HttpStatusCode.RequestEntityTooLarge);
        }

        var settings = options.Value;
        var drive = string.IsNullOrWhiteSpace(settings.OutputDriveId) ? "me/drive" : $"drives/{Segment(settings.OutputDriveId)}";
        var folder = string.Join('/', settings.OutputFolderPath.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Segment));
        var path = folder.Length == 0 ? Segment(fileName) : $"{folder}/{Segment(fileName)}";
        using var request = await CreateRequestAsync(HttpMethod.Put,
            $"{drive}/root:/{path}:/content?@microsoft.graph.conflictBehavior=rename", cancellationToken);
        request.Content = new ByteArrayContent(content);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        using var response = await SendAsync(request, cancellationToken);
        var item = await response.Content.ReadFromJsonAsync<DriveItem>(Json, cancellationToken)
            ?? throw new GraphException("Graph returned an empty item.", HttpStatusCode.BadGateway);
        return ToDriveFile(item, item.ParentReference?.DriveId ?? settings.OutputDriveId);
    }

    public async Task<byte[]> ConvertToPdfAsync(string driveId, string itemId, CancellationToken cancellationToken)
    {
        using var request = await CreateRequestAsync(HttpMethod.Get,
            $"drives/{Segment(driveId)}/items/{Segment(itemId)}/content?format=pdf", cancellationToken);
        using var response = await SendAsync(request, cancellationToken, HttpCompletionOption.ResponseHeadersRead);
        return await ReadLimitedAsync(response, 50 * 1024 * 1024, cancellationToken);
    }

    public async Task DeleteAsync(string driveId, string itemId, CancellationToken cancellationToken)
    {
        using var request = await CreateRequestAsync(HttpMethod.Delete, $"drives/{Segment(driveId)}/items/{Segment(itemId)}", cancellationToken);
        using var response = await SendAsync(request, cancellationToken);
    }

    private async Task<HttpRequestMessage> CreateRequestAsync(HttpMethod method, string relativeUrl, CancellationToken cancellationToken)
    {
        var header = await tokens.CreateAuthorizationHeaderForUserAsync(options.Value.Scopes,
            cancellationToken: cancellationToken);
        var request = new HttpRequestMessage(method, new Uri(new Uri(options.Value.BaseUrl), relativeUrl));
        request.Headers.Authorization = AuthenticationHeaderValue.Parse(header);
        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken,
        HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead)
    {
        var response = await http.SendAsync(request, completion, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        var status = response.StatusCode;
        response.Dispose();
        throw new GraphException(status switch
        {
            HttpStatusCode.NotFound => "The file was not found or you don't have access to it.",
            HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized => "You don't have access to this file.",
            _ => $"Microsoft Graph request failed with status {(int)status}."
        }, status);
    }

    private static async Task<byte[]> ReadLimitedAsync(HttpResponseMessage response, long maxBytes, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength > maxBytes)
        {
            throw new GraphException("The file is too large.", HttpStatusCode.RequestEntityTooLarge);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                throw new GraphException("The file is too large.", HttpStatusCode.RequestEntityTooLarge);
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static DriveFile ToDriveFile(DriveItem item, string driveId) =>
        new(item.ParentReference?.DriveId ?? driveId, item.Id, item.Name, item.Size, item.WebUrl ?? "");

    private static string Segment(string value) => Uri.EscapeDataString(value);

    private sealed record DriveItem(string Id, string Name, long Size, string? WebUrl, JsonElement? File, ParentReference? ParentReference);

    private sealed record ParentReference([property: JsonPropertyName("driveId")] string? DriveId);
}

/// <summary>Registered when the API runs with mock authentication: there is no user token to exchange.</summary>
public sealed class UnavailableGraphFiles : IGraphFiles
{
    private static GraphException Unavailable() =>
        new("SharePoint access requires real authentication (UseMockAuthentication is enabled).", HttpStatusCode.NotImplemented);

    public Task<DriveFile> GetFileAsync(string driveId, string itemId, CancellationToken cancellationToken) => throw Unavailable();
    public Task<byte[]> DownloadAsync(string driveId, string itemId, long maxBytes, CancellationToken cancellationToken) => throw Unavailable();
    public Task<DriveFile> UploadAsync(string fileName, byte[] content, string contentType, CancellationToken cancellationToken) => throw Unavailable();
    public Task<byte[]> ConvertToPdfAsync(string driveId, string itemId, CancellationToken cancellationToken) => throw Unavailable();
    public Task DeleteAsync(string driveId, string itemId, CancellationToken cancellationToken) => throw Unavailable();
}
