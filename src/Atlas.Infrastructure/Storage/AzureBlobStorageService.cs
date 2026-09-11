using Atlas.Application.Common.Interfaces;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace Atlas.Infrastructure.Storage;

/// <summary>
/// Implements <see cref="IBlobStorageService"/> against Azure Blob Storage (or its
/// local Azurite emulator — the two speak the same wire protocol, so this class
/// doesn't know or care which one it's actually talking to). One pre-resolved
/// <see cref="BlobContainerClient"/> is injected — see the "Blob Storage" block in
/// DependencyInjection.AddInfrastructure for how that client is built and which
/// container it points at.
/// </summary>
public sealed class AzureBlobStorageService : IBlobStorageService
{
    private readonly BlobContainerClient _containerClient;

    public AzureBlobStorageService(BlobContainerClient containerClient)
    {
        _containerClient = containerClient;
    }

    public async Task UploadAsync(string blobName, Stream content, string contentType, CancellationToken cancellationToken)
    {
        var blobClient = _containerClient.GetBlobClient(blobName);

        // Overwrites by default (BlobClient.UploadAsync(Stream, ...) does not throw
        // on an existing blob the way the parameterless-conditions overload can be
        // made to). That's fine here: TicketService always generates a fresh,
        // GUID-prefixed blob name per upload, so a collision never legitimately
        // happens — this isn't relied on to "update" an existing attachment's bytes.
        await blobClient.UploadAsync(
            content,
            new BlobHttpHeaders { ContentType = contentType },
            cancellationToken: cancellationToken);
    }

    public async Task<BlobDownload?> DownloadAsync(string blobName, CancellationToken cancellationToken)
    {
        var blobClient = _containerClient.GetBlobClient(blobName);

        // Checking existence first (rather than catching the RequestFailedException
        // a missing blob would throw from DownloadStreamingAsync) keeps "attachment
        // not found" an ordinary, expected return value here — matching how
        // TicketRepository.GetByIdAsync returns null instead of throwing, and how
        // ITicketService.DownloadAttachmentAsync's null case turns into a 404
        // rather than tripping the exception middleware's generic 500 branch.
        if (!await blobClient.ExistsAsync(cancellationToken))
        {
            return null;
        }

        var response = await blobClient.DownloadStreamingAsync(cancellationToken: cancellationToken);
        return new BlobDownload(response.Value.Content, response.Value.Details.ContentType);
    }

    public async Task DeleteAsync(string blobName, CancellationToken cancellationToken)
    {
        // "IfExists" — deleting something already gone (or never uploaded, e.g. the
        // compensating cleanup in TicketService.AddAttachmentAsync racing a caller
        // that retried the whole request) is a no-op, not an error.
        await _containerClient.DeleteBlobIfExistsAsync(blobName, cancellationToken: cancellationToken);
    }
}
