using ClassicUO.Configuration;
using FluentAssertions;
using Xunit;

namespace ClassicUO.UnitTests.Configuration
{
    public class SettingsNormalizationTests
    {
        [Fact]
        public void NormalizeAndValidate_Should_Default_Invalid_Ports()
        {
            var settings = new Settings
            {
                Port = 0,
                UpdatePort = 0
            };

            settings.NormalizeAndValidate();

            settings.Port.Should().Be(2593);
            settings.UpdatePort.Should().Be(443);
        }

        [Fact]
        public void NormalizeAndValidate_Should_Derive_UpdateHost_And_UpdatePort_From_UpdateUrl()
        {
            var settings = new Settings
            {
                UpdateUrl = "https://patch.example.com:8443/files/manifest.json"
            };

            settings.NormalizeAndValidate();

            settings.UpdateHost.Should().Be("patch.example.com");
            settings.UpdatePort.Should().Be(8443);
        }

        [Fact]
        public void NormalizeAndValidate_Should_Build_UpdateUrl_When_Only_Host_And_Port_Are_Set()
        {
            var settings = new Settings
            {
                UpdateHost = "downloads.example.com",
                UpdatePort = 8080
            };

            settings.NormalizeAndValidate();

            settings.UpdateUrl.Should().Be("https://downloads.example.com:8080/");
        }

        [Fact]
        public void NormalizeAndValidate_Should_Trim_Profile_Fields()
        {
            var settings = new Settings
            {
                Username = "  tester  ",
                IP = "  127.0.0.1  ",
                ServerName = "  Shard One  ",
                UpdateUrl = "  https://patch.example.com/manifest.json  ",
                UpdatePublicKey = "  aabbcc  "
            };

            settings.NormalizeAndValidate();

            settings.Username.Should().Be("tester");
            settings.IP.Should().Be("127.0.0.1");
            settings.ServerName.Should().Be("Shard One");
            settings.UpdateUrl.Should().Be("https://patch.example.com/manifest.json");
            settings.UpdatePublicKey.Should().Be("aabbcc");
        }
    }
}
