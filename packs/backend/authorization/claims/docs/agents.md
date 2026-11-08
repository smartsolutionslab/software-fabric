{{~ roles = project["backend.authorization"] | array.contains "roles" ~}}
## Authorization: by claim

- Access by claim goes through a policy, never a claim check. A module's policies are registered in its Api project's `<Module>Policies : IModulePolicies`, under names its Application project declares in `<Module>PolicyNames`, `<module>.<permission>`, built with `RequireClaim("department", "finance")`{{ if roles }}, together with `RequireRole(...)` when a permission needs both{{ end }}. `HasClaim` anywhere, or `RequireClaim` outside an `IModulePolicies`, fails the architecture rule `OnlyModulePoliciesCheckClaims`. Reading a claim to show it (`FindFirst`) is fine; never compare it to decide access.
- Claims come from the token {{ token_issuer }} issues, never from request input: do not add claims from headers, query strings or bodies.
- To add a permission: add a constant to the module's `<Module>PolicyNames` in Application and a policy to its `<Module>Policies` in Api (`policies.AddPolicy(BillingPolicyNames.ApproveInvoices, policy => policy.RequireClaim("department", "finance"));`), and name it with `.RequireAuthorization(BillingPolicyNames.ApproveInvoices)`.
- {{ add_claim_rule }} The pack's ADR has the steps.
- Every new endpoint that requires a claim policy gets an integration test with a user who meets it, one who does not (403) and no token (401), like `<Module>ClaimTests`.
- Local users: `developer` / `developer` is in `department=finance`{{ if roles }} and holds `manager`{{ end }}; `analyst` is in `finance`{{ if roles }} without a role{{ end }}; `clerk` is in `sales`{{ if roles }} and holds `manager`{{ end }}; `visitor` has no department. Each module's sample policy `<module>.manage` requires {{ if roles }}`manager` and {{ end }}`department=finance`.
