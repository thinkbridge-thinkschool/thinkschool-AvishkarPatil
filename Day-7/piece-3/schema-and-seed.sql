-- ============================================================
-- Day 7 — Set Operations | SQL Server
-- Schema: QuoteDB
-- ============================================================

USE master;
GO

IF DB_ID('QuoteDB') IS NOT NULL
    DROP DATABASE QuoteDB;
GO
CREATE DATABASE QuoteDB;
GO
USE QuoteDB;
GO

-- ────────────────────────────────────────────────
-- TABLES
-- ────────────────────────────────────────────────

CREATE TABLE authors (
    author_id   INT PRIMARY KEY IDENTITY(1,1),
    name        VARCHAR(100)  NOT NULL,
    birth_year  INT,
    nationality VARCHAR(60),
    category    VARCHAR(20)   NOT NULL   -- 'classic' | 'modern' | 'contemporary'
                              CHECK (category IN ('classic','modern','contemporary')),
    is_active   BIT           NOT NULL DEFAULT 1,
    created_at  DATETIME      NOT NULL DEFAULT GETDATE()
);

CREATE TABLE quotes (
    quote_id    INT PRIMARY KEY IDENTITY(1,1),
    author_id   INT           NOT NULL REFERENCES authors(author_id),
    content     NVARCHAR(500) NOT NULL,
    year_said   INT,
    created_at  DATETIME      NOT NULL DEFAULT GETDATE()
);

CREATE TABLE tags (
    tag_id       INT PRIMARY KEY IDENTITY(1,1),
    name         VARCHAR(50)  NOT NULL UNIQUE,
    category     VARCHAR(30)  NOT NULL,  -- 'philosophy' | 'motivation' | 'literature' | 'science' ...
    created_at   DATETIME     NOT NULL DEFAULT GETDATE(),
    usage_count  INT          NOT NULL DEFAULT 0
);

CREATE TABLE quote_tags (
    quote_id  INT NOT NULL REFERENCES quotes(quote_id),
    tag_id    INT NOT NULL REFERENCES tags(tag_id),
    PRIMARY KEY (quote_id, tag_id)
);
GO

-- ────────────────────────────────────────────────
-- SEED DATA  (50 rows spread across all 4 tables)
-- ────────────────────────────────────────────────

-- 12 authors  (mix of classic / modern / contemporary)
INSERT INTO authors (name, birth_year, nationality, category) VALUES
('Aristotle',           -384, 'Greek',        'classic'),
('Marcus Aurelius',      121, 'Roman',         'classic'),
('William Shakespeare',  1564,'English',       'classic'),
('Jane Austen',          1775,'English',       'classic'),
('Friedrich Nietzsche',  1844,'German',        'classic'),
('Virginia Woolf',       1882,'English',       'modern'),
('Ernest Hemingway',     1899,'American',      'modern'),
('Albert Camus',         1913,'French',        'modern'),
('Maya Angelou',         1928,'American',      'modern'),
('Toni Morrison',        1931,'American',      'contemporary'),
('Haruki Murakami',      1949,'Japanese',      'contemporary'),
('Chimamanda Adichie',   1977,'Nigerian',      'contemporary');
GO

-- 20 quotes  (authors 1–12, some authors intentionally have NO tag links → Q1)
INSERT INTO quotes (author_id, content, year_said) VALUES
(1,  'The whole is more than the sum of its parts.',                      -350),
(1,  'We are what we repeatedly do.',                                     -330),
(2,  'You have power over your mind, not outside events.',                 170),
(2,  'The impediment to action advances action.',                          175),
(3,  'All the world is a stage.',                                         1599),
(3,  'To thine own self be true.',                                        1601),
(4,  'It is a truth universally acknowledged.',                           1813),
(5,  'Without music, life would be a mistake.',                           1889),
(6,  'You cannot find peace by avoiding life.',                           1929),
(6,  'A woman must have money and a room of her own.',                    1929),
(7,  'The world is a fine place and worth fighting for.',                 1940),
(8,  'In the midst of winter I found an invincible summer.',              1954),
(9,  'I know why the caged bird sings.',                                  1969),
(9,  'You may encounter many defeats, but you must not be defeated.',     1977),
(10, 'If you have some power, your job is to empower somebody else.',     2004),
(11, 'If you only read the books that everyone else is reading, you cannot read yourself.',1987),
(12, 'The single story creates stereotypes.',                             2009),
-- Authors 4 and 5 (Austen, Nietzsche) only have quotes — no quote_tags rows → appear in Q1
(4,  'Vanity working on a weak head produces every kind of mischief.',    1815),
(5,  'That which does not kill us, makes us stronger.',                   1888),
(10, 'Make a difference about something other than yourselves.',          2008);
GO

-- 12 tags
INSERT INTO tags (name, category) VALUES
('philosophy',    'philosophy'),
('stoicism',      'philosophy'),
('resilience',    'motivation'),
('identity',      'literature'),
('music',         'arts'),
('nature',        'science'),
('feminism',      'social'),
('existentialism','philosophy'),
('courage',       'motivation'),
('society',       'social'),
('reading',       'literature'),
('power',         'social');
GO

-- 18 quote_tag links
-- Deliberately exclude quote_ids 7,18,19 (Austen Q1&Q2, Nietzsche Q2)
-- so those authors have quotes but NO tags → appear in Q1
INSERT INTO quote_tags (quote_id, tag_id) VALUES
(1,  1),   -- Aristotle Q1 → philosophy
(2,  1),   -- Aristotle Q2 → philosophy
(3,  2),   -- Aurelius Q1  → stoicism
(3,  3),   -- Aurelius Q1  → resilience
(4,  2),   -- Aurelius Q2  → stoicism
(5,  4),   -- Shakespeare Q1 → identity
(6,  4),   -- Shakespeare Q2 → identity
(8,  5),   -- Nietzsche Q1  → music
(9,  7),   -- Woolf Q1      → feminism
(10, 7),   -- Woolf Q2      → feminism
(11, 3),   -- Hemingway Q1  → resilience
(12, 8),   -- Camus Q1      → existentialism
(13, 9),   -- Angelou Q1    → courage
(14, 3),   -- Angelou Q2    → resilience
(15, 12),  -- Morrison Q1   → power
(16, 11),  -- Murakami Q1   → reading
(17, 10),  -- Adichie Q1    → society
(20, 12);  -- Morrison Q2   → power
GO