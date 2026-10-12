using System;
using System.Collections.Generic;
using System.Net;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MetadataSource.SkyHook;
using NzbDrone.Core.MetadataSource.SkyHook.Resource;
using NzbDrone.Core.MetadataSource.ThePornDb;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Credits;
using NzbDrone.Core.Movies.Performers;
using NzbDrone.Core.Movies.Studios;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MetadataSource.SkyHook
{
    [TestFixture]
    public class SkyHookProxyJavFixture : CoreTest<SkyHookProxy>
    {
        private MovieMetadata _metadata;

        [SetUp]
        public void Setup()
        {
            _metadata = ThePornDbJavProxy.MapJav(new TpdbJavResource
            {
                Id = "12345678-1234-4234-9234-123456789012",
                Type = "Jav",
                Title = "Sample Title",
                ExternalId = "DBIF-001"
            });
            Mocker.GetMock<IThePornDbJavProxy>().Setup(p => p.GetJavInfo(It.IsAny<string>()))
                .Returns(Tuple.Create(_metadata, (Studio)null, new List<Performer>(), new List<Credit>()));
            Mocker.GetMock<IThePornDbJavProxy>().Setup(p => p.Search(It.IsAny<string>()))
                .Returns(new List<MovieMetadata> { _metadata });
        }

        [TestCase("https://theporndb.net/jav/studio-example-title", "studio-example-title")]
        [TestCase("tpdbjav:11655355", "11655355")]
        public void should_route_explicit_jav_lookup_independently_of_default_movie_source(string term, string identifier)
        {
            Mocker.GetMock<IConfigService>().SetupGet(c => c.WhisparrMovieMetadataSource).Returns(MovieMetadataType.TMDB);
            var movie = (Movie)Subject.SearchForNewEntity(term, ItemType.Movie)[0];
            movie.ForeignId.Should().Be(_metadata.ForeignId);
            Mocker.GetMock<IThePornDbJavProxy>().Verify(p => p.GetJavInfo(identifier), Times.Once);
        }

        [TestCase("DBIF-001")]
        [TestCase("H113SL-013AI")]
        [TestCase("Example Title")]
        public void should_preserve_code_or_title_search_terms_for_jav(string term)
        {
            Mocker.GetMock<IConfigService>().SetupGet(c => c.WhisparrMovieMetadataSource).Returns(MovieMetadataType.TPDBJAV);
            Subject.SearchForNewEntity(term, ItemType.Movie).Should().ContainSingle();
            Mocker.GetMock<IThePornDbJavProxy>().Verify(p => p.Search(term), Times.Once);
        }

        [Test]
        public void should_return_an_existing_jav_library_entry()
        {
            var existing = new Movie { Id = 42, MovieMetadata = _metadata };
            Mocker.GetMock<IMovieService>().Setup(s => s.FindByForeignId(_metadata.ForeignId)).Returns(existing);
            Subject.SearchForNewMovie("tpdbjav:11655355").Should().ContainSingle().Which.Should().BeSameAs(existing);
        }

        [Test]
        public void should_return_empty_for_a_missing_jav_record()
        {
            Mocker.GetMock<IThePornDbJavProxy>().Setup(p => p.GetJavInfo("11655355")).Throws(new MovieNotFoundException("11655355"));
            Subject.SearchForNewMovie("tpdbjav:11655355").Should().BeEmpty();
        }

        [TestCase("tpdbid:1")]
        [TestCase("tmdb:1")]
        [TestCase("https://theporndb.net/movies/example")]
        public void should_explain_wrong_catalog_ids_in_jav_mode(string term)
        {
            Mocker.GetMock<IConfigService>().SetupGet(c => c.WhisparrMovieMetadataSource).Returns(MovieMetadataType.TPDBJAV);
            var act = () => Subject.SearchForNewEntity(term, ItemType.Movie);
            act.Should().Throw<InvalidSearchTermException>();
        }

        [Test]
        public void should_explain_jav_movie_lookup_in_scene_screen()
        {
            var act = () => Subject.SearchForNewEntity("tpdbjav:11655355", ItemType.Scene);
            act.Should().Throw<InvalidSearchTermException>().WithMessage("*Movies*Add New*");
        }

        private Movie GivenTpdbMovieSearch(bool includeJav)
        {
            Mocker.GetMock<IConfigService>().SetupGet(c => c.WhisparrMovieMetadataSource).Returns(MovieMetadataType.TPDB);
            Mocker.GetMock<IConfigService>().SetupGet(c => c.ThePornDbIncludeJav).Returns(includeJav);
            var existing = new Movie { Id = 43, MovieMetadata = new MovieMetadata { ForeignId = "tpdbId:western-id", Title = "Western Title" } };
            Mocker.GetMock<IMovieService>().Setup(s => s.FindByForeignId("tpdbId:western-id")).Returns(existing);
            var resources = new List<MovieResource>
            {
                new MovieResource { ItemType = ItemType.Movie, ForeignIds = new ExternalIdResource { TpdbId = "western-id" } }
            };
            Mocker.GetMock<IHttpClient>().Setup(c => c.Get<List<MovieResource>>(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) => new HttpResponse<List<MovieResource>>(new HttpResponse(request, new HttpHeader(), resources.ToJson(), HttpStatusCode.OK)));
            return existing;
        }

        [TestCase("DBIF-001")]
        [TestCase("H113SL-013AI")]
        [TestCase("Example Title")]
        public void should_include_both_catalogs_only_when_enabled(string term)
        {
            var western = GivenTpdbMovieSearch(true);
            var results = Subject.SearchForNewEntity(term, ItemType.Movie);
            results.Should().HaveCount(2);
            results[0].Should().BeSameAs(western);
            ((Movie)results[1]).ForeignId.Should().Be(_metadata.ForeignId);
            Mocker.GetMock<IThePornDbJavProxy>().Verify(p => p.Search(term), Times.Once);
        }

        [Test]
        public void should_not_query_jav_for_an_unchanged_tpdb_configuration()
        {
            var western = GivenTpdbMovieSearch(false);
            Subject.SearchForNewMovie("Example Title").Should().ContainSingle().Which.Should().BeSameAs(western);
            Mocker.GetMock<IThePornDbJavProxy>().Verify(p => p.Search(It.IsAny<string>()), Times.Never);
        }

        [Test]
        public void should_not_expand_tmdb_searches_with_the_tpdb_option()
        {
            GivenTpdbMovieSearch(true);
            Mocker.GetMock<IConfigService>().SetupGet(c => c.WhisparrMovieMetadataSource).Returns(MovieMetadataType.TMDB);
            Subject.SearchForNewMovie("Example Title");
            Mocker.GetMock<IThePornDbJavProxy>().Verify(p => p.Search(It.IsAny<string>()), Times.Never);
        }

        [Test]
        public void should_not_expand_scene_searches_with_the_tpdb_option()
        {
            GivenTpdbMovieSearch(true);
            Subject.SearchForNewEntity("Example Title", ItemType.Scene);
            Mocker.GetMock<IThePornDbJavProxy>().Verify(p => p.Search(It.IsAny<string>()), Times.Never);
        }

        [Test]
        public void should_retain_western_results_when_optional_jav_search_fails()
        {
            var western = GivenTpdbMovieSearch(true);
            Mocker.GetMock<IThePornDbJavProxy>().Setup(p => p.Search(It.IsAny<string>())).Throws(new SkyHookException("Synthetic unavailable catalog"));
            Subject.SearchForNewMovie("Example Title").Should().ContainSingle().Which.Should().BeSameAs(western);
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_not_fan_out_explicit_jav_ids_in_combined_mode()
        {
            GivenTpdbMovieSearch(true);
            Subject.SearchForNewMovie("tpdbjav:11655355").Should().ContainSingle().Which.ForeignId.Should().Be(_metadata.ForeignId);
            Mocker.GetMock<IHttpClient>().Verify(c => c.Get<List<MovieResource>>(It.IsAny<HttpRequest>()), Times.Never);
            Mocker.GetMock<IThePornDbJavProxy>().Verify(p => p.Search(It.IsAny<string>()), Times.Never);
        }

        [TestCase("tpdb:11655355")]
        [TestCase("tpdbid:11655355")]
        [TestCase("https://theporndb.net/movies/example-title")]
        public void should_not_query_jav_for_an_explicit_movie_lookup(string term)
        {
            GivenTpdbMovieSearch(true);
            Mocker.GetMock<IHttpClient>().Setup(c => c.Get<MovieResource>(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) => new HttpResponse<MovieResource>(new HttpResponse(request, new HttpHeader(), "", HttpStatusCode.NotFound)));
            Mocker.GetMock<IThePornDbJavProxy>().Setup(p => p.Search(It.IsAny<string>())).Throws(new Exception("Unexpected JAV request"));
            Subject.SearchForNewMovie(term);
            Mocker.GetMock<IThePornDbJavProxy>().Verify(p => p.Search(It.IsAny<string>()), Times.Never);
        }
    }
}
