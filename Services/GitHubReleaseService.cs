using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using GitHubLauncher.Core.Models;

namespace GitHubLauncher.Core.Services
{
    public sealed class GitHubReleaseFetchResult
    {
        public HttpStatusCode StatusCode { get; init; }
        public IReadOnlyList<GitHubRelease> Releases { get; init; } = [];
        public string? ETag { get; init; }
        public bool IsNotModified => StatusCode == HttpStatusCode.NotModified;
    }

    public static class GitHubReleaseService
    {
        public static async Task<GitHubReleaseFetchResult> FetchReleasesAsync(
            HttpClient httpClient,
            string repository,
            string? token = null,
            string? etag = null)
        {
            if (string.IsNullOrWhiteSpace(repository))
            {
                return new GitHubReleaseFetchResult
                {
                    StatusCode = HttpStatusCode.BadRequest
                };
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repository}/releases");

            if (!string.IsNullOrWhiteSpace(etag))
            {
                request.Headers.TryAddWithoutValidation("If-None-Match", etag);
            }

            if (!string.IsNullOrWhiteSpace(token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            var response = await httpClient.SendAsync(request).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                return new GitHubReleaseFetchResult
                {
                    StatusCode = response.StatusCode,
                    ETag = response.Headers.ETag?.Tag
                };
            }

            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var releases = JsonSerializer.Deserialize<List<GitHubRelease>>(responseContent) ?? [];

            return new GitHubReleaseFetchResult
            {
                StatusCode = response.StatusCode,
                Releases = releases,
                ETag = response.Headers.ETag?.Tag
            };
        }

        public static async Task<List<GitHubRelease>> FetchReleasesWithAssetsAsync(
            HttpClient httpClient,
            string repository,
            string? token = null)
        {
            var result = await FetchReleasesAsync(httpClient, repository, token).ConfigureAwait(false);
            return result.Releases
                .Where(release => release.assets != null && release.assets.Length > 0)
                .ToList();
        }

        public static List<GitHubAsset> GetDownloadableAssets(GitHubRelease release)
        {
            return (release.assets ?? [])
                .Where(asset => !asset.name.Contains("flatpak", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        /// <summary>
        /// Picks the release that should be treated as "latest" for a given
        /// platform. Normally that's just the first (most recently published)
        /// release, but some projects publish separate releases per platform
        /// under different tags (e.g. "nightly-windows" and "nightly-linux").
        /// In that case whichever was published most recently would otherwise
        /// always "win", even for users on a different platform than that
        /// release targets. This instead prefers the most recent release that
        /// actually has a downloadable asset matching the given platform, and
        /// only falls back to the very first release if none of them look
        /// like they target this platform (e.g. asset names carry no platform
        /// hint at all).
        /// </summary>
        public static GitHubRelease? SelectLatestForPlatform(IReadOnlyList<GitHubRelease> releases, string platformIdentifier)
        {
            if (releases == null || releases.Count == 0)
                return null;

            if (!string.IsNullOrWhiteSpace(platformIdentifier))
            {
                var platformMatch = releases.FirstOrDefault(release =>
                    GetDownloadableAssets(release).Any(asset => PlatformAssetMatcher.MatchesPlatform(asset.name, platformIdentifier)));

                if (platformMatch != null)
                    return platformMatch;
            }

            return releases.FirstOrDefault();
        }

        /// <summary>
        /// Returns a string that uniquely identifies the *content* of a release,
        /// suitable for storing in version.txt and for update comparisons.
        /// <para>
        /// For a normal semantic-version tag (e.g. "v1.2.3") the tag name alone
        /// already identifies the release, since a tag is never reused. Some
        /// projects, however, publish "rolling" builds under a fixed tag (e.g.
        /// "latest-nightly", "continuous", "pre-release"), reusing the same
        /// release/tag build after build. In that case the tag name stays
        /// identical across completely different builds, so relying on it
        /// alone makes every build look like "the same version" that's
        /// already installed.
        /// </para>
        /// <para>
        /// To tell those builds apart we use the most recent "updated_at"
        /// timestamp across the release's assets - CI workflows almost always
        /// replace (delete-and-reupload, or overwrite) the actual files when
        /// they publish a new build, even when they don't touch the tag/
        /// release itself, so this reliably changes when there's genuinely
        /// something new to download. <see cref="GitHubRelease.target_commitish"/>
        /// is used only as a fallback when no asset timestamp is available,
        /// since GitHub does not reliably update it for a tag that already
        /// exists.
        /// </para>
        /// </summary>
        public static string GetVersionIdentifier(GitHubRelease? release)
        {
            if (release == null)
                return string.Empty;

            var tag = release.tag_name ?? string.Empty;

            if (!IsRollingTag(tag))
                return tag;

            var latestAssetUpdate = (release.assets ?? [])
                .Select(asset => asset.updated_at)
                .Where(updatedAt => !string.IsNullOrWhiteSpace(updatedAt))
                .OrderByDescending(updatedAt => updatedAt, StringComparer.Ordinal)
                .FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(latestAssetUpdate))
            {
                return $"{tag} ({FormatTimestamp(latestAssetUpdate)})";
            }

            if (!string.IsNullOrWhiteSpace(release.target_commitish))
            {
                var commit = release.target_commitish.Trim();
                var shortCommit = commit.Length > 7 ? commit[..7] : commit;
                return $"{tag} ({shortCommit})";
            }

            return tag;
        }

        /// <summary>
        /// Turns an ISO-8601 asset "updated_at" value (e.g.
        /// "2026-07-23T23:36:34Z") into a compact, sortable-looking discriminator
        /// for display in version.txt / the UI. Falls back to the raw string if
        /// it can't be parsed, so a format change on GitHub's side never breaks
        /// update detection - it would just look a little less tidy.
        /// </summary>
        private static string FormatTimestamp(string isoTimestamp)
        {
            return DateTimeOffset.TryParse(isoTimestamp, out var parsed)
                ? parsed.UtcDateTime.ToString("yyyyMMdd-HHmmss")
                : isoTimestamp;
        }

        /// <summary>
        /// A tag "looks" like a normal, one-time release version if it's made up
        /// purely of dot-separated numeric segments (ignoring an optional
        /// leading "v"), e.g. "v1.2.3" or "2024.03". Anything else - words like
        /// "latest-nightly", "nightly", "canary", "dev-build" - is treated as a
        /// rolling tag that can be re-published against different commits.
        /// </summary>
        private static bool IsRollingTag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return false;

            var normalized = tag.Trim().TrimStart('v', 'V');
            var segments = normalized.Split('.', StringSplitOptions.RemoveEmptyEntries);

            if (segments.Length == 0)
                return true;

            return !segments.All(segment => segment.Length > 0 && segment.All(char.IsDigit));
        }
    }
}
