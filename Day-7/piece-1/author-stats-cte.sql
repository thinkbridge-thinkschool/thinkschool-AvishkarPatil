USE QuotesDb;
GO

WITH QuoteStats AS (
    SELECT AuthorID, COUNT(*) AS QuoteCount
    FROM dbo.Quotes
    GROUP BY AuthorID
),
RankedQuotes AS (
    SELECT
        AuthorID,
        QuoteText,
        CreatedDate,
        ROW_NUMBER() OVER (
            PARTITION BY AuthorID
            ORDER BY CreatedDate DESC, QuoteID DESC   -- deterministic tiebreak
        ) AS rn
    FROM dbo.Quotes
)
SELECT TOP (10)
    a.AuthorName,
    qs.QuoteCount,
    rq.QuoteText   AS MostRecentQuote,
    rq.CreatedDate AS MostRecentDate
FROM dbo.Authors  a
INNER JOIN QuoteStats   qs ON qs.AuthorID = a.AuthorID
INNER JOIN RankedQuotes rq ON rq.AuthorID = a.AuthorID AND rq.rn = 1
ORDER BY qs.QuoteCount DESC, a.AuthorName ASC;