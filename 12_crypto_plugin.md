# Crypto Plugin

## Loading the Plugin

The crypto plugin is loaded like every other official CFGS plugin.

```cfs
import "dist/Debug/net10.0/CFGS.StandardLibrary.dll";
import "dist/Debug/net10.0/CFGS.Security.Crypto.dll";
```

After that, hashing, HMAC, random bytes, AES GCM, RSA, ECDSA, Ed25519, X25519, PBKDF2, JWT helpers, HOTP and TOTP helpers, X509 certificate helpers, and binary conversion helpers are available globally.

## Core Value Model

The plugin uses three shapes that fit naturally into CFGS.

- Plain strings for human readable input and common outputs like hex and base64.
- Dictionaries for structured payloads such as AES GCM encrypted packets.
- Handle objects for binary blobs and asymmetric keys.

The central binary handle is `CryptoBlob`. You usually get it from `crypto_blob(...)`, `crypto_random_bytes(...)`, or `crypto_aes_key()`.

## Builtins Overview

### Binary Conversion and Randomness

- `crypto_blob(value, inputEncoding = "utf8")`
- `crypto_random_bytes(count, output = "blob")`
- `crypto_uuid()`

`output` can be `blob`, `bytes`, `hex`, `base64`, `base32`, or `utf8`.

Example.

```cfs
var raw = crypto_random_bytes(16);
print(raw.hex());
print(raw.base64());
```

### Hashing and HMAC

- `crypto_hash(value, algorithm = "sha256", inputEncoding = "utf8", output = "hex")`
- `crypto_hmac(key, value, algorithm = "sha256", inputEncoding = "utf8", output = "hex")`
- `crypto_fixed_time_equals(left, right, leftEncoding = "auto", rightEncoding = "auto")`

Supported hash families are `md5`, `sha1`, `sha256`, `sha384`, and `sha512`.

Example.

```cfs
print(crypto_hash("abc"));
print(crypto_hmac("secret", "payload"));
```

### Password Derivation

- `crypto_pbkdf2(password, salt, iterations, length, algorithm = "sha256", output = "hex")`

This is the right tool when you need deterministic key material from a password and a salt.

```cfs
var salt = crypto_random_bytes(16);
var key = crypto_pbkdf2("passphrase", salt, 100000, 32, "sha256", "blob");
print(key.base64());
```

### AES GCM

- `crypto_aes_key(size = 32, output = "blob")`
- `crypto_aes_gcm_encrypt(key, plaintext, nonce = null, aad = null, inputEncoding = "utf8", output = "base64")`
- `crypto_aes_gcm_decrypt(key, payload, inputEncoding = "base64", output = "utf8")`

`crypto_aes_gcm_encrypt` returns a dictionary with these keys.

- `algorithm`
- `encoding`
- `cipher`
- `nonce`
- `tag`
- `aad` when present

Example.

```cfs
var key = crypto_aes_key();
var packet = crypto_aes_gcm_encrypt(key, "hello crypto");
var plain = crypto_aes_gcm_decrypt(key, packet);
print(plain);
```

### JWT

- `crypto_jwt_sign(payload, key, algorithm = "hs256", headers = optional)`
- `crypto_jwt_decode(token)`
- `crypto_jwt_verify(token, key, algorithm = optional, verifyTime = true, clockSkewSeconds = 0)`

Supported JWT signing algorithms are `hs256`, `hs384`, `hs512`, `rs256`, `rs384`, `rs512`, `ps256`, `ps384`, `ps512`, `es256`, `es384`, `es512`, and `eddsa`.

The key depends on the algorithm family.

- HMAC JWTs use a string, byte array, or `CryptoBlob`.
- RSA JWTs use an `RsaHandle` or an X509 certificate that exposes an RSA key.
- ECDSA JWTs use an `EcdsaHandle` or an X509 certificate that exposes an ECDSA key.
- EdDSA JWTs use an `Ed25519Handle`.

`crypto_jwt_decode` returns a dictionary with `header`, `payload`, `signature`, `signing_input`, and `algorithm`.

