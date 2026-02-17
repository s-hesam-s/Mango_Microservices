namespace Mango.Services.AuthAPI.RabbmitMQSender
{
    public interface IRabbitMQAuthMessageSender
    {
        Task SendMessageAsync(Object message, string queueName);
    }
}
