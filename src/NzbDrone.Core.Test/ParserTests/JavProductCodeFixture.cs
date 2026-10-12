using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ParserTests
{
    [TestFixture]
    public class JavProductCodeFixture : CoreTest
    {
        [TestCase("DBIF-001.1080p.mp4", "DBIF-001")]
        [TestCase("DBIF-001 - Sample Title (2020) [WEBDL-2160p].mkv", "DBIF-001")]
        [TestCase("H113SL-013AI - Sample Title (2020) [WEBDL-1080p].mp4", "H113SL-013AI")]
        [TestCase("[Tracker] H113SL-013AI.1080p.mp4", "H113SL-013AI")]
        [TestCase("FC2-PPV-1234567 - Sample Title (2020).mp4", "FC2-PPV-1234567")]
        public void should_preserve_product_code_before_titles_years_and_quality(string title, string code)
        {
            var parsed = Parser.Parser.ParseMovieTitle(title, true);
            parsed.Should().NotBeNull();
            parsed.Code.Should().Be(code);
        }
    }
}
