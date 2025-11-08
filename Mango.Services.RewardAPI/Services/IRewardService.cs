
using Mango.Services.RewardAPI.Models.Dto;

namespace Mango.Services.RewardAPI.Services
{
    public interface IRewardService
    {
        Task UpdateRewards(RewardsDto rewardsDto);
    }
}
