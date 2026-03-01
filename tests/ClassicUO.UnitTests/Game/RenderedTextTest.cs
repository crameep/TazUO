using ClassicUO.Game;
using FluentAssertions;
using Xunit;

namespace ClassicUO.UnitTests.Game;

public class RenderedTextTest
{
    [Fact]
    public void Create_ShouldPersistHtmlSettingsPerInstance()
    {
        RenderedText text = RenderedText.Create(
            "test",
            isHTML: true,
            htmlColor: 0x123456FF,
            hasBackgroundColor: true
        );

        text.IsHTML.Should().BeTrue();
        text.HTMLColor.Should().Be(0x123456FF);
        text.HasBackgroundColor.Should().BeTrue();

        text.Destroy();
    }
}
