using System.ComponentModel.DataAnnotations;

namespace PitakaApp.Api.Attributes;

public class RequiresUtcOffset : ValidationAttribute
{
    public override bool IsValid(object? value)
    {
        if (value == null) return true;
        
        if (value is DateTime dt)
        {
            return dt.Kind != DateTimeKind.Unspecified;
        }

        return false;
    }

    public override string FormatErrorMessage(string name)
    {
        return $"{name} must include a timezone offset (e.g. 'Z' or '+08:00')";
    }
}