using System;
using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MetadataSource.SkyHook;
using NzbDrone.Core.MetadataSource.ThePornDb;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Credits;
using NzbDrone.Core.Movies.Performers;
using NzbDrone.Core.Movies.Studios;
using NzbDrone.Core.Test.Framework;

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
            Mocker.GetMock<IConfigService>().SetupGet(c => c.WhisparrMovieMetadataSource).Returns(MovieMetadataType.TPDB_JAV);
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
            Mocker.GetMock<IConfigService>().SetupGet(c => c.WhisparrMovieMetadataSource).Returns(MovieMetadataType.TPDB_JAV);
            var act = () => Subject.SearchForNewEntity(term, ItemType.Movie);
            act.Should().Throw<InvalidSearchTermException>();
        }

        [Test]
        public void should_explain_jav_movie_lookup_in_scene_screen()
        {
            var act = () => Subject.SearchForNewEntity("tpdbjav:11655355", ItemType.Scene);
            act.Should().Throw<InvalidSearchTermException>().WithMessage("*Movies*Add New*");
        }
    }
}
