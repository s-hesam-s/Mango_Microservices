namespace Mango.Services.OrderAPI.RabbmitMQSender
{
    public interface IRabbitMQOrderMessageSender
    {
        Task SendMessageAsync(Object message, string exchangeName);
    }
}
