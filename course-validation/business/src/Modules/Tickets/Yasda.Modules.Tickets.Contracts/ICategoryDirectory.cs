namespace Yasda.Modules.Tickets.Contracts;

// What other modules may ask about categories. Categories belong to Tickets; Sla keeps only a category's number.
public interface ICategoryDirectory
{
    // Is there a category with this number? (Switched-off categories still exist.)
    Task<bool> ExistsAsync(long categoryId, CancellationToken ct);
}
