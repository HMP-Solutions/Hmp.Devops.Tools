using System.Threading.Tasks;

namespace Hmp.Devops.Tools.EnvironmentRemover.Interfaces
{
    public interface IPullRequestPayloadParser
    {
        Task<Result<PullRequestInfo>> ParseAsync(string jsonBody);
    }
}
