------------------------------------------------------------
-- 1. CREATE DATABASE
------------------------------------------------------------
IF DB_ID('QuotesDB') IS NULL
    CREATE DATABASE QuotesDB;
GO

USE QuotesDB;
GO

------------------------------------------------------------
-- 2. CREATE TABLES
------------------------------------------------------------
IF OBJECT_ID('dbo.Quotes',  'U') IS NOT NULL DROP TABLE dbo.Quotes;
IF OBJECT_ID('dbo.Authors', 'U') IS NOT NULL DROP TABLE dbo.Authors;
GO

CREATE TABLE dbo.Authors (
    AuthorID    INT IDENTITY(1,1) PRIMARY KEY,
    AuthorName  NVARCHAR(100) NOT NULL UNIQUE
);

CREATE TABLE dbo.Quotes (
    QuoteID     INT IDENTITY(1,1) PRIMARY KEY,
    AuthorID    INT           NOT NULL,
    QuoteText   NVARCHAR(500) NOT NULL,
    CreatedDate DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_Quotes_Authors FOREIGN KEY (AuthorID)
        REFERENCES dbo.Authors(AuthorID)
);

CREATE INDEX IX_Quotes_Author_Date
    ON dbo.Quotes (AuthorID, CreatedDate DESC);
GO

------------------------------------------------------------
-- 3. INSERT DATA  (7 authors, 19 quotes)
------------------------------------------------------------
INSERT INTO dbo.Authors (AuthorName) VALUES
('Albert Einstein'),
('Mark Twain'),
('Maya Angelou'),
('Oscar Wilde'),
('Mahatma Gandhi'),
('Confucius'),
('Steve Jobs'),
('Nelson Mandela'),
('Aristotle'),
('Benjamin Franklin');


INSERT INTO dbo.Quotes (AuthorID, QuoteText, CreatedDate) VALUES
-- Albert Einstein (3)
(1, N'Imagination is more important than knowledge.',                                            '2024-01-15'),
(1, N'Life is like riding a bicycle. To keep your balance, you must keep moving.',               '2024-06-20'),
(1, N'The important thing is not to stop questioning.',                                          '2025-03-10'),
-- Mark Twain (3)
(2, N'The secret of getting ahead is getting started.',                                          '2024-02-12'),
(2, N'Whenever you find yourself on the side of the majority, it is time to pause and reflect.', '2024-09-05'),
(2, N'Kindness is the language which the deaf can hear and the blind can see.',                  '2025-04-18'),
-- Maya Angelou (3)
(3, N'There is no greater agony than bearing an untold story inside you.',                       '2024-03-22'),
(3, N'If you don''t like something, change it. If you can''t change it, change your attitude.',  '2024-11-11'),
(3, N'Try to be a rainbow in someone''s cloud.',                                                 '2025-02-14'),
-- Oscar Wilde (3)
(4, N'Be yourself; everyone else is already taken.',                                             '2024-04-01'),
(4, N'We are all in the gutter, but some of us are looking at the stars.',                       '2024-08-19'),
(4, N'I can resist everything except temptation.',                                               '2025-05-07'),
-- Mahatma Gandhi (2)
(5, N'Be the change that you wish to see in the world.',                                         '2024-05-30'),
(5, N'The weak can never forgive. Forgiveness is the attribute of the strong.',                  '2025-01-25'),
-- Confucius (3)
(6, N'It does not matter how slowly you go as long as you do not stop.',                         '2024-07-14'),
(6, N'Our greatest glory is not in never falling, but in rising every time we fall.',            '2024-10-08'),
(6, N'Wherever you go, go with all your heart.',                                                 '2025-03-30'),
-- Steve Jobs (2)
(7, N'Stay hungry, stay foolish.',                                                               '2024-06-05'),
(7, N'Innovation distinguishes between a leader and a follower.',                                '2025-04-22'),
(8, N'It always seems impossible until it is done.', '2025-05-01'),
(9, N'We are what we repeatedly do. Excellence, then, is not an act but a habit.', '2025-05-02'),
(10, N'An investment in knowledge pays the best interest.', '2025-05-03');
GO
GO

------------------------------------------------------------
-- 4. QUICK SANITY CHECK
------------------------------------------------------------
SELECT * FROM dbo.Authors;
SELECT * FROM dbo.Quotes ORDER BY AuthorID, CreatedDate;