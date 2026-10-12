using System.Collections.Generic;
using System.IO;
using FizzWare.NBuilder;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Credits;
using NzbDrone.Core.Movies.Performers;
using NzbDrone.Core.Movies.Studios;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MovieTests
{
    [TestFixture]
    public class AddMovieFixture : CoreTest<AddMovieService>
    {
        private MovieMetadata _fakeMovie;

        [SetUp]
        public void Setup()
        {
            _fakeMovie = Builder<MovieMetadata>
                .CreateNew()
                .Build();
        }

        private void GivenValidMovie(int tmdbId)
        {
            Mocker.GetMock<IProvideMovieInfo>()
                  .Setup(s => s.GetMovieInfo(tmdbId))
                  .Returns(new System.Tuple<MovieMetadata, Studio, List<Performer>, List<Credit>>(_fakeMovie, new Studio(), new List<Performer>(), new List<Credit>()));
        }

        private void GivenValidPath()
        {
            Mocker.GetMock<IBuildFileNames>()
                  .Setup(s => s.GetMovieFolder(It.IsAny<Movie>(), null))
                  .Returns<Movie, NamingConfig>((c, n) => c.Title);

            Mocker.GetMock<IAddMovieValidator>()
                  .Setup(s => s.Validate(It.IsAny<Movie>()))
                  .Returns(new ValidationResult());
        }

        [Test]
        public void should_add_jav_using_qualified_id_before_generic_tpdb_id()
        {
            _fakeMovie.ForeignId = "tpdbjav:12345678-1234-4234-9234-123456789012";
            _fakeMovie.TpdbId = "12345678-1234-4234-9234-123456789012";
            _fakeMovie.TmdbId = 0;
            _fakeMovie.MetadataSource = Movies.MetadataSource.TpdbJav;
            var newMovie = new Movie { ForeignId = _fakeMovie.ForeignId, TpdbId = _fakeMovie.TpdbId, RootFolderPath = @"C:\Test\Movies" };
            Mocker.GetMock<IProvideMovieInfo>().Setup(p => p.GetTpdbJavInfo(_fakeMovie.TpdbId))
                .Returns(System.Tuple.Create(_fakeMovie, (Studio)null, new List<Performer>(), new List<Credit>()));
            GivenValidPath();
            Subject.AddMovie(newMovie).ForeignId.Should().Be(_fakeMovie.ForeignId);
            Mocker.GetMock<IProvideMovieInfo>().Verify(p => p.GetTpdbMovieInfo(It.IsAny<string>()), Times.Never);
            Mocker.GetMock<IProvideMovieInfo>().Verify(p => p.GetSceneInfo(It.IsAny<string>()), Times.Never);
        }

        [Test]
        public void should_be_able_to_add_a_movie_without_passing_in_title()
        {
            var newMovie = new Movie
            {
                ForeignId = "1",
                TmdbId = 1,
                RootFolderPath = @"C:\Test\Movies"
            };

            GivenValidMovie(newMovie.TmdbId);
            GivenValidPath();

            var series = Subject.AddMovie(newMovie);

            series.Title.Should().Be(_fakeMovie.Title);
        }

        [Test]
        public void should_have_proper_path()
        {
            var newMovie = new Movie
            {
                ForeignId = "1",
                TmdbId = 1,
                RootFolderPath = @"C:\Test\Movies"
            };

            GivenValidMovie(newMovie.TmdbId);
            GivenValidPath();

            var series = Subject.AddMovie(newMovie);

            series.Path.Should().Be(Path.Combine(newMovie.RootFolderPath, _fakeMovie.Title));
        }

        [Test]
        public void should_throw_if_movie_validation_fails()
        {
            var newMovie = new Movie
            {
                ForeignId = "1",
                TmdbId = 1,
                Path = @"C:\Test\Movie\Title1"
            };

            GivenValidMovie(newMovie.TmdbId);

            Mocker.GetMock<IAddMovieValidator>()
                  .Setup(s => s.Validate(It.IsAny<Movie>()))
                  .Returns(new ValidationResult(new List<ValidationFailure>
                                                {
                                                    new ValidationFailure("Path", "Test validation failure")
                                                }));

            Assert.Throws<ValidationException>(() => Subject.AddMovie(newMovie));
        }

        [Test]
        public void should_throw_if_movie_cannot_be_found()
        {
            var newMovie = new Movie
            {
                ForeignId = "1",
                TmdbId = 1,
                Path = @"C:\Test\Movie\Title1"
            };

            Mocker.GetMock<IProvideMovieInfo>()
                  .Setup(s => s.GetMovieInfo(newMovie.TmdbId))
                  .Throws(new MovieNotFoundException("Movie Not Found"));

            Mocker.GetMock<IAddMovieValidator>()
                  .Setup(s => s.Validate(It.IsAny<Movie>()))
                  .Returns(new ValidationResult(new List<ValidationFailure>
                                                {
                                                    new ValidationFailure("Path", "Test validation failure")
                                                }));

            Assert.Throws<ValidationException>(() => Subject.AddMovie(newMovie));

            ExceptionVerification.ExpectedErrors(1);
        }
    }
}
