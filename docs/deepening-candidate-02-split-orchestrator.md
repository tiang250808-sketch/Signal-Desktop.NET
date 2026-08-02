# 候选 2 深化方案：拆分 SignalClientOrchestrator

## 现状

`SignalClientOrchestrator`（51KB）是一个上帝对象，在单一类中混合了 5+ 个不相关职责：设备绑定、手机注册、消息 WebSocket 生命周期、PreKey 管理、事件发射、简单 API 路由。

## 深化方案

### 模块划分

将 Orchestrator 拆分为 4 个内部模块，Orchestrator 本身降级为薄门面：

```
SignalClientOrchestrator（门面）
├── ProvisioningHandler    — 设备绑定流程
├── RegistrationHandler    — 手机注册状态机
├── MessageSocketHandler   — WS 生命周期 + 信封处理
└── PreKeyManager          — PreKey 水位线 + 注册
```

### 设计决策

| 维度 | 决策 |
|------|------|
| **接口方式** | 内部类（`internal sealed class`），不暴露 public 接口。`ISignalSidecarClient` 仍是测试边界 |
| **共享状态** | `ClientState` 对象统一管理 `Account`、`Protocol`、`Events` Channel、`_gate` 锁 |
| **Store 依赖** | 暂不拆分 `IMessageStore`，各模块暂时接收完整接口。未来再细化 |
| **门面保留** | 简单 API 方法（ListConversations、SendTextMessage 等 10 个方法）+ SubscribeEvents 保留在门面 |
| **PreKey 触发** | `PreKeyManager` 独立，被 `ConnectAsync`（启动时）和 `MessageSocketHandler`（运行时水位线）两处调用 |

### ClientState 共享对象

```csharp
internal sealed class ClientState
{
    private readonly object _gate = new();
    public Channel<SidecarEvent> Events { get; } = Channel.CreateUnbounded<SidecarEvent>();
    public AccountCredentials? Account { get; private set; }
    public ISignalProtocolService? Protocol { get; private set; }

    public void SetAccount(AccountCredentials account) { lock (_gate) Account = account; }
    public void SetProtocol(ISignalProtocolService protocol) { lock (_gate) Protocol = protocol; }
    public (AccountCredentials?, ISignalProtocolService?) Snapshot() { lock (_gate) return (Account, Protocol); }
    public void Emit(SidecarEvent ev) => Events.Writer.TryWrite(ev);
}
```

### 模块依赖关系

```
SignalClientOrchestrator (facade)
├── _state (ClientState) — 共享状态
├── ProvisioningHandler(options, cipher, state, rest)
│   └── 完成后调用 _state.SetAccount() / _state.SetProtocol()
├── RegistrationHandler(options, state, rest)
│   └── 完成后调用 _state.SetAccount() / _state.SetProtocol()
├── MessageSocketHandler(options, protocol, store, state)
│   └── 运行时调用 preKeyManager.EnsureWaterline()
├── PreKeyManager(protocol, store, rest)
└── 直接实现：ListConversations, SendTextMessage, GetMessages, ListContacts,
    UpsertContact, GetSettings, UpdateSettings, SendReadReceipt, StageAttachment,
    HealthAsync, GetAccountStatusAsync, SubscribeEventsAsync
```

### 收益

| 维度 | 改善 |
|------|------|
| **Locality** | 修改注册流程只影响 RegistrationHandler，不影响消息处理 |
| **可测试性** | 每个 Handler 可通过构造函数注入 mock 独立测试 |
| **AI 可导航性** | 新开发者通过文件名即可定位代码，无需在 51KB 中搜索 |
| **删除测试** | 删除任一 Handler 会导致复杂度集中在门面中，证明它们值得存在 |
| **接口不变** | `ISignalSidecarClient` 不变，`MainViewModel` 不受影响 |

### 不在此方案中的内容（未来可考虑）

- `IMessageStore` 拆分为窄接口（候选 3）
- `INetworkManager` 统一连接抽象（候选 4）
- 自动重连策略（指数退避）
- 会话序列化版本控制
