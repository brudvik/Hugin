using FluentAssertions;
using Hugin.Protocol;
using Xunit;

namespace Hugin.Protocol.Tests;

/// <summary>
/// Tests for the CapabilityManager class.
/// </summary>
public class CapabilityManagerTests
{
    #region Capability Class Tests

    [Fact]
    public void CapabilityConstructorSetsPropertiesCorrectly()
    {
        // Act
        var cap = new Capability("test-cap", "value1,value2", requiresAck: true);

        // Assert
        cap.Name.Should().Be("test-cap");
        cap.Value.Should().Be("value1,value2");
        cap.RequiresAck.Should().BeTrue();
    }

    [Fact]
    public void CapabilityToStringWithValueReturnsNameEqualsValue()
    {
        // Arrange
        var cap = new Capability("sasl", "PLAIN,EXTERNAL");

        // Act
        var result = cap.ToString();

        // Assert
        result.Should().Be("sasl=PLAIN,EXTERNAL");
    }

    [Fact]
    public void CapabilityToStringWithoutValueReturnsName()
    {
        // Arrange
        var cap = new Capability("multi-prefix");

        // Act
        var result = cap.ToString();

        // Assert
        result.Should().Be("multi-prefix");
    }

    #endregion

    #region CapabilityManager Tests

    [Fact]
    public void InitialStateHasNoEnabledCapabilities()
    {
        // Arrange
        var manager = new CapabilityManager();

        // Assert
        manager.EnabledCapabilities.Should().BeEmpty();
        manager.IsNegotiating.Should().BeFalse();
    }

    [Fact]
    public void EnableAddsCapabilityToEnabled()
    {
        // Arrange
        var manager = new CapabilityManager();

        // Act
        var result = manager.Enable("multi-prefix");

        // Assert
        result.Should().BeTrue();
        manager.IsEnabled("multi-prefix").Should().BeTrue();
        manager.EnabledCapabilities.Should().Contain("multi-prefix");
    }

    [Fact]
    public void EnableReturnsFalseForUnsupportedCapability()
    {
        // Arrange
        var manager = new CapabilityManager();

        // Act
        var result = manager.Enable("unsupported-cap");

        // Assert
        result.Should().BeFalse();
        manager.EnabledCapabilities.Should().BeEmpty();
    }

    [Fact]
    public void DisableRemovesCapabilityFromEnabled()
    {
        // Arrange
        var manager = new CapabilityManager();
        manager.Enable("multi-prefix");

        // Act
        var result = manager.Disable("multi-prefix");

        // Assert
        result.Should().BeTrue();
        manager.IsEnabled("multi-prefix").Should().BeFalse();
    }

