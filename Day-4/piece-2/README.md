# Day 4 · Piece 2 — Drive Auth Codebase to 96% Line Coverage

## Coverage Report Summary

> Source: `Quotes.Tests.Integration` — latest cobertura run (`coverage.cobertura.xml`)

| Metric | Value |
|---|---|
| Line coverage | **96.31%** (627 / 651 lines) |
| Branch coverage | **74.54%** (82 / 110 branches) |
| Coverable lines | 651 |
| Total complexity | 180 |

### Per-class branch coverage (notable gaps)

| Class | File | Line Rate | Branch Rate |
|---|---|---|---|
| `QuoteOwnerHandler` | `Authorization/QuoteOwnerRequirement.cs` | 100% | 62.5% |
| `TokenService` | `Services/TokenService.cs` | 96.4% | 66.7% |
| `User` | `Models/User.cs` | 85.0% | 60.0% |
| `AppDbContext` | `Data/AppDbContext.cs` | 100% | 50.0% |
| `DbSeeder` | `Data/DbSeeder.cs` | 90.9% | 50.0% |

---

## Which uncovered branch surprised me most?

The branch that surprised me most was inside `QuoteOwnerHandler` (`Authorization/QuoteOwnerRequirement.cs`). It had **100% line coverage but only 62.5% branch coverage** — every line executed, yet some conditional paths were never walked. The handler checks whether the resource's owner ID matches the authenticated user's `sub` claim; but the branch that fires when the claim is **missing entirely** (the `null` / not-found path) was never exercised by any test. That is exactly the path an attacker would probe first — submitting a request with a stripped or forged token where the claim isn't present at all.

What I learned: **line coverage is a weak proxy for confidence in security-critical code.** Covering a branch means forcing both the `true` and `false` outcomes of every decision point, not just stepping through lines. After this exercise I now check branch coverage specifically — not just line coverage — for any authorization or validation handler before calling it done.

---

## Links

- **Repository:** [https://github.com/thinkbridge-thinkschool/thinkschool-AvishkarPatil](https://github.com/thinkbridge-thinkschool/thinkschool-AvishkarPatil)
- **Folder:** [Day-4/piece-2](https://github.com/thinkbridge-thinkschool/thinkschool-AvishkarPatil/tree/main/Day-4/piece-2)
- **Cobertura XML (committed):** [Quotes.Tests.Integration/TestResults/e158a4b7.../coverage.cobertura.xml](https://github.com/thinkbridge-thinkschool/thinkschool-AvishkarPatil/blob/main/Day-4/piece-2/Quotes.Tests.Integration/TestResults/e158a4b7-4de0-4b0b-8e0b-43fc6760a7c2/coverage.cobertura.xml)
- **CI Run:** [GitHub Actions — main branch](https://github.com/thinkbridge-thinkschool/thinkschool-AvishkarPatil/actions)
