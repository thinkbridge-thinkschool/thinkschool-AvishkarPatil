-- ── 1. Create DB (skip if it already exists) ──────────────────────────────
IF DB_ID('quotes_db') IS NULL
    CREATE DATABASE quotes_db;
GO

USE quotes_db;
GO

-- ── 2. Create table ────────────────────────────────────────────────────────
DROP TABLE IF EXISTS quotes;

CREATE TABLE quotes (
    quote_id   INT IDENTITY(1,1) PRIMARY KEY,
    author     NVARCHAR(100)  NOT NULL,
    body       NVARCHAR(500)  NOT NULL,
    created_at DATE           NOT NULL
);
GO

-- ── 3. Seed 50 rows ────────────────────────────────────────────────────────
INSERT INTO quotes (author, body, created_at) VALUES
  -- Seneca
  ('Seneca', 'We suffer more in imagination than in reality.',            '2024-01-03'),
  ('Seneca', 'Luck is what happens when preparation meets opportunity.',  '2024-01-10'),
  ('Seneca', 'Begin at once to live.',                                    '2024-01-18'),
  ('Seneca', 'No man was ever wise by chance.',                           '2024-01-25'),
  ('Seneca', 'Difficulties strengthen the mind as labor does the body.',  '2024-02-02'),
  ('Seneca', 'A sword never kills anybody; it is a tool in the hand.',    '2024-02-09'),
  ('Seneca', 'It is not that I am brave, it is that I am busy.',          '2024-02-17'),
  ('Seneca', 'Retire into yourself as much as possible.',                 '2024-02-24'),
  ('Seneca', 'The time will come when diligent research will bring.',     '2024-03-03'),
  ('Seneca', 'He who is brave is free.',                                  '2024-03-10'),
  -- Marcus Aurelius
  ('Marcus', 'You have power over your mind, not outside events.',        '2024-01-05'),
  ('Marcus', 'The impediment to action advances action.',                 '2024-01-14'),
  ('Marcus', 'Waste no more time arguing what a good man should be.',     '2024-01-22'),
  ('Marcus', 'Very little is needed to make a happy life.',               '2024-01-30'),
  ('Marcus', 'Accept the things to which fate binds you.',                '2024-02-07'),
  ('Marcus', 'If it is not right, do not do it.',                         '2024-02-15'),
  ('Marcus', 'The best revenge is to be unlike him who performed it.',    '2024-02-22'),
  ('Marcus', 'Nowhere can man find a quieter or more untroubled retreat.','2024-03-01'),
  ('Marcus', 'Do not indulge in dreams of what you do not have.',         '2024-03-08'),
  ('Marcus', 'When you wake up in the morning, think of the privilege.',  '2024-03-15'),
  -- Epictetus
  ('Epictetus', 'It is not what happens to you, but how you react.',      '2024-01-07'),
  ('Epictetus', 'Make the best use of what is in your power.',            '2024-01-20'),
  ('Epictetus', 'He is a wise man who does not grieve for what he lacks.','2024-01-28'),
  ('Epictetus', 'First say to yourself what you would be, then do.',      '2024-02-05'),
  ('Epictetus', 'Seek not the good in external things; seek it in yourself.','2024-02-13'),
  ('Epictetus', 'Men are disturbed not by things but by their opinions.', '2024-02-20'),
  ('Epictetus', 'No man is free who is not master of himself.',           '2024-02-28'),
  ('Epictetus', 'Practice yourself in little things.',                    '2024-03-06'),
  ('Epictetus', 'We cannot choose our external circumstances.',           '2024-03-13'),
  ('Epictetus', 'The key is to keep company only with people who uplift you.','2024-03-20'),
  -- Aristotle
  ('Aristotle', 'We are what we repeatedly do. Excellence is a habit.',   '2024-01-08'),
  ('Aristotle', 'The more you know, the more you know you do not know.',  '2024-01-16'),
  ('Aristotle', 'Knowing yourself is the beginning of all wisdom.',       '2024-01-24'),
  ('Aristotle', 'It is the mark of an educated mind to entertain a thought.','2024-02-01'),
  ('Aristotle', 'Happiness depends upon ourselves.',                      '2024-02-10'),
  ('Aristotle', 'Quality is not an act, it is a habit.',                  '2024-02-18'),
  ('Aristotle', 'Hope is a waking dream.',                                '2024-02-26'),
  ('Aristotle', 'Patience is bitter, but its fruit is sweet.',            '2024-03-05'),
  ('Aristotle', 'To perceive is to suffer.',                              '2024-03-12'),
  ('Aristotle', 'Education is the best provision for old age.',           '2024-03-19'),
  -- Plato
  ('Plato', 'Be kind, for everyone you meet is fighting a hard battle.',  '2024-01-09'),
  ('Plato', 'Wise men talk because they have something to say.',          '2024-01-17'),
  ('Plato', 'The measure of a man is what he does with power.',           '2024-01-25'),
  ('Plato', 'Opinion is the medium between knowledge and ignorance.',     '2024-02-03'),
  ('Plato', 'Good actions give strength to ourselves and inspire good.',  '2024-02-11'),
  ('Plato', 'Courage is knowing what not to fear.',                       '2024-02-19'),
  ('Plato', 'There is no harm in repeating a good thing.',                '2024-02-27'),
  ('Plato', 'The greatest wealth is to live content with little.',        '2024-03-07'),
  ('Plato', 'Every heart sings a song, incomplete, until another sings.', '2024-03-14'),
  ('Plato', 'We can easily forgive a child who is afraid of the dark.',   '2024-03-21');
GO