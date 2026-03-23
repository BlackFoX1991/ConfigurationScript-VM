using CFGS_VM.VMCore;
using CFGS_VM.VMCore.Extensions;
using CFGS_VM.VMCore.Plugin;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Agreement;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace CFGS.Security.Crypto
{
    public sealed class CryptoBlob
    {
        private readonly byte[] _bytes;

        public CryptoBlob(byte[] bytes)
        {
            _bytes = bytes?.ToArray() ?? Array.Empty<byte>();
        }

        public int Length => _bytes.Length;

        public byte[] ToArray() => _bytes.ToArray();

        public override string ToString() => Convert.ToHexString(_bytes).ToLowerInvariant();
    }

    public sealed class RsaHandle : IDisposable
    {
        private bool _disposed;

        public RsaHandle(RSA rsa)
        {
            Value = rsa ?? throw new ArgumentNullException(nameof(rsa));
        }

        ~RsaHandle() { Dispose(); }

        public RSA Value { get; }

        public int KeySize
        {
            get
            {
                ThrowIfDisposed();
                return Value.KeySize;
            }
        }

        public bool HasPrivateKey
        {
            get
            {
                ThrowIfDisposed();
                try
                {
                    Value.ExportParameters(true);
                    return true;
                }
                catch (CryptographicException)
                {
                    return false;
                }
            }
        }

        public void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(RsaHandle));
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Value.Dispose();
        }
    }

    public sealed class EcdsaHandle : IDisposable
    {
        private bool _disposed;

        public EcdsaHandle(ECDsa ecdsa, string curveName)
        {
            Value = ecdsa ?? throw new ArgumentNullException(nameof(ecdsa));
            CurveName = string.IsNullOrWhiteSpace(curveName) ? "unknown" : curveName;
        }

        ~EcdsaHandle() { Dispose(); }

        public ECDsa Value { get; }

        public string CurveName { get; }

        public int KeySize
        {
            get
            {
                ThrowIfDisposed();
                return Value.KeySize;
            }
        }

        public bool HasPrivateKey
        {
            get
            {
                ThrowIfDisposed();
                try
                {
                    Value.ExportParameters(true);
                    return true;
                }
                catch (CryptographicException)
                {
                    return false;
                }
            }
        }

        public void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(EcdsaHandle));
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Value.Dispose();
        }
    }

    public sealed class X509CertHandle : IDisposable
    {
        private bool _disposed;

        public X509CertHandle(X509Certificate2 certificate)
        {
            Value = certificate ?? throw new ArgumentNullException(nameof(certificate));
        }

        ~X509CertHandle() { Dispose(); }

        public X509Certificate2 Value { get; }

        public bool HasPrivateKey
        {
            get
            {
                ThrowIfDisposed();
                return Value.HasPrivateKey;
            }
        }

        public void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(X509CertHandle));
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Value.Dispose();
        }
    }

    public sealed class Ed25519Handle : IDisposable
    {
        private bool _disposed;

        public Ed25519Handle(Ed25519PublicKeyParameters publicKey, Ed25519PrivateKeyParameters? privateKey)
        {
            PublicKey = publicKey ?? throw new ArgumentNullException(nameof(publicKey));
            PrivateKey = privateKey;
        }

        public Ed25519PublicKeyParameters PublicKey { get; }

        public Ed25519PrivateKeyParameters? PrivateKey { get; }

        public bool HasPrivateKey
        {
            get
            {
                ThrowIfDisposed();
                return PrivateKey != null;
            }
        }

        public void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(Ed25519Handle));
        }

        public void Dispose()
        {
            _disposed = true;
        }
    }

    public sealed class X25519Handle : IDisposable
    {
        private bool _disposed;

        public X25519Handle(X25519PublicKeyParameters publicKey, X25519PrivateKeyParameters? privateKey)
        {
            PublicKey = publicKey ?? throw new ArgumentNullException(nameof(publicKey));
            PrivateKey = privateKey;
        }

        public X25519PublicKeyParameters PublicKey { get; }

        public X25519PrivateKeyParameters? PrivateKey { get; }

        public bool HasPrivateKey
        {
            get
            {
                ThrowIfDisposed();
                return PrivateKey != null;
            }
        }

        public void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(X25519Handle));
        }

        public void Dispose()
        {
            _disposed = true;
        }
    }

    public sealed class CFGS_CRYPTO : IVmPlugin
    {
        public static bool AllowCrypto { get; set; } = true;

        public void Register(IBuiltinRegistry builtins, IIntrinsicRegistry intrinsics)
        {
            RegisterBuiltins(builtins);
            RegisterAdvancedBuiltins(builtins);
            RegisterJwtAndOtpBuiltins(builtins);
            RegisterX509Builtins(builtins);
            RegisterModernCurveBuiltins(builtins);
            RegisterBlobIntrinsics(intrinsics);
            RegisterStringIntrinsics(intrinsics);
            RegisterArrayIntrinsics(intrinsics);
            RegisterRsaIntrinsics(intrinsics);
            RegisterEcdsaIntrinsics(intrinsics);
            RegisterX509Intrinsics(intrinsics);
            RegisterEd25519Intrinsics(intrinsics);
            RegisterX25519Intrinsics(intrinsics);
        }

        private static void RegisterBuiltins(IBuiltinRegistry builtins)
        {
            builtins.Register(new BuiltinDescriptor("crypto_blob", 1, 2, (args, instr) => Guard(instr, () =>
            {
                string inputEncoding = ReadStringArg(args, 1, "utf8");
                return new CryptoBlob(ConvertToBytes(args[0], inputEncoding, instr, "value"));
            })));

            builtins.Register(new BuiltinDescriptor("crypto_random_bytes", 1, 2, (args, instr) => Guard(instr, () =>
            {
                int count = ReadInt(args[0], "count", instr, minValue: 0);
                string outputEncoding = NormalizeOutputEncoding(ReadStringArg(args, 1, "blob"), instr, "output");
                byte[] bytes = new byte[count];
                RandomNumberGenerator.Fill(bytes);
                return BytesToValue(bytes, outputEncoding, instr);
            })));

            builtins.Register(new BuiltinDescriptor("crypto_uuid", 0, 0, (args, instr) => Guard(instr, () =>
            {
                return Guid.NewGuid().ToString("D");
            })));

            builtins.Register(new BuiltinDescriptor("crypto_hash", 1, 4, (args, instr) => Guard(instr, () =>
            {
                string algorithm = ReadStringArg(args, 1, "sha256");
                string inputEncoding = ReadStringArg(args, 2, "utf8");
                string outputEncoding = NormalizeOutputEncoding(ReadStringArg(args, 3, "hex"), instr, "output");
                byte[] data = ConvertToBytes(args[0], inputEncoding, instr, "value");
                return BytesToValue(ComputeHash(data, algorithm, instr), outputEncoding, instr);
            })));

            builtins.Register(new BuiltinDescriptor("crypto_hmac", 2, 5, (args, instr) => Guard(instr, () =>
            {
                string algorithm = ReadStringArg(args, 2, "sha256");
                string inputEncoding = ReadStringArg(args, 3, "utf8");
                string outputEncoding = NormalizeOutputEncoding(ReadStringArg(args, 4, "hex"), instr, "output");
                byte[] key = ConvertToBytes(args[0], "auto", instr, "key");
                byte[] data = ConvertToBytes(args[1], inputEncoding, instr, "value");
                using HMAC hmac = CreateHmac(algorithm, key, instr);
                return BytesToValue(hmac.ComputeHash(data), outputEncoding, instr);
            })));

            builtins.Register(new BuiltinDescriptor("crypto_fixed_time_equals", 2, 4, (args, instr) => Guard(instr, () =>
            {
                string leftEncoding = ReadStringArg(args, 2, "auto");
                string rightEncoding = ReadStringArg(args, 3, "auto");
                byte[] left = ConvertToBytes(args[0], leftEncoding, instr, "left");
                byte[] right = ConvertToBytes(args[1], rightEncoding, instr, "right");

                if (left.Length != right.Length)
                    return false;

                return CryptographicOperations.FixedTimeEquals(left, right);
            })));

            builtins.Register(new BuiltinDescriptor("crypto_pbkdf2", 4, 6, (args, instr) => Guard(instr, () =>
            {
                byte[] password = ConvertToBytes(args[0], "auto", instr, "password");
                byte[] salt = ConvertToBytes(args[1], "auto", instr, "salt");
                int iterations = ReadInt(args[2], "iterations", instr, minValue: 10000);
                int length = ReadInt(args[3], "length", instr, minValue: 1);
                string algorithm = ReadStringArg(args, 4, "sha256");
                string outputEncoding = NormalizeOutputEncoding(ReadStringArg(args, 5, "hex"), instr, "output");
                HashAlgorithmName hash = ParsePbkdf2HashAlgorithm(algorithm, instr);
                byte[] derived = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, hash, length);
                return BytesToValue(derived, outputEncoding, instr);
            })));

            builtins.Register(new BuiltinDescriptor("crypto_aes_key", 0, 2, (args, instr) => Guard(instr, () =>
            {
                int size = args.Count >= 1 ? ReadInt(args[0], "size", instr, minValue: 16) : 32;
                string outputEncoding = NormalizeOutputEncoding(ReadStringArg(args, 1, "blob"), instr, "output");

                if (size is not (16 or 24 or 32))
                    throw Runtime(instr, "AES keys must be 16, 24, or 32 bytes long");

                byte[] key = new byte[size];
                RandomNumberGenerator.Fill(key);
                return BytesToValue(key, outputEncoding, instr);
            })));

            builtins.Register(new BuiltinDescriptor("crypto_aes_gcm_encrypt", 2, 6, (args, instr) => Guard(instr, () =>
            {
                byte[] key = GetAesKeyBytes(args[0], instr);
                string inputEncoding = ReadStringArg(args, 4, "utf8");
                string outputEncoding = NormalizeOutputEncoding(ReadStringArg(args, 5, "base64"), instr, "output");
                byte[] plaintext = ConvertToBytes(args[1], inputEncoding, instr, "plaintext");

                byte[] nonce;
                if (args.Count >= 3 && args[2] != null)
                {
                    nonce = ConvertToBytes(args[2], "auto", instr, "nonce");
                    if (nonce.Length != 12)
                        throw Runtime(instr, "AES GCM nonce must be exactly 12 bytes");
                }
                else
                {
                    nonce = new byte[12];
                    RandomNumberGenerator.Fill(nonce);
                }

                byte[]? aad = args.Count >= 4 && args[3] != null
                    ? ConvertToBytes(args[3], "auto", instr, "aad")
                    : null;

                byte[] cipher = new byte[plaintext.Length];
                byte[] tag = new byte[16];

                using (AesGcm aes = new(key, tag.Length))
                {
                    aes.Encrypt(nonce, plaintext, cipher, tag, aad);
                }

                Dictionary<string, object> payload = new(StringComparer.OrdinalIgnoreCase)
                {
                    ["algorithm"] = "aes_gcm",
                    ["encoding"] = outputEncoding,
                    ["cipher"] = BytesToValue(cipher, outputEncoding, instr),
                    ["nonce"] = BytesToValue(nonce, outputEncoding, instr),
                    ["tag"] = BytesToValue(tag, outputEncoding, instr)
                };

                if (aad != null)
                    payload["aad"] = BytesToValue(aad, outputEncoding, instr);

                return payload;
            })));
        }

        private static void RegisterAdvancedBuiltins(IBuiltinRegistry builtins)
        {
            builtins.Register(new BuiltinDescriptor("crypto_aes_gcm_decrypt", 2, 4, (args, instr) => Guard(instr, () =>
            {
                byte[] key = GetAesKeyBytes(args[0], instr);

                if (args[1] is not Dictionary<string, object> payload)
                    throw Runtime(instr, "AES GCM decrypt expects a payload dictionary");

                string inputEncoding = ReadStringArg(args, 2,
                    payload.TryGetValue("encoding", out object? enc) ? enc?.ToString() ?? "base64" : "base64");
                string outputEncoding = NormalizeOutputEncoding(ReadStringArg(args, 3, "utf8"), instr, "output");

                byte[] cipher = ConvertToBytes(RequireField(payload, "cipher", instr), inputEncoding, instr, "payload.cipher");
                byte[] nonce = ConvertToBytes(RequireField(payload, "nonce", instr), inputEncoding, instr, "payload.nonce");
                byte[] tag = ConvertToBytes(RequireField(payload, "tag", instr), inputEncoding, instr, "payload.tag");

                if (nonce.Length != 12)
                    throw Runtime(instr, "AES GCM nonce must be exactly 12 bytes");

                if (tag.Length is < 12 or > 16)
                    throw Runtime(instr, "AES GCM tag must be between 12 and 16 bytes");

                byte[]? aad = payload.TryGetValue("aad", out object? aadValue) && aadValue != null
                    ? ConvertToBytes(aadValue, inputEncoding, instr, "payload.aad")
                    : null;

                byte[] plaintext = new byte[cipher.Length];
                using (AesGcm aes = new(key, tag.Length))
                {
                    aes.Decrypt(nonce, cipher, tag, plaintext, aad);
                }

                return BytesToValue(plaintext, outputEncoding, instr);
            })));

            builtins.Register(new BuiltinDescriptor("crypto_rsa", 0, 1, (args, instr) => Guard(instr, () =>
            {
                int bits = args.Count >= 1 ? ReadInt(args[0], "bits", instr, minValue: 1024) : 2048;
                if (bits % 8 != 0)
                    throw Runtime(instr, "RSA key size must be a multiple of 8");

                RSA rsa = RSA.Create();
                rsa.KeySize = bits;
                return new RsaHandle(rsa);
            })));

            builtins.Register(new BuiltinDescriptor("crypto_rsa_from_pem", 1, 1, (args, instr) => Guard(instr, () =>
            {
                string pem = args[0]?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(pem))
                    throw Runtime(instr, "PEM text must not be empty");

                RSA rsa = RSA.Create();
                rsa.ImportFromPem(pem);
                return new RsaHandle(rsa);
            })));

            builtins.Register(new BuiltinDescriptor("crypto_ecdsa", 0, 1, (args, instr) => Guard(instr, () =>
            {
                string curveName = ReadStringArg(args, 0, "nistP256");
                ECCurve curve = ParseCurve(curveName, instr);
                ECDsa ecdsa = ECDsa.Create(curve);
                return new EcdsaHandle(ecdsa, NormalizeCurveName(curveName));
            })));

            builtins.Register(new BuiltinDescriptor("crypto_ecdsa_from_pem", 1, 1, (args, instr) => Guard(instr, () =>
            {
                string pem = args[0]?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(pem))
                    throw Runtime(instr, "PEM text must not be empty");

                ECDsa ecdsa = ECDsa.Create();
                ecdsa.ImportFromPem(pem);
                return new EcdsaHandle(ecdsa, GetCurveName(ecdsa));
            })));
        }

        private static void RegisterJwtAndOtpBuiltins(IBuiltinRegistry builtins)
        {
            builtins.Register(new BuiltinDescriptor("crypto_jwt_sign", 2, 4, (args, instr) => Guard(instr, () =>
            {
                object payload = args[0] ?? throw Runtime(instr, "JWT payload must not be null");
                object key = args[1] ?? throw Runtime(instr, "JWT signing key must not be null");
                string algorithm = NormalizeJwtAlgorithm(ReadStringArg(args, 2, "hs256"), instr);
                Dictionary<string, object>? headers = args.Count >= 4 ? args[3] as Dictionary<string, object> : null;
                return JwtSign(payload, key, algorithm, headers, instr);
            })));

            builtins.Register(new BuiltinDescriptor("crypto_jwt_decode", 1, 1, (args, instr) => Guard(instr, () =>
            {
                return JwtDecode(args[0]?.ToString() ?? string.Empty, instr);
            })));

            builtins.Register(new BuiltinDescriptor("crypto_jwt_verify", 2, 5, (args, instr) => Guard(instr, () =>
            {
                string token = args[0]?.ToString() ?? string.Empty;
                object key = args[1] ?? throw Runtime(instr, "JWT verification key must not be null");
                string? algorithm = args.Count >= 3 && args[2] != null ? NormalizeJwtAlgorithm(args[2]?.ToString() ?? string.Empty, instr) : null;
                bool verifyTime = args.Count >= 4 ? Convert.ToBoolean(args[3], CultureInfo.InvariantCulture) : true;
                long clockSkew = args.Count >= 5 ? Convert.ToInt64(args[4], CultureInfo.InvariantCulture) : 0L;
                return JwtVerify(token, key, algorithm, verifyTime, clockSkew, instr);
            })));

            builtins.Register(new BuiltinDescriptor("crypto_hotp", 2, 5, (args, instr) => Guard(instr, () =>
            {
                byte[] secret = ConvertToBytes(args[0], ReadStringArg(args, 4, "base32"), instr, "secret");
                long counter = Convert.ToInt64(args[1], CultureInfo.InvariantCulture);
                int digits = args.Count >= 3 ? ReadInt(args[2], "digits", instr, minValue: 1) : 6;
                string algorithm = ReadStringArg(args, 3, "sha1");
                return ComputeHotp(secret, counter, digits, algorithm, instr);
            })));

            builtins.Register(new BuiltinDescriptor("crypto_totp_secret", 0, 2, (args, instr) => Guard(instr, () =>
            {
                int size = args.Count >= 1 ? ReadInt(args[0], "size", instr, minValue: 1) : 20;
                string output = NormalizeOutputEncoding(ReadStringArg(args, 1, "base32"), instr, "output");
                byte[] secret = new byte[size];
                RandomNumberGenerator.Fill(secret);
                return BytesToValue(secret, output, instr);
            })));

            builtins.Register(new BuiltinDescriptor("crypto_totp", 1, 6, (args, instr) => Guard(instr, () =>
            {
                byte[] secret = ConvertToBytes(args[0], ReadStringArg(args, 5, "base32"), instr, "secret");
                long unixSeconds = args.Count >= 2 && args[1] != null ? Convert.ToInt64(args[1], CultureInfo.InvariantCulture) : DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                int digits = args.Count >= 3 ? ReadInt(args[2], "digits", instr, minValue: 1) : 6;
                int period = args.Count >= 4 ? ReadInt(args[3], "period", instr, minValue: 1) : 30;
                string algorithm = ReadStringArg(args, 4, "sha1");
                long counter = unixSeconds / period;
                return ComputeHotp(secret, counter, digits, algorithm, instr);
            })));

            builtins.Register(new BuiltinDescriptor("crypto_totp_verify", 2, 8, (args, instr) => Guard(instr, () =>
            {
                byte[] secret = ConvertToBytes(args[0], ReadStringArg(args, 7, "base32"), instr, "secret");
                string code = (args[1]?.ToString() ?? string.Empty).Trim();
                long unixSeconds = args.Count >= 3 && args[2] != null ? Convert.ToInt64(args[2], CultureInfo.InvariantCulture) : DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                int digits = args.Count >= 4 ? ReadInt(args[3], "digits", instr, minValue: 1) : 6;
                int period = args.Count >= 5 ? ReadInt(args[4], "period", instr, minValue: 1) : 30;
                int window = args.Count >= 6 ? ReadInt(args[5], "window", instr, minValue: 0) : 1;
                string algorithm = ReadStringArg(args, 6, "sha1");
                long counter = unixSeconds / period;

                for (int offset = -window; offset <= window; offset++)
                {
                    long currentCounter = counter + offset;
                    if (currentCounter < 0)
                        continue;

                    string expected = ComputeHotp(secret, currentCounter, digits, algorithm, instr);
                    if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(code)))
                        return true;
                }

                return false;
            })));
        }

        private static void RegisterX509Builtins(IBuiltinRegistry builtins)
        {
            builtins.Register(new BuiltinDescriptor("crypto_x509_self_signed", 2, 3, (args, instr) => Guard(instr, () =>
            {
                string subject = args[0]?.ToString() ?? string.Empty;
                object key = args[1] ?? throw Runtime(instr, "certificate key must not be null");
                Dictionary<string, object>? options = args.Count >= 3 ? args[2] as Dictionary<string, object> : null;
                return CreateSelfSignedCertificate(subject, key, options, instr);
            })));

            builtins.Register(new BuiltinDescriptor("crypto_x509_from_pem", 1, 2, (args, instr) => Guard(instr, () =>
            {
                string certPem = args[0]?.ToString() ?? string.Empty;
                string? keyPem = args.Count >= 2 ? args[1]?.ToString() : null;
                return ImportCertificateFromPem(certPem, keyPem, instr);
            })));

            builtins.Register(new BuiltinDescriptor("crypto_x509_from_pfx", 1, 3, (args, instr) => Guard(instr, () =>
            {
                byte[] pfx = ConvertToBytes(args[0], ReadStringArg(args, 2, "base64"), instr, "pfx");
                string password = args.Count >= 2 ? args[1]?.ToString() ?? string.Empty : string.Empty;
                X509Certificate2 cert = X509CertificateLoader.LoadPkcs12(
                    pfx,
                    password,
                    X509KeyStorageFlags.Exportable | X509KeyStorageFlags.EphemeralKeySet,
                    Pkcs12LoaderLimits.Defaults);
                return new X509CertHandle(cert);
            })));
        }

        private static void RegisterModernCurveBuiltins(IBuiltinRegistry builtins)
        {
            builtins.Register(new BuiltinDescriptor("crypto_ed25519", 0, 0, (args, instr) => Guard(instr, () =>
            {
                SecureRandom random = new();
                Ed25519PrivateKeyParameters privateKey = new(random);
                return new Ed25519Handle(privateKey.GeneratePublicKey(), privateKey);
            })));

            builtins.Register(new BuiltinDescriptor("crypto_ed25519_from_pem", 1, 1, (args, instr) => Guard(instr, () =>
            {
                string pem = args[0]?.ToString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(pem))
                    throw Runtime(instr, "PEM text must not be empty");
                return ImportEd25519FromPem(pem, instr);
            })));

            builtins.Register(new BuiltinDescriptor("crypto_x25519", 0, 0, (args, instr) => Guard(instr, () =>
            {
                SecureRandom random = new();
                X25519PrivateKeyParameters privateKey = new(random);
                return new X25519Handle(privateKey.GeneratePublicKey(), privateKey);
            })));

            builtins.Register(new BuiltinDescriptor("crypto_x25519_from_pem", 1, 1, (args, instr) => Guard(instr, () =>
            {
                string pem = args[0]?.ToString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(pem))
                    throw Runtime(instr, "PEM text must not be empty");
                return ImportX25519FromPem(pem, instr);
            })));
        }

        private static void RegisterBlobIntrinsics(IIntrinsicRegistry intrinsics)
        {
            Type t = typeof(CryptoBlob);

            intrinsics.Register(t, new IntrinsicDescriptor("len", 0, 0, (recv, args, instr) => Guard(instr, () => ((CryptoBlob)recv).Length)));
            intrinsics.Register(t, new IntrinsicDescriptor("bytes", 0, 0, (recv, args, instr) => Guard(instr, () => ToVmByteArray(((CryptoBlob)recv).ToArray()))));
            intrinsics.Register(t, new IntrinsicDescriptor("hex", 0, 0, (recv, args, instr) => Guard(instr, () => BytesToHex(((CryptoBlob)recv).ToArray()))));
            intrinsics.Register(t, new IntrinsicDescriptor("base64", 0, 0, (recv, args, instr) => Guard(instr, () => Convert.ToBase64String(((CryptoBlob)recv).ToArray()))));
            intrinsics.Register(t, new IntrinsicDescriptor("base32", 0, 0, (recv, args, instr) => Guard(instr, () => BytesToBase32(((CryptoBlob)recv).ToArray()))));
            intrinsics.Register(t, new IntrinsicDescriptor("utf8", 0, 0, (recv, args, instr) => Guard(instr, () => Encoding.UTF8.GetString(((CryptoBlob)recv).ToArray()))));
            intrinsics.Register(t, new IntrinsicDescriptor("clone", 0, 0, (recv, args, instr) => Guard(instr, () => new CryptoBlob(((CryptoBlob)recv).ToArray()))));

            intrinsics.Register(t, new IntrinsicDescriptor("hash", 0, 2, (recv, args, instr) => Guard(instr, () =>
            {
                string algorithm = ReadStringArg(args, 0, "sha256");
                string outputEncoding = NormalizeOutputEncoding(ReadStringArg(args, 1, "hex"), instr, "output");
                byte[] data = ((CryptoBlob)recv).ToArray();
                return BytesToValue(ComputeHash(data, algorithm, instr), outputEncoding, instr);
            })));

            intrinsics.Register(t, new IntrinsicDescriptor("hmac", 1, 3, (recv, args, instr) => Guard(instr, () =>
            {
                byte[] key = ConvertToBytes(args[0], "auto", instr, "key");
                string algorithm = ReadStringArg(args, 1, "sha256");
                string outputEncoding = NormalizeOutputEncoding(ReadStringArg(args, 2, "hex"), instr, "output");
                using HMAC hmac = CreateHmac(algorithm, key, instr);
                return BytesToValue(hmac.ComputeHash(((CryptoBlob)recv).ToArray()), outputEncoding, instr);
            })));

            intrinsics.Register(t, new IntrinsicDescriptor("fixed_time_equals", 1, 2, (recv, args, instr) => Guard(instr, () =>
            {
                string otherEncoding = ReadStringArg(args, 1, "auto");
                byte[] left = ((CryptoBlob)recv).ToArray();
                byte[] right = ConvertToBytes(args[0], otherEncoding, instr, "other");

                if (left.Length != right.Length)
                    return false;

                return CryptographicOperations.FixedTimeEquals(left, right);
            })));
        }

        private static void RegisterStringIntrinsics(IIntrinsicRegistry intrinsics)
        {
            intrinsics.Register(typeof(string), new IntrinsicDescriptor("to_blob", 0, 1, (recv, args, instr) => Guard(instr, () =>
            {
                string inputEncoding = ReadStringArg(args, 0, "utf8");
                return new CryptoBlob(ConvertToBytes((string)recv, inputEncoding, instr, "value"));
            })));
        }

        private static void RegisterArrayIntrinsics(IIntrinsicRegistry intrinsics)
        {
            intrinsics.Register(typeof(List<object>), new IntrinsicDescriptor("to_blob", 0, 0, (recv, args, instr) => Guard(instr, () =>
            {
                return new CryptoBlob(ConvertVmArrayToBytes((List<object>)recv, instr, "value"));
            })));
        }

        private static void RegisterRsaIntrinsics(IIntrinsicRegistry intrinsics)
        {
            Type t = typeof(RsaHandle);

            intrinsics.Register(t, new IntrinsicDescriptor("close", 0, 0, (recv, args, instr) =>
            {
                ((RsaHandle)recv).Dispose();
                return null!;
            }));

            intrinsics.Register(t, new IntrinsicDescriptor("has_private", 0, 0, (recv, args, instr) => Guard(instr, () => ((RsaHandle)recv).HasPrivateKey)));
            intrinsics.Register(t, new IntrinsicDescriptor("key_size", 0, 0, (recv, args, instr) => Guard(instr, () => ((RsaHandle)recv).KeySize)));

            intrinsics.Register(t, new IntrinsicDescriptor("public_pem", 0, 0, (recv, args, instr) => Guard(instr, () =>
            {
                RsaHandle handle = (RsaHandle)recv;
                handle.ThrowIfDisposed();
                return handle.Value.ExportSubjectPublicKeyInfoPem();
            })));

            intrinsics.Register(t, new IntrinsicDescriptor("private_pem", 0, 0, (recv, args, instr) => Guard(instr, () =>
            {
                RsaHandle handle = (RsaHandle)recv;
                handle.ThrowIfDisposed();
                if (!handle.HasPrivateKey)
                    throw Runtime(instr, "RSA handle does not contain a private key");
                return handle.Value.ExportPkcs8PrivateKeyPem();
            })));

            intrinsics.Register(t, new IntrinsicDescriptor("encrypt", 1, 4, (recv, args, instr) => Guard(instr, () =>
            {
                RsaHandle handle = (RsaHandle)recv;
                handle.ThrowIfDisposed();
                byte[] plaintext = ConvertToBytes(args[0], ReadStringArg(args, 1, "utf8"), instr, "plaintext");
                string outputEncoding = NormalizeOutputEncoding(ReadStringArg(args, 2, "base64"), instr, "output");
                RSAEncryptionPadding padding = ParseRsaEncryptionPadding(ReadStringArg(args, 3, "oaep-sha256"), instr);
                return BytesToValue(handle.Value.Encrypt(plaintext, padding), outputEncoding, instr);
            })));

            intrinsics.Register(t, new IntrinsicDescriptor("decrypt", 1, 4, (recv, args, instr) => Guard(instr, () =>
            {
                RsaHandle handle = (RsaHandle)recv;
                handle.ThrowIfDisposed();
                if (!handle.HasPrivateKey)
                    throw Runtime(instr, "RSA handle does not contain a private key");

                byte[] cipher = ConvertToBytes(args[0], ReadStringArg(args, 1, "base64"), instr, "ciphertext");
                string outputEncoding = NormalizeOutputEncoding(ReadStringArg(args, 2, "utf8"), instr, "output");
                RSAEncryptionPadding padding = ParseRsaEncryptionPadding(ReadStringArg(args, 3, "oaep-sha256"), instr);
                return BytesToValue(handle.Value.Decrypt(cipher, padding), outputEncoding, instr);
            })));

            intrinsics.Register(t, new IntrinsicDescriptor("sign", 1, 5, (recv, args, instr) => Guard(instr, () =>
            {
                RsaHandle handle = (RsaHandle)recv;
                handle.ThrowIfDisposed();
                if (!handle.HasPrivateKey)
                    throw Runtime(instr, "RSA handle does not contain a private key");

                byte[] data = ConvertToBytes(args[0], ReadStringArg(args, 2, "utf8"), instr, "data");
                HashAlgorithmName hash = ParseHashAlgorithmName(ReadStringArg(args, 1, "sha256"), instr);
                string outputEncoding = NormalizeOutputEncoding(ReadStringArg(args, 3, "base64"), instr, "output");
                RSASignaturePadding padding = ParseRsaSignaturePadding(ReadStringArg(args, 4, "pkcs1"), instr);
                return BytesToValue(handle.Value.SignData(data, hash, padding), outputEncoding, instr);
            })));

            intrinsics.Register(t, new IntrinsicDescriptor("verify", 2, 6, (recv, args, instr) => Guard(instr, () =>
            {
                RsaHandle handle = (RsaHandle)recv;
                handle.ThrowIfDisposed();

                byte[] data = ConvertToBytes(args[0], ReadStringArg(args, 3, "utf8"), instr, "data");
                byte[] signature = ConvertToBytes(args[1], ReadStringArg(args, 4, "base64"), instr, "signature");
                HashAlgorithmName hash = ParseHashAlgorithmName(ReadStringArg(args, 2, "sha256"), instr);
                RSASignaturePadding padding = ParseRsaSignaturePadding(ReadStringArg(args, 5, "pkcs1"), instr);
                return handle.Value.VerifyData(data, signature, hash, padding);
            })));
        }

        private static void RegisterEcdsaIntrinsics(IIntrinsicRegistry intrinsics)
        {
            Type t = typeof(EcdsaHandle);

            intrinsics.Register(t, new IntrinsicDescriptor("close", 0, 0, (recv, args, instr) =>
            {
                ((EcdsaHandle)recv).Dispose();
                return null!;
            }));

            intrinsics.Register(t, new IntrinsicDescriptor("has_private", 0, 0, (recv, args, instr) => Guard(instr, () => ((EcdsaHandle)recv).HasPrivateKey)));
            intrinsics.Register(t, new IntrinsicDescriptor("curve", 0, 0, (recv, args, instr) => Guard(instr, () => ((EcdsaHandle)recv).CurveName)));
            intrinsics.Register(t, new IntrinsicDescriptor("key_size", 0, 0, (recv, args, instr) => Guard(instr, () => ((EcdsaHandle)recv).KeySize)));

            intrinsics.Register(t, new IntrinsicDescriptor("public_pem", 0, 0, (recv, args, instr) => Guard(instr, () =>
            {
                EcdsaHandle handle = (EcdsaHandle)recv;
                handle.ThrowIfDisposed();
                return handle.Value.ExportSubjectPublicKeyInfoPem();
            })));

            intrinsics.Register(t, new IntrinsicDescriptor("private_pem", 0, 0, (recv, args, instr) => Guard(instr, () =>
            {
                EcdsaHandle handle = (EcdsaHandle)recv;
                handle.ThrowIfDisposed();
                if (!handle.HasPrivateKey)
                    throw Runtime(instr, "ECDSA handle does not contain a private key");
                return handle.Value.ExportPkcs8PrivateKeyPem();
            })));

            intrinsics.Register(t, new IntrinsicDescriptor("sign", 1, 4, (recv, args, instr) => Guard(instr, () =>
            {
                EcdsaHandle handle = (EcdsaHandle)recv;
                handle.ThrowIfDisposed();
                if (!handle.HasPrivateKey)
                    throw Runtime(instr, "ECDSA handle does not contain a private key");

                byte[] data = ConvertToBytes(args[0], ReadStringArg(args, 2, "utf8"), instr, "data");
                HashAlgorithmName hash = ParseHashAlgorithmName(ReadStringArg(args, 1, "sha256"), instr);
                string outputEncoding = NormalizeOutputEncoding(ReadStringArg(args, 3, "base64"), instr, "output");
                return BytesToValue(handle.Value.SignData(data, hash), outputEncoding, instr);
            })));

            intrinsics.Register(t, new IntrinsicDescriptor("verify", 2, 5, (recv, args, instr) => Guard(instr, () =>
            {
                EcdsaHandle handle = (EcdsaHandle)recv;
                handle.ThrowIfDisposed();

                byte[] data = ConvertToBytes(args[0], ReadStringArg(args, 3, "utf8"), instr, "data");
                byte[] signature = ConvertToBytes(args[1], ReadStringArg(args, 4, "base64"), instr, "signature");
                HashAlgorithmName hash = ParseHashAlgorithmName(ReadStringArg(args, 2, "sha256"), instr);
                return handle.Value.VerifyData(data, signature, hash);
            })));
        }

        private static void RegisterX509Intrinsics(IIntrinsicRegistry intrinsics)
        {
            Type t = typeof(X509CertHandle);

            intrinsics.Register(t, new IntrinsicDescriptor("close", 0, 0, (recv, args, instr) =>
            {
                ((X509CertHandle)recv).Dispose();
                return null!;
            }));

            intrinsics.Register(t, new IntrinsicDescriptor("subject", 0, 0, (recv, args, instr) => Guard(instr, () => ((X509CertHandle)recv).Value.Subject)));
            intrinsics.Register(t, new IntrinsicDescriptor("issuer", 0, 0, (recv, args, instr) => Guard(instr, () => ((X509CertHandle)recv).Value.Issuer)));
            intrinsics.Register(t, new IntrinsicDescriptor("thumbprint", 0, 0, (recv, args, instr) => Guard(instr, () => ((X509CertHandle)recv).Value.Thumbprint ?? string.Empty)));
            intrinsics.Register(t, new IntrinsicDescriptor("serial", 0, 0, (recv, args, instr) => Guard(instr, () => ((X509CertHandle)recv).Value.SerialNumber ?? string.Empty)));
            intrinsics.Register(t, new IntrinsicDescriptor("version", 0, 0, (recv, args, instr) => Guard(instr, () => ((X509CertHandle)recv).Value.Version)));
            intrinsics.Register(t, new IntrinsicDescriptor("not_before", 0, 0, (recv, args, instr) => Guard(instr, () => ((X509CertHandle)recv).Value.NotBefore)));
            intrinsics.Register(t, new IntrinsicDescriptor("not_after", 0, 0, (recv, args, instr) => Guard(instr, () => ((X509CertHandle)recv).Value.NotAfter)));
            intrinsics.Register(t, new IntrinsicDescriptor("has_private", 0, 0, (recv, args, instr) => Guard(instr, () => ((X509CertHandle)recv).HasPrivateKey)));

            intrinsics.Register(t, new IntrinsicDescriptor("algorithm", 0, 0, (recv, args, instr) => Guard(instr, () =>
            {
                X509Certificate2 cert = ((X509CertHandle)recv).Value;
                if (cert.GetRSAPublicKey() != null) return "RSA";
                if (cert.GetECDsaPublicKey() != null) return "ECDSA";
                return cert.PublicKey?.Oid?.FriendlyName ?? cert.PublicKey?.Oid?.Value ?? "unknown";
            })));

            intrinsics.Register(t, new IntrinsicDescriptor("pem", 0, 0, (recv, args, instr) => Guard(instr, () =>
            {
                return EncodePem("CERTIFICATE", ((X509CertHandle)recv).Value.Export(X509ContentType.Cert));
            })));

            intrinsics.Register(t, new IntrinsicDescriptor("public_key_pem", 0, 0, (recv, args, instr) => Guard(instr, () =>
            {
                X509Certificate2 cert = ((X509CertHandle)recv).Value;
                if (cert.GetRSAPublicKey() is RSA rsa)
                    return rsa.ExportSubjectPublicKeyInfoPem();
                if (cert.GetECDsaPublicKey() is ECDsa ecdsa)
                    return ecdsa.ExportSubjectPublicKeyInfoPem();
                throw Runtime(instr, "certificate public key algorithm is not supported");
            })));

            intrinsics.Register(t, new IntrinsicDescriptor("private_pem", 0, 0, (recv, args, instr) => Guard(instr, () =>
            {
                X509Certificate2 cert = ((X509CertHandle)recv).Value;
                if (!cert.HasPrivateKey)
                    throw Runtime(instr, "certificate does not contain a private key");

                if (cert.GetRSAPrivateKey() is RSA rsa)
                    return rsa.ExportPkcs8PrivateKeyPem();
                if (cert.GetECDsaPrivateKey() is ECDsa ecdsa)
                    return ecdsa.ExportPkcs8PrivateKeyPem();
                throw Runtime(instr, "certificate private key algorithm is not supported");
            })));

            intrinsics.Register(t, new IntrinsicDescriptor("export_pfx", 0, 2, (recv, args, instr) => Guard(instr, () =>
            {
                X509Certificate2 cert = ((X509CertHandle)recv).Value;
                string password = args.Count >= 1 ? args[0]?.ToString() ?? string.Empty : string.Empty;
                string outputEncoding = NormalizeOutputEncoding(ReadStringArg(args, 1, "base64"), instr, "output");
                byte[] pfx = cert.Export(X509ContentType.Pfx, password);
                return BytesToValue(pfx, outputEncoding, instr);
            })));
        }

        private static void RegisterEd25519Intrinsics(IIntrinsicRegistry intrinsics)
        {
            Type t = typeof(Ed25519Handle);

            intrinsics.Register(t, new IntrinsicDescriptor("close", 0, 0, (recv, args, instr) =>
            {
                ((Ed25519Handle)recv).Dispose();
                return null!;
            }));

            intrinsics.Register(t, new IntrinsicDescriptor("has_private", 0, 0, (recv, args, instr) => Guard(instr, () => ((Ed25519Handle)recv).HasPrivateKey)));

            intrinsics.Register(t, new IntrinsicDescriptor("public_pem", 0, 0, (recv, args, instr) => Guard(instr, () =>
            {
                Ed25519Handle handle = (Ed25519Handle)recv;
                handle.ThrowIfDisposed();
                return ExportPublicKeyPem(handle.PublicKey);
            })));

            intrinsics.Register(t, new IntrinsicDescriptor("private_pem", 0, 0, (recv, args, instr) => Guard(instr, () =>
            {
                Ed25519Handle handle = (Ed25519Handle)recv;
                handle.ThrowIfDisposed();
                if (!handle.HasPrivateKey || handle.PrivateKey == null)
                    throw Runtime(instr, "Ed25519 handle does not contain a private key");
                return ExportPrivateKeyPem(handle.PrivateKey);
            })));

            intrinsics.Register(t, new IntrinsicDescriptor("sign", 1, 3, (recv, args, instr) => Guard(instr, () =>
            {
                Ed25519Handle handle = (Ed25519Handle)recv;
                handle.ThrowIfDisposed();
                if (!handle.HasPrivateKey || handle.PrivateKey == null)
                    throw Runtime(instr, "Ed25519 handle does not contain a private key");

                byte[] data = ConvertToBytes(args[0], ReadStringArg(args, 1, "utf8"), instr, "data");
                string outputEncoding = NormalizeOutputEncoding(ReadStringArg(args, 2, "base64"), instr, "output");
                return BytesToValue(SignEd25519(data, handle.PrivateKey), outputEncoding, instr);
            })));

            intrinsics.Register(t, new IntrinsicDescriptor("verify", 2, 4, (recv, args, instr) => Guard(instr, () =>
            {
                Ed25519Handle handle = (Ed25519Handle)recv;
                handle.ThrowIfDisposed();

                byte[] data = ConvertToBytes(args[0], ReadStringArg(args, 2, "utf8"), instr, "data");
                byte[] signature = ConvertToBytes(args[1], ReadStringArg(args, 3, "base64"), instr, "signature");
                return VerifyEd25519(data, signature, handle.PublicKey);
            })));
        }

        private static void RegisterX25519Intrinsics(IIntrinsicRegistry intrinsics)
        {
            Type t = typeof(X25519Handle);

            intrinsics.Register(t, new IntrinsicDescriptor("close", 0, 0, (recv, args, instr) =>
            {
                ((X25519Handle)recv).Dispose();
                return null!;
            }));

            intrinsics.Register(t, new IntrinsicDescriptor("has_private", 0, 0, (recv, args, instr) => Guard(instr, () => ((X25519Handle)recv).HasPrivateKey)));

            intrinsics.Register(t, new IntrinsicDescriptor("public_pem", 0, 0, (recv, args, instr) => Guard(instr, () =>
            {
                X25519Handle handle = (X25519Handle)recv;
                handle.ThrowIfDisposed();
                return ExportPublicKeyPem(handle.PublicKey);
            })));

            intrinsics.Register(t, new IntrinsicDescriptor("private_pem", 0, 0, (recv, args, instr) => Guard(instr, () =>
            {
                X25519Handle handle = (X25519Handle)recv;
                handle.ThrowIfDisposed();
                if (!handle.HasPrivateKey || handle.PrivateKey == null)
                    throw Runtime(instr, "X25519 handle does not contain a private key");
                return ExportPrivateKeyPem(handle.PrivateKey);
            })));

            intrinsics.Register(t, new IntrinsicDescriptor("derive", 1, 2, (recv, args, instr) => Guard(instr, () =>
            {
                X25519Handle handle = (X25519Handle)recv;
                handle.ThrowIfDisposed();
                if (!handle.HasPrivateKey || handle.PrivateKey == null)
                    throw Runtime(instr, "X25519 handle does not contain a private key");

                X25519PublicKeyParameters peerKey = ReadX25519PeerPublicKey(args[0], instr);
                string outputEncoding = NormalizeOutputEncoding(ReadStringArg(args, 1, "base64"), instr, "output");
                return BytesToValue(DeriveX25519SharedSecret(handle.PrivateKey, peerKey), outputEncoding, instr);
            })));
        }

        private static object Guard(Instruction instr, Func<object> body)
        {
            EnsureAllowed(instr);

            try
            {
                return body();
            }
            catch (VMException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw Runtime(instr, $"crypto error ({ex.GetType().Name}): {ex.Message}");
            }
        }

        private static void EnsureAllowed(Instruction instr)
        {
            if (!AllowCrypto)
                throw Runtime(instr, "cryptographic operations are disabled (AllowCrypto=false)");
        }

        private static VMException Runtime(Instruction instr, string message)
        {
            return new VMException(
                $"Runtime error: {message}",
                instr.Line,
                instr.Col,
                instr.OriginFile,
                VM.IsDebugging,
                VM.DebugStream!);
        }

        private static object RequireField(Dictionary<string, object> payload, string key, Instruction instr)
        {
            if (!payload.TryGetValue(key, out object? value) || value == null)
                throw Runtime(instr, $"payload field '{key}' is required");

            return value;
        }

        private static int ReadInt(object? value, string name, Instruction instr, int minValue)
        {
            try
            {
                int parsed = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                if (parsed < minValue)
                    throw Runtime(instr, $"{name} must be >= {minValue}");
                return parsed;
            }
            catch (VMException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw Runtime(instr, $"{name} must be an integer ({ex.GetType().Name}: {ex.Message})");
            }
        }

        private static string ReadStringArg(List<object> args, int index, string defaultValue)
        {
            if (args.Count <= index || args[index] == null)
                return defaultValue;

            return args[index]?.ToString() ?? defaultValue;
        }

        private static string NormalizeInputEncoding(string encoding, Instruction instr, string name)
        {
            string normalized = (encoding ?? "utf8").Trim().ToLowerInvariant();
            return normalized switch
            {
                "" => "utf8",
                "auto" => "auto",
                "utf8" => "utf8",
                "utf-8" => "utf8",
                "hex" => "hex",
                "base64" => "base64",
                "b64" => "base64",
                "base32" => "base32",
                "b32" => "base32",
                _ => throw Runtime(instr, $"{name} encoding '{encoding}' is not supported")
            };
        }

        private static string NormalizeOutputEncoding(string encoding, Instruction instr, string name)
        {
            string normalized = (encoding ?? "hex").Trim().ToLowerInvariant();
            return normalized switch
            {
                "" => "hex",
                "blob" => "blob",
                "bytes" => "bytes",
                "array" => "bytes",
                "hex" => "hex",
                "base64" => "base64",
                "b64" => "base64",
                "base32" => "base32",
                "b32" => "base32",
                "utf8" => "utf8",
                "utf-8" => "utf8",
                _ => throw Runtime(instr, $"{name} encoding '{encoding}' is not supported")
            };
        }

        private static string NormalizeJwtAlgorithm(string algorithm, Instruction instr)
        {
            string normalized = (algorithm ?? "hs256").Trim().ToLowerInvariant();
            return normalized switch
            {
                "" => "hs256",
                "hs256" => "hs256",
                "hs384" => "hs384",
                "hs512" => "hs512",
                "rs256" => "rs256",
                "rs384" => "rs384",
                "rs512" => "rs512",
                "ps256" => "ps256",
                "ps384" => "ps384",
                "ps512" => "ps512",
                "es256" => "es256",
                "es384" => "es384",
                "es512" => "es512",
                "eddsa" => "eddsa",
                _ => throw Runtime(instr, $"JWT algorithm '{algorithm}' is not supported")
            };
        }

        private static string GetJwtHeaderAlgorithm(string algorithm)
        {
            return algorithm switch
            {
                "eddsa" => "EdDSA",
                _ => algorithm.ToUpperInvariant()
            };
        }

        private static string JwtSign(object payload, object key, string algorithm, Dictionary<string, object>? headers, Instruction instr)
        {
            Dictionary<string, object> header = new(StringComparer.Ordinal)
            {
                ["alg"] = GetJwtHeaderAlgorithm(algorithm),
                ["typ"] = "JWT"
            };

            if (headers != null)
            {
                foreach (KeyValuePair<string, object> kv in headers)
                    header[kv.Key] = kv.Value;
            }

            header["alg"] = GetJwtHeaderAlgorithm(algorithm);
            if (!header.ContainsKey("typ"))
                header["typ"] = "JWT";

            string headerJson = SerializeCompactJson(header);
            string payloadJson = SerializeCompactJson(payload);
            string signingInput = Base64UrlEncode(Encoding.UTF8.GetBytes(headerJson)) + "." + Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));
            byte[] signature = SignJwtSignature(Encoding.ASCII.GetBytes(signingInput), key, algorithm, instr);
            return signingInput + "." + Base64UrlEncode(signature);
        }

        private static Dictionary<string, object> JwtDecode(string token, Instruction instr)
        {
            JwtParts parts = ParseJwtParts(token, instr);
            return BuildJwtResult(parts, valid: null, reason: null);
        }

        private static Dictionary<string, object> JwtVerify(string token, object key, string? algorithm, bool verifyTime, long clockSkewSeconds, Instruction instr)
        {
            JwtParts parts = ParseJwtParts(token, instr);
            string effectiveAlgorithm = algorithm ?? NormalizeJwtAlgorithm(parts.Algorithm, instr);
            if (!string.Equals(effectiveAlgorithm, NormalizeJwtAlgorithm(parts.Algorithm, instr), StringComparison.Ordinal))
                return BuildJwtResult(parts, valid: false, reason: "algorithm mismatch");

            bool signatureValid = VerifyJwtSignature(Encoding.ASCII.GetBytes(parts.SigningInput), parts.SignatureBytes, key, effectiveAlgorithm, instr);
            if (!signatureValid)
                return BuildJwtResult(parts, valid: false, reason: "invalid signature");

            if (verifyTime && parts.Payload is Dictionary<string, object> payload)
            {
                string? timeReason = ValidateJwtTimeClaims(payload, clockSkewSeconds);
                if (timeReason != null)
                    return BuildJwtResult(parts, valid: false, reason: timeReason);
            }

            return BuildJwtResult(parts, valid: true, reason: null);
        }

        private static string ComputeHotp(byte[] secret, long counter, int digits, string algorithm, Instruction instr)
        {
            if (counter < 0)
                throw Runtime(instr, "counter must be >= 0");

            if (digits <= 0 || digits > 10)
                throw Runtime(instr, "digits must be between 1 and 10");

            byte[] counterBytes = BitConverter.GetBytes(counter);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(counterBytes);

            using HMAC hmac = CreateHmac(algorithm, secret, instr);
            byte[] hash = hmac.ComputeHash(counterBytes);
            int offset = hash[^1] & 0x0F;
            int binary =
                ((hash[offset] & 0x7F) << 24) |
                ((hash[offset + 1] & 0xFF) << 16) |
                ((hash[offset + 2] & 0xFF) << 8) |
                (hash[offset + 3] & 0xFF);

            long mod = 1;
            for (int i = 0; i < digits; i++)
                mod *= 10;

            long otp = binary % mod;
            return otp.ToString(CultureInfo.InvariantCulture).PadLeft(digits, '0');
        }

        private static byte[] SignJwtSignature(byte[] data, object key, string algorithm, Instruction instr)
        {
            return algorithm switch
            {
                "hs256" or "hs384" or "hs512" => SignJwtHmac(data, key, algorithm, instr),
                "rs256" or "rs384" or "rs512" => UseJwtRsa(key, requirePrivate: true, instr, rsa => rsa.SignData(data, ParseJwtHashAlgorithm(algorithm, instr), RSASignaturePadding.Pkcs1)),
                "ps256" or "ps384" or "ps512" => UseJwtRsa(key, requirePrivate: true, instr, rsa => rsa.SignData(data, ParseJwtHashAlgorithm(algorithm, instr), RSASignaturePadding.Pss)),
                "es256" or "es384" or "es512" => UseJwtEcdsa(key, requirePrivate: true, instr, ecdsa => ecdsa.SignData(data, ParseJwtHashAlgorithm(algorithm, instr), DSASignatureFormat.IeeeP1363FixedFieldConcatenation)),
                "eddsa" => UseJwtEd25519(key, requirePrivate: true, instr, handle => SignEd25519(data, handle.PrivateKey!)),
                _ => throw Runtime(instr, $"JWT algorithm '{algorithm}' is not supported")
            };
        }

        private static bool VerifyJwtSignature(byte[] data, byte[] signature, object key, string algorithm, Instruction instr)
        {
            return algorithm switch
            {
                "hs256" or "hs384" or "hs512" => VerifyJwtHmac(data, signature, key, algorithm, instr),
                "rs256" or "rs384" or "rs512" => UseJwtRsa(key, requirePrivate: false, instr, rsa => rsa.VerifyData(data, signature, ParseJwtHashAlgorithm(algorithm, instr), RSASignaturePadding.Pkcs1)),
                "ps256" or "ps384" or "ps512" => UseJwtRsa(key, requirePrivate: false, instr, rsa => rsa.VerifyData(data, signature, ParseJwtHashAlgorithm(algorithm, instr), RSASignaturePadding.Pss)),
                "es256" or "es384" or "es512" => UseJwtEcdsa(key, requirePrivate: false, instr, ecdsa => ecdsa.VerifyData(data, signature, ParseJwtHashAlgorithm(algorithm, instr), DSASignatureFormat.IeeeP1363FixedFieldConcatenation)),
                "eddsa" => UseJwtEd25519(key, requirePrivate: false, instr, handle => VerifyEd25519(data, signature, handle.PublicKey)),
                _ => throw Runtime(instr, $"JWT algorithm '{algorithm}' is not supported")
            };
        }

        private static byte[] SignJwtHmac(byte[] data, object key, string algorithm, Instruction instr)
        {
            byte[] secret = ConvertToBytes(key, "auto", instr, "jwt key");
            using HMAC hmac = CreateHmac(ParseJwtHmacAlgorithm(algorithm, instr), secret, instr);
            return hmac.ComputeHash(data);
        }

        private static bool VerifyJwtHmac(byte[] data, byte[] signature, object key, string algorithm, Instruction instr)
        {
            byte[] expected = SignJwtHmac(data, key, algorithm, instr);
            return expected.Length == signature.Length && CryptographicOperations.FixedTimeEquals(expected, signature);
        }

        private static T UseJwtRsa<T>(object key, bool requirePrivate, Instruction instr, Func<RSA, T> action)
        {
            switch (key)
            {
                case RsaHandle rsaHandle:
                    rsaHandle.ThrowIfDisposed();
                    if (requirePrivate && !rsaHandle.HasPrivateKey)
                        throw Runtime(instr, "RSA key does not contain a private key");
                    return action(rsaHandle.Value);

                case X509CertHandle certHandle:
                    certHandle.ThrowIfDisposed();
                    using (RSA? rsa = requirePrivate ? certHandle.Value.GetRSAPrivateKey() : certHandle.Value.GetRSAPublicKey())
                    {
                        if (rsa == null)
                            throw Runtime(instr, "certificate does not expose an RSA key");
                        return action(rsa);
                    }

                default:
                    throw Runtime(instr, "JWT RSA algorithms require an RSA handle or X509 certificate");
            }
        }

        private static T UseJwtEcdsa<T>(object key, bool requirePrivate, Instruction instr, Func<ECDsa, T> action)
        {
            switch (key)
            {
                case EcdsaHandle ecdsaHandle:
                    ecdsaHandle.ThrowIfDisposed();
                    if (requirePrivate && !ecdsaHandle.HasPrivateKey)
                        throw Runtime(instr, "ECDSA key does not contain a private key");
                    return action(ecdsaHandle.Value);

                case X509CertHandle certHandle:
                    certHandle.ThrowIfDisposed();
                    using (ECDsa? ecdsa = requirePrivate ? certHandle.Value.GetECDsaPrivateKey() : certHandle.Value.GetECDsaPublicKey())
                    {
                        if (ecdsa == null)
                            throw Runtime(instr, "certificate does not expose an ECDSA key");
                        return action(ecdsa);
                    }

                default:
                    throw Runtime(instr, "JWT ECDSA algorithms require an ECDSA handle or X509 certificate");
            }
        }

        private static T UseJwtEd25519<T>(object key, bool requirePrivate, Instruction instr, Func<Ed25519Handle, T> action)
        {
            if (key is not Ed25519Handle handle)
                throw Runtime(instr, "JWT EdDSA algorithms require an Ed25519 handle");

            handle.ThrowIfDisposed();
            if (requirePrivate && !handle.HasPrivateKey)
                throw Runtime(instr, "Ed25519 key does not contain a private key");

            return action(handle);
        }

        private static HashAlgorithmName ParseJwtHashAlgorithm(string algorithm, Instruction instr)
        {
            return NormalizeJwtAlgorithm(algorithm, instr) switch
            {
                "hs256" or "rs256" or "ps256" or "es256" => HashAlgorithmName.SHA256,
                "hs384" or "rs384" or "ps384" or "es384" => HashAlgorithmName.SHA384,
                "hs512" or "rs512" or "ps512" or "es512" => HashAlgorithmName.SHA512,
                _ => throw Runtime(instr, $"JWT algorithm '{algorithm}' is not supported")
            };
        }

        private static string ParseJwtHmacAlgorithm(string algorithm, Instruction instr)
        {
            return NormalizeJwtAlgorithm(algorithm, instr) switch
            {
                "hs256" => "sha256",
                "hs384" => "sha384",
                "hs512" => "sha512",
                _ => throw Runtime(instr, $"JWT HMAC algorithm '{algorithm}' is not supported")
            };
        }

        private static string? ValidateJwtTimeClaims(Dictionary<string, object> payload, long clockSkewSeconds)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            if (TryReadUnixTimeClaim(payload, "nbf", out long nbf) && now + clockSkewSeconds < nbf)
                return "token not active yet";

            if (TryReadUnixTimeClaim(payload, "iat", out long iat) && now + clockSkewSeconds < iat)
                return "token issued in the future";

            if (TryReadUnixTimeClaim(payload, "exp", out long exp) && now - clockSkewSeconds >= exp)
                return "token expired";

            return null;
        }

        private static bool TryReadUnixTimeClaim(Dictionary<string, object> payload, string key, out long value)
        {
            value = 0;
            if (!payload.TryGetValue(key, out object? raw) || raw == null)
                return false;

            try
            {
                switch (raw)
                {
                    case int i:
                        value = i;
                        return true;
                    case long l:
                        value = l;
                        return true;
                    case decimal d:
                        value = (long)d;
                        return true;
                    case double dbl:
                        value = (long)dbl;
                        return true;
                    default:
                        if (long.TryParse(raw.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed))
                        {
                            value = parsed;
                            return true;
                        }
                        return false;
                }
            }
            catch
            {
                return false;
            }
        }

        private sealed class JwtParts
        {
            public required Dictionary<string, object> Header { get; init; }
            public required object Payload { get; init; }
            public required byte[] SignatureBytes { get; init; }
            public required string SignatureText { get; init; }
            public required string SigningInput { get; init; }
            public required string Algorithm { get; init; }
        }

        private static JwtParts ParseJwtParts(string token, Instruction instr)
        {
            string[] parts = (token ?? string.Empty).Split('.');
            if (parts.Length != 3)
                throw Runtime(instr, "JWT must contain exactly three sections");

            byte[] headerBytes = Base64UrlDecode(parts[0], instr, "jwt.header");
            byte[] payloadBytes = Base64UrlDecode(parts[1], instr, "jwt.payload");
            byte[] signatureBytes = Base64UrlDecode(parts[2], instr, "jwt.signature");

            object headerObj = ParseJsonVm(Encoding.UTF8.GetString(headerBytes));
            if (headerObj is not Dictionary<string, object> header)
                throw Runtime(instr, "JWT header must decode to a dictionary");

            object payload = ParseJsonVm(Encoding.UTF8.GetString(payloadBytes));
            string algorithm = header.TryGetValue("alg", out object? alg) ? alg?.ToString() ?? string.Empty : string.Empty;
            if (string.IsNullOrWhiteSpace(algorithm))
                throw Runtime(instr, "JWT header is missing 'alg'");

            return new JwtParts
            {
                Header = header,
                Payload = payload,
                SignatureBytes = signatureBytes,
                SignatureText = parts[2],
                SigningInput = parts[0] + "." + parts[1],
                Algorithm = algorithm
            };
        }

        private static Dictionary<string, object> BuildJwtResult(JwtParts parts, bool? valid, string? reason)
        {
            Dictionary<string, object> result = new(StringComparer.Ordinal)
            {
                ["header"] = parts.Header,
                ["payload"] = parts.Payload,
                ["signature"] = parts.SignatureText,
                ["signing_input"] = parts.SigningInput,
                ["algorithm"] = parts.Algorithm
            };

            if (valid != null)
                result["valid"] = valid.Value;
            if (!string.IsNullOrWhiteSpace(reason))
                result["reason"] = reason;

            return result;
        }

        private static X509CertHandle CreateSelfSignedCertificate(string subject, object key, Dictionary<string, object>? options, Instruction instr)
        {
            if (string.IsNullOrWhiteSpace(subject))
                throw Runtime(instr, "certificate subject must not be empty");

            string normalizedSubject = NormalizeCertificateSubject(subject);
            string hashName = ReadOptionString(options, "hash", "sha256");
            HashAlgorithmName hash = ParseCertificateHashAlgorithm(hashName, instr);
            bool isCa = ReadOptionBool(options, "ca", false);

            DateTimeOffset notBefore = ReadOptionDateTime(options, "not_before") ?? DateTimeOffset.UtcNow.AddMinutes(-5);
            DateTimeOffset notAfter = ReadOptionDateTime(options, "not_after") ??
                                      notBefore.AddDays(ReadOptionInt(options, "days", 365));

            if (notAfter <= notBefore)
                throw Runtime(instr, "certificate not_after must be greater than not_before");

            CertificateRequest request;
            bool isRsa;

            switch (key)
            {
                case RsaHandle rsaHandle:
                    rsaHandle.ThrowIfDisposed();
                    if (!rsaHandle.HasPrivateKey)
                        throw Runtime(instr, "certificate RSA key does not contain a private key");
                    request = new CertificateRequest(new X500DistinguishedName(normalizedSubject), rsaHandle.Value, hash, ParseRsaSignaturePadding(ReadOptionString(options, "padding", "pkcs1"), instr));
                    isRsa = true;
                    break;

                case EcdsaHandle ecdsaHandle:
                    ecdsaHandle.ThrowIfDisposed();
                    if (!ecdsaHandle.HasPrivateKey)
                        throw Runtime(instr, "certificate ECDSA key does not contain a private key");
                    request = new CertificateRequest(new X500DistinguishedName(normalizedSubject), ecdsaHandle.Value, hash);
                    isRsa = false;
                    break;

                default:
                    throw Runtime(instr, "certificate creation requires an RSA or ECDSA key handle");
            }

            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(isCa, isCa, ReadOptionInt(options, "path_length", 0), critical: isCa));
            request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

            X509KeyUsageFlags keyUsage = ReadKeyUsage(options, isCa, isRsa);
            request.CertificateExtensions.Add(new X509KeyUsageExtension(keyUsage, critical: true));

            OidCollection enhancedUsages = ReadEnhancedUsages(options);
            if (enhancedUsages.Count > 0)
                request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(enhancedUsages, critical: false));

            SubjectAlternativeNameBuilder san = new();
            bool hasSan = AddSubjectAlternativeNames(san, options);
            if (hasSan)
                request.CertificateExtensions.Add(san.Build());

            X509Certificate2 cert = request.CreateSelfSigned(notBefore, notAfter);
            return new X509CertHandle(cert);
        }

        private static X509CertHandle ImportCertificateFromPem(string certPem, string? keyPem, Instruction instr)
        {
            byte[] certBytes = ExtractPem(certPem, "CERTIFICATE", instr);
            X509Certificate2 cert = X509CertificateLoader.LoadCertificate(certBytes);

            if (string.IsNullOrWhiteSpace(keyPem))
                return new X509CertHandle(cert);

            if (TryImportRsaFromPem(keyPem!, out RSA? rsa))
            {
                if (rsa == null)
                    throw Runtime(instr, "private key PEM is not a supported RSA key");
                using (rsa)
                {
                    return new X509CertHandle(cert.CopyWithPrivateKey(rsa));
                }
            }

            if (TryImportEcdsaFromPem(keyPem!, out ECDsa? ecdsa))
            {
                if (ecdsa == null)
                    throw Runtime(instr, "private key PEM is not a supported ECDSA key");
                using (ecdsa)
                {
                    return new X509CertHandle(cert.CopyWithPrivateKey(ecdsa));
                }
            }

            throw Runtime(instr, "private key PEM is not a supported RSA or ECDSA key");
        }

        private static string NormalizeCertificateSubject(string subject)
        {
            string trimmed = subject.Trim();
            return trimmed.Contains('=') ? trimmed : "CN=" + trimmed;
        }

        private static HashAlgorithmName ParseCertificateHashAlgorithm(string algorithm, Instruction instr)
        {
            HashAlgorithmName hash = ParseHashAlgorithmName(algorithm, instr);
            if (hash == HashAlgorithmName.MD5)
                throw Runtime(instr, "X509 certificates do not support MD5 in this plugin");
            return hash;
        }

        private static string ReadOptionString(Dictionary<string, object>? options, string key, string defaultValue)
        {
            if (options == null || !options.TryGetValue(key, out object? value) || value == null)
                return defaultValue;
            return value.ToString() ?? defaultValue;
        }

        private static bool ReadOptionBool(Dictionary<string, object>? options, string key, bool defaultValue)
        {
            if (options == null || !options.TryGetValue(key, out object? value) || value == null)
                return defaultValue;
            return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
        }

        private static int ReadOptionInt(Dictionary<string, object>? options, string key, int defaultValue)
        {
            if (options == null || !options.TryGetValue(key, out object? value) || value == null)
                return defaultValue;
            return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }

        private static DateTimeOffset? ReadOptionDateTime(Dictionary<string, object>? options, string key)
        {
            if (options == null || !options.TryGetValue(key, out object? value) || value == null)
                return null;

            return ConvertToDateTimeOffset(value);
        }

        private static DateTimeOffset ConvertToDateTimeOffset(object value)
        {
            return value switch
            {
                DateTime dt => new DateTimeOffset(dt.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(dt, DateTimeKind.Utc) : dt),
                DateTimeOffset dto => dto,
                int i => DateTimeOffset.FromUnixTimeSeconds(i),
                long l => DateTimeOffset.FromUnixTimeSeconds(l),
                double d => DateTimeOffset.FromUnixTimeSeconds((long)d),
                decimal dec => DateTimeOffset.FromUnixTimeSeconds((long)dec),
                string s when DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind | DateTimeStyles.AllowWhiteSpaces, out DateTimeOffset parsed) => parsed,
                string s when DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind | DateTimeStyles.AllowWhiteSpaces, out DateTime parsedDt) => new DateTimeOffset(parsedDt.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(parsedDt, DateTimeKind.Utc) : parsedDt),
                _ => throw new InvalidOperationException($"Unsupported date value '{value}'")
            };
        }

        private static X509KeyUsageFlags ReadKeyUsage(Dictionary<string, object>? options, bool isCa, bool isRsa)
        {
            if (options == null || !options.TryGetValue("usage", out object? raw) || raw == null)
            {
                X509KeyUsageFlags defaults = X509KeyUsageFlags.DigitalSignature;
                if (isRsa)
                    defaults |= X509KeyUsageFlags.KeyEncipherment;
                if (isCa)
                    defaults |= X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign;
                return defaults;
            }

            X509KeyUsageFlags flags = 0;
            foreach (string item in ReadStringList(raw))
            {
                flags |= item.Trim().ToLowerInvariant() switch
                {
                    "digitalsignature" or "digital_signature" or "digital-signature" => X509KeyUsageFlags.DigitalSignature,
                    "nonrepudiation" or "contentcommitment" or "content_commitment" => X509KeyUsageFlags.NonRepudiation,
                    "keyencipherment" or "key_encipherment" => X509KeyUsageFlags.KeyEncipherment,
                    "dataencipherment" or "data_encipherment" => X509KeyUsageFlags.DataEncipherment,
                    "keyagreement" or "key_agreement" => X509KeyUsageFlags.KeyAgreement,
                    "keycertsign" or "key_cert_sign" => X509KeyUsageFlags.KeyCertSign,
                    "crlsign" or "crl_sign" => X509KeyUsageFlags.CrlSign,
                    "encipheronly" or "encipher_only" => X509KeyUsageFlags.EncipherOnly,
                    "decipheronly" or "decipher_only" => X509KeyUsageFlags.DecipherOnly,
                    _ => throw new InvalidOperationException($"Unsupported key usage '{item}'")
                };
            }

            return flags;
        }

        private static OidCollection ReadEnhancedUsages(Dictionary<string, object>? options)
        {
            OidCollection collection = new();
            if (options == null || !options.TryGetValue("enhanced_usage", out object? raw) || raw == null)
                return collection;

            foreach (string item in ReadStringList(raw))
            {
                string normalized = item.Trim();
                string oid = normalized.ToLowerInvariant() switch
                {
                    "serverauth" or "server_auth" => "1.3.6.1.5.5.7.3.1",
                    "clientauth" or "client_auth" => "1.3.6.1.5.5.7.3.2",
                    "codesigning" or "code_signing" => "1.3.6.1.5.5.7.3.3",
                    "emailprotection" or "email_protection" => "1.3.6.1.5.5.7.3.4",
                    "timestamping" => "1.3.6.1.5.5.7.3.8",
                    "ocspsigning" or "ocsp_signing" => "1.3.6.1.5.5.7.3.9",
                    _ => normalized
                };
                collection.Add(new Oid(oid));
            }

            return collection;
        }

        private static bool AddSubjectAlternativeNames(SubjectAlternativeNameBuilder san, Dictionary<string, object>? options)
        {
            bool hasAny = false;
            if (options == null)
                return false;

            if (options.TryGetValue("dns", out object? dns) && dns != null)
            {
                foreach (string item in ReadStringList(dns))
                {
                    san.AddDnsName(item);
                    hasAny = true;
                }
            }

            if (options.TryGetValue("emails", out object? emails) && emails != null)
            {
                foreach (string item in ReadStringList(emails))
                {
                    san.AddEmailAddress(item);
                    hasAny = true;
                }
            }

            if (options.TryGetValue("uris", out object? uris) && uris != null)
            {
                foreach (string item in ReadStringList(uris))
                {
                    san.AddUri(new Uri(item, UriKind.RelativeOrAbsolute));
                    hasAny = true;
                }
            }

            return hasAny;
        }

        private static List<string> ReadStringList(object value)
        {
            if (value is string s)
                return new List<string> { s };

            if (value is List<object> list)
            {
                List<string> strings = new(list.Count);
                foreach (object? item in list)
                    strings.Add(item?.ToString() ?? string.Empty);
                return strings;
            }

            return new List<string> { value.ToString() ?? string.Empty };
        }

        private static Ed25519Handle ImportEd25519FromPem(string pem, Instruction instr)
        {
            if (pem.Contains("-----BEGIN PRIVATE KEY-----", StringComparison.Ordinal))
            {
                byte[] keyBytes = ExtractPem(pem, "PRIVATE KEY", instr);
                AsymmetricKeyParameter key = PrivateKeyFactory.CreateKey(keyBytes);
                if (key is not Ed25519PrivateKeyParameters privateKey)
                    throw Runtime(instr, "PEM does not contain an Ed25519 private key");
                return new Ed25519Handle(privateKey.GeneratePublicKey(), privateKey);
            }

            if (pem.Contains("-----BEGIN PUBLIC KEY-----", StringComparison.Ordinal))
            {
                byte[] keyBytes = ExtractPem(pem, "PUBLIC KEY", instr);
                AsymmetricKeyParameter key = PublicKeyFactory.CreateKey(keyBytes);
                if (key is not Ed25519PublicKeyParameters publicKey)
                    throw Runtime(instr, "PEM does not contain an Ed25519 public key");
                return new Ed25519Handle(publicKey, null);
            }

            throw Runtime(instr, "PEM must contain a PUBLIC KEY or PRIVATE KEY block");
        }

        private static X25519Handle ImportX25519FromPem(string pem, Instruction instr)
        {
            if (pem.Contains("-----BEGIN PRIVATE KEY-----", StringComparison.Ordinal))
            {
                byte[] keyBytes = ExtractPem(pem, "PRIVATE KEY", instr);
                AsymmetricKeyParameter key = PrivateKeyFactory.CreateKey(keyBytes);
                if (key is not X25519PrivateKeyParameters privateKey)
                    throw Runtime(instr, "PEM does not contain an X25519 private key");
                return new X25519Handle(privateKey.GeneratePublicKey(), privateKey);
            }

            if (pem.Contains("-----BEGIN PUBLIC KEY-----", StringComparison.Ordinal))
            {
                byte[] keyBytes = ExtractPem(pem, "PUBLIC KEY", instr);
                AsymmetricKeyParameter key = PublicKeyFactory.CreateKey(keyBytes);
                if (key is not X25519PublicKeyParameters publicKey)
                    throw Runtime(instr, "PEM does not contain an X25519 public key");
                return new X25519Handle(publicKey, null);
            }

            throw Runtime(instr, "PEM must contain a PUBLIC KEY or PRIVATE KEY block");
        }

        private static string ExportPrivateKeyPem(AsymmetricKeyParameter privateKey)
        {
            return EncodePem("PRIVATE KEY", PrivateKeyInfoFactory.CreatePrivateKeyInfo(privateKey).GetEncoded());
        }

        private static string ExportPublicKeyPem(AsymmetricKeyParameter publicKey)
        {
            return EncodePem("PUBLIC KEY", SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(publicKey).GetEncoded());
        }

        private static byte[] SignEd25519(byte[] data, Ed25519PrivateKeyParameters privateKey)
        {
            Ed25519Signer signer = new();
            signer.Init(true, privateKey);
            signer.BlockUpdate(data, 0, data.Length);
            return signer.GenerateSignature();
        }

        private static bool VerifyEd25519(byte[] data, byte[] signature, Ed25519PublicKeyParameters publicKey)
        {
            Ed25519Signer signer = new();
            signer.Init(false, publicKey);
            signer.BlockUpdate(data, 0, data.Length);
            return signer.VerifySignature(signature);
        }

        private static X25519PublicKeyParameters ReadX25519PeerPublicKey(object? value, Instruction instr)
        {
            switch (value)
            {
                case X25519Handle handle:
                    handle.ThrowIfDisposed();
                    return handle.PublicKey;

                case string pem:
                    return ImportX25519FromPem(pem, instr).PublicKey;

                default:
                    throw Runtime(instr, "X25519 peer must be an X25519 handle or a PEM string");
            }
        }

        private static byte[] DeriveX25519SharedSecret(X25519PrivateKeyParameters privateKey, X25519PublicKeyParameters peerPublicKey)
        {
            X25519Agreement agreement = new();
            agreement.Init(privateKey);
            byte[] sharedSecret = new byte[32];
            agreement.CalculateAgreement(peerPublicKey, sharedSecret, 0);
            return sharedSecret;
        }

        private static bool TryImportRsaFromPem(string pem, out RSA? rsa)
        {
            try
            {
                RSA created = RSA.Create();
                created.ImportFromPem(pem);
                rsa = created;
                return true;
            }
            catch
            {
                rsa = null;
                return false;
            }
        }

        private static bool TryImportEcdsaFromPem(string pem, out ECDsa? ecdsa)
        {
            try
            {
                ECDsa created = ECDsa.Create();
                created.ImportFromPem(pem);
                ecdsa = created;
                return true;
            }
            catch
            {
                ecdsa = null;
                return false;
            }
        }

        private static string SerializeCompactJson(object value)
        {
            return JsonSerializer.Serialize(value, new JsonSerializerOptions
            {
                WriteIndented = false
            });
        }

        private static object ParseJsonVm(string json)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return JsonToVmValue(document.RootElement) ?? new Dictionary<string, object>(StringComparer.Ordinal);
        }

        private static object? JsonToVmValue(JsonElement elem)
        {
            switch (elem.ValueKind)
            {
                case JsonValueKind.Object:
                    Dictionary<string, object> dict = new(StringComparer.Ordinal);
                    foreach (JsonProperty prop in elem.EnumerateObject())
                        dict[prop.Name] = JsonToVmValue(prop.Value)!;
                    return dict;

                case JsonValueKind.Array:
                    List<object> list = new();
                    foreach (JsonElement item in elem.EnumerateArray())
                        list.Add(JsonToVmValue(item)!);
                    return list;

                case JsonValueKind.String:
                    return elem.GetString() ?? string.Empty;

                case JsonValueKind.Number:
                    if (elem.TryGetInt32(out int i32)) return i32;
                    if (elem.TryGetInt64(out long i64)) return i64;
                    if (elem.TryGetDecimal(out decimal dec)) return dec;
                    if (elem.TryGetDouble(out double dbl)) return dbl;
                    return elem.GetRawText();

                case JsonValueKind.True:
                    return true;

                case JsonValueKind.False:
                    return false;

                case JsonValueKind.Null:
                case JsonValueKind.Undefined:
                    return null;

                default:
                    return elem.GetRawText();
            }
        }

        private static string Base64UrlEncode(byte[] bytes)
        {
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static byte[] Base64UrlDecode(string text, Instruction instr, string name)
        {
            string normalized = (text ?? string.Empty).Replace('-', '+').Replace('_', '/');
            int pad = normalized.Length % 4;
            if (pad != 0)
                normalized = normalized.PadRight(normalized.Length + (4 - pad), '=');

            try
            {
                return Convert.FromBase64String(normalized);
            }
            catch (Exception ex)
            {
                throw Runtime(instr, $"{name} is not valid base64url ({ex.GetType().Name}: {ex.Message})");
            }
        }

        private static byte[] ParseBase32(string text, Instruction instr, string name)
        {
            string normalized = (text ?? string.Empty)
                .Trim()
                .TrimEnd('=')
                .Replace(" ", string.Empty)
                .Replace("-", string.Empty)
                .ToUpperInvariant();

            const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
            List<byte> bytes = new();
            int buffer = 0;
            int bitsLeft = 0;

            foreach (char c in normalized)
            {
                int idx = alphabet.IndexOf(c);
                if (idx < 0)
                    throw Runtime(instr, $"{name} is not valid base32");

                buffer = (buffer << 5) | idx;
                bitsLeft += 5;

                if (bitsLeft >= 8)
                {
                    bitsLeft -= 8;
                    bytes.Add((byte)((buffer >> bitsLeft) & 0xFF));
                }
            }

            return bytes.ToArray();
        }

        private static string BytesToBase32(byte[] bytes)
        {
            const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
            StringBuilder sb = new();
            int buffer = 0;
            int bitsLeft = 0;

            foreach (byte b in bytes)
            {
                buffer = (buffer << 8) | b;
                bitsLeft += 8;

                while (bitsLeft >= 5)
                {
                    bitsLeft -= 5;
                    sb.Append(alphabet[(buffer >> bitsLeft) & 31]);
                }
            }

            if (bitsLeft > 0)
                sb.Append(alphabet[(buffer << (5 - bitsLeft)) & 31]);

            return sb.ToString();
        }

        private static string EncodePem(string label, byte[] bytes)
        {
            StringBuilder sb = new();
            sb.Append("-----BEGIN ").Append(label).AppendLine("-----");
            string base64 = Convert.ToBase64String(bytes);
            for (int i = 0; i < base64.Length; i += 64)
                sb.AppendLine(base64.Substring(i, Math.Min(64, base64.Length - i)));
            sb.Append("-----END ").Append(label).AppendLine("-----");
            return sb.ToString();
        }

        private static byte[] ExtractPem(string pem, string label, Instruction instr)
        {
            string begin = "-----BEGIN " + label + "-----";
            string end = "-----END " + label + "-----";
            int start = pem.IndexOf(begin, StringComparison.Ordinal);
            int finish = pem.IndexOf(end, StringComparison.Ordinal);
            if (start < 0 || finish < 0 || finish <= start)
                throw Runtime(instr, $"PEM block '{label}' was not found");

            start += begin.Length;
            string content = pem[start..finish]
                .Replace("\r", string.Empty)
                .Replace("\n", string.Empty)
                .Trim();

            try
            {
                return Convert.FromBase64String(content);
            }
            catch (Exception ex)
            {
                throw Runtime(instr, $"PEM block '{label}' is not valid base64 ({ex.GetType().Name}: {ex.Message})");
            }
        }

        private static byte[] ConvertToBytes(object? value, string encoding, Instruction instr, string name)
        {
            if (value == null)
                throw Runtime(instr, $"{name} must not be null");

            if (value is CryptoBlob blob)
                return blob.ToArray();

            if (value is byte[] rawBytes)
                return rawBytes.ToArray();

            if (value is List<object> list)
                return ConvertVmArrayToBytes(list, instr, name);

            if (value is char ch)
                return Encoding.UTF8.GetBytes(new string(ch, 1));

            if (value is string text)
            {
                return NormalizeInputEncoding(encoding, instr, name) switch
                {
                    "auto" => Encoding.UTF8.GetBytes(text),
                    "utf8" => Encoding.UTF8.GetBytes(text),
                    "hex" => ParseHex(text, instr, name),
                    "base64" => ParseBase64(text, instr, name),
                    "base32" => ParseBase32(text, instr, name),
                    _ => throw Runtime(instr, $"{name} encoding '{encoding}' is not supported")
                };
            }

            throw Runtime(instr, $"{name} must be a string, byte array, or CryptoBlob");
        }

        private static byte[] ConvertVmArrayToBytes(List<object> list, Instruction instr, string name)
        {
            byte[] bytes = new byte[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                try
                {
                    int value = Convert.ToInt32(list[i], CultureInfo.InvariantCulture);
                    if (value is < 0 or > 255)
                        throw Runtime(instr, $"{name}[{i}] must be between 0 and 255");
                    bytes[i] = (byte)value;
                }
                catch (VMException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    throw Runtime(instr, $"{name}[{i}] is not a valid byte ({ex.GetType().Name}: {ex.Message})");
                }
            }

            return bytes;
        }

        private static byte[] ParseHex(string text, Instruction instr, string name)
        {
            string normalized = (text ?? string.Empty).Trim();
            if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                normalized = normalized[2..];

            try
            {
                return Convert.FromHexString(normalized);
            }
            catch (Exception ex)
            {
                throw Runtime(instr, $"{name} is not valid hex ({ex.GetType().Name}: {ex.Message})");
            }
        }

        private static byte[] ParseBase64(string text, Instruction instr, string name)
        {
            try
            {
                return Convert.FromBase64String((text ?? string.Empty).Trim());
            }
            catch (Exception ex)
            {
                throw Runtime(instr, $"{name} is not valid base64 ({ex.GetType().Name}: {ex.Message})");
            }
        }

        private static object BytesToValue(byte[] bytes, string outputEncoding, Instruction instr)
        {
            string output = NormalizeOutputEncoding(outputEncoding, instr, "output");
            return output switch
            {
                "blob" => new CryptoBlob(bytes),
                "bytes" => ToVmByteArray(bytes),
                "hex" => BytesToHex(bytes),
                "base64" => Convert.ToBase64String(bytes),
                "base32" => BytesToBase32(bytes),
                "utf8" => Encoding.UTF8.GetString(bytes),
                _ => throw Runtime(instr, $"output encoding '{outputEncoding}' is not supported")
            };
        }

        private static List<object> ToVmByteArray(byte[] bytes)
        {
            List<object> list = new(bytes.Length);
            foreach (byte b in bytes)
                list.Add((int)b);
            return list;
        }

        private static string BytesToHex(byte[] bytes)
        {
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        private static byte[] ComputeHash(byte[] data, string algorithm, Instruction instr)
        {
            using HashAlgorithm hash = CreateHashAlgorithm(algorithm, instr);
            return hash.ComputeHash(data);
        }

        private static HashAlgorithm CreateHashAlgorithm(string algorithm, Instruction instr)
        {
            return NormalizeHashAlgorithm(algorithm, instr) switch
            {
                "md5" => MD5.Create(),
                "sha1" => SHA1.Create(),
                "sha256" => SHA256.Create(),
                "sha384" => SHA384.Create(),
                "sha512" => SHA512.Create(),
                _ => throw Runtime(instr, $"hash algorithm '{algorithm}' is not supported")
            };
        }

        private static HMAC CreateHmac(string algorithm, byte[] key, Instruction instr)
        {
            return NormalizeHashAlgorithm(algorithm, instr) switch
            {
                "md5" => new HMACMD5(key),
                "sha1" => new HMACSHA1(key),
                "sha256" => new HMACSHA256(key),
                "sha384" => new HMACSHA384(key),
                "sha512" => new HMACSHA512(key),
                _ => throw Runtime(instr, $"HMAC algorithm '{algorithm}' is not supported")
            };
        }

        private static string NormalizeHashAlgorithm(string algorithm, Instruction instr)
        {
            string normalized = (algorithm ?? "sha256").Trim().ToLowerInvariant();
            return normalized switch
            {
                "" => "sha256",
                "md5" => "md5",
                "sha1" => "sha1",
                "sha-1" => "sha1",
                "sha256" => "sha256",
                "sha-256" => "sha256",
                "sha384" => "sha384",
                "sha-384" => "sha384",
                "sha512" => "sha512",
                "sha-512" => "sha512",
                _ => throw Runtime(instr, $"hash algorithm '{algorithm}' is not supported")
            };
        }

        private static HashAlgorithmName ParseHashAlgorithmName(string algorithm, Instruction instr)
        {
            return NormalizeHashAlgorithm(algorithm, instr) switch
            {
                "md5" => HashAlgorithmName.MD5,
                "sha1" => HashAlgorithmName.SHA1,
                "sha256" => HashAlgorithmName.SHA256,
                "sha384" => HashAlgorithmName.SHA384,
                "sha512" => HashAlgorithmName.SHA512,
                _ => throw Runtime(instr, $"hash algorithm '{algorithm}' is not supported")
            };
        }

        private static HashAlgorithmName ParsePbkdf2HashAlgorithm(string algorithm, Instruction instr)
        {
            return NormalizeHashAlgorithm(algorithm, instr) switch
            {
                "sha1" => HashAlgorithmName.SHA1,
                "sha256" => HashAlgorithmName.SHA256,
                "sha384" => HashAlgorithmName.SHA384,
                "sha512" => HashAlgorithmName.SHA512,
                "md5" => throw Runtime(instr, "PBKDF2 does not support MD5 in this plugin"),
                _ => throw Runtime(instr, $"PBKDF2 hash algorithm '{algorithm}' is not supported")
            };
        }

        private static byte[] GetAesKeyBytes(object? value, Instruction instr)
        {
            byte[] key = ConvertToBytes(value, "auto", instr, "key");
            if (key.Length is not (16 or 24 or 32))
                throw Runtime(instr, "AES keys must be 16, 24, or 32 bytes long");
            return key;
        }

        private static RSAEncryptionPadding ParseRsaEncryptionPadding(string padding, Instruction instr)
        {
            string normalized = (padding ?? "oaep-sha256").Trim().ToLowerInvariant();
            return normalized switch
            {
                "" => RSAEncryptionPadding.OaepSHA256,
                "pkcs1" => RSAEncryptionPadding.Pkcs1,
                "oaep-sha1" => RSAEncryptionPadding.OaepSHA1,
                "oaep-sha256" => RSAEncryptionPadding.OaepSHA256,
                "oaep-sha384" => RSAEncryptionPadding.OaepSHA384,
                "oaep-sha512" => RSAEncryptionPadding.OaepSHA512,
                _ => throw Runtime(instr, $"RSA encryption padding '{padding}' is not supported")
            };
        }

        private static RSASignaturePadding ParseRsaSignaturePadding(string padding, Instruction instr)
        {
            string normalized = (padding ?? "pkcs1").Trim().ToLowerInvariant();
            return normalized switch
            {
                "" => RSASignaturePadding.Pkcs1,
                "pkcs1" => RSASignaturePadding.Pkcs1,
                "pss" => RSASignaturePadding.Pss,
                _ => throw Runtime(instr, $"RSA signature padding '{padding}' is not supported")
            };
        }

        private static ECCurve ParseCurve(string curveName, Instruction instr)
        {
            return NormalizeCurveName(curveName) switch
            {
                "nistP256" => ECCurve.NamedCurves.nistP256,
                "nistP384" => ECCurve.NamedCurves.nistP384,
                "nistP521" => ECCurve.NamedCurves.nistP521,
                _ => throw Runtime(instr, $"curve '{curveName}' is not supported")
            };
        }

        private static string NormalizeCurveName(string curveName)
        {
            string normalized = (curveName ?? "nistP256").Trim();
            if (string.IsNullOrWhiteSpace(normalized))
                return "nistP256";

            return normalized.ToLowerInvariant() switch
            {
                "p256" => "nistP256",
                "nistp256" => "nistP256",
                "prime256v1" => "nistP256",
                "secp256r1" => "nistP256",
                "p384" => "nistP384",
                "nistp384" => "nistP384",
                "secp384r1" => "nistP384",
                "p521" => "nistP521",
                "nistp521" => "nistP521",
                "secp521r1" => "nistP521",
                _ => normalized
            };
        }

        private static string GetCurveName(ECDsa ecdsa)
        {
            try
            {
                ECParameters parameters = ecdsa.ExportParameters(false);
                if (!string.IsNullOrWhiteSpace(parameters.Curve.Oid.FriendlyName))
                    return parameters.Curve.Oid.FriendlyName!;
                if (!string.IsNullOrWhiteSpace(parameters.Curve.Oid.Value))
                    return parameters.Curve.Oid.Value!;
            }
            catch
            {
            }

            return "unknown";
        }
    }
}
