using Atlas.Application.Common.Exceptions;
using Atlas.Application.Common.Interfaces;
using Atlas.Application.Common.Models;
using Atlas.Application.Tickets.Dtos;
using Atlas.Application.Tickets.Services;
using Atlas.Domain.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Atlas.Api.Controllers;

/// <summary>
/// Ticket endpoints. Every action requires a valid bearer token (Del 5); the
/// acting user id comes from the token's "sub" claim via ICurrentUserService,
/// never from a query parameter — the earlier actorUserId/createdByUserId
/// parameters were a pre-auth stopgap and are gone now that login is real.
/// Each action is additionally gated behind the permission policy that
/// matches what it does, so e.g. a Customer's token (Read+Create only) gets a
/// 403 from ASP.NET Core's authorization middleware before the action even
/// runs if they try to assign or delete a ticket.
/// </summary>
[ApiController]
[Route("api/tickets")]
[Produces("application/json")]
[Authorize]
public sealed class TicketsController : ControllerBase
{
    private readonly ITicketService _ticketService;
    private readonly ICurrentUserService _currentUserService;

    public TicketsController(ITicketService ticketService, ICurrentUserService currentUserService)
    {
        _ticketService = ticketService;
        _currentUserService = currentUserService;
    }

    /// <summary>
    /// The signed-in user's id, taken from the JWT's "sub" claim. [Authorize]
    /// on the controller guarantees a valid token was presented, so this
    /// should never actually be null — the exception is a defensive fallback,
    /// not an expected code path.
    /// </summary>
    private Guid ActorUserId =>
        _currentUserService.UserId
            ?? throw new AuthenticationException("The request's access token did not contain a valid user id.");

