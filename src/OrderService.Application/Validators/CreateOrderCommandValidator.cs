using FluentValidation;
using OrderService.Application.Commands.CreateOrder;

namespace OrderService.Application.Validators;

public class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderCommandValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.Items).NotEmpty();
        RuleFor(x => x.Items.Count()).LessThanOrEqualTo(20).WithMessage("Max 20 items");

        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.Quantity).GreaterThan(0);
            item.RuleFor(i => i.UnitPrice).GreaterThan(0);
        });

        RuleFor(x => x)
            .Must(x =>
            {
                var total = x.Items.Sum(i => i.UnitPrice * i.Quantity);
                return total >= 100m && total <= 50_000m;
            })
            .WithMessage("Order total must be between 100 and 50,000 TL");
    }
}