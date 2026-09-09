using Atlas.Application.Common.Exceptions;
using Atlas.Application.Common.Interfaces;
using Atlas.Application.Common.Models;
using Atlas.Application.Teams.Dtos;
using Atlas.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Atlas.Application.Teams.Services;

/// <summary>
/// Orchestrates team use cases — mirrors ProjectService's shape exactly.
/// Team.RemoveMember existed on the domain entity from the start; it's what
/// Project.RemoveMember was later added to match, closing out Del 7.
/// </summary>
public sealed class TeamService : ITeamService
{
    private readonly ITeamRepository _teamRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<TeamService> _logger;

    public TeamService(ITeamRepository teamRepository, IUnitOfWork unitOfWork, ILogger<TeamService> logger)
    {
        _teamRepository = teamRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<PagedResult<TeamDto>> SearchAsync(TeamListQuery query, CancellationToken cancellationToken)
    {
        var normalizedQuery = Normalize(query);
        var (items, totalCount) = await _teamRepository.SearchAsync(normalizedQuery, cancellationToken);

        var memberCounts = await _teamRepository.GetMemberCountsAsync(items.Select(t => t.Id).ToList(), cancellationToken);

        var dtos = items.Select(t => ToDto(t, memberCounts.GetValueOrDefault(t.Id))).ToList();
        return new PagedResult<TeamDto>(dtos, normalizedQuery.Page, normalizedQuery.PageSize, totalCount);
    }

    public async Task<TeamDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var team = await _teamRepository.GetByIdAsync(id, cancellationToken);
        if (team is null)
        {
            return null;
        }

        var memberDetails = await _teamRepository.GetMemberDetailsAsync(id, cancellationToken);
        return ToDetailDto(team, memberDetails);
    }

    public async Task<TeamDetailDto> CreateAsync(CreateTeamRequest request, CancellationToken cancellationToken)
    {
        var team = Team.Create(request.OrganizationId, request.Name);

        await _teamRepository.AddAsync(team, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Team {TeamId} '{Name}' created for organization {OrganizationId}", team.Id, team.Name, team.OrganizationId);

        return ToDetailDto(team, []);
    }

    public async Task<TeamDetailDto> AddMemberAsync(Guid id, AddTeamMemberRequest request, CancellationToken cancellationToken)
    {
        var team = await GetTeamOrThrowAsync(id, cancellationToken);

        team.AddMember(request.UserId);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("User {UserId} added to team {TeamId}", request.UserId, id);
        return ToDetailDto(team, await _teamRepository.GetMemberDetailsAsync(id, cancellationToken));
    }

    public async Task<TeamDetailDto> RemoveMemberAsync(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        var team = await GetTeamOrThrowAsync(id, cancellationToken);

        // Team.RemoveMember is a deliberate no-op if userId isn't a member —
        // see the doc comment on TeamTests.RemoveMember_UnknownUser_IsANoOp
        // for why that's the right call here.
        team.RemoveMember(userId);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("User {UserId} removed from team {TeamId}", userId, id);
        return ToDetailDto(team, await _teamRepository.GetMemberDetailsAsync(id, cancellationToken));
    }

    private async Task<Team> GetTeamOrThrowAsync(Guid id, CancellationToken cancellationToken)
    {
        var team = await _teamRepository.GetByIdAsync(id, cancellationToken);
        if (team is null)
        {
            throw new NotFoundException(nameof(Team), id);
        }

        return team;
    }

    private static TeamListQuery Normalize(TeamListQuery query)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize switch
        {
            < 1 => 25,
            > TeamListQuery.MaxPageSize => TeamListQuery.MaxPageSize,
            _ => query.PageSize
        };

        return new TeamListQuery
        {
            OrganizationId = query.OrganizationId,
            Search = query.Search,
            Page = page,
            PageSize = pageSize
        };
    }

    private static TeamDto ToDto(Team team, int memberCount) => new(
        team.Id,
        team.OrganizationId,
        team.Name,
        memberCount,
        team.CreatedAtUtc);

    private static TeamDetailDto ToDetailDto(Team team, IReadOnlyList<(Guid UserId, string FullName, DateTime JoinedAtUtc)> members) => new(
        team.Id,
        team.OrganizationId,
        team.Name,
        members.Select(m => new TeamMemberDto(m.UserId, m.FullName, m.JoinedAtUtc)).ToList(),
        team.CreatedAtUtc,
        team.ModifiedAtUtc);
}
