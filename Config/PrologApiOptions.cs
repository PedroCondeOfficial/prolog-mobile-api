using System.ComponentModel.DataAnnotations;

namespace prolog_mobile_api.Config;

public class PrologApiOptions
{
    public const string SectionName = "PrologApi";

    [Required, Url]
    public string BaseUrl { get; set; } = string.Empty;

    [Required]
    public string ApiKey { get; set; } = string.Empty;
}
