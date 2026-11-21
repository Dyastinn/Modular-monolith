using Yasda.Modules.Identity.Contracts;
using Yasda.Modules.Knowledge.Domain;
using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Knowledge.Features;

// The person making this request, as an article needs to know them: their name (copied onto what they change) and which of the
// access facts that come from other modules are true of them. Several handlers need the same two answers.
internal sealed class ActingUser(ICurrentUser currentUser, IUserDirectory users, ITeamDirectory teams)
{
    public UserId Id => new(currentUser.UserId);

    public async Task<Person> PersonAsync(CancellationToken ct)
    {
        var name = await users.NameOfAsync(currentUser.UserId, ct) ?? currentUser.Username;
        return new Person(Id, name);
    }

    // Only a team lead can lead the author's team, so only a team lead costs Identity a question.
    public async Task<ArticleAccess> AccessAsync(UserId author, CancellationToken ct)
    {
        var isEditor = KnowledgeRoles.IsEditor(currentUser.Role);
        var leadsAuthorsTeam = false;
        if (currentUser.Role == Roles.TeamLead)
            leadsAuthorsTeam = await teams.LeadsTeamOfAsync(currentUser.UserId, author.Value, ct);
        return new ArticleAccess(isEditor, leadsAuthorsTeam);
    }
}