    [Fact]
    public void DisableReturnsFalseForNotEnabledCapability()
    {
        // Arrange
        var manager = new CapabilityManager();

        // Act
        var result = manager.Disable("multi-prefix");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsEnabledReturnsTrueForEnabledCapability()
    {
        // Arrange
        var manager = new CapabilityManager();
        manager.Enable("server-time");

        // Assert
        manager.IsEnabled("server-time").Should().BeTrue();
    }

    [Fact]
    public void IsEnabledReturnsFalseForDisabledCapability()
    {
        // Arrange
        var manager = new CapabilityManager();

        // Assert
        manager.IsEnabled("server-time").Should().BeFalse();
    }

    [Fact]
    public void ClearRemovesAllEnabledCapabilities()
    {
        // Arrange
        var manager = new CapabilityManager();
        manager.Enable("multi-prefix");
        manager.Enable("server-time");
        manager.Enable("away-notify");

        // Act
        manager.Clear();

        // Assert
        manager.EnabledCapabilities.Should().BeEmpty();
    }

    [Fact]
    public void IsSupportedReturnsTrueForKnownCapabilities()
    {
        // Assert
        CapabilityManager.IsSupported("multi-prefix").Should().BeTrue();
        CapabilityManager.IsSupported("sasl").Should().BeTrue();
        CapabilityManager.IsSupported("server-time").Should().BeTrue();
        CapabilityManager.IsSupported("batch").Should().BeTrue();
    }

    [Fact]
    public void IsSupportedReturnsFalseForUnknownCapabilities()
    {
        // Assert
        CapabilityManager.IsSupported("unknown-cap").Should().BeFalse();
        CapabilityManager.IsSupported("not-a-capability").Should().BeFalse();
    }

    [Fact]
    public void GetCapabilityListReturnsAllCapabilities()
    {
        // Act
        var list = CapabilityManager.GetCapabilityList();

        // Assert
        list.Should().Contain("multi-prefix");
        list.Should().Contain("sasl=");
        list.Should().Contain("server-time");
        list.Should().Contain("batch");
    }

    [Fact]
    public void GetCapabilityListWithoutValuesReturnsOnlyNames()
    {
        // Act
        var list = CapabilityManager.GetCapabilityList(includeValues: false);

        // Assert
        list.Should().Contain("multi-prefix");
        list.Should().Contain("sasl");
        list.Should().NotContain("=");
    }

    #endregion

    #region Convenience Property Tests

    [Fact]
    public void HasMultiPrefixReflectsEnabledState()
    {
        // Arrange
        var manager = new CapabilityManager();

        // Assert - initially false
        manager.HasMultiPrefix.Should().BeFalse();

        // Act - enable
        manager.Enable("multi-prefix");

        // Assert - now true
        manager.HasMultiPrefix.Should().BeTrue();
    }

    [Fact]
    public void HasAwayNotifyReflectsEnabledState()
    {
        // Arrange
        var manager = new CapabilityManager();
        manager.Enable("away-notify");

        // Assert
        manager.HasAwayNotify.Should().BeTrue();
    }

    [Fact]
    public void HasExtendedJoinReflectsEnabledState()
    {
        // Arrange
        var manager = new CapabilityManager();
        manager.Enable("extended-join");

        // Assert
        manager.HasExtendedJoin.Should().BeTrue();
    }

    [Fact]
    public void HasServerTimeReflectsEnabledState()
    {
        // Arrange
        var manager = new CapabilityManager();
        manager.Enable("server-time");

        // Assert
        manager.HasServerTime.Should().BeTrue();
    }

    [Fact]
    public void HasBatchReflectsEnabledState()
    {
        // Arrange
        var manager = new CapabilityManager();
        manager.Enable("batch");

        // Assert
        manager.HasBatch.Should().BeTrue();
    }

    [Fact]
    public void HasEchoMessageReflectsEnabledState()
    {
        // Arrange
        var manager = new CapabilityManager();
        manager.Enable("echo-message");

        // Assert
        manager.HasEchoMessage.Should().BeTrue();
    }

    [Fact]
    public void HasChatHistoryReflectsEnabledState()
    {
        // Arrange
        var manager = new CapabilityManager();
        manager.Enable("draft/chathistory");

        // Assert
        manager.HasChatHistory.Should().BeTrue();
    }

    #endregion

    #region Supported Capabilities Tests

    [Fact]
    public void SupportedCapabilitiesIncludesSaslWithMechanisms()
    {
        // Arrange
        var saslCap = CapabilityManager.SupportedCapabilities["sasl"];

        // Assert
        saslCap.Name.Should().Be("sasl");
        saslCap.Value.Should().Contain("PLAIN");
        saslCap.Value.Should().Contain("EXTERNAL");
    }

    [Fact]
    public void SupportedCapabilitiesIncludesDraftSpecs()
    {
        // Assert
        CapabilityManager.SupportedCapabilities.Should().ContainKey("draft/chathistory");
        CapabilityManager.SupportedCapabilities.Should().ContainKey("draft/event-playback");
    }

    [Fact]
    public void SupportedCapabilitiesAreNotCaseSensitive()
    {
        // Assert
        CapabilityManager.SupportedCapabilities.ContainsKey("MULTI-PREFIX").Should().BeTrue();
        CapabilityManager.SupportedCapabilities.ContainsKey("Multi-Prefix").Should().BeTrue();
        CapabilityManager.SupportedCapabilities.ContainsKey("multi-prefix").Should().BeTrue();
    }

    #endregion

    #region Negotiation State Tests

    [Fact]
    public void IsNegotiatingCanBeSetAndRead()
    {
        // Arrange
        var manager = new CapabilityManager();

        // Act
        manager.IsNegotiating = true;

        // Assert
        manager.IsNegotiating.Should().BeTrue();
    }

    #endregion
}
