using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting.Azure.Functions.Annotations;

/// <summary>
/// Annotation to link an Azure Functions App resource to its host environment resource. This is used to indicate that the annotated Functions App is part of the hosting environment and should be treated as such by any processing logic that handles resource annotations.
/// </summary>
/// <param name="environment"></param>
public class FunctionAppToHostAnnotation(AzureFunctionsEnvironmentResource environment) : IResourceAnnotation
{
    public AzureFunctionsEnvironmentResource Environment { get; } = environment;
}
