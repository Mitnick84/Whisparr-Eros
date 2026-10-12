using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Validation.Paths;
using Whisparr.Api.V3.Config;

namespace NzbDrone.Api.Test.v3.Config
{
    [TestFixture]
    public class HostConfigJavFixture
    {
        private Mock<IConfigService> _configService;
        private HostConfigController _controller;

        [SetUp]
        public void Setup()
        {
            _configService = new Mock<IConfigService>();
            _configService.SetupGet(c => c.ThePornDbApiKey).Returns("synthetic-test-key");
            _controller = new HostConfigController(new Mock<IConfigFileProvider>().Object,
                _configService.Object,
                new Mock<IUserService>().Object,
                new FileExistsValidator<HostConfigResource>(new Mock<IDiskProvider>().Object));
        }

        [TestCase(null, "")]
        [TestCase("", "")]
        [TestCase("synthetic-test-key", "********")]
        public void should_hide_the_key_in_general_settings(string key, string expected)
        {
            _configService.SetupGet(c => c.ThePornDbApiKey).Returns(key);
            _controller.GetHostConfig().ThePornDbApiKey.Should().Be(expected);
        }

        [TestCase("********", "synthetic-test-key")]
        [TestCase("replacement-test-key", "replacement-test-key")]
        [TestCase("", "")]
        public void should_preserve_replace_or_clear_the_key(string value, string expected)
        {
            _controller.SaveHostConfig(new HostConfigResource { Id = 1, TrustedNetworks = "", ThePornDbApiKey = value });
            _configService.Verify(c => c.SaveConfigDictionary(It.Is<Dictionary<string, object>>(d =>
                (string)d[nameof(HostConfigResource.ThePornDbApiKey)] == expected)));
        }

        [Test]
        public void should_serialize_the_jav_source_with_the_frontend_option_key()
        {
            STJson.ToJson(MovieMetadataType.TPDBJAV).Should().Be("\"tpdbjav\"");
            STJson.Deserialize<HostConfigResource>("{\"whisparrMovieMetadataSource\":\"tpdbjav\"}")
                .WhisparrMovieMetadataSource.Should().Be(MovieMetadataType.TPDBJAV);
        }

        [Test]
        public void should_expose_and_save_the_optional_combined_search_setting()
        {
            _configService.SetupGet(c => c.ThePornDbIncludeJav).Returns(true);
            _controller.GetHostConfig().ThePornDbIncludeJav.Should().BeTrue();
            _controller.SaveHostConfig(new HostConfigResource
            {
                Id = 1,
                TrustedNetworks = "",
                ThePornDbApiKey = "********",
                ThePornDbIncludeJav = true
            });
            _configService.Verify(c => c.SaveConfigDictionary(It.Is<Dictionary<string, object>>(d =>
                (bool)d[nameof(HostConfigResource.ThePornDbIncludeJav)])));
        }
    }
}
