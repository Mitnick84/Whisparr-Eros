using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using Newtonsoft.Json;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MetadataSource.SkyHook;
using NzbDrone.Core.MetadataSource.SkyHook.Resource;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Credits;
using NzbDrone.Core.Movies.Performers;
using NzbDrone.Core.Movies.Studios;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.MetadataSource.ThePornDb
{
    public interface IThePornDbJavProxy
    {
        Tuple<MovieMetadata, Studio, List<Performer>, List<Credit>> GetJavInfo(string identifier);
        List<MovieMetadata> Search(string term);
    }

    public class ThePornDbJavProxy : IThePornDbJavProxy
    {
        public const string IdPrefix = "tpdbjav:";
        private readonly IHttpClient _httpClient;
        private readonly IConfigService _configService;

        public ThePornDbJavProxy(IHttpClient httpClient, IConfigService configService)
        {
            _httpClient = httpClient;
            _configService = configService;
        }

        public static bool IsJavLookup(string term)
        {
            return term?.Trim().StartsWith(IdPrefix, StringComparison.OrdinalIgnoreCase) == true ||
                   (Uri.TryCreate(term, UriKind.Absolute, out var uri) &&
                    uri.Scheme == Uri.UriSchemeHttps &&
                    string.Equals(uri.Host, "theporndb.net", StringComparison.OrdinalIgnoreCase) &&
                    uri.AbsolutePath.StartsWith("/jav/", StringComparison.OrdinalIgnoreCase));
        }

        public static string GetIdentifier(string term)
        {
            term = term?.Trim() ?? string.Empty;
            if (term.StartsWith(IdPrefix, StringComparison.OrdinalIgnoreCase))
            {
                term = term.Substring(IdPrefix.Length).Trim();
            }
            else if (IsJavLookup(term) && Uri.TryCreate(term, UriKind.Absolute, out var uri))
            {
                term = uri.AbsolutePath.Substring("/jav/".Length).TrimEnd('/');
            }

            // Identifiers are a single slug, UUID or numeric ID. Never accept a URL as an API resource.
            if (term.Length == 0 || term.Length > 250 || term.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
            {
                throw new InvalidSearchTermException("Use a ThePornDB JAV URL, UUID, numeric ID or slug after tpdbjav:");
            }

            return term;
        }

        public Tuple<MovieMetadata, Studio, List<Performer>, List<Credit>> GetJavInfo(string identifier)
        {
            var request = CreateRequest().Resource(GetIdentifier(identifier)).Build();
            var response = _httpClient.Get(request);
            CheckResponse(response, identifier);
            var resource = new HttpResponse<TpdbResponse<TpdbJavResource>>(response).Resource.Data;
            var movie = MapJav(resource);

            // TPDB performer/studio UUIDs are not StashDB IDs. Keep cast names and embedded studio metadata.
            return Tuple.Create(movie, (Studio)null, new List<Performer>(), movie.Credits);
        }

        public List<MovieMetadata> Search(string term)
        {
            var request = CreateRequest().AddQueryParam("q", term).AddQueryParam("per_page", "50").Build();
            var response = _httpClient.Get(request);
            CheckResponse(response, term);
            var resources = new HttpResponse<TpdbResponse<List<TpdbJavResource>>>(response).Resource.Data;
            if (resources == null)
            {
                throw new SkyHookException("ThePornDB JAV search returned an invalid response.");
            }

            return resources.Select(MapJav).ToList();
        }

        private HttpRequestBuilder CreateRequest()
        {
            var apiKey = _configService.ThePornDbApiKey;
            if (apiKey.IsNullOrWhiteSpace())
            {
                throw new SkyHookException("Configure a ThePornDB API key in General Settings to search the JAV catalog.");
            }

            var builder = new HttpRequestBuilder("https://api.theporndb.net/jav")
            {
                HttpAccept = HttpAccept.Json,
                SuppressHttpError = true,
                LogHttpError = false,
                AllowAutoRedirect = false,
                RateLimit = TimeSpan.FromSeconds(1)
            };
            builder.Headers.Set("Authorization", "Bearer " + apiKey.Trim());
            return builder;
        }

        private static void CheckResponse(HttpResponse response, string identifier)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new MovieNotFoundException(identifier);
            }

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new SkyHookException("ThePornDB rejected the API key. Check the key and JAV access in General Settings.");
            }

            if (response.HasHttpError || response.HasHttpRedirect)
            {
                // Do not include the response body, request headers or a redirected URL in the error.
                throw new SkyHookException("ThePornDB JAV request failed with HTTP {0}.", (int)response.StatusCode);
            }
        }

        public static MovieMetadata MapJav(TpdbJavResource resource)
        {
            if (resource == null || !Guid.TryParse(resource.Id, out var id) || resource.Title.IsNullOrWhiteSpace() ||
                !string.Equals(resource.Type, "Jav", StringComparison.OrdinalIgnoreCase))
            {
                throw new SkyHookException("ThePornDB returned an invalid JAV record.");
            }

            var uuid = id.ToString();
            var hasDate = DateTime.TryParseExact(resource.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date);
            var movie = new MovieMetadata
            {
                ForeignId = IdPrefix + uuid,
                TpdbId = uuid,
                MetadataSource = Movies.MetadataSource.TpdbJav,
                ItemType = ItemType.Movie,
                Title = resource.Title,
                CleanTitle = resource.Title.CleanMovieTitle(),
                SortTitle = Parser.Parser.NormalizeTitle(resource.Title),
                Code = (resource.Sku.IsNotNullOrWhiteSpace() ? resource.Sku : resource.ExternalId)?.Trim(),
                Overview = resource.Description,
                ReleaseDate = hasDate ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null,
                ReleaseDateUtc = hasDate ? date : null,
                Year = hasDate ? date.Year : 0,
                OriginalLanguage = Language.Japanese,
                Runtime = resource.Duration is > 0 and <= int.MaxValue ? (int)Math.Ceiling(resource.Duration.Value / 60m) : 0,
                Website = "https://theporndb.net/jav/" + uuid,
                Status = hasDate && date <= DateTime.UtcNow ? MovieStatusType.Released : MovieStatusType.Announced,
                Genres = resource.Tags?.Where(t => t?.Name.IsNotNullOrWhiteSpace() == true).Select(t => t.Name).Distinct().ToList() ?? new List<string>()
            };

            if (resource.Site?.Name.IsNotNullOrWhiteSpace() == true)
            {
                movie.StudioTitle = resource.Site.Name;
                movie.Studio = new StudioResource
                {
                    Title = resource.Site.Name,
                    Network = resource.Site.Network?.Name ?? resource.Site.Parent?.Name,
                    Homepage = resource.Site.Url,
                    ForeignIds = new ExternalIdResource { TpdbId = resource.Site.Uuid },
                    Aliases = new List<string>(),
                    Images = new List<ImageResource>()
                };
            }

            AddImage(movie, resource.Posters?.Large ?? resource.PosterImage ?? resource.Poster, MediaCoverTypes.Poster);
            AddImage(movie, resource.Background?.Large ?? resource.Image, MediaCoverTypes.Fanart);

            foreach (var performer in resource.Performers ?? new List<TpdbPerformerResource>())
            {
                if (performer?.Name.IsNullOrWhiteSpace() != false)
                {
                    continue;
                }

                movie.Credits.Add(new Credit
                {
                    PersonName = performer.Name,
                    Type = CreditType.Cast,
                    Order = movie.Credits.Count,
                    Performer = new CreditPerformer { Name = performer.Name, TpdbId = performer.Id }
                });
            }

            movie.PerformerNames = movie.Credits.Select(c => c.PersonName).Distinct().ToList();
            return movie;
        }

        private static void AddImage(MovieMetadata movie, string url, MediaCoverTypes type)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            {
                movie.Images.Add(new MediaCover.MediaCover(type, url));
            }
        }
    }

    public class TpdbResponse<T>
    {
        public T Data { get; set; }
    }

    public class TpdbJavResource
    {
        public string Id { get; set; }
        public string Type { get; set; }
        public string Title { get; set; }
        public string Sku { get; set; }
        [JsonProperty("external_id")]
        public string ExternalId { get; set; }
        public string Description { get; set; }
        public string Date { get; set; }
        public decimal? Duration { get; set; }
        public string Image { get; set; }
        [JsonProperty("poster_image")]
        public string PosterImage { get; set; }
        public string Poster { get; set; }
        public TpdbImageResource Posters { get; set; }
        public TpdbImageResource Background { get; set; }
        public TpdbSiteResource Site { get; set; }
        public List<TpdbPerformerResource> Performers { get; set; }
        public List<TpdbTagResource> Tags { get; set; }
    }

    public class TpdbImageResource
    {
        public string Large { get; set; }
    }

    public class TpdbSiteResource
    {
        public string Uuid { get; set; }
        public string Name { get; set; }
        public string Url { get; set; }
        public TpdbSiteResource Network { get; set; }
        public TpdbSiteResource Parent { get; set; }
    }

    public class TpdbPerformerResource
    {
        public string Id { get; set; }
        public string Name { get; set; }
    }

    public class TpdbTagResource
    {
        public string Name { get; set; }
    }
}
