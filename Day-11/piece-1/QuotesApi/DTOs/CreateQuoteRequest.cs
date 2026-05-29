using System.ComponentModel.DataAnnotations;

namespace QuotesApi.DTOs;

public class CreateQuoteRequest
{
    [Required]
    [MaxLength(200)]
    public string Author { get; set; } = string.Empty;

    [Required]
    [MaxLength(1000)]
    public string Text { get; set; } = string.Empty;
}
