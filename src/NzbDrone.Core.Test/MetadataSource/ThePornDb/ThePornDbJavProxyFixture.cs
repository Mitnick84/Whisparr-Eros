using System;
using System.Collections.Generic;
using System.Net;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MetadataSource.SkyHook;
using NzbDrone.Core.MetadataSource.ThePornDb;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.ThePornDb
{
    [TestFixture]
    public class ThePornDbJavProxyFixture : CoreTest<ThePornDbJavProxy>
    {
        private const string Uuid = "12345678-1234-4234-9234-123456789012";
        private HttpRequest _request;
        private TpdbJavResource _resource;

        [SetUp]
        public void Setup()
        {
            _resource = new TpdbJavResource { Id = Uuid, Type = "Jav", Title = "Sample Title", ExternalId = "DBIF-001", Date = "2020-01-02" };
            Mocker.GetMock<IConfigService>().SetupGet(c => c.ThePornDbApiKey).Returns("synthetic-test-key");
            GivenResponse(new { data = _resource });
        }

        private void GivenResponse(object content, HttpStatusCode status = HttpStatusCode.OK)
        {
            Mocker.GetMock<IHttpClient>().Setup(c => c.Get(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(r =>
                {
                    _request = r;
                    return new HttpResponse(r, new HttpHeader { ContentType = "application/json" }, JsonConvert.SerializeObject(content), status);
                });
        }

        [TestCase("tpdbjav:12345678-1234-4234-9234-123456789012", Uuid)]
        [TestCase("TPDBJAV: 11655355", "11655355")]
        [TestCase("https://theporndb.net/jav/studio-example-title/", "studio-example-title")]
        [TestCase("https://theporndb.net/jav/studio-example-title?x=1", "studio-example-title")]
        public void should_extract_explicit_jav_identifiers(string term, string expected)
        {
            ThePornDbJavProxy.IsJavLookup(term).Should().BeTrue();
            ThePornDbJavProxy.GetIdentifier(term).Should().Be(expected);
        }

        [TestCase("tpdbjav:")]
        [TestCase(null)]
        [TestCase("tpdbjav:../movies/1")]
        [TestCase("tpdbjav:two words")]
        [TestCase("tpdbjav:https://example.com")]
        [TestCase("https://theporndb.net/jav/example/extra")]
        public void should_reject_invalid_resource_paths(string term)
        {
            var act = () => Subject.GetJavInfo(term);
            act.Should().Throw<InvalidSearchTermException>();
            Mocker.GetMock<IHttpClient>().Verify(c => c.Get(It.IsAny<HttpRequest>()), Times.Never);
        }

        [TestCase("https://theporndb.net/movies/1")]
        [TestCase("https://theporndb.net.example.com/jav/1")]
        [TestCase("https://example.com/jav/1")]
        [TestCase("http://theporndb.net/jav/1")]
        [TestCase("tpdbid:1")]
        public void should_not_claim_other_catalogs_or_domains(string term)
        {
            ThePornDbJavProxy.IsJavLookup(term).Should().BeFalse();
        }

        [Test]
        public void should_use_fixed_api_origin_and_header_auth_without_redirects()
        {
            var result = Subject.GetJavInfo("11655355");
            _request.Url.FullUri.Should().Be("https://api.theporndb.net/jav/11655355");
            _request.Headers.GetValues("Authorization").Should().ContainSingle("Bearer synthetic-test-key");
            _request.AllowAutoRedirect.Should().BeFalse();
            _request.LogResponseContent.Should().BeFalse();
            _request.RateLimit.Should().Be(TimeSpan.FromSeconds(1));
            _request.ToString(true, true).Should().NotContain("synthetic-test-key");
            result.Item1.ForeignId.Should().Be("tpdbjav:" + Uuid);
            result.Item1.TpdbId.Should().Be(Uuid);
            result.Item1.StashId.Should().BeNull();
            result.Item1.MetadataSource.Should().Be(Movies.MetadataSource.TpdbJav);
            result.Item1.ItemType.Should().Be(ItemType.Movie);
            result.Item2.Should().BeNull();
            result.Item3.Should().BeEmpty();
        }

        [Test]
        public void should_search_the_jav_catalog_with_original_product_code()
        {
            GivenResponse(new { data = new[] { _resource } });
            Subject.Search("H113SL-013AI").Should().ContainSingle();
            _request.Url.FullUri.Should().Contain("q=H113SL-013AI").And.Contain("per_page=50");
        }

        [Test]
        public void should_handle_empty_search_results()
        {
            GivenResponse(new { data = Array.Empty<TpdbJavResource>() });
            Subject.Search("missing").Should().BeEmpty();
        }

        [Test]
        public void should_reject_missing_search_data()
        {
            GivenResponse(new { error = "invalid" });
            var act = () => Subject.Search("missing");
            act.Should().Throw<SkyHookException>();
        }

        [TestCase(HttpStatusCode.Unauthorized)]
        [TestCase(HttpStatusCode.Forbidden)]
        [TestCase(HttpStatusCode.TooManyRequests)]
        [TestCase(HttpStatusCode.ServiceUnavailable)]
        [TestCase(HttpStatusCode.Redirect)]
        public void should_report_api_failures_without_response_body_or_secrets(HttpStatusCode status)
        {
            GivenResponse(new { error = "synthetic-test-key" }, status);
            var act = () => Subject.GetJavInfo(Uuid);
            act.Should().Throw<SkyHookException>().Which.Message.Should().NotContain("synthetic-test-key");
        }

        [Test]
        public void should_distinguish_a_missing_record_from_an_api_failure()
        {
            GivenResponse(new { }, HttpStatusCode.NotFound);
            var act = () => Subject.GetJavInfo(Uuid);
            act.Should().Throw<MovieNotFoundException>();
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        public void should_require_an_api_key(string key)
        {
            Mocker.GetMock<IConfigService>().SetupGet(c => c.ThePornDbApiKey).Returns(key);
            var act = () => Subject.GetJavInfo(Uuid);
            act.Should().Throw<SkyHookException>().WithMessage("*API key*");
            Mocker.GetMock<IHttpClient>().Verify(c => c.Get(It.IsAny<HttpRequest>()), Times.Never);
        }

        [Test]
        public void should_map_optional_metadata_without_assigning_tpdb_ids_to_stash()
        {
            _resource.Sku = " H113SL-013AI ";
            _resource.Site = new TpdbSiteResource { Name = "Example Studio", Uuid = Uuid, Network = new TpdbSiteResource { Name = "Example Network" } };
            _resource.Performers = new List<TpdbPerformerResource> { null, new TpdbPerformerResource(), new TpdbPerformerResource { Name = "Sample Performer", Id = Uuid } };
            _resource.Tags = new List<TpdbTagResource> { null, new TpdbTagResource { Name = "Example" } };
            _resource.Posters = new TpdbImageResource { Large = "https://example.com/poster.jpg" };
            _resource.Background = new TpdbImageResource { Large = "https://example.com/background.jpg" };
            var metadata = ThePornDbJavProxy.MapJav(_resource);
            metadata.Code.Should().Be("H113SL-013AI");
            metadata.StudioTitle.Should().Be("Example Studio");
            metadata.Studio.Network.Should().Be("Example Network");
            metadata.StudioForeignId.Should().BeNull();
            metadata.Studio.ForeignIds.StashId.Should().BeNull();
            metadata.PerformerForeignIds.Should().BeEmpty();
            metadata.Credits.Should().ContainSingle().Which.PerformerForeignId.Should().BeNull();
            metadata.PerformerNames.Should().ContainSingle("Sample Performer");
            metadata.Genres.Should().ContainSingle("Example");
            metadata.Images.Should().HaveCount(2);
            metadata.OriginalLanguage.Should().Be(Language.Japanese);
            metadata.Year.Should().Be(2020);
            metadata.Status.Should().Be(MovieStatusType.Released);
        }

        [TestCase(null, 0)]
        [TestCase(0, 0)]
        [TestCase(-1, 0)]
        [TestCase(7200, 120)]
        [TestCase(7201, 121)]
        public void should_convert_tpdb_duration_in_seconds_to_minutes(int? seconds, int minutes)
        {
            _resource.Duration = seconds;
            ThePornDbJavProxy.MapJav(_resource).Runtime.Should().Be(minutes);
        }

        [Test]
        public void should_deserialize_the_public_api_field_names()
        {
            Mocker.GetMock<IHttpClient>().Setup(c => c.Get(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(r => new HttpResponse(
                    r,
                    new HttpHeader { ContentType = "application/json" },
                    "{\"data\":{\"id\":\"" + Uuid + "\",\"type\":\"Jav\",\"title\":\"Sample Title\",\"external_id\":\"DBIF-001\",\"poster_image\":\"https://example.com/poster.jpg\",\"duration\":7200}}"));
            var metadata = Subject.GetJavInfo(Uuid).Item1;
            metadata.Code.Should().Be("DBIF-001");
            metadata.Runtime.Should().Be(120);
            metadata.Images.Should().ContainSingle().Which.RemoteUrl.Should().Be("https://example.com/poster.jpg");
        }

        [TestCase(null)]
        [TestCase("bad date")]
        [TestCase("3000-01-01")]
        public void should_handle_missing_invalid_or_future_dates(string date)
        {
            _resource.Date = date;
            var metadata = ThePornDbJavProxy.MapJav(_resource);
            metadata.Status.Should().Be(MovieStatusType.Announced);
            metadata.Images.Should().BeEmpty();
            metadata.Credits.Should().BeEmpty();
        }

        [TestCase("Movie", Uuid, "Sample")]
        [TestCase("Scene", Uuid, "Sample")]
        [TestCase("Jav", "not-a-uuid", "Sample")]
        [TestCase("Jav", Uuid, "")]
        public void should_reject_records_without_canonical_jav_identity(string type, string id, string title)
        {
            _resource.Type = type;
            _resource.Id = id;
            _resource.Title = title;
            var act = () => ThePornDbJavProxy.MapJav(_resource);
            act.Should().Throw<SkyHookException>();
        }
    }
}
