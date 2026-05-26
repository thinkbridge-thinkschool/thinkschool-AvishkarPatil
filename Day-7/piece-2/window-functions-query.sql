SELECT
    author,
    created_at,
    LEFT(body, 45) + '…'                                          AS body_snippet,

    ROW_NUMBER() OVER (PARTITION BY author ORDER BY created_at, quote_id)  AS quote_seq,
    RANK()       OVER (PARTITION BY author ORDER BY created_at)            AS quote_rank,
    SUM(1)       OVER (PARTITION BY author ORDER BY created_at
                       ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW)   AS running_count,

    LAG(created_at)  OVER (PARTITION BY author ORDER BY created_at)        AS prev_quote_date,

    -- DATEDIFF replaces the PostgreSQL  date - date  subtraction
    DATEDIFF(DAY,
        LAG(created_at) OVER (PARTITION BY author ORDER BY created_at),
        created_at)                                               AS days_since_prev,

    LEAD(created_at) OVER (PARTITION BY author ORDER BY created_at)        AS next_quote_date

FROM  quotes
ORDER BY author, created_at;