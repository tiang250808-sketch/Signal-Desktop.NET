# 候选 1 深化方案：ManagedSignalProtocol 内部重构

## 现状分析

### 当前结构

`ManagedSignalProtocol`（334 行）是一个单一类，同时承担以下职责：

1. **密钥管理** — `GenerateDeviceKeysAsync`、`CreateSignedPreKey`、`CreatePlaceholderKyber`
2. **会话握手** — `ProcessPreKeyBundleAsync`（X3DH 计算 + DH 协议）
3. **消息加密** — `EncryptAsync`（消息密钥派生 + AES-CBC 加密）
4. **消息解密** — `DecryptAsync`（MAC 验证 + 解密 + 会话状态更新）
5. **会话序列化** — `SessionState` 的 JSON 编解码

### 问题

- 职责混合：一个类里既有密钥生成逻辑，又有消息加解密逻辑
- 难以测试：要测试消息加密，必须先经过完整的密钥生成和会话握手流程
- locality 差：修改消息密钥派生算法会影响到整个类，风险范围大
- 内部状态管理薄弱：使用 `object _gate` 锁保护，JSON 序列化无版本控制

### 约束

- 必须保留 `ISignalProtocolService` 接口不变（`ManagedSignalProtocol` 和 `FfiSignalProtocolService` 共享同一接口）
- `ManagedSignalProtocol` 是 FFI 不可用时的降级回退方案，必须保留

---

## 深化方案：内部三模块拆分

将 `ManagedSignalProtocol` 的内部逻辑拆分为三个内部类，`ManagedSignalProtocol` 本身降级为薄门面：

```
ManagedSignalProtocol（门面）
├── KeyManager          — 密钥生成、PreKey 创建
├── SessionCipher       — X3DH 握手 + 消息加解密
└── SessionState        — 状态模型 + JSON 序列化（已存在，保持）
```

### 1. KeyManager

**职责**：生成设备密钥、创建 SignedPreKey、OneTimePreKey、KyberPreKey（占位）。

```csharp
internal sealed class KeyManager
{
    private readonly IMessageStore _store;

    public KeyManager(IMessageStore store) { ... }

    public async Task<GeneratedDeviceKeys> GenerateDeviceKeysAsync(
        AccountCredentials credentials, bool enablePq, CancellationToken ct) { ... }

    internal static SignedPreKeyRecord CreateSignedPreKey(
        uint keyId, byte[] identityPrivate) { ... }

    internal static KyberPreKeyRecord CreatePlaceholderKyber(
        uint keyId, byte[] identityPrivate) { ... }
}
```

### 2. SessionCipher

**职责**：X3DH 握手处理、消息加密、消息解密、会话状态轮转。

```csharp
internal sealed class SessionCipher
{
    private readonly IMessageStore _store;
    private readonly AccountCredentials _credentials;
    private readonly object _gate = new();

    public SessionCipher(IMessageStore store, AccountCredentials credentials) { ... }

    public async Task ProcessPreKeyBundleAsync(
        string recipientServiceId, int deviceId,
        RemotePreKeyBundle bundle, CancellationToken ct) { ... }

    public async Task<EncryptedPayload> EncryptAsync(
        string recipientServiceId, int deviceId,
        byte[] plaintext, CancellationToken ct) { ... }

    public async Task<DecryptResult> DecryptAsync(
        string senderServiceId, int senderDeviceId,
        int envelopeType, byte[] ciphertext, CancellationToken ct) { ... }
}
```

### 3. SessionState（保持现状）

**职责**：会话状态模型 + JSON 序列化/反序列化。

```csharp
internal sealed class SessionState
{
    public int RegistrationId { get; set; }
    public byte[] RootKey { get; set; } = [];
    public byte[] SendChainKey { get; set; } = [];
    public byte[]? ReceiveChainKey { get; set; }
    public int SendCounter { get; set; }
    public int ReceiveCounter { get; set; }
    public byte[] TheirIdentityKey { get; set; } = [];
    public byte[] TheirRatchetKey { get; set; } = [];
    public byte[] OurEphemeralPrivate { get; set; } = [];
    public byte[] OurEphemeralPublic { get; set; } = [];
    public uint TheirSignedPreKeyId { get; set; }
    public uint? TheirPreKeyId { get; set; }
    public bool IsPreKeySession { get; set; }
}
```

### 门面类变化

重构后的 `ManagedSignalProtocol` 只做路由：

```csharp
public sealed class ManagedSignalProtocol : ISignalProtocolService
{
    private readonly KeyManager _keyManager;
    private readonly SessionCipher _sessionCipher;

    public ManagedSignalProtocol(IMessageStore store, AccountCredentials credentials)
    {
        _keyManager = new KeyManager(store);
        _sessionCipher = new SessionCipher(store, credentials);
    }

    public bool UsesNativeFfi => false;
    public int GenerateRegistrationId() => RandomNumberGenerator.GetInt32(1, 0x3FFF);

    public Task<GeneratedDeviceKeys> GenerateDeviceKeysAsync(
        AccountCredentials credentials, bool enablePq, CancellationToken ct) =>
        _keyManager.GenerateDeviceKeysAsync(credentials, enablePq, ct);

    public Task ProcessPreKeyBundleAsync(
        string recipientServiceId, int deviceId,
        RemotePreKeyBundle bundle, CancellationToken ct) =>
        _sessionCipher.ProcessPreKeyBundleAsync(recipientServiceId, deviceId, bundle, ct);

    public Task<EncryptedPayload> EncryptAsync(
        string recipientServiceId, int deviceId,
        byte[] plaintext, CancellationToken ct) =>
        _sessionCipher.EncryptAsync(recipientServiceId, deviceId, plaintext, ct);

    // ... 其他委托方法
}
```

---

## 收益

| 维度 | 改善 |
|------|------|
| **Locality** | 密钥管理变更只影响 `KeyManager`，加解密变更只影响 `SessionCipher` |
| **可测试性** | `KeyManager` 和 `SessionCipher` 可通过 `IMessageStore` 独立测试 |
| **AI 可导航性** | 新开发者能快速定位到正确模块，而非在 300+ 行中搜索 |
| **删除测试** | 删除任一内部类会导致复杂度集中在门面中，证明它们值得存在 |
| **接口不变** | `ISignalProtocolService` 不变，`FfiSignalProtocolService` 不受影响 |

---

## 不在此方案中的内容（未来可考虑）

- 会话序列化版本控制（当前 JSON 格式无版本号）
- 密码学原语抽象（CipherSuite 策略接口）
- 完整的 Double Ratchet 实现（当前为简化版）
- 后量子安全（Kyber 占位）
