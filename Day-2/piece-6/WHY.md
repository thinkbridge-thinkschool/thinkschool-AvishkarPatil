# Why the Rich Domain Model?

The anemic `Quote` was a plain data bag — public setters on every field, no validation, no behavior. Any code anywhere in the codebase could write `quote.Text = ""` or `quote.Author = new string('x', 5000)` and the object would silently accept it. Business rules lived in the endpoint, which meant they were easy to forget and impossible to enforce from other call sites.

The rich model moves those rules into the only place that can't be bypassed: construction. `Quote.Create()` is the single gate through which every `Quote` ever born must pass. Once it exits that factory, its `Text` is immutable and its invariants are guaranteed — not by convention, but by the type system. There's no setter to call, no workaround to reach for.

**What did this buy concretely?**

- **Immutability of Text**: The field has a private setter. No future endpoint, background job, or migration script can overwrite quote text after creation. The anemic version had no such protection.
- **Consistent validation**: Author and Text constraints live in one place. If the rule changes, there's one edit, not a hunt across every endpoint that constructs a Quote.
- **Soft-delete as a domain operation**: `SoftDelete()` makes the intent explicit. Calling code can't accidentally set `IsDeleted = false` to "undelete" — there's no such method.

**The bug the anemic version would have shipped:**

The original endpoint validated that Author and Text were non-empty, but used `[MinLength(2)]` and `[MinLength(5)]` on the DTO. A second endpoint added later — say, a bulk-import endpoint — would instantiate `Quote` directly, skip the DTO entirely, and happily write a one-character author or a 50,000-character text to the database. The rich model makes that impossible: the factory throws regardless of who calls it.
