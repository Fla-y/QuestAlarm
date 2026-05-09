using Microsoft.VisualStudio.TestTools.UnitTesting;
using QuestAlarm.Desktop.Services;
using Serilog.Core;

namespace QuestAlarm.Desktop.Tests;

[TestClass]
public sealed class ChallengeActivityServiceTests
{
    [TestMethod]
    public void MarkActivityStoresLatestActivityForSession()
    {
        var service = new ChallengeActivityService(Logger.None);
        var sessionId = Guid.NewGuid();

        var first = service.MarkActivity(sessionId, "first");
        var second = service.MarkActivity(sessionId, "second");
        var stored = service.GetActivity(sessionId);

        Assert.IsNotNull(stored);
        Assert.AreEqual(sessionId, stored.SessionId);
        Assert.AreEqual("second", stored.Source);
        Assert.IsTrue(second.LastActivityUtc >= first.LastActivityUtc);
        Assert.AreEqual(second.LastActivityUtc, stored.LastActivityUtc);
    }

    [TestMethod]
    public void ClearActivityRemovesStoredActivity()
    {
        var service = new ChallengeActivityService(Logger.None);
        var sessionId = Guid.NewGuid();

        service.MarkActivity(sessionId, "test");
        service.ClearActivity(sessionId);

        Assert.IsNull(service.GetActivity(sessionId));
    }

    [TestMethod]
    public void BlankActivitySourceIsNormalizedToUnknown()
    {
        var service = new ChallengeActivityService(Logger.None);

        var snapshot = service.MarkActivity(Guid.NewGuid(), " ");

        Assert.AreEqual("unknown", snapshot.Source);
    }
}