`crypto_jwt_verify` returns the same structure plus `valid` and optionally `reason`.

Example.

```cfs
var token = crypto_jwt_sign({"sub": "user1", "exp": 4102444800}, "secret");
var checked = crypto_jwt_verify(token, "secret");
print(checked.valid);
```

An Ed25519 based JWT looks like this.

```cfs
var signer = crypto_ed25519();
var token = crypto_jwt_sign({"sub": "api-client"}, signer, "eddsa");
var checked = crypto_jwt_verify(token, signer, "eddsa");
print(checked.valid);
```

### HOTP and TOTP

- `crypto_totp_secret(size = 20, output = "base32")`
- `crypto_hotp(secret, counter, digits = 6, algorithm = "sha1", inputEncoding = "base32")`
- `crypto_totp(secret, unixSeconds = now, digits = 6, period = 30, algorithm = "sha1", inputEncoding = "base32")`
- `crypto_totp_verify(secret, code, unixSeconds = now, digits = 6, period = 30, window = 1, algorithm = "sha1", inputEncoding = "base32")`

HOTP and TOTP accept secrets as base32 by default because that is the common provisioning format. The same functions also work with `blob`, `bytes`, `hex`, or `base64` inputs if you pass the matching `inputEncoding`.

Example.

```cfs
var secret = crypto_totp_secret();
var code = crypto_totp(secret);
print(code);
print(crypto_totp_verify(secret, code));
```

### RSA

- `crypto_rsa(bits = 2048)`
- `crypto_rsa_from_pem(pem)`

RSA handles support these intrinsics.

- `close()`
- `has_private()`
- `key_size()`
- `public_pem()`
- `private_pem()`
- `encrypt(data, inputEncoding = "utf8", output = "base64", padding = "oaep-sha256")`
- `decrypt(ciphertext, inputEncoding = "base64", output = "utf8", padding = "oaep-sha256")`
- `sign(data, hash = "sha256", inputEncoding = "utf8", output = "base64", padding = "pkcs1")`
- `verify(data, signature, hash = "sha256", inputEncoding = "utf8", signatureEncoding = "base64", padding = "pkcs1")`

Supported encryption paddings are `pkcs1`, `oaep-sha1`, `oaep-sha256`, `oaep-sha384`, and `oaep-sha512`.

Supported signature paddings are `pkcs1` and `pss`.

Example.

```cfs
var rsa = crypto_rsa(2048);
var sig = rsa.sign("signed text");
print(rsa.verify("signed text", sig));
```

### ECDSA

- `crypto_ecdsa(curve = "nistP256")`
- `crypto_ecdsa_from_pem(pem)`

ECDSA handles support these intrinsics.

- `close()`
- `has_private()`
- `curve()`
- `key_size()`
- `public_pem()`
- `private_pem()`
- `sign(data, hash = "sha256", inputEncoding = "utf8", output = "base64")`
- `verify(data, signature, hash = "sha256", inputEncoding = "utf8", signatureEncoding = "base64")`

Supported curves are `nistP256`, `nistP384`, and `nistP521`.

Example.

```cfs
var ecdsa = crypto_ecdsa();
var sig = ecdsa.sign("hello");
print(ecdsa.verify("hello", sig));
```

### Ed25519

- `crypto_ed25519()`
- `crypto_ed25519_from_pem(pem)`

Ed25519 handles support these intrinsics.

- `close()`
- `has_private()`
- `public_pem()`
- `private_pem()`
- `sign(data, inputEncoding = "utf8", output = "base64")`
- `verify(data, signature, inputEncoding = "utf8", signatureEncoding = "base64")`

Ed25519 uses a fixed signature format and does not ask for a separate hash parameter. That makes it a good fit when you want a compact modern signing API with fewer moving parts.

Example.

```cfs
var signer = crypto_ed25519();
var signature = signer.sign("payload");
print(signer.verify("payload", signature));
```

### X25519

- `crypto_x25519()`
- `crypto_x25519_from_pem(pem)`

X25519 handles support these intrinsics.

