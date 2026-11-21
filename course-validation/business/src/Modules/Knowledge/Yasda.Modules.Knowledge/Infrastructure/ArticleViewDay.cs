using Yasda.Modules.Knowledge.Domain;

namespace Yasda.Modules.Knowledge.Infrastructure;

// How many times an article was opened on one day. Only a count: no person is stored, so there is nothing personal to keep or
// delete. It has no rules of its own, which is why it lives here and not in the domain; it is added to with one atomic
// statement (see RecordArticleViewHandler).
internal sealed class ArticleViewDay
{
    public ArticleId ArticleId { get; set; }
    public DateOnly Day { get; set; }
    public int Views { get; set; }
}
