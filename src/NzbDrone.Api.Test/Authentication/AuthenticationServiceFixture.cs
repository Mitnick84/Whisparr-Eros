using System;
using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using NzbDrone.Test.Common;
using Whisparr.Http.Authentication;

namespace NzbDrone.Api.Test.Authentication
{
    [TestFixture]
    public class AuthenticationServiceFixture : TestBase<AuthenticationService>
    {
        private HttpRequest _request;

        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<ICacheManager>(Mocker.Resolve<CacheManager>());

            Mocker.GetMock<IConfigFileProvider>()
                  .SetupGet(c => c.AuthenticationMethod)
                  .Returns(AuthenticationType.Forms);

            Mocker.GetMock<IUserService>()
                  .Setup(c => c.FindUser("user", "right"))
                  .Returns(new User { Username = "user" });

            _request = GivenRequest("192.0.2.10");
        }

        private static HttpRequest GivenRequest(string remoteIp)
        {
            var request = new DefaultHttpContext().Request;
            request.HttpContext.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);

            return request;
        }

        private void GivenFailedAttempts(int count)
        {
            for (var i = 0; i < count; i++)
            {
                Subject.Login(_request, "user", "wrong").Should().BeNull();
            }
        }

        [Test]
        public void should_not_lock_out_before_five_failed_attempts()
        {
            GivenFailedAttempts(4);

            Subject.GetLockoutEndTime(_request).Should().BeNull();
            Subject.Login(_request, "user", "right").Should().NotBeNull();
        }

        [Test]
        public void should_lock_out_after_five_failed_attempts()
        {
            GivenFailedAttempts(5);

            Subject.GetLockoutEndTime(_request).Should().BeCloseTo(DateTime.UtcNow.AddMinutes(5), TimeSpan.FromSeconds(10));
        }

        [Test]
        public void should_refuse_the_right_password_while_locked_out()
        {
            GivenFailedAttempts(5);

            Subject.Login(_request, "user", "right").Should().BeNull();

            Mocker.GetMock<IUserService>()
                  .Verify(c => c.FindUser("user", "right"), Times.Never());
        }

        [Test]
        public void should_only_lock_out_the_address_that_failed()
        {
            GivenFailedAttempts(5);

            var otherRequest = GivenRequest("192.0.2.20");

            Subject.GetLockoutEndTime(otherRequest).Should().BeNull();
            Subject.Login(otherRequest, "user", "right").Should().NotBeNull();
        }

        [Test]
        public void should_reset_the_count_after_a_successful_login()
        {
            GivenFailedAttempts(4);

            Subject.Login(_request, "user", "right").Should().NotBeNull();

            GivenFailedAttempts(4);

            Subject.GetLockoutEndTime(_request).Should().BeNull();
        }
    }
}
