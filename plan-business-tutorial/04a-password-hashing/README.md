# Phase 4 — companion hashing tool (`04a-password-hashing`)

> This folder is a small helper for [Phase 4 — JWT Auth](../04-jwt-auth.md). The seed rows in
> [`Identity/003_seed.sql`](../03a-database-queries/Identity/003_seed.sql) ship with **placeholder**
> password hashes (`'<hash-of-Password123!>'`). Before you can log in, those placeholders have to be
> replaced with real PBKDF2 hashes. This script produces them **without** running the app.

## Why a Python script?

The real hashing lives in C# (`PasswordHasher.Hash`, Phase 4). But generating seed hashes doesn't need
the whole app booted — it just needs the *same algorithm with the same parameters*. [`hash_password.py`](hash_password.py)
is a byte-for-byte port: **PBKDF2-HMAC-SHA256**, a **16-byte** salt, a **32-byte** key, **600,000**
iterations, stored as `iterations.base64Salt.base64Hash`.

Because the format and parameters are identical, a hash produced here **verifies correctly** against the
C# `Verify` method — and vice-versa. Standard library only; nothing to `pip install`.

> 🔑 The stored string carries its own iteration count (`600000.…`), which is exactly why this works:
> `Verify` reads the parameters back out of the stored value, so it doesn't care *which* language wrote
> the hash — only that the bytes line up.

## Usage

```bash
# Hash the default seed password ("Password123!") — prints one line:
python hash_password.py

# Hash a specific password:
python hash_password.py "MySecret!"

# Print a full INSERT block ready to paste over the placeholder in
# 03a-database-queries/Identity/003_seed.sql (a fresh, independently-salted hash per user):
python hash_password.py --seed
```

Copy the `--seed` output over the placeholder `INSERT` in
[`Identity/003_seed.sql`](../03a-database-queries/Identity/003_seed.sql), then re-run that seed script.
All three demo users (`rep`, `manager`, `admin`) share the password `Password123!`.

> ⚠️ Each run generates a **new random salt**, so the hashes change every time and all three seed rows
> differ — even though the password is the same. That's the salt doing its job (Phase 4, *"Why do we use
> a salt?"*), not a bug. Any of these hashes will still verify against `Password123!`.

> 🚫 This is a **local dev convenience** for seeding a teaching database. It is not part of the built
> solution and isn't wired into DI or the app — the app always hashes through the C# `PasswordHasher`.
