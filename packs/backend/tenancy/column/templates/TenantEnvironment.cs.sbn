using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting;

/// <summary>The tenants staff may write for (ForTenant()), given to the host as Tenancy:Tenants, one entry each.</summary>
internal static class TenantEnvironment
{
    public static IResourceBuilder<ProjectResource> WithTenants(this IResourceBuilder<ProjectResource> project, IReadOnlyList<string> tenants)
    {
        for (var index = 0; index < tenants.Count; index++)
        {
            project.WithEnvironment($"Tenancy__Tenants__{index}", tenants[index]);
        }

        return project;
    }
}
