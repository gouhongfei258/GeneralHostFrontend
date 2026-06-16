# Avalonia 工业上位机客户端框架架构

## 目标定位

本项目以 .NET 8 + Avalonia 为基础，面向通用工业上位机客户端场景：

- PLC、串口、USB、网络协议接入
- 统一 Tag 数据采集与质量戳管理
- 权限驱动的动态工作区
- 高频日志与实时数据看板
- 嵌入式数据库查看器
- 可视化节点逻辑编辑器
- 可拖拽画布式 HMI 画面编辑器
- 统一配置中心

当前实现已进入第三阶段：核心边界、Simulator/HSL 驱动、异步数据管道、Avalonia 工作台、正式 DI 组合根、Serilog 文件日志与 UI Sink、JSON 配置加载、SQLite 数据查看器适配器、设备配置窗口与可视化逻辑编辑器均已落地。画布式 HMI 画面编辑器作为下一阶段 UI 组态能力纳入架构设计。

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
- `HttpCommunicationDriver`
- `TcpClientCommunicationDriver`
- `TcpServerCommunicationDriver`
- `CommunicationDriverFactory`
- `CommunicationConnectionPool`

HSL 适配器当前支持：

- `DriverKind.ModbusTcp`
- `DriverKind.ModbusUdp`
- `DriverKind.ModbusRtu`
- `DriverKind.SiemensS7`
- `DriverKind.SiemensFetchWrite`
- `DriverKind.SiemensPpiOverTcp`
- `DriverKind.OmronFins`
- `DriverKind.OmronFinsUdp`
- `DriverKind.OmronHostLinkOverTcp`
- `DriverKind.OmronHostLinkCModeOverTcp`
- `DriverKind.OmronCip`
- `DriverKind.OmronConnectedCip`
- `DriverKind.MelsecMc` / `MelsecMcUdp` / `MelsecMcAscii` / `MelsecMcAsciiUdp` / `MelsecMcR` / `MelsecA1E` / `MelsecA1EAscii` / `MelsecA3COverTcp` / `MelsecFxLinksOverTcp` / `MelsecFxSerialOverTcp` / `MelsecCip`
- `DriverKind.KeyenceMc` / `KeyenceMcAscii` / `KeyenceNanoOverTcp`
- `DriverKind.PanasonicMc` / `PanasonicMewtocolOverTcp`
- `DriverKind.AllenBradleyCip` / `AllenBradleyConnectedCip` / `AllenBradleyPccc` / `AllenBradleySlc`
- `DriverKind.BeckhoffAds`
- `DriverKind.DeltaTcp` / `DeltaSerialOverTcp` / `DeltaSerialAsciiOverTcp`
- `DriverKind.FatekProgramOverTcp`
- `DriverKind.InovanceTcp` / `InovanceSerialOverTcp` / `InovanceEasy` / `InovanceConnectedCip`
- `DriverKind.FujiSph` / `FujiSpbOverTcp`
- `DriverKind.GeSrtp`
- `DriverKind.LsFastEnet` / `LsCnetOverTcp`
- `DriverKind.XinJeTcp` / `XinJeInternal` / `XinJeSerialOverTcp`
- `DriverKind.YaskawaMemobusTcp` / `YaskawaMemobusUdp`
- `DriverKind.MegMeetTcp` / `MegMeetSerialOverTcp`

通用网络适配器当前支持：

- `DriverKind.Http`
- `DriverKind.TcpClient`
- `DriverKind.TcpServer`

`CommunicationEndpoint.Address` 用于 IP 地址或串口名，`Port` 用于 TCP 端口。协议细节通过 `Parameters` 配置，例如 `station`、`baudRate`、`plcType`、`rack`、`slot`、`dataFormat`。String/Bytes 类型 Tag 可在地址后追加 `;length=16` 指定读取长度，例如 `D100;length=16`。

HSL 设备参数由设备管理窗口的 `Defaults` 写入 `Parameters`，并在 `HslCommunicationDriverFactory` 创建对应 HSL client 时应用；典型参数包括 Siemens 的 `plcType`、`rack`、`slot`、Omron FINS/HostLink 的 `da1`、`sa1`、`unitNumber`、Melsec MC 的 `networkNumber`、`plcNumber`、`targetIOStation`、Allen-Bradley/Omron CIP 的连接参数、Beckhoff ADS 的 `amsPort`、LS FastEnet 的 `companyId`、`baseNo`、`slotNo`、Yaskawa Memobus 的 `cpuFrom`、`cpuTo` 等。

HTTP 适配器的 `Address` 为 Base URL，Tag 地址为请求路径，可追加 `;jsonPath=data.value` 提取 JSON 字段；常用参数包括 `readMethod`、`writeMethod`、`heartbeatPath`、`contentType`、`header.*`、`writeBodyTemplate`。TCP Client 适配器连接远端 `Address:Port`，默认发送 `READ {address}` / `WRITE {address} {value}`，可用 `readTemplate`、`writeTemplate`、`terminator`、`responseTerminator` 覆盖。TCP Server 适配器默认监听本地 `Address:Port`，接收客户端上报的 `address=value` 或 `{"address":"...","value":...}` 并缓存为 Tag 值，写入时向客户端广播 `WRITE {address} {value}`；接收结束符使用 `terminator`，广播写命令结束符使用 `writeTerminator`，未配置时回退到 `terminator`。

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

### 10. 画布式 HMI 编辑器

画布式 HMI 编辑器用于在应用内部提供类似 WinForms Designer / 组态软件的画面编辑能力，但不生成 C# 或 AXAML 代码。用户编辑的是 HMI 画面文档，运行时根据文档动态渲染控件并绑定实时 Tag 数据。

