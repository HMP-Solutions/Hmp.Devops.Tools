using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting.Azure.Functions.Annotations;

public class AzureFunctionStorageAssignmentAnnotation(IResourceWithParent<AzureQueueStorageResource> storageComponent) : IResourceAnnotation
{
    public IResourceWithParent<AzureQueueStorageResource> StorageComponent { get; } = storageComponent;
}
