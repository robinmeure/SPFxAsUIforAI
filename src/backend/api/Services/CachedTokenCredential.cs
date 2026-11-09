using Azure.Core;

namespace Chatbot.Api.Services;

/// <summary>
/// Caches access tokens per scope. The OpenAI SDK's <c>BearerTokenPolicy</c> asks the credential for a token on every
/// request, and credentials such as <c>AzureCliCredential</c> take seconds per call, so without this every Foundry
/// call paid that cost before it was sent.
/// </summary>
public sealed class CachedTokenCredential(TokenCredential inner, TimeProvider time) : TokenCredential
{
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(5);
    private readonly Dictionary<string, AccessToken> _tokens = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _lock = new(1, 1);

    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
        GetTokenAsync(requestContext, cancellationToken).AsTask().GetAwaiter().GetResult();

    public override async ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        // Claims challenges (for example Continuous Access Evaluation) must always reach the real credential.
        if (requestContext.Claims is not null)
        {
            return await inner.GetTokenAsync(requestContext, cancellationToken);
        }

        var key = $"{requestContext.TenantId}|{string.Join(' ', requestContext.Scopes)}";
        if (TryGetFresh(key, out var token))
        {
            return token;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (TryGetFresh(key, out token))
            {
                return token;
            }

            token = await inner.GetTokenAsync(requestContext, cancellationToken);
            lock (_tokens)
            {
                _tokens[key] = token;
            }

            return token;
        }
        finally
        {
            _lock.Release();
        }
    }

    private bool TryGetFresh(string key, out AccessToken token)
    {
        lock (_tokens)
        {
            if (_tokens.TryGetValue(key, out token))
            {
                var now = time.GetUtcNow();
                return token.ExpiresOn - now > RefreshMargin && (token.RefreshOn is not { } refreshOn || refreshOn > now);
            }
        }

        return false;
    }
}
