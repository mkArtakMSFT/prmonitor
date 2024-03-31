using Octokit;

namespace prmonitor;

internal abstract class GitHubDataRetrieverBase
{
    protected GitHubClient Client { get; }

    protected string Org { get; }

    protected string Repo { get; }

    protected GitHubDataRetrieverBase(GitHubClient client, string org, string repo)
    {
        Client = client;
        Org = org;
        Repo = repo;
    }
}
