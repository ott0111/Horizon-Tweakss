BEGIN;

CREATE TABLE IF NOT EXISTS users (
    id UUID PRIMARY KEY,
    display_name VARCHAR(80) NOT NULL,
    profile_picture_url TEXT NULL,
    primary_email TEXT NULL,
    primary_email_normalized TEXT NULL,
    email_verified_at TIMESTAMPTZ NULL,
    onboarding_step INTEGER NOT NULL DEFAULT 0 CHECK (onboarding_step >= 0),
    onboarding_completed BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE UNIQUE INDEX IF NOT EXISTS users_primary_email_normalized_unique
    ON users(primary_email_normalized)
    WHERE primary_email_normalized IS NOT NULL;

CREATE TABLE IF NOT EXISTS auth_identities (
    id UUID PRIMARY KEY,
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    provider VARCHAR(24) NOT NULL CHECK (provider IN ('email', 'google', 'discord', 'epic')),
    provider_account_id TEXT NOT NULL,
    provider_email TEXT NULL,
    provider_display_name TEXT NULL,
    provider_avatar_url TEXT NULL,
    linked_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    last_used_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE(provider, provider_account_id),
    UNIQUE(user_id, provider)
);

CREATE TABLE IF NOT EXISTS password_credentials (
    user_id UUID PRIMARY KEY REFERENCES users(id) ON DELETE CASCADE,
    password_hash TEXT NOT NULL,
    password_changed_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS sessions (
    id UUID PRIMARY KEY,
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    access_token_hash CHAR(64) NOT NULL UNIQUE,
    refresh_token_hash CHAR(64) NOT NULL UNIQUE,
    access_expires_at TIMESTAMPTZ NOT NULL,
    refresh_expires_at TIMESTAMPTZ NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    last_seen_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    revoked_at TIMESTAMPTZ NULL,
    device_name TEXT NULL,
    user_agent TEXT NULL
);

CREATE INDEX IF NOT EXISTS sessions_user_active_idx ON sessions(user_id, refresh_expires_at) WHERE revoked_at IS NULL;

CREATE TABLE IF NOT EXISTS account_tokens (
    id UUID PRIMARY KEY,
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    purpose VARCHAR(32) NOT NULL CHECK (purpose IN ('email_verification', 'password_reset')),
    token_hash CHAR(64) NOT NULL UNIQUE,
    expires_at TIMESTAMPTZ NOT NULL,
    consumed_at TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS account_tokens_lookup_idx ON account_tokens(purpose, token_hash, expires_at) WHERE consumed_at IS NULL;

CREATE TABLE IF NOT EXISTS oauth_attempts (
    id UUID PRIMARY KEY,
    provider VARCHAR(24) NOT NULL CHECK (provider IN ('google', 'discord', 'epic')),
    state_hash CHAR(64) NOT NULL UNIQUE,
    client_state TEXT NOT NULL,
    desktop_redirect_uri TEXT NOT NULL,
    link_user_id UUID NULL REFERENCES users(id) ON DELETE CASCADE,
    expires_at TIMESTAMPTZ NOT NULL,
    consumed_at TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS desktop_authorization_codes (
    id UUID PRIMARY KEY,
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    code_hash CHAR(64) NOT NULL UNIQUE,
    desktop_redirect_uri TEXT NOT NULL,
    is_new_account BOOLEAN NOT NULL DEFAULT FALSE,
    expires_at TIMESTAMPTZ NOT NULL,
    consumed_at TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

ALTER TABLE desktop_authorization_codes ADD COLUMN IF NOT EXISTS is_new_account BOOLEAN NOT NULL DEFAULT FALSE;

CREATE INDEX IF NOT EXISTS desktop_codes_lookup_idx ON desktop_authorization_codes(code_hash, expires_at) WHERE consumed_at IS NULL;

COMMIT;
