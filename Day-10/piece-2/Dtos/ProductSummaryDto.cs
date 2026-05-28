namespace QueryTranslationDemo.Dtos;

// Only the columns a read-only list view actually needs.
// Projecting to this type tells EF to emit SELECT ProductId, Name, Category
// instead of SELECT *, keeping the result set narrow and the index options open.
public sealed class ProductSummaryDto
{
    public int    ProductId { get; init; }
    public string Name      { get; init; } = "";
    public string Category  { get; init; } = "";
}
