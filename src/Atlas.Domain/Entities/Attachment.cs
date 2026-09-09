using Atlas.Domain.Common;
using Atlas.Domain.Exceptions;

namespace Atlas.Domain.Entities;

/// <summary>
/// Metadata for a file attached to a ticket. The file's bytes live in Azure Blob
/// Storage (see Del 10) — only the pointer (<see cref="BlobName"/>) is stored here,
/// which is the standard split between relational data and object/file storage.
/// </summary>
public sealed class Attachment : Entity
{
    public Guid TicketId { get; private set; }
    public string FileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long SizeInBytes { get; private set; }

    /// <summary>Path/key of the blob inside the storage container, e.g. "tickets/1042/screenshot.png".</summary>
    public string BlobName { get; private set; } = string.Empty;

    public Guid UploadedByUserId { get; private set; }

    private const long MaxSizeInBytes = 25 * 1024 * 1024; // 25 MB

    private Attachment()
    {
        // Required by EF Core for materialization.
    }

    internal static Attachment Create(Guid ticketId, string fileName, string contentType, long sizeInBytes, string blobName, Guid uploadedByUserId)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new DomainException("Attachment file name cannot be empty.");

        if (sizeInBytes <= 0 || sizeInBytes > MaxSizeInBytes)
            throw new DomainException($"Attachment size must be between 1 byte and {MaxSizeInBytes / (1024 * 1024)} MB.");

        return new Attachment
        {
            TicketId = ticketId,
            FileName = fileName.Trim(),
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
            SizeInBytes = sizeInBytes,
            BlobName = blobName,
            UploadedByUserId = uploadedByUserId
        };
    }
}
