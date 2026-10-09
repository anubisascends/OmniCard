// One-time setup keys an admin gives a user (new account or required password reset). Mirrors the
// server's SetupKeys rules: 6–64 letters/digits, compared case-insensitively.

export const SETUP_KEY_MIN = 6;
export const SETUP_KEY_MAX = 64;

// No 0/O, 1/I/L: generated keys are meant to be read aloud or copied by hand.
const ALPHABET = 'ABCDEFGHJKMNPQRSTUVWXYZ23456789';

export function isValidSetupKey(key: string): boolean {
  const k = key.trim();
  return k.length >= SETUP_KEY_MIN && k.length <= SETUP_KEY_MAX && /^[A-Za-z0-9]+$/.test(k);
}

/** A random 10-character key from an unambiguous alphabet (crypto-strength). */
export function generateSetupKey(length = 10): string {
  const out: string[] = [];
  const bytes = new Uint32Array(1);
  // Rejection sampling keeps every character equally likely.
  const limit = Math.floor(0x100000000 / ALPHABET.length) * ALPHABET.length;
  while (out.length < length) {
    crypto.getRandomValues(bytes);
    if (bytes[0] < limit) out.push(ALPHABET[bytes[0] % ALPHABET.length]);
  }
  return out.join('');
}