- `close()`
- `has_private()`
- `public_pem()`
- `private_pem()`
- `derive(peer, output = "base64")`

`derive` accepts another `X25519Handle` or a PEM string. The result is the shared secret produced by the X25519 key agreement.

Example.

```cfs
var alice = crypto_x25519();
var bob = crypto_x25519();

var shared1 = alice.derive(bob);
var shared2 = bob.derive(alice);

print(shared1 == shared2);
```

### X509 Certificates

- `crypto_x509_self_signed(subject, key, options = optional)`
- `crypto_x509_from_pem(certPem, keyPem = optional)`
- `crypto_x509_from_pfx(data, password = "", inputEncoding = "base64")`

`crypto_x509_self_signed` accepts an RSA or ECDSA key handle and an optional options dictionary. These options are currently supported.

- `hash`
- `days`
- `not_before`
- `not_after`
- `ca`
- `path_length`
- `padding` for RSA certificates
- `usage`
- `enhanced_usage`
- `dns`
- `emails`
- `uris`

X509 handles support these intrinsics.

- `close()`
- `subject()`
- `issuer()`
- `thumbprint()`
- `serial()`
- `version()`
- `not_before()`
- `not_after()`
- `has_private()`
- `algorithm()`
- `pem()`
- `public_key_pem()`
- `private_pem()`
- `export_pfx(password = "", output = "base64")`

Example.

```cfs
var rsa = crypto_rsa();
var cert = crypto_x509_self_signed("CN=demo.local", rsa, {
    "dns": ["demo.local"],
    "enhanced_usage": ["serverAuth"]
});

print(cert.subject());
print(cert.has_private());
```

## `CryptoBlob` Intrinsics

`CryptoBlob` is the binary bridge between strings, arrays, and cryptographic operations.

- `len()`
- `bytes()`
- `hex()`
- `base64()`
- `base32()`
- `utf8()`
- `clone()`
- `hash(algorithm = "sha256", output = "hex")`
- `hmac(key, algorithm = "sha256", output = "hex")`
- `fixed_time_equals(other, otherEncoding = "auto")`

You can also create blobs directly from strings and arrays.

- `"hello".to_blob()`
- `[104, 105].to_blob()`

Example.

```cfs
var blob = "hello".to_blob();
print(blob.hex());
print(blob.hash());
```

## PEM Import and Export

RSA and ECDSA handles export PEM directly. Public keys are exported as SubjectPublicKeyInfo PEM. Private keys are exported as PKCS#8 PEM.

```cfs
var rsa = crypto_rsa();
var publicPem = rsa.public_pem();
var privatePem = rsa.private_pem();

var imported = crypto_rsa_from_pem(privatePem);
print(imported.has_private());
```

## Practical Notes

- Strings are treated as UTF 8 unless you explicitly pass `hex` or `base64`.
- Base32 is available as both input and output format now, which makes OTP secrets and provisioning style data much cleaner.
- If you already have raw binary in CFGS, keep it as `CryptoBlob` or a byte array instead of bouncing through text.
- `crypto_fixed_time_equals` is the correct comparison for signatures, MACs, tokens, and derived secrets.
- `md5` and `sha1` exist for compatibility. For new work, prefer `sha256` or stronger.
- For symmetric encryption, use `crypto_aes_key()` instead of hand written password strings whenever possible.
- JWT verification can enforce `iat`, `nbf`, and `exp` automatically through `crypto_jwt_verify`.
- X509 creation is intentionally focused on self signed workflows and certificate packaging inside scripts.

## Minimal End to End Example

```cfs
import "dist/Debug/net10.0/CFGS.StandardLibrary.dll";
import "dist/Debug/net10.0/CFGS.Security.Crypto.dll";

var key = crypto_aes_key();
var packet = crypto_aes_gcm_encrypt(key, "secret");
print(packet.cipher);
print(crypto_aes_gcm_decrypt(key, packet));

var rsa = crypto_rsa();
var sig = rsa.sign("payload");
print(rsa.verify("payload", sig));
```
