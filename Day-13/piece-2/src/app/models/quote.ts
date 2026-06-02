// Domain models — mirror EXACTLY what the Week-1 API returns.
//
//   GET /api/quotes?page={n}&size={n}   →  Quote[]   (list; !IsDeleted, paged)
//   GET /api/quotes/{id}                →  Quote     (detail) or 404
//
// The JSON is the serialised EF entity (QuotesApi/Models/Quote.cs), so the
// field names here must match its public properties one-for-one. No `any`
// anywhere — list and detail share this single typed shape.

export interface Quote {
  id:        number;
  author:    string;
  text:      string;        // NOT "body"/"content" — the entity property is Text
  createdAt: string;        // ISO-8601 (DateTime serialised)
  isDeleted: boolean;       // present on the entity; API already filters these out
  ownerId:   number | null; // int? on the server → number | null here
}
