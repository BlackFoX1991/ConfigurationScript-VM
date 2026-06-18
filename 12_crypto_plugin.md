# Crypto Plugin

## Loading the Plugin

The crypto plugin is loaded like every other official CFGS plugin.

```cfs
import "dist/Debug/net10.0/CFGS.StandardLibrary.dll";
import "dist/Debug/net10.0/CFGS.Security.Crypto.dll";
```

After that, hashing, HMAC, random bytes, AES GCM, RSA, ECDSA, Ed25519, X25519, PBKDF2, JWT helpers, HOTP and TOTP helpers, X509 certificate helpers, and binary conversion helpers are available through `crypto` and `std.crypto`.

## Core Value Model

The plugin uses three shapes that fit naturally into CFGS.

- Plain strings for human readable input and common outputs like hex and base64.
- Dictionaries for structured payloads such as AES GCM encrypted packets.
- Handle objects for binary blobs and asymmetric keys.

The central binary handle is `CryptoBlob`. You usually get it from `crypto.blob(...)`, `crypto.randomBytes(...)`, or `crypto.aesKey()`.

## Namespace Members Overview

### Binary Conversion and Randomness

- `crypto.blob(value, inputEncoding = "utf8")`
- `crypto.randomBytes(count, output = "blob")`
- `crypto.uuid()`

`output` can be `blob`, `bytes`, `hex`, `base64`, `base32`, or `utf8`.

Example.

```cfs
var raw = crypto.randomBytes(16);
print(raw.hex());
print(raw.base64());
```

### Hashing and HMAC

- `crypto.hash(value, algorithm = "sha256", inputEncoding = "utf8", output = "hex")`
- `crypto.hmac(key, value, algorithm = "sha256", inputEncoding = "utf8", output = "hex")`
- `crypto.fixedTimeEquals(left, right, leftEncoding = "auto", rightEncoding = "auto")`

Supported hash families are `md5`, `sha1`, `sha256`, `sha384`, and `sha512`.

Example.

```cfs
print(crypto.hash("abc"));
print(crypto.hmac("secret", "payload"));
```

### Password Derivation

- `crypto.pbkdf2(password, salt, iterations, length, algorithm = "sha256", output = "hex")`

The `iterations` parameter must be at least 10000 to prevent insecure configurations. This is the right tool when you need deterministic key material from a password and a salt.

```cfs
var salt = crypto.randomBytes(16);
var key = crypto.pbkdf2("passphrase", salt, 100000, 32, "sha256", "blob");
print(key.base64());
```

### AES GCM

- `crypto.aesKey(size = 32, output = "blob")`
- `crypto.aesGcmEncrypt(key, plaintext, nonce = null, aad = null, inputEncoding = "utf8", output = "base64")`
- `crypto.aesGcmDecrypt(key, payload, inputEncoding = "base64", output = "utf8")`

`crypto.aesGcmEncrypt` returns a dictionary with these keys.

- `algorithm`
- `encoding`
- `cipher`
- `nonce`
- `tag`
- `aad` when present

Example.

```cfs
var key = crypto.aesKey();
var packet = crypto.aesGcmEncrypt(key, "hello crypto");
var plain = crypto.aesGcmDecrypt(key, packet);
print(plain);
```

### JWT

- `crypto.jwtSign(payload, key, algorithm = "hs256", headers = optional)`
- `crypto.jwtDecode(token)`
- `crypto.jwtVerify(token, key, algorithm, verifyTime = true, clockSkewSeconds = 0)`

> **Security:** `algorithm` is **required** for verification. You must pin the algorithm you
> expect (e.g. `"rs256"`); the verifier never derives it from the token header. This prevents
> algorithm-confusion attacks (such as forging an RS256 token as HS256 using the public key as
> the HMAC secret). A token whose header algorithm does not match the pinned one is reported as
> invalid with `reason = "algorithm mismatch"`.

Supported JWT signing algorithms are `hs256`, `hs384`, `hs512`, `rs256`, `rs384`, `rs512`, `ps256`, `ps384`, `ps512`, `es256`, `es384`, `es512`, and `eddsa`.

The key depends on the algorithm family.

- HMAC JWTs use a string, byte array, or `CryptoBlob`.
- RSA JWTs use an `RsaHandle` or an X509 certificate that exposes an RSA key.
- ECDSA JWTs use an `EcdsaHandle` or an X509 certificate that exposes an ECDSA key.
- EdDSA JWTs use an `Ed25519Handle`.

`crypto.jwtDecode` returns a dictionary with `header`, `payload`, `signature`, `signing_input`, and `algorithm`.

`crypto.jwtVerify` returns the same structure plus `valid` and optionally `reason`.

Example.

```cfs
var token = crypto.jwtSign({"sub": "user1", "exp": 4102444800}, "secret");
var checked = crypto.jwtVerify(token, "secret", "hs256");
print(checked.valid);
```

An Ed25519 based JWT looks like this.

```cfs
var signer = crypto.ed25519();
var token = crypto.jwtSign({"sub": "api-client"}, signer, "eddsa");
var checked = crypto.jwtVerify(token, signer, "eddsa");
print(checked.valid);
```

### HOTP and TOTP

- `crypto.totpSecret(size = 20, output = "base32")`
- `crypto.hotp(secret, counter, digits = 6, algorithm = "sha1", inputEncoding = "base32")`
- `crypto.totp(secret, unixSeconds = now, digits = 6, period = 30, algorithm = "sha1", inputEncoding = "base32")`
- `crypto.totpVerify(secret, code, unixSeconds = now, digits = 6, period = 30, window = 1, algorithm = "sha1", inputEncoding = "base32")`

HOTP and TOTP accept secrets as base32 by default because that is the common provisioning format. The same functions also work with `blob`, `bytes`, `hex`, or `base64` inputs if you pass the matching `inputEncoding`.

Example.

```cfs
var secret = crypto.totpSecret();
var code = crypto.totp(secret);
print(code);
print(crypto.totpVerify(secret, code));
```

### RSA

- `crypto.rsa(bits = 2048)`
- `crypto.rsaFromPem(pem)`

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
var rsa = crypto.rsa(2048);
var sig = rsa.sign("signed text");
print(rsa.verify("signed text", sig));
```

### ECDSA

- `crypto.ecdsa(curve = "nistP256")`
- `crypto.ecdsaFromPem(pem)`

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
var ecdsa = crypto.ecdsa();
var sig = ecdsa.sign("hello");
print(ecdsa.verify("hello", sig));
```

### Ed25519

- `crypto.ed25519()`
- `crypto.ed25519FromPem(pem)`

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
var signer = crypto.ed25519();
var signature = signer.sign("payload");
print(signer.verify("payload", signature));
```

### X25519

- `crypto.x25519()`
- `crypto.x25519FromPem(pem)`

X25519 handles support these intrinsics.

- `close()`
- `has_private()`
- `public_pem()`
- `private_pem()`
- `derive(peer, output = "base64")`

`derive` accepts another `X25519Handle` or a PEM string. The result is the shared secret produced by the X25519 key agreement.

Example.

```cfs
var alice = crypto.x25519();
var bob = crypto.x25519();

var shared1 = alice.derive(bob);
var shared2 = bob.derive(alice);

print(shared1 == shared2);
```

### X509 Certificates

- `crypto.x509SelfSigned(subject, key, options = optional)`
- `crypto.x509FromPem(certPem, keyPem = optional)`
- `crypto.x509FromPfx(data, password = "", inputEncoding = "base64")`

`crypto.x509SelfSigned` accepts an RSA or ECDSA key handle and an optional options dictionary. These options are currently supported.

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
var rsa = crypto.rsa();
var cert = crypto.x509SelfSigned("CN=demo.local", rsa, {
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
var rsa = crypto.rsa();
var publicPem = rsa.public_pem();
var privatePem = rsa.private_pem();

var imported = crypto.rsaFromPem(privatePem);
print(imported.has_private());
```

## Practical Notes

- Strings are treated as UTF 8 unless you explicitly pass `hex` or `base64`.
- Base32 is available as both input and output format now, which makes OTP secrets and provisioning style data much cleaner.
- If you already have raw binary in CFGS, keep it as `CryptoBlob` or a byte array instead of bouncing through text.
- `crypto.fixedTimeEquals` is the correct comparison for signatures, MACs, tokens, and derived secrets.
- `md5` and `sha1` exist for compatibility. For new work, prefer `sha256` or stronger.
- For symmetric encryption, use `crypto.aesKey()` instead of hand written password strings whenever possible.
- JWT verification can enforce `iat`, `nbf`, and `exp` automatically through `crypto.jwtVerify`.
- X509 creation is intentionally focused on self signed workflows and certificate packaging inside scripts.

## Minimal End to End Example

```cfs
import "dist/Debug/net10.0/CFGS.StandardLibrary.dll";
import "dist/Debug/net10.0/CFGS.Security.Crypto.dll";

var key = crypto.aesKey();
var packet = crypto.aesGcmEncrypt(key, "secret");
print(packet.cipher);
print(crypto.aesGcmDecrypt(key, packet));

var rsa = crypto.rsa();
var sig = rsa.sign("payload");
print(rsa.verify("payload", sig));
```
