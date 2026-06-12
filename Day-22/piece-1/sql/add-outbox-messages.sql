-- Day-20: Add OutboxMessages table for the Transactional Outbox Pattern.
--
-- Why this script is needed:
--   The application uses EnsureCreated() for SQL Server, which creates the full
--   schema only on a brand-new database.  On an existing database it is a no-op,
--   so new entities added to the EF model must be created with an explicit script.
--
-- This script is idempotent: safe to re-run if the table already exists.

IF NOT EXISTS (
    SELECT 1
    FROM   sys.objects
    WHERE  object_id = OBJECT_ID(N'[dbo].[OutboxMessages]')
      AND  type      = N'U'
)
BEGIN
    CREATE TABLE [dbo].[OutboxMessages] (
        -- Surrogate PK; relay orders by CreatedAt but Id is also a stable identity.
        [Id]          int            NOT NULL IDENTITY(1,1),

        -- Discriminator e.g. "QuoteCreated". Allows future event types to share
        -- this single table without ambiguity.
        [MessageType] nvarchar(100)  NOT NULL,

        -- Full JSON payload serialised once at write time. The relay never
        -- needs to re-query the domain entity.
        [Payload]     nvarchar(max)  NOT NULL,

        -- Stable GUID chosen at write time and reused as the Service Bus MessageId
        -- on every publish attempt, enabling idempotent deduplication downstream.
        [MessageId]   nvarchar(128)  NOT NULL,

        -- Relay ordering: oldest events published first to preserve causal order.
        [CreatedAt]   datetime2      NOT NULL,

        -- NULL  = not yet published (relay must process this row).
        -- Non-null = relay successfully sent the message; row is inert.
        [ProcessedAt] datetime2      NULL,

        -- Last publish error for operator diagnostics.
        -- Row is retried on the next poll cycle regardless.
        [Error]       nvarchar(500)  NULL,

        CONSTRAINT [PK_OutboxMessages] PRIMARY KEY CLUSTERED ([Id] ASC)
    );

    -- The relay queries WHERE ProcessedAt IS NULL on every poll cycle.
    -- Without this index that is a full-table scan; with it, a fast seek.
    CREATE NONCLUSTERED INDEX [IX_OutboxMessages_ProcessedAt]
        ON [dbo].[OutboxMessages] ([ProcessedAt] ASC);

    PRINT 'OutboxMessages table + index created successfully.';
END
ELSE
BEGIN
    PRINT 'OutboxMessages table already exists — nothing to do.';
END
