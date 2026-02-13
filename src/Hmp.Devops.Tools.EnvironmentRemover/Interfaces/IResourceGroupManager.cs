using System.Threading.Tasks;

namespace Hmp.Devops.Tools.EnvironmentRemover.Interfaces
{
    public interface IResourceGroupManager
    {
        Task<Result<bool>> DeleteResourceGroupAsync(string repositoryName, string pullRequestId);
    }
}
