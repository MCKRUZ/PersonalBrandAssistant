using FluentValidation.TestHelper;
using PBA.Application.Features.Content.Dtos;
using PBA.Application.Features.Content.Validators;
using Xunit;

namespace PBA.Application.Tests.Features.Content.Validators;

public class ScheduleContentRequestValidatorTests
{
    private readonly ScheduleContentRequestValidator _validator = new();

    [Fact]
    public void Validate_PastDate_HasError()
    {
        var request = new ScheduleContentRequest
        {
            ScheduledAt = DateTimeOffset.UtcNow.AddMinutes(-5)
        };
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.ScheduledAt);
    }

    [Fact]
    public void Validate_FutureDate_NoErrors()
    {
        var request = new ScheduleContentRequest
        {
            ScheduledAt = DateTimeOffset.UtcNow.AddHours(1)
        };
        var result = _validator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }
}
