-- Reset the 10 seeded sample tickets to their initial state so they can be re-processed by the agent.
-- Idempotent: safe to run repeatedly. Works whether the rows were edited or deleted.
-- Also clears agent runs/steps for these tickets (AgentSteps cascade-delete with their AgentRun).
--
-- Run with: psql "<connection-string>" -f scripts/reset-seed-tickets.sql
--   (or paste into pgAdmin / the Neon SQL editor)

BEGIN;

-- 1. Remove prior agent activity for these tickets (steps cascade from runs).
DELETE FROM "AgentRuns"
WHERE "TicketId" IN (
    '11111111-1111-1111-1111-111111111101',
    '11111111-1111-1111-1111-111111111102',
    '11111111-1111-1111-1111-111111111103',
    '11111111-1111-1111-1111-111111111104',
    '11111111-1111-1111-1111-111111111105',
    '11111111-1111-1111-1111-111111111106',
    '11111111-1111-1111-1111-111111111107',
    '11111111-1111-1111-1111-111111111108',
    '11111111-1111-1111-1111-111111111109',
    '11111111-1111-1111-1111-111111111110'
);

-- 2. Upsert the tickets back to their seeded values with Status='New', no draft.
INSERT INTO "Tickets"
    ("Id", "Subject", "Body", "RequesterEmail", "Category", "Urgency", "Status", "DraftReply", "CreatedAtUtc", "UpdatedAtUtc")
VALUES
    ('11111111-1111-1111-1111-111111111101', 'Cannot login to portal',
     'I keep getting ''invalid credentials'' on the staff portal even though my password is correct.',
     'dewi@contoso.com', 'AccountAccess', 'High', 'New', NULL, TIMESTAMPTZ '2025-01-01 09:00:00+00', NULL),
    ('11111111-1111-1111-1111-111111111102', 'VPN keeps disconnecting',
     'The corporate VPN drops every few minutes when I work from home, making it hard to stay connected.',
     'budi@contoso.com', 'Network', 'Medium', 'New', NULL, TIMESTAMPTZ '2025-01-01 09:00:00+00', NULL),
    ('11111111-1111-1111-1111-111111111103', 'Request: install Visual Studio',
     'Please install Visual Studio 2022 Professional on my workstation for the new project.',
     'arif@contoso.com', 'Software', 'Low', 'New', NULL, TIMESTAMPTZ '2025-01-01 09:00:00+00', NULL),
    ('11111111-1111-1111-1111-111111111104', 'Printer offline 3rd floor',
     'The shared printer near the 3rd floor kitchen shows as offline and nobody can print.',
     'siti@contoso.com', 'Hardware', 'Medium', 'New', NULL, TIMESTAMPTZ '2025-01-01 09:00:00+00', NULL),
    ('11111111-1111-1111-1111-111111111105', 'URGENT: production app down for all users',
     'The order-management application is returning 500 errors for everyone. This is a full outage affecting production.',
     'ops@contoso.com', 'Software', 'Critical', 'New', NULL, TIMESTAMPTZ '2025-01-01 09:00:00+00', NULL),
    ('11111111-1111-1111-1111-111111111106', 'Forgot password',
     'I forgot my Windows password and cannot sign in to my laptop. Please help me reset it.',
     'rina@contoso.com', 'AccountAccess', 'Medium', 'New', NULL, TIMESTAMPTZ '2025-01-01 09:00:00+00', NULL),
    ('11111111-1111-1111-1111-111111111107', 'Email not syncing on phone',
     'My Outlook mailbox stopped syncing on my iPhone since yesterday; new messages only appear on the desktop.',
     'tono@contoso.com', 'Email', 'Low', 'New', NULL, TIMESTAMPTZ '2025-01-01 09:00:00+00', NULL),
    ('11111111-1111-1111-1111-111111111108', 'New laptop request',
     'Onboarding a new analyst next week; please provision a standard laptop and accessories.',
     'hr@contoso.com', 'Hardware', 'Low', 'New', NULL, TIMESTAMPTZ '2025-01-01 09:00:00+00', NULL),
    ('11111111-1111-1111-1111-111111111109', 'Suspected data breach in finance share',
     'Several files on the finance share were renamed with a strange extension and a ransom note appeared. Possible ransomware.',
     'finance@contoso.com', 'Other', 'Critical', 'New', NULL, TIMESTAMPTZ '2025-01-01 09:00:00+00', NULL),
    ('11111111-1111-1111-1111-111111111110', 'Wi-Fi down across 2nd floor',
     'Wireless access points on the 2nd floor are unreachable; about 20 people have no connectivity.',
     'facilities@contoso.com', 'Network', 'High', 'New', NULL, TIMESTAMPTZ '2025-01-01 09:00:00+00', NULL)
ON CONFLICT ("Id") DO UPDATE SET
    "Subject"        = EXCLUDED."Subject",
    "Body"           = EXCLUDED."Body",
    "RequesterEmail" = EXCLUDED."RequesterEmail",
    "Category"       = EXCLUDED."Category",
    "Urgency"        = EXCLUDED."Urgency",
    "Status"         = 'New',
    "DraftReply"     = NULL,
    "UpdatedAtUtc"   = NULL;

COMMIT;
