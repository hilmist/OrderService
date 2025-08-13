using FluentValidation;

namespace OrderService.Application.Commands.CreateOrder;

public sealed class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderCommandValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        
        // Items var mı? boş mu? üst sınır 20
        RuleFor(x => x.Items)
            .NotEmpty().WithMessage("At least one item is required.")
            .Must(items => items.Count() <= 20)
            .WithMessage("Maximum items per order is 20.");
        
        // Her bir item kuralı
        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ProductId).NotEmpty();
            item.RuleFor(i => i.Quantity).GreaterThan(0);
            item.RuleFor(i => i.UnitPrice).GreaterThan(0m);
        });
        // Toplam tutar 100–50000 TL aralığında olmalı
        RuleFor(x => x)
            .Must(cmd =>
            {
                decimal total = cmd.Items.Sum(i => i.UnitPrice * i.Quantity);
                return total >= 100m && total <= 50_000m;
            })
            .WithMessage("Order total must be between 100 TL and 50,000 TL.");
    }
}