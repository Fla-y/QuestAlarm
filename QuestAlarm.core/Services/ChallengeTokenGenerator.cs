using System.Security.Cryptography;

namespace QuestAlarm.Core.Services;

public static class ChallengeTokenGenerator
{
    public static string CreateToken()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    }
}
