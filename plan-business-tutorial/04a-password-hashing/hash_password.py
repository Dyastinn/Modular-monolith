#!/usr/bin/env python3
"""
Phase 4 companion — generate PBKDF2 password hashes for the seed data.

This is a byte-for-byte port of the ``PasswordHasher.Hash`` method from
[Phase 4 — JWT Auth](../04-jwt-auth.md). It exists so you can produce the
seed hashes for ``Identity/003_seed.sql`` WITHOUT having to spin up the app
and call ``Hash()`` in C#.

The stored format is identical to the C# version:

    iterations.base64Salt.base64Hash

so a hash produced here verifies correctly against the C# ``Verify`` method,
and vice-versa. The knobs below match the C# constants exactly:

    SaltSize   = 16      (16 random bytes mixed into every password)
    KeySize    = 32      (a 32-byte / 256-bit derived key)
    Iterations = 600_000 (the PBKDF2 work factor)
    algorithm  = SHA256

.NET's ``Rfc2898DeriveBytes.Pbkdf2(string password, ...)`` encodes the
password as UTF-8 before hashing, which is why we call
``password.encode("utf-8")`` below.

Usage
-----
    # Hash the default seed password and print one line:
    python hash_password.py

    # Hash a specific password:
    python hash_password.py "MySecret!"

    # Print a ready-to-paste INSERT block for Identity/003_seed.sql
    # (a fresh, independently-salted hash per user):
    python hash_password.py --seed

No third-party packages required — standard library only.
"""

import argparse
import base64
import hashlib
import secrets

# --- These MUST stay in lock-step with PasswordHasher.cs (Phase 4) ---------
SALT_SIZE = 16          # bytes
KEY_SIZE = 32           # bytes (256-bit)
ITERATIONS = 600_000    # PBKDF2 work factor
ALGORITHM = "sha256"

# The three seed users from Identity/003_seed.sql, all sharing one password.
SEED_PASSWORD = "Password123!"
SEED_USERS = [
    ("rep", "SalesRep"),
    ("manager", "SalesManager"),
    ("admin", "Admin"),
]


def hash_password(password: str) -> str:
    """Return 'iterations.base64Salt.base64Hash' — the exact C# stored format."""
    salt = secrets.token_bytes(SALT_SIZE)
    key = hashlib.pbkdf2_hmac(
        ALGORITHM,
        password.encode("utf-8"),
        salt,
        ITERATIONS,
        dklen=KEY_SIZE,
    )
    return f"{ITERATIONS}.{base64.b64encode(salt).decode()}.{base64.b64encode(key).decode()}"


def print_seed_block() -> None:
    """Print an INSERT block ready to paste into Identity/003_seed.sql."""
    print("IF NOT EXISTS (SELECT 1 FROM [identity].AppUser)")
    print("BEGIN")
    print("    INSERT INTO [identity].AppUser (Username, PasswordHash, Role) VALUES")
    rows = []
    for username, role in SEED_USERS:
        stored = hash_password(SEED_PASSWORD)
        rows.append(f"     ('{username}', '{stored}', '{role}')")
    print(",\n".join(rows) + ";")
    print("END")
    print("GO")


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Generate PBKDF2 password hashes matching Phase 4's PasswordHasher."
    )
    parser.add_argument(
        "password",
        nargs="?",
        default=SEED_PASSWORD,
        help=f"Password to hash (default: {SEED_PASSWORD!r}).",
    )
    parser.add_argument(
        "--seed",
        action="store_true",
        help="Print a full INSERT block for Identity/003_seed.sql (one hash per user).",
    )
    args = parser.parse_args()

    if args.seed:
        print_seed_block()
    else:
        print(hash_password(args.password))


if __name__ == "__main__":
    main()
