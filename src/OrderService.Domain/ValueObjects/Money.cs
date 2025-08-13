namespace OrderService.Domain.ValueObjects
{
    public class Money
    {
        public decimal Amount { get; private set; }
        public string  Currency { get; private set; } = "TRY";

        
        private Money() { }

        public Money(decimal amount, string currency = "TRY")
        {
            Amount = amount;
            Currency = currency;
        }

        public Money WithAmount(decimal newAmount) => new Money(newAmount, Currency);
        public override string ToString() => $"{Amount:0.00} {Currency}";
    }
}