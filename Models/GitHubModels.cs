namespace GitHubLauncher.Core.Models
{
    public class GitHubRelease
    {
        public string tag_name { get; set; } = string.Empty;
        // The commit (or branch) specified when the release's tag was created.
        // NOTE: per GitHub's own API docs this is "unused if the Git tag
        // already exists" - so for a rolling tag (e.g. "latest-nightly")
        // that's reused build after build via a force-pushed ref, this can
        // stay frozen on whatever branch name was used the very first time,
        // or come back empty. Treat it as a best-effort fallback only; the
        // per-asset "updated_at" timestamps below are the reliable signal.
        public string target_commitish { get; set; } = string.Empty;
        public GitHubAsset[] assets { get; set; } = [];
        public bool prerelease { get; set; }
    }

    public class GitHubAsset
    {
        public string name { get; set; } = string.Empty;
        public string browser_download_url { get; set; } = string.Empty;
        // When this specific file was last uploaded/replaced. This is what
        // actually tells two builds published under the same rolling tag
        // apart, since CI workflows almost always delete-and-reupload (or
        // overwrite) an asset when they publish a new build, even when the
        // release/tag itself is reused.
        public string updated_at { get; set; } = string.Empty;
    }
}
