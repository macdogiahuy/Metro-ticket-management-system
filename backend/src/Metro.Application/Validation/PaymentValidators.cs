using FluentValidation;
using Metro.Application.Abstractions.Payment;

namespace Metro.Application.Validation;

public sealed class CreatePaymentRequestValidator : AbstractValidator<CreatePaymentRequest>
{
    public CreatePaymentRequestValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.IdempotencyKey)
            .MaximumLength(120)
            .When(x => x.IdempotencyKey is not null);
    }
}
