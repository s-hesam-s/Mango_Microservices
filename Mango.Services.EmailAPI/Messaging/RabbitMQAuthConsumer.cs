using Mango.Services.EmailAPI.Services;
using Newtonsoft.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;

namespace Mango.Services.EmailAPI.Messaging
{
    public class RabbitMQAuthConsumer : BackgroundService
    {
        private readonly IConfiguration _configuration;
        private readonly EmailService _emailService;

        private IConnection? _connection;
        private IChannel? _channel;

        public RabbitMQAuthConsumer(
            IConfiguration configuration,
            EmailService emailService)
        {
            _configuration = configuration;
            _emailService = emailService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var factory = new ConnectionFactory
            {
                HostName = "localhost",
                UserName = "guest",
                Password = "guest"
            };

            _connection = await factory.CreateConnectionAsync(stoppingToken);
            _channel = await _connection.CreateChannelAsync(
                                options: null,
                                cancellationToken: stoppingToken);

            var queueName = _configuration.GetValue<string>(
                "TopicAndQueueNames:RegisterUserQueue");

            await _channel.QueueDeclareAsync(
                queue: queueName,
                durable: false,
                exclusive: false,
                autoDelete: false,
                arguments: null,
                cancellationToken: stoppingToken);

            var consumer = new AsyncEventingBasicConsumer(_channel);

            consumer.ReceivedAsync += async (sender, ea) =>
            {
                var content = Encoding.UTF8.GetString(ea.Body.ToArray());
                var email = JsonConvert.DeserializeObject<string>(content);

                if (!string.IsNullOrEmpty(email))
                {
                    await HandleMessage(email);
                }

                await _channel.BasicAckAsync(
                    deliveryTag: ea.DeliveryTag,
                    multiple: false,
                    cancellationToken: stoppingToken);
            };

            await _channel.BasicConsumeAsync(
                queue: queueName,
                autoAck: false,
                consumer: consumer,
                cancellationToken: stoppingToken);

            await Task.Delay(Timeout.Infinite, stoppingToken);
        }

        private async Task HandleMessage(string email)
        {
            // Your email logic here
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_channel != null)
                await _channel.CloseAsync(cancellationToken);

            if (_connection != null)
                await _connection.CloseAsync(cancellationToken);

            await base.StopAsync(cancellationToken);
        }
    }
}
