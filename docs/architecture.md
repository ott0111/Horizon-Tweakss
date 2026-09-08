# Architecture

`Horizon.App` is the composition root and contains only WPF presentation concerns. ViewModels depend on interfaces in `Horizon.Core`; they do not call PowerShell, the registry, SQLite, or the file system directly.

The tweak flow is:

1. `ICompatibilityScanner` combines catalogue metadata, detected hardware/software, entitlements, the operation registry, and the active-change journal.
2. `ITweakEngine` resolves the requested definitions and serializes execution through one gate.
3. Each `ITweakOperation` reads its current value, captures an exact backup, applies the desired state, reads again, and verifies.
4. `IActiveChangeStore` retains previous and applied values independently of display history.
5. `IHistoryService` writes the completed session and individual results to SQLite.
6. Restore uses the captured previous value, reads the result back, and removes the active backup only after successful verification.

`Horizon.System` owns Windows inventory and the encoded PowerShell command runner. `Horizon.Services` owns cleanup, AppX, Startup, benchmark, purchase/update adapters, and automatic Gaming Mode. `Horizon.Restore` orchestrates undo-last, session restore, and restore-all.

Authentication is implemented by `backend/`, an Express/TypeScript service backed by PostgreSQL. It owns users, provider identities, scrypt password credentials, hashed opaque sessions, account tokens, OAuth attempts, and one-time desktop authorization codes. The desktop service calls only Horizon endpoints and protects its Horizon refresh credential with current-user Windows DPAPI.

Provider OAuth uses the system browser. The backend callback exchanges the provider code and consumes provider profile data; provider secrets and provider tokens never enter WPF. A short-lived single-use Horizon code is redirected to a random `127.0.0.1` desktop listener and exchanged for a Horizon session. Matching display names never merge accounts, and matching email addresses require explicit linking when another Horizon user already owns the address.

Checkout, update manifests, and licensing APIs remain behind service contracts so production endpoints can evolve without moving logic into views.
