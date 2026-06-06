# Avalonia 工业上位机客户端框架架构

## 目标定位

本项目以 .NET 8 + Avalonia 为基础，面向通用工业上位机客户端场景：

- PLC、串口、USB、网络协议接入
- 统一 Tag 数据采集与质量戳管理
- 权限驱动的动态工作区
- 高频日志与实时数据看板
- 嵌入式数据库查看器
- 可视化节点逻辑编辑器
- 统一配置中心

当前实现已进入第三阶段：核心边界、Simulator/HSL 驱动、异步数据管道、Avalonia 工作台、正式 DI 组合根、Serilog 文件日志与 UI Sink、JSON 配置加载、SQLite 数据查看器适配器、设备配置窗口与可视化逻辑编辑器均已落地。

## 分层原则

项目暂时保持单 csproj，但代码按 Clean Architecture / Hexagonal Architecture 组织：

- `Core/`：领域模型和端口接口，不引用 Avalonia、数据库、PLC、MQTT、HTTP、Serilog 等具体实现。
- `Application/`：应用编排服务，负责扫描调度、心跳、重连、日志记录、管道发布。
- `Infrastructure/`：具体适配器，例如 Simulator 通信驱动、连接池、有界 Channel 数据管道、Serilog Sink、JSON 配置存储、SQLite 数据查看器适配器。
- `Infrastructure/DependencyInjection/CompositionRoot.cs`：桌面端组合根，负责装配所有服务并配置 Serilog。
- `ViewModels/` 与 `Views/`：Avalonia UI 与 MVVM 展示层，只消费 Application/Core 暴露的数据流。

后续可无痛拆分为：

- `GeneralHost.Core`
- `GeneralHost.Application`
- `GeneralHost.Infrastructure.*`
- `GeneralHost.Avalonia`

## 核心模块

### 1. Tag 模型

所有硬件点位统一抽象为 `TagDefinition` 与 `TagValue`：

- 名称、设备、地址、数据类型、读写权限
- 扫描周期
- 质量戳、时间戳
- 工程单位、上下限
- 线性转换规则

代码位置：`Core/Tags/TagModels.cs`

### 2. 硬件通信端口

统一驱动接口为 `ICommunicationDriver`，支持：

- Connect/Disconnect
- 单点读、批量读、写入
- 心跳
- 驱动状态流
- `IAsyncDisposable`

连接池接口为 `ICommunicationConnectionPool`，用于多设备复用、状态聚合和生命周期管理。

代码位置：`Core/Communication/CommunicationContracts.cs`

当前实现：

- `SimulatorCommunicationDriver`
- `HslCommunicationDriver`
- `CommunicationDriverFactory`
- `CommunicationConnectionPool`

HSL 适配器当前支持：

- `DriverKind.ModbusTcp`
- `DriverKind.ModbusRtu`
- `DriverKind.SiemensS7`
- `DriverKind.OmronFins`

`CommunicationEndpoint.Address` 用于 IP 地址或串口名，`Port` 用于 TCP 端口。协议细节通过 `Parameters` 配置，例如 `station`、`baudRate`、`plcType`、`rack`、`slot`、`dataFormat`。String/Bytes 类型 Tag 可在地址后追加 `;length=16` 指定读取长度，例如 `D100;length=16`。

代码位置：`Infrastructure/Communication/`

### 3. 异步数据管道

高频采集数据不得直接推 UI。当前 `ITagDataPipeline` 基于有界 `Channel<TagValue>`：

- 容量限制
- 满载时丢弃最旧数据
- 批量发布
- UI 订阅批处理结果

代码位置：

- `Core/Pipelines/TagPipelineContracts.cs`
- `Infrastructure/Pipelines/TagDataPipeline.cs`

### 4. 网络通信端口

网络侧抽象为：

- `IPubSubChannel`
- `IRequestResponseChannel`
- `IPayloadSerializer`

用于后续适配 MQTT、TCP/UDP、HTTP/HTTPS，并统一 JSON/Protobuf/Bytes 载荷。

代码位置：`Core/Network/NetworkContracts.cs`

### 5. RBAC 与动态 UI

Core 层定义：

- `UserSession`
- `RoleDefinition`
- `WorkspaceKind`
- `KnownPermissions`
- `IAuthorizationService`

当前 UI 已通过工作区模拟权限显隐状态，后续可接入真实登录、角色表和控件级 `Permission` 附加属性。

代码位置：`Core/Security/RbacContracts.cs`

### 6. 嵌入式数据库查看器端口

Core 层定义：

- `IDataViewerQueryService`
- `IDatabaseHealthMonitor`
- 分页、过滤、排序、CSV 导出模型

当前实现：

- `SqliteDataViewerQueryService`
- 首次启动自动创建 `Data/host.db`
- 自动创建 `AlarmHistory`、`Recipes`、`SystemConfig` 空表与索引
- 通过 SQLite 元数据动态枚举表和列
- 独立数据库查看窗口支持多表切换、关键字过滤、分页、CSV 导出
- 数据库健康状态展示

后续 Infrastructure 可继续接入 LiteDB、DuckDB 或企业已有数据库。

代码位置：

- `Core/Database/DatabaseContracts.cs`
- `Infrastructure/Database/SqliteDataViewerQueryService.cs`

### 7. Live Log Viewer

Core 层定义 `ILiveLogService`。当前实现为高容量内存环形队列 + Channel 实时流，UI 侧使用虚拟化 `ListBox`，限制最多 1000 条显示项，避免日志高速刷新卡顿。

Serilog 已接入：

- UI Sink：`LiveLogSerilogSink`
- 文件 Sink：`Logs/host-.log`
- 异步写入
- 按天滚动
- 文件大小限制
- 保留 31 个日志文件

代码位置：

- `Core/Logging/LogContracts.cs`
- `Infrastructure/Logging/InMemoryLiveLogService.cs`
- `Infrastructure/Logging/Serilog/LiveLogSerilogSink.cs`

### 8. 配置中心

Core 层定义：

- `ISettingsStore<TSettings>`
- `ISettingsValidator<TSettings>`
- `SettingsValidationResult`

当前实现：

- `JsonSettingsStore<TSettings>`
- `HostSettings`
- `HostSettingsValidator`
- 首次启动自动生成 `Config/hostsettings.json`
- 启动时加载并校验配置

其中校验器会拦截过低扫描周期、非法限值、非法并发/限流参数，避免运行时配置伤害硬件。

当前 UI 已提供独立 `Tag Manager` 窗口：

- Tag 新增
- Tag 复制
- Tag 删除
- Tag 字段编辑
- 保存到 `Config/hostsettings.json`
- 通过 `HostSettingsValidator` 执行保存校验
- 保存后通过配置热加载刷新主界面 Tag 列表
- 运行中保存 Tag 改动会自动重建 Runtime 扫描任务

代码位置：

- `Core/Settings/SettingsContracts.cs`
- `Infrastructure/Settings/JsonSettingsStore.cs`
- `Application/HostSettings.cs`
- `Application/HostSettingsValidator.cs`

### 9. 可视化逻辑编辑器

Core 层定义节点图、连接、代码生成和编译端口：

- `LogicGraphDocument`
- `LogicNodeDefinition`
- `LogicConnectionDefinition`
- `ILogicGraphStore`
- `ILogicCodeGenerator`
- `ILogicCompiler`
- `IGeneratedHostLogic`
- `IHostLogicContext`

当前实现：

