using System.ComponentModel.DataAnnotations;

namespace PitakaApp.Api.Options;

public class EmailOption
{
    public const string SectionName = "Email";

    // The SMTP host the app sends through. In the Docker loop this is the
    // smtp4dev service name; in the SDK loop it is whatever the developer
    // points at. Validated on start so a bad host fails on boot, not on the
    // first person who forgets their password.
    [Required(ErrorMessage = "Email:Host must be set — the SMTP host the API sends through.")]
    public string Host { get; set; } = string.Empty;

    [Range(1, 65535, ErrorMessage = "Email:Port must be a TCP port between 1 and 65535.")]
    public int Port { get; set; }

    [Required(ErrorMessage = "Email:FromAddress must be set."), EmailAddress]
    public string FromAddress { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email:FromName must be set.")]
    public string FromName { get; set; } = string.Empty;
}
