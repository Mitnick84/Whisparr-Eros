using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
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
    }
}
