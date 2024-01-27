using System.Threading.Tasks;

namespace prmonitor;

internal interface IUserNameResolver
{
    Task<string> ResolveUsernameForLogin(string login);
}