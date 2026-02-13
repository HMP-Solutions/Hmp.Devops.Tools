// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting.Azure.Functions.Annotations;

public static partial class AzureFunctionsExtensions
{
    /// <summary>
    /// Annotation to mark a project as an Azure Functions project.
    /// Used to skip Docker container builds during deployment.
    /// </summary>
    public class AzureFunctionsProjectAnnotation : IResourceAnnotation
    {
        // Marker annotation - just used for identification
    }
}