using Octokit;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace prmonitor;

internal class UserNameResolver : IUserNameResolver
{
    private readonly GitHubClient _client;
    private Dictionary<string, string> msUserNamesCache = new Dictionary<string, string>();

    public UserNameResolver(GitHubClient client)
    {
        _client = client;
    }

    public async Task<string> ResolveUsernameForLogin(string login)
    {
        if (login is null)
            return null;

        if (msUserNamesCache.TryGetValue(login, out var name))
            return name;

        var un = await _client.User.Get(login);
        msUserNamesCache.Add(login, un.Name);

        return un.Name;
    }
}
