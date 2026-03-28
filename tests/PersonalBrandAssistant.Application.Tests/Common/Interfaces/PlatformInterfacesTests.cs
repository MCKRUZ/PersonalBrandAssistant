using PersonalBrandAssistant.Application.Common.Interfaces;

namespace PersonalBrandAssistant.Application.Tests.Common.Interfaces;

public class PlatformInterfacesTests
{
    [Fact]
    public void ISocialPlatform_DefinesRequiredMembers()
    {
        var type = typeof(ISocialPlatform);

        Assert.NotNull(type.GetProperty("Type"));
        Assert.NotNull(type.GetMethod("PublishAsync"));
        Assert.NotNull(type.GetMethod("DeletePostAsync"));
        Assert.NotNull(type.GetMethod("GetEngagementAsync"));
        Assert.NotNull(type.GetMethod("GetProfileAsync"));
        Assert.NotNull(type.GetMethod("ValidateContentAsync"));
    }

    [Fact]
    public void IOAuthManager_DefinesOAuthLifecycleMethods()
    {
        var type = typeof(IOAuthManager);

        Assert.NotNull(type.GetMethod("GenerateAuthUrlAsync"));
        Assert.NotNull(type.GetMethod("ExchangeCodeAsync"));
        Assert.NotNull(type.GetMethod("RefreshTokenAsync"));
        Assert.NotNull(type.GetMethod("RevokeTokenAsync"));
    }

    [Fact]
    public void IRateLimiter_DefinesRateLimitMethods()
    {
        var type = typeof(IRateLimiter);

        Assert.NotNull(type.GetMethod("CanMakeRequestAsync"));
        Assert.NotNull(type.GetMethod("RecordRequestAsync"));
        Assert.NotNull(type.GetMethod("GetStatusAsync"));
    }

    [Fact]
    public void IMediaStorage_DefinesStorageMethods()
    {
        var type = typeof(IMediaStorage);

        Assert.NotNull(type.GetMethod("SaveAsync"));
        Assert.NotNull(type.GetMethod("GetStreamAsync"));
        Assert.NotNull(type.GetMethod("GetPathAsync"));
        Assert.NotNull(type.GetMethod("DeleteAsync"));
        Assert.NotNull(type.GetMethod("GetSignedUrlAsync"));
    }

    [Fact]
    public void IPlatformContentFormatter_DefinesPlatformAndFormatAndValidate()
    {
        var type = typeof(IPlatformContentFormatter);

        Assert.NotNull(type.GetProperty("Platform"));
        Assert.NotNull(type.GetMethod("FormatAndValidate"));
    }
}
