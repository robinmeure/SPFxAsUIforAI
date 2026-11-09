namespace Chatbot.Api.Services.Storage;

internal static class DocumentTransitions
{
    public static UserDocument? Complete(UserDocument current, UserDocument result) =>
        current.Status == DocumentStatus.Deleted ? current with { AwaitingIngestion = false }
        : current.Status == DocumentStatus.Processing && result.Status is DocumentStatus.Ready or DocumentStatus.Failed
            ? current with { Status = result.Status, ChunkCount = result.ChunkCount, Error = result.Error, UpdatedAt = result.UpdatedAt }
            : null;

    public static UserDocument Delete(UserDocument current) => current.Status == DocumentStatus.Deleted ? current : current with
    {
        Status = DocumentStatus.Deleted,
        CleanupPending = true,
        AwaitingIngestion = current.Status == DocumentStatus.Processing,
        FileName = "",
        SourceUrl = null,
        Error = null
    };

    public static UserDocument? Clean(UserDocument current) =>
        current.Status == DocumentStatus.Deleted && !current.AwaitingIngestion
            ? current with { CleanupPending = false, AwaitingIngestion = false }
            : null;
}