Core 层建议定义画面文档、控件定义、绑定和存储端口：

- `HmiPageDocument`
- `HmiWidgetDefinition`
- `HmiWidgetBindingDefinition`
- `HmiWidgetStyleDefinition`
- `HmiPageViewport`
- `IHmiPageStore`

第一版画面文档建议保存为 JSON，默认路径为 `Config/hmi-pages/*.json`。文档只描述画面结构和绑定关系，不直接保存 Avalonia 控件实例：

- 页面名称、设计尺寸、背景、网格设置
- 控件类型、位置、尺寸、旋转角度、层级 `ZIndex`
- 控件样式，例如颜色、字体、边框、透明度、状态色
- Tag 绑定，例如主值、可见性、启用状态、颜色、报警状态
- 格式化规则，例如数值格式、单位、上下限、枚举文本
- 权限标记，例如编辑权限、操作权限、可见权限

编辑器 UI 建议分为编辑模式和运行模式：

- 编辑模式使用 Avalonia `Canvas` 或自定义 `DesignerCanvas` 作为主画布，提供拖拽添加、选中、移动、缩放、对齐、复制、删除、层级调整和网格吸附。
- 左侧工具箱提供 HMI 控件类型，例如数值显示、文本、按钮、状态灯、图片、SVG 图元、报警牌、趋势图占位控件。
- 右侧属性面板编辑选中控件的几何、样式、Tag 绑定、格式化、权限和交互行为。
- 运行模式读取同一份 `HmiPageDocument`，订阅 `ITagDataPipeline` 或 Tag 快照服务，将实时 `TagValue` 投影到控件 ViewModel。
- 编辑器保存的是画面配置，修改画面后不需要重新编译应用。

HMI 控件建议使用小而稳定的控件模型，不直接暴露 Avalonia 控件树给存储层：

- `ValueText`：显示实时数值、单位、质量戳和格式化文本
- `StateIndicator`：根据布尔值、枚举或阈值显示状态色
- `CommandButton`：绑定写 Tag、脉冲写入或触发逻辑命令
- `Image` / `SvgImage`：显示设备图元，可按状态切换资源或颜色
- `TrendChart`：显示指定 Tag 的实时或历史趋势，第一版可先作为占位控件
- `Container`：用于分组、背景面板和局部坐标系

与现有模块的关系：

- HMI 运行态只消费 Tag 管道和配置，不直接访问硬件驱动。
- HMI 控件的写入行为应通过 Application/Core 暴露的命令端口完成，避免 UI 直接调用通信驱动。
- HMI 权限应复用 RBAC 模型，将页面、控件、写入操作分别映射到权限。
- HMI 页面存储可复用 `JsonSettingsStore` 的实现思路，但建议独立为 `IHmiPageStore`，便于后续切换数据库或远程配置中心。
- 可视化逻辑编辑器负责后端逻辑编排，HMI 编辑器负责前端画面组态，两者通过 Tag、命令和权限边界协作。

推荐代码位置：

- `Core/Hmi/HmiContracts.cs`
- `Infrastructure/Hmi/JsonHmiPageStore.cs`
- `ViewModels/Hmi/`
- `Views/Hmi/`

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

画布式 HMI 编辑器尚未接入当前可运行流程。接入后推荐流程为：点击 `Open HMI` 打开画面编辑器，编辑并保存 `HmiPageDocument`，运行模式读取画面文档并订阅实时 Tag 数据刷新控件状态。

## 下一阶段建议

1. 打磨 HMI 编辑器到可交付状态：补齐撤销/重做、键盘快捷键、多选对齐、画布缩放、页面复制/重命名、控件锁定、页面保存冲突提示和文档脏状态提示。
2. 完善 HMI 运行态闭环：将 `ITagWriteService` 从当前管道回写实现升级为真正经过 Application/Runtime 的写入命令，统一校验 Tag 可写性、类型转换、设备连接状态、写入结果和失败日志。
3. 增加 HMI 文档校验与迁移：为 `HmiPageDocument`、控件、绑定、动作、资源引用提供验证器和版本迁移器，保证旧 JSON 可兼容加载，损坏配置可给出明确诊断。
4. 建立 HMI 自动化测试基线：覆盖 JSON round-trip、默认页面生成、文件名清洗、控件目录默认值、Tag 绑定刷新、动作执行、页面跳转、模板保存/加载和资源解析。
5. 接入权限与审计第一版：实现登录/角色/权限配置，将工作区、HMI 页面、控件可见性、控件操作和 Tag 写入全部纳入 RBAC，并把写入动作记录到操作审计日志。
6. 完善数据与报警能力：扩展 SQLite 表结构和服务边界，落地报警历史、操作审计、配方、系统配置，随后让 HMI 的 `AlarmList`、`TrendChart` 从真实服务读取数据。
7. 强化运行时生命周期：引入 `Microsoft.Extensions.Hosting` 或等价应用宿主管理方式，把 `HostRuntime`、Tag 管道、日志、配置热加载、HMI 运行窗口和退出清理统一纳入生命周期。
8. 推进逻辑编辑器运行集成：将生成的 `IGeneratedHostLogic` 接入 `HostRuntime`，实现编译域卸载、启停控制、执行审计、安全白名单，并让 HMI 动作可触发受控逻辑命令。
9. 扩展真实通信和设备接入：在现有 HSL、HTTP、TCP 基础上补充通用 SerialPort、USB HID/USB Bulk，并为关键驱动增加连接诊断、重连策略和最小集成测试。