- 使用 `NodifyM.Avalonia` 提供节点画布、节点位置、连接器拖拽连线、连接线与迷你地图
- 图结构保存到 `Config/logicgraph.json`
- 用户通过节点和属性编辑逻辑图
- 后端通过 `CSharpLogicCodeGenerator` 根据图结构生成 C# 逻辑代码
- `NatashaLogicCompiler` 当前执行生成代码的编译诊断，后续可扩展为可卸载编译域和 `IGeneratedHostLogic` 实例加载
- 节点端口区分 `Flow` 与 `Value`，Value 端口已带 `Any/Boolean/Number/String/TagValue/Struct/Object/Error` 类型信息
- PLC 特定内容读取采用“结构定义 + 读取节点”方案：设备连接仍由设备配置窗口维护，地址字段仍由 Tag/结构定义维护，节点只选择 `deviceId`、`schemaName`、`baseAddress` 和读取模式

第一版支持节点：

- Timer
- OnTagChanged
- ReadTag / ReadTagCached / ReadTagDirect
- ReadPlcStruct
- Compare
- Switch
- WriteTag
- PulseBit
- Delay
- Expression
- Log

PLC 结构读取推荐模型：

- `LogicPlcStructDefinition` 定义结构名称与字段列表
- `LogicPlcStructFieldDefinition` 定义字段名、PLC 地址、数据类型与可选长度
- `ReadPlcStruct` 节点输出 `Struct`，生成代码通过 `IHostLogicContext.ReadPlcStructAsync(...)` 访问后端通信上下文
- 第一版生成器仍使用 `currentValue/currentTagValue/currentStruct` 简化数据流，后续可升级为图 IR 与严格端口数据流

代码位置：

- `Core/Logic/LogicContracts.cs`
- `Infrastructure/Logic/JsonLogicGraphStore.cs`
- `Infrastructure/Logic/CSharpLogicCodeGenerator.cs`
- `Infrastructure/Logic/NatashaLogicCompiler.cs`
- `ViewModels/Logic/`
- `Views/Logic/`

## 当前可运行流程

1. `App` 调用 `CompositionRoot.Build()` 创建服务容器。
2. 组合根装配 Simulator/HSL 驱动、连接池、Tag 管道、LiveLog 服务、Serilog、配置存储、数据查看器与 `HostRuntime`。
3. 启动时加载 `Config/hostsettings.json`，不存在时自动生成默认配置。
4. 点击 `Start` 后，`HostRuntime` 按设备分组启动扫描任务与心跳任务。
5. Simulator 或 HSL 真实通信驱动读取 Tag 值并附带质量戳、时间戳。
6. Tag 值进入有界管道并被批量发布。
7. ViewModel 在 UI 线程批量更新 Tag 看板。
8. Serilog 同时写入 UI LiveLog 和 `Logs/host-.log` 滚动文件。
9. 点击 `Open Database` 打开独立数据库查看窗口。
10. 数据查看器通过 `IDataViewerQueryService` 动态枚举 SQLite 表，分页读取当前表数据，可按关键字过滤并导出 CSV。
11. 点击 `Open Tags` 打开独立 Tag 管理窗口，编辑并保存 Tag 配置。
12. 点击 `Open Devices` 打开独立设备管理窗口，编辑 PLC IP、端口、串口与协议参数。
13. 点击 `Open Logic` 打开可视化逻辑编辑器，编辑节点图、保存 JSON、生成并编译检查后端 C# 逻辑代码。

## 下一阶段建议

1. 实现 DataGrid 版通用查看器：动态列、过滤条件、页码输入、Excel 导出。
2. 扩展 SQLite：配方、报警历史、操作审计、系统配置表。
3. 扩展更多真实通信适配器：USB HID、USB Bulk、通用 SerialPort 自由协议。
4. 实现权限系统：登录、角色管理、工作区路由、控件级权限附加属性。
5. 引入 `Microsoft.Extensions.Hosting`，使 Runtime 生命周期与桌面应用生命周期更自然地绑定。
6. 增加自动化测试：Simulator/HSL 驱动、配置验证、管道背压、扫描调度。
7. 将逻辑编辑器生成的 `IGeneratedHostLogic` 接入 `HostRuntime`，并实现编译域卸载、启停控制、执行审计和安全白名单。
