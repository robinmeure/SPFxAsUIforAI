using System.Security.Claims;
using Microsoft.Identity.Web;

namespace Chatbot.Api.Controllers;

public static class UserIdentity
{
    /// <summary>Used as the owner of chats and documents when the API runs with mock authentication.</summary>
    public const string LocalDevelopmentUserId = "local-development-user";

    /// <summary>
    /// The Entra object ID (oid) of the signed-in user: stable per user and tenant, and the partition key for their data.
    /// </summary>
    public static string GetUserId(this ClaimsPrincipal user, IConfiguration configuration)
    {
        if (configuration.GetValue<bool>("UseMockAuthentication"))
        {
            return LocalDevelopmentUserId;
        }

        var objectId = user.GetObjectId();
        return Guid.TryParse(objectId, out var id)
            ? id.ToString()
            : throw new UnauthorizedAccessException("The access token has no valid object ID (oid) claim.");
    }
}
