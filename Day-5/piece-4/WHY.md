# Why policies, not roles?

`RequireRole("admin")` sounds reasonable until you ask: *what does "admin" mean for this specific endpoint?* The answer changes as the system grows, so the check becomes a guess. A policy names the *capability* the caller needs — `can-edit-quotes` — and the rule encoding that capability lives in one place: `AddPolicy(...)` in `InfrastructureExtensions.cs`. The endpoint declares intent; the policy defines the rule.

## Policy 1 — claim-based (`can-edit-quotes`)

```csharp
options.AddPolicy("can-edit-quotes",
    p => p.RequireClaim("scope", "quotes.write"));
```

The token carries `scope: quotes.write` only when the user's `Role` is `"writer"`. A viewer's token is structurally identical except for that claim's absence. The POST endpoint doesn't know about roles at all — it sees only the policy:

```csharp
.RequireAuthorization("can-edit-quotes")
```

**Why a `scope` claim rather than a `role` claim?** Scopes describe *what the token may do*; roles describe *who the user is*. When the same user gets a narrower token (e.g., a machine-to-machine flow) the role claim would still grant write access. The scope claim only travels if the token was explicitly minted for that capability.

## Policy 2 — custom requirement (`can-delete-own-quote`)

```csharp
options.AddPolicy("can-delete-own-quote",
    p => p.Requirements.Add(new QuoteOwnerRequirement()));
```

This rule cannot be expressed as a claim check because it depends on the *resource* being acted on. `QuoteOwnerHandler` receives the `Quote` object and the `ClaimsPrincipal` together:

```csharp
protected override Task HandleRequirementAsync(
    AuthorizationHandlerContext context,
    QuoteOwnerRequirement requirement,
    Quote resource)
{
    if (resource.OwnerId?.ToString() == context.User.FindFirstValue("sub"))
        context.Succeed(requirement);
    return Task.CompletedTask;
}
```

The endpoint loads the quote, then calls `IAuthorizationService.AuthorizeAsync(user, quote, "can-delete-own-quote")`. If the handler doesn't call `Succeed`, the framework returns 403. The policy name decouples the endpoint from the specific check — swapping the rule (e.g., allowing admins to delete anything) means editing the handler, not the endpoint.

**The important asymmetry:** the handler calls `Succeed` but never `Fail`. Not calling `Succeed` is enough to deny. Calling `Fail` would short-circuit even if another handler for the same requirement would have succeeded — only use it when you want guaranteed denial regardless of other handlers.
