using System.ComponentModel.DataAnnotations;

namespace QuotesApi.DTOs;

public class CreateQuoteRequest
{
    [Required]
    [MinLength(2)]
    public string Author { get; set; } = string.Empty;

    [Required]
    [MinLength(5)]
    public string Text { get; set; } = string.Empty;
}