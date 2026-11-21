namespace Yasda.Modules.Knowledge.Domain;

internal readonly record struct ArticleId(long Value);

// Owned by the Identity module, referenced by id only.
internal readonly record struct UserId(long Value);

// A person as an article remembers them: Identity's number for them, and a copy of their name, so a screen can say who did
// something without asking Identity.
internal readonly record struct Person(UserId Id, string Name);

// What the handler found out about the person acting, for the rules that depend on other modules' data. Everything else the
// article knows itself (its author, its edit list). IsEditor: a KbEditor or an Admin. LeadsAuthorsTeam: a team lead in a
// team the author belongs to.
internal readonly record struct ArticleAccess(bool IsEditor, bool LeadsAuthorsTeam);
