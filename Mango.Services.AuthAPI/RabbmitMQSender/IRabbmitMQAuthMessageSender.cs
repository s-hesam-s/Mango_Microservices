namespace Mango.Services.AuthAPI.RabbmitMQSender
{
    public interface IRabbmitMQAuthMessageSender
    {
        Task SendMessageAsync(Object message, string queueName);
    }
}