    /// <summary>
    /// GET /api/tickets?status=Open&amp;priority=High&amp;assignedTo={guid}&amp;page=2&amp;pageSize=25
    /// </summary>
    [HttpGet]
    [Authorize(Policy = Permissions.TicketRead)]
    [ProducesResponseType(typeof(PagedResult<TicketDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<TicketDto>>> Search([FromQuery] TicketListQuery query, CancellationToken cancellationToken)
    {
        var result = await _ticketService.SearchAsync(query, cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Permissions.TicketRead)]
    [ProducesResponseType(typeof(TicketDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TicketDetailDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var ticket = await _ticketService.GetByIdAsync(id, cancellationToken);
        return ticket is null ? NotFound() : Ok(ticket);
    }

    [HttpPost]
    [Authorize(Policy = Permissions.TicketCreate)]
    [ProducesResponseType(typeof(TicketDetailDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<TicketDetailDto>> Create(
        [FromBody] CreateTicketRequest request,
        CancellationToken cancellationToken)
    {
        var created = await _ticketService.CreateAsync(request, ActorUserId, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPost("{id:guid}/assign")]
    [Authorize(Policy = Permissions.TicketAssign)]
    [ProducesResponseType(typeof(TicketDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TicketDetailDto>> Assign(
        Guid id,
        [FromBody] AssignTicketRequest request,
        CancellationToken cancellationToken)
    {
        var updated = await _ticketService.AssignAsync(id, request, ActorUserId, cancellationToken);
        return Ok(updated);
    }

    [HttpPost("{id:guid}/status")]
    [Authorize(Policy = Permissions.TicketUpdate)]
    [ProducesResponseType(typeof(TicketDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<TicketDetailDto>> ChangeStatus(
        Guid id,
        [FromBody] ChangeTicketStatusRequest request,
        CancellationToken cancellationToken)
    {
        var updated = await _ticketService.ChangeStatusAsync(id, request, ActorUserId, cancellationToken);
        return Ok(updated);
    }

    [HttpPost("{id:guid}/priority")]
    [Authorize(Policy = Permissions.TicketUpdate)]
    [ProducesResponseType(typeof(TicketDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<TicketDetailDto>> ChangePriority(
        Guid id,
        [FromBody] ChangeTicketPriorityRequest request,
        CancellationToken cancellationToken)
    {
        var updated = await _ticketService.ChangePriorityAsync(id, request, ActorUserId, cancellationToken);
        return Ok(updated);
    }

    [HttpPost("{id:guid}/comments")]
    [Authorize(Policy = Permissions.TicketUpdate)]
    [ProducesResponseType(typeof(TicketCommentDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TicketCommentDto>> AddComment(
        Guid id,
        [FromBody] AddTicketCommentRequest request,
        CancellationToken cancellationToken)
    {
        var comment = await _ticketService.AddCommentAsync(id, request, ActorUserId, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id }, comment);
    }

    /// <summary>
    /// POST /api/tickets/{id}/reopen — only valid from Closed or Cancelled;
    /// see Ticket.Reopen for the guard. Gated by TicketUpdate, same as status
    /// changes generally, since reopening is just another status transition.
    /// </summary>
    [HttpPost("{id:guid}/reopen")]
    [Authorize(Policy = Permissions.TicketUpdate)]
    [ProducesResponseType(typeof(TicketDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<TicketDetailDto>> Reopen(Guid id, CancellationToken cancellationToken)
    {
        var updated = await _ticketService.ReopenAsync(id, ActorUserId, cancellationToken);
        return Ok(updated);
    }

    /// <summary>
    /// POST /api/tickets/{id}/tags — body is a tag name, not a tag id; see
    /// AddTicketTagRequest's doc comment for why.
    /// </summary>
    [HttpPost("{id:guid}/tags")]
    [Authorize(Policy = Permissions.TicketUpdate)]
    [ProducesResponseType(typeof(TicketDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TicketDetailDto>> AddTag(
        Guid id,
        [FromBody] AddTicketTagRequest request,
        CancellationToken cancellationToken)
    {
        var updated = await _ticketService.AddTagAsync(id, request, ActorUserId, cancellationToken);
        return Ok(updated);
    }

    /// <summary>
    /// POST /api/tickets/{id}/tags/remove — not a DELETE, because the tag name
    /// lives in a body rather than the route (DELETE with a body is legal HTTP
    /// but poorly supported by some clients/proxies, so this project avoids it).
    /// </summary>
    [HttpPost("{id:guid}/tags/remove")]
    [Authorize(Policy = Permissions.TicketUpdate)]
    [ProducesResponseType(typeof(TicketDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TicketDetailDto>> RemoveTag(
        Guid id,
        [FromBody] RemoveTicketTagRequest request,
        CancellationToken cancellationToken)
    {
        var updated = await _ticketService.RemoveTagAsync(id, request, ActorUserId, cancellationToken);
        return Ok(updated);
    }

    /// <summary>
    /// POST /api/tickets/{id}/attachments — multipart/form-data upload, field name
    /// "file". Gated by TicketUpdate rather than a dedicated Attachment permission:
    /// attaching a file is the same "can this user change this ticket" question as
    /// adding a comment (see Permissions.cs — TicketDelete is the example of a
    /// genuinely narrower policy that *does* earn its own constant, because hard
    /// delete is a materially bigger risk than any of these).
    /// RequestSizeLimit sits a little above Attachment's own 25 MB domain limit, so
    /// a too-large file gets ASP.NET Core's request all the way into the pipeline
    /// far enough to hit that domain check and come back as a friendly 400 —
    /// instead of Kestrel's own limit rejecting it earlier with a bare connection
    /// reset that Swagger UI / curl show no useful message for.
    /// </summary>
    [HttpPost("{id:guid}/attachments")]
    [Authorize(Policy = Permissions.TicketUpdate)]
    [ProducesResponseType(typeof(AttachmentDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [RequestSizeLimit(30_000_000)]
    public async Task<ActionResult<AttachmentDto>> UploadAttachment(Guid id, IFormFile file, CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        var attachment = await _ticketService.AddAttachmentAsync(id, file.FileName, file.ContentType, stream, file.Length, ActorUserId, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id }, attachment);
    }

    /// <summary>
    /// GET /api/tickets/{id}/attachments/{attachmentId}/download — streams the raw
    /// file bytes with the original content type and file name. Gated by
    /// TicketRead, same as every other read of ticket data.
    /// </summary>
    [HttpGet("{id:guid}/attachments/{attachmentId:guid}/download")]
    [Authorize(Policy = Permissions.TicketRead)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadAttachment(Guid id, Guid attachmentId, CancellationToken cancellationToken)
    {
        var download = await _ticketService.DownloadAttachmentAsync(id, attachmentId, cancellationToken);
        return download is null ? NotFound() : File(download.Content, download.ContentType, download.FileName);
    }

    /// <summary>
    /// DELETE /api/tickets/{id} — a genuine hard delete, gated by the narrow
    /// Permissions.TicketDelete policy (Manager/Admin only). See
    /// TicketService.DeleteAsync's doc comment for why this is distinct from
    /// the soft-cancel path (POST .../status with Cancelled).
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Permissions.TicketDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _ticketService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
