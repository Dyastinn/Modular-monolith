namespace Erp.Shared.BuildingBlocks;

// The body of a 201 Created: the new thing's id. Its address is in the Location header.
public sealed record CreatedId(long Id);
