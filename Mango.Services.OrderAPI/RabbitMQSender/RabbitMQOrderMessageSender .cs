using Mango.Services.OrderAPI.RabbmitMQSender;
using Newtonsoft.Json;
using RabbitMQ.Client;
using System.Text;
using System.Threading.Channels;

namespace Mango.Services.OrderAPI.RabbitMQSender
{
    public class RabbitMQOrderMessageSender : IRabbitMQOrderMessageSender
    {
        private readonly string _hostName;
        private readonly string _username;
        private readonly string _password;

        private IConnection? _connection;

        private const string OrderCreated_RewardsUpdateQueue = "RewardsUpdateQueue";
        private const string OrderCreated_EmailUpdateQueue = "EmailUpdateQueue";

        public RabbitMQOrderMessageSender()
        {
            _hostName = "localhost";
            _username = "guest";
            _password = "guest";
        }

        public async Task SendMessageAsync(object message, string exchangeName)
        {
            await EnsureConnectionAsync();

            await using var channel = await _connection!.CreateChannelAsync();

            //await channel.ExchangeDeclareAsync(
            //    exchange: exchangeName,
            //    type: ExchangeType.Fanout,
            //    durable: false,
            //    autoDelete: false,
            //    arguments: null);

            await channel.ExchangeDeclareAsync(
                exchange: exchangeName,
                type: ExchangeType.Direct,
                durable: false,
                autoDelete: false,
                arguments: null);

            await channel.QueueDeclareAsync(
                queue: OrderCreated_EmailUpdateQueue,
                durable: false,
                exclusive: false,
                autoDelete: false,
                arguments: null);

            await channel.QueueDeclareAsync(
                queue: OrderCreated_RewardsUpdateQueue,
                durable: false,
                exclusive: false,
                autoDelete: false,
                arguments: null);

            await channel.QueueBindAsync(
                    queue: OrderCreated_EmailUpdateQueue,
                    exchange: exchangeName,
                    routingKey: "EmailUpdate");

            await channel.QueueBindAsync(
                    queue: OrderCreated_RewardsUpdateQueue,
                    exchange: exchangeName,
                    routingKey: "RewardsUpdate");

            var json = JsonConvert.SerializeObject(message);
            var body = Encoding.UTF8.GetBytes(json);

            //await channel.BasicPublishAsync(
            //    exchange: exchangeName,
            //    routingKey: "",
            //    body: body);

            await channel.BasicPublishAsync(
                exchange: exchangeName,
                routingKey: "EmailUpdate",
                body: body);

            await channel.BasicPublishAsync(
                exchange: exchangeName,
                routingKey: "RewardsUpdate",
                body: body);
        }

        private async Task EnsureConnectionAsync()
        {
            if (_connection != null && _connection.IsOpen)
                return;

            var factory = new ConnectionFactory
            {
                HostName = _hostName,
                UserName = _username,
                Password = _password
            };

            _connection = await factory.CreateConnectionAsync();
        }
    }
}
