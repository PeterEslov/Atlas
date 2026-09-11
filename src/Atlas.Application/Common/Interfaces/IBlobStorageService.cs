namespace Atlas.Application.Common.Interfaces;

/// <summary>
/// Persistence contract for file bytes, mirroring how <see cref="ITicketRepository"/>
/// is the contract for ticket rows: the Application layer depends only on this
/// interface, never on the Azure Storage SDK directly (Clean Architecture's
/// "dependencies point inward" rule — see docs/ARCHITECTURE.md). Atlas.Infrastructure
/// provides the real implementation against Azure Blob Storage (or its local
/// Azurite emulator); a future unit test could swap in an in-memory fake without
/// either project ever referencing Azure.Storage.Blobs.
/// </summary>
public interface IBlobStorageService
{
    /// <summary>
    /// Uploads <paramref name="content"/> under <paramref name="blobName"/>, overwriting
    /// any existing blob with that name. Callers are expected to generate a
    /// collision-safe name (see TicketService.AddAttachmentAsync) rather than relying
    /// on this to reject duplicates.
    /// </summary>
    Task UploadAsync(string blobName, Stream content, string contentType, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the blob's bytes and content type, or null if no blob exists under
    /// that name — a missing blob is an expected, handleable outcome (not an
    /// exception), since callers turn it into a 404 rather than a 500.
    /// </summary>
    Task<BlobDownload?> DownloadAsync(string blobName, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the blob if it exists; a no-op (not an error) if it doesn't. Used both
    /// for the compensating cleanup when a domain/database write fails after a blob
    /// was already uploaded, and for real deletions once those exist (see the "known
    /// simplifications" note on Ticket hard-delete in docs/ARCHITECTURE.md).
    /// </summary>
    Task DeleteAsync(string blobName, CancellationToken cancellationToken);
}

/// <summary>Bytes plus the content type they were stored with, returned by IBlobStorageService.DownloadAsync.</summary>
public sealed record BlobDownload(Stream Content, string ContentType);
