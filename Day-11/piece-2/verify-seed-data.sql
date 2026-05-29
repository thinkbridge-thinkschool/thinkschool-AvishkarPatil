USE QuotesApiPerf;
SELECT 'Quotes' AS [Table], COUNT(*) AS [Rows] FROM Quotes
UNION ALL SELECT 'Collections',     COUNT(*) FROM Collections
UNION ALL SELECT 'CollectionItems', COUNT(*) FROM CollectionItems
UNION ALL SELECT 'Users',           COUNT(*) FROM Users;
