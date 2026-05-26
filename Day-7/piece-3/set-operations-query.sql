-- ════════════════════════════════════════════════════════
-- Q1: Authors who have at least one quote BUT no tag links
-- Operator: EXCEPT
-- Why: Start with the full set of "authors with quotes",
--      then subtract those who appear in quote_tags.
--      EXCEPT removes every row from Set A that appears in Set B.
-- ════════════════════════════════════════════════════════
USE QuoteDB;
GO

SELECT a.author_id, a.name
FROM   authors a
JOIN   quotes  q ON q.author_id = a.author_id       -- Set A: has at least one quote

EXCEPT

SELECT a.author_id, a.name
FROM   authors a
JOIN   quotes     q  ON q.author_id  = a.author_id
JOIN   quote_tags qt ON qt.quote_id  = q.quote_id;  -- Set B: has at least one tag


-- ════════════════════════════════════════════════════════
-- Q2: Tags whose names appear in BOTH the 'philosophy'
--     AND 'motivation' category buckets.
-- Operator: INTERSECT
-- Why: INTERSECT returns only the rows that exist in
--      *every* result set — the overlap region of a Venn
--      diagram. Here we ask: which tag names show up on
--      the philosophy list AND also on the motivation list?
--      A JOIN could do this, but INTERSECT states the
--      intent in plain set language and de-duplicates for
--      free — no DISTINCT needed.
-- ════════════════════════════════════════════════════════


SELECT name FROM tags WHERE category = 'philosophy'
INTERSECT
SELECT name FROM tags WHERE category IN ('philosophy','motivation');




-- ════════════════════════════════════════════════════════
-- Q3: All distinct tags used by either 'philosophy' or
--     'motivation' category, combined into one list
-- Operator: UNION  (deduplicates automatically)
-- Why: We want a merged, de-duped list from two category
--      buckets. UNION ALL would keep duplicates; UNION
--      gives us the distinct combined set — exactly right
--      for a "master tag list across categories" report.
-- ════════════════════════════════════════════════════════

SELECT t.name AS tag_name,  t.category
FROM   tags t
WHERE  t.category = 'philosophy'

UNION

SELECT t.name, t.category
FROM   tags t
WHERE  t.category = 'motivation';