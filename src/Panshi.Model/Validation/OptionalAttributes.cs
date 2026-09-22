using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace Panshi.Model.Validation;

/// <summary>选填手机号：null/空串放行（红线 #15），严格国内 11 位正则（红线 #16）。</summary>
public sealed class OptionalPhoneAttribute : ValidationAttribute
{
    private static readonly Regex Phone = new(
        @"^1(?:3\d|4[0145689]|5[0-35-9]|6[2567]|7[0-8]|8\d|9[0-35-9])\d{8}$",
        RegexOptions.Compiled);

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        => value is null or "" ? ValidationResult.Success!
            : Phone.IsMatch(value.ToString()!) ? ValidationResult.Success!
            : new ValidationResult($"{validationContext.DisplayName} 不是有效的手机号");
}

/// <summary>选填邮箱：null/空串放行（红线 #15）。</summary>
public sealed class OptionalEmailAddressAttribute : ValidationAttribute
{
    private static readonly Regex Email = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        => value is null or "" ? ValidationResult.Success!
            : Email.IsMatch(value.ToString()!) ? ValidationResult.Success!
            : new ValidationResult($"{validationContext.DisplayName} 不是有效的邮箱地址");
}
