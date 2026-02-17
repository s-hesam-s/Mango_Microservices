namespace Mango.Services.ShoppingCartAPI.RabbmitMQSender
{
    public interface IRabbitMQCartMessageSender
    {
        Task SendMessageAsync(Object message, string queueName);
    }
}
