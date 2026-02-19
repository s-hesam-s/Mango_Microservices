using Mango.Services.RewardAPI.Models.Dto;
using Mango.Services.RewardAPI.Services;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;

namespace Mango.Services.RewardAPI.Messaging
{
    public class RabbitMQOrderConsumer : BackgroundService
    {
        private readonly IConfiguration _configuration;
        private readonly RewardService _rewardService;

        private IConnection? _connection;
        private IChannel? _channel;

        private const string OrderCreated_RewardsUpdateQueue = "RewardsUpdateQueue";

        //string queueName = "";

        public RabbitMQOrderConsumer(
            IConfiguration configuration,
            RewardService rewardService)
        {
            _configuration = configuration;
            _rewardService = rewardService;
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

            string exchangeName = _configuration.GetValue<string>("TopicAndQueueNames:OrderCreatedTopic");

            //await _channel.ExchangeDeclareAsync(
            //    exchange: exchangeName,
            //    type: ExchangeType.Fanout,
            //    durable: false,
            //    autoDelete: false,
            //    arguments: null,
            //    cancellationToken: stoppingToken);

            await _channel.ExchangeDeclareAsync(
                exchange: exchangeName,
                type: ExchangeType.Direct,
                durable: false,
                autoDelete: false,
                arguments: null,
                cancellationToken: stoppingToken);

            ////Declare temporary queue (server-generated name)
            //var queueDeclareResult = await _channel.QueueDeclareAsync(
            //    queue: "",
            //    durable: false,
            //    exclusive: true,
            //    autoDelete: true,
            //    arguments: null,
            //    cancellationToken: stoppingToken);

            //queueName = queueDeclareResult.QueueName;

            await _channel.QueueDeclareAsync(
                queue: OrderCreated_RewardsUpdateQueue,
                durable: false,
                exclusive: false,
                autoDelete: false,
                arguments: null,
                cancellationToken: stoppingToken);

            //await _channel.QueueBindAsync(
            //        queue: queueName,
            //        exchange: exchangeName,
            //        routingKey: "",
            //        cancellationToken: stoppingToken);

            await _channel.QueueBindAsync(
                    queue: OrderCreated_RewardsUpdateQueue,
                    exchange: exchangeName,
                    routingKey: "RewardsUpdate",
                    cancellationToken: stoppingToken);

            var consumer = new AsyncEventingBasicConsumer(_channel);

            consumer.ReceivedAsync += async (sender, ea) =>
            {
                try
                {
                    var content = Encoding.UTF8.GetString(ea.Body.ToArray());
                    RewardsDto rewardsDto = JsonConvert.DeserializeObject<RewardsDto>(content);

                    if (rewardsDto is not null)
                    {
                        await HandleMessage(rewardsDto);
                    }

                    await _channel.BasicAckAsync(
                        deliveryTag: ea.DeliveryTag,
                        multiple: false,
                        cancellationToken: stoppingToken);
                }
                catch (Exception ex)
                {
                    // Optional: send to dead-letter or log error
                    await _channel.BasicNackAsync(
                        ea.DeliveryTag,
                        false,
                        true,
                        stoppingToken);
                }
            };

            //await _channel.BasicConsumeAsync(
            //    queue: queueName,
            //    autoAck: false,
            //    consumer: consumer,
            //    cancellationToken: stoppingToken);

            await _channel.BasicConsumeAsync(
                queue: OrderCreated_RewardsUpdateQueue,
                autoAck: false,
                consumer: consumer,
                cancellationToken: stoppingToken);

            await Task.Delay(Timeout.Infinite, stoppingToken);
        }

        private async Task HandleMessage(RewardsDto rewardsDto)
        {
            await _rewardService.UpdateRewards(rewardsDto);
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
