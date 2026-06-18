# General Host Frontend 架构说明

## 1. 项目定位

General Host Frontend 是一个基于 .NET 8 和 Avalonia 的通用工业上位机客户端框架。项目目标不是只做一个固定画面程序，而是逐步形成一个可配置、可扩展、可运行的 HMI/SCADA 宿主：

- 统一接入 PLC、模拟设备、HTTP、TCP 等数据源。
- 通过 Tag 模型抽象设备点位、数据质量、工程单位和读写权限。
- 用异步管线把高频数据推送到主界面、HMI 画面和逻辑运行时。
- 提供设备配置、Tag 配置、数据库查看、实时日志、可视化逻辑编辑和 HMI 画面设计能力。
- 通过 JSON 文档保存配置、逻辑图、HMI 页面、资源和模板，便于后续工程打包、导入导出和版本迁移。

参考项目 `VelsonWang/HmiFuncDesigner` 更偏传统 HMI 组态工具，覆盖项目管理、系统变量、实时数据库、画面编辑、运行解析、控件库、脚本逻辑、权限和实时数据库等产品闭环。本项目的技术底座更现代，但还需要继续补齐 HMI 工程管理、实时数据库、报警趋势、权限审计和正式运行态。

## 2. 当前状态

当前项目已经进入可运行原型阶段：

- 应用可启动/停止 `HostRuntime`。
- 默认配置使用 Simulator 设备，首次启动无需真实 PLC。
- 设备、Tag、通信参数和管线参数保存在 `Config/hostsettings.json`。
- 主界面显示实时 Tag、实时日志和运行状态。
- 支持设备配置窗口、Tag 配置窗口、数据库查看窗口、逻辑编辑窗口、HMI 编辑窗口和 HMI 运行窗口。
- HMI 已有页面文档、控件目录、资源目录、模板目录、编辑预览和运行窗口。
- 逻辑编辑器可以保存节点图，生成 C# 代码，并通过 Natasha 编译加载到运行时。
- SQLite 数据库查看器已可枚举表、分页查询、过滤和导出。

需要注意：部分功能仍是原型或占位能力。例如 HMI 写 Tag 服务当前先写回数据管线，尚未完全经过 `HostRuntime` 的真实硬件写入链路；报警、趋势、配方表已经预留，但还没有独立业务服务；RBAC 目前只有领域契约和主界面工作区模拟，还没有真实登录和权限校验闭环。

## 3. 分层结构

项目暂时保持单 `csproj`，代码按 Clean Architecture / Hexagonal Architecture 分层组织：

```text
Core/                            领域模型和端口接口
Application/                     应用编排、运行时和 HMI 应用服务
Infrastructure/                  外部技术适配器
ViewModels/                      Avalonia MVVM 状态和命令
Views/                           Avalonia XAML 窗口、控件和行为
Assets/                          应用图标和资源
```

后续可以平滑拆分为：

```text
GeneralHost.Core
GeneralHost.Application
GeneralHost.Infrastructure.*
GeneralHost.Avalonia
```

### Core

`Core` 不直接引用 Avalonia、Serilog、SQLite、HSL 或其他具体实现。它定义项目长期稳定的领域语言：

- `Core/Tags`：Tag 定义、Tag 值、数据类型、质量、读写模式、缩放规则。
- `Core/Communication`：通信端点、驱动接口、连接池接口、读写命令、驱动状态。
- `Core/Pipelines`：Tag 数据管线接口。
- `Core/Logic`：逻辑图、节点、连线、代码生成、编译、运行上下文。
- `Core/Hmi`：HMI 页面文档、控件定义、绑定、事件、动作、资源、模板和写 Tag 端口。
- `Core/Database`：数据库查看、健康状态、过滤、排序和导出模型。
- `Core/Logging`：实时日志服务和日志过滤模型。
- `Core/Security`：用户会话、角色、权限、工作区和授权服务契约。
- `Core/Network`：Pub/Sub、Request/Response、Payload Serializer 等后续网络端口。
- `Core/Settings`：配置存储、配置验证和配置变更观察。

### Application

`Application` 负责编排核心运行流程：

- `HostRuntime`：启动/停止扫描任务、心跳任务、Tag 缓存、设置热加载和逻辑运行。
- `HostSettings`：设备、Tag、通信和管线配置。
- `HostSettingsValidator`：配置校验。
- `Application/Hmi/HmiTagWriteService`：HMI 控件写 Tag 入口。当前版本负责校验 Tag 存在性、读写权限和基础类型转换，然后发布到 Tag 管线；后续应升级为经过 `HostRuntime` 的统一写入命令。

### Infrastructure

`Infrastructure` 实现外部技术适配：

- `Communication`：Simulator、HSL PLC、HTTP、TCP Client、TCP Server 驱动和连接池。
- `Pipelines`：基于 `Channel<TagValue>` 的有界异步 Tag 管线。
- `Settings`：JSON 配置存储。
- `Logging`：内存实时日志服务、Serilog UI Sink、文件日志。
- `Database`：SQLite 查询服务、内存查询服务、Excel/CSV 相关导出能力。
- `Logic`：JSON 逻辑图存储、C# 代码生成、Natasha 编译器。
- `Hmi`：JSON HMI 页面存储、文件系统资源存储、JSON 控件模板存储。
- `DependencyInjection`：桌面端组合根，集中注册服务和 ViewModel 工厂。

### ViewModels 和 Views

UI 层使用 Avalonia + CommunityToolkit.Mvvm：

- 主界面：运行状态、实时 Tag、实时日志、工作区模拟和工具窗口入口。
- 设备编辑器：维护设备端点、驱动类型和协议参数。
- Tag 编辑器：维护 Tag 名称、地址、类型、读写权限、扫描周期和工程信息。
- 数据库查看器：动态枚举 SQLite 表、分页查询、过滤、导出和健康状态。
- 逻辑编辑器：节点画布、连接器、属性编辑、保存、生成和编译。
- HMI 编辑器：页面列表、控件工具箱、画布、属性面板、资源、模板和运行预览。
- HMI 运行器：加载 HMI 页面文档，订阅实时 Tag，执行页面跳转和控件动作。

## 4. 运行时数据流

主流程如下：

1. `Program` 启动 Avalonia 应用。
2. `App` 加载或创建 `Config/hostsettings.json`。
3. `CompositionRoot.Build(...)` 注册配置、通信、管线、日志、数据库、逻辑和 HMI 服务。
4. 主界面创建 `MainWindowViewModel`，订阅设置变更、Tag 管线和实时日志。
5. 点击 `Start` 后，`HostRuntime` 按设备分组启动扫描任务和心跳任务。
6. 通信驱动读取 Tag，形成 `TagValue`。
7. `HostRuntime` 更新内存 Tag 缓存，并发布到 `ITagDataPipeline`。
8. 主界面、HMI 编辑器预览、HMI 运行器等订阅管线并批量更新 UI。
9. 逻辑图保存为 JSON 后，由代码生成器生成 C#，Natasha 编译器编译为可卸载逻辑实例。
10. 运行时循环执行生成逻辑，逻辑上下文可读缓存 Tag、直接读 PLC 结构、写 Tag 和记录日志。
11. 停止运行时或关闭应用时，扫描任务、逻辑编译域、订阅任务和日志服务释放资源。

## 5. Tag 和通信模型

`TagDefinition` 是设备数据的统一抽象，包含：

- Tag 名称。
- 设备 ID。
- 设备地址。
- 数据类型。
- 读写模式。
- 扫描周期。
- 工程单位。
- 上下限。
- 线性缩放规则。

`ICommunicationDriver` 统一驱动能力：

- 连接和断开。
- 单点读取和批量读取。
- 写入。
- 心跳。
- 状态订阅。
- 异步释放。

当前通信实现包括：

- `SimulatorCommunicationDriver`
- `HslCommunicationDriver`
- `HttpCommunicationDriver`
- `TcpClientCommunicationDriver`
- `TcpServerCommunicationDriver`
- `CommunicationDriverFactory`
- `CommunicationConnectionPool`

HSL 适配器覆盖多类常见 PLC 协议，例如 Modbus、Siemens、Omron、Melsec、Keyence、Panasonic、Allen-Bradley、Beckhoff、Delta、Inovance、Yaskawa 等。HTTP/TCP 驱动用于接入轻量接口设备、网关或测试服务。

## 6. 异步数据管线

高频采集数据不直接推 UI。`ITagDataPipeline` 使用有界 `Channel<TagValue>`：

- 限制容量，避免 UI 或下游消费慢时无限堆积。
- 满载时丢弃最旧数据。
- 按批次发布。
- 支持多个消费者订阅。

该管线目前是主界面、HMI 编辑器预览、HMI 运行器和 HMI 写入模拟链路的实时数据通道。下一阶段应在其旁边增加独立的实时数据库快照服务，用于保存最新值、质量、时间戳、报警状态和写入状态。

## 7. HMI 架构

HMI 采用“文档驱动渲染”方案，不保存 Avalonia 控件实例，也不生成 AXAML/C# 文件。用户编辑的是 `HmiPageDocument`，运行时根据文档动态渲染控件。

### 当前模型

HMI 核心模型位于 `Core/Hmi/HmiContracts.cs`：

- `HmiPageDocument`：页面 ID、名称、尺寸、背景、网格和控件集合。
- `HmiWidgetDefinition`：控件 ID、类型、标题、坐标、大小、层级、绑定、属性、事件和权限键。
- `HmiGridDefinition`：网格显示、吸附和网格大小。
- `HmiEventDefinition`：控件事件。
- `HmiActionDefinition`：事件动作。
- `IHmiPageStore`：页面列表、加载、保存和删除。
- `IHmiWidgetCatalog`：控件目录。
- `IHmiResourceStore`：图片等资源解析。
- `IHmiTemplateStore`：控件模板保存和加载。
- `ITagWriteService`：HMI 写 Tag 端口。

当前控件目录包含：

- `ValueText`
- `StateIndicator`
- `CommandButton`
- `Text`
- `InputBox`
- `SwitchButton`
- `Image`
- `Rectangle`
- `Ellipse`
- `Line`
- `Container`
- `ProgressBar`
- `TrendChart`
- `AlarmList`

当前动作模型包含：

- `WriteTag`
- `NavigatePage`
- `SetVisible`
- `SetEnabled`
- `SetProperty`
- `Delay`

### 当前编辑器能力

`HmiEditorViewModel` 已提供：

- 加载和保存页面。
- 新建、复制、删除控件。
- 多选。
- 复制和粘贴。
- 层级调整。
- 基础对齐。
- 网格设置。
- 控件属性编辑。
- Tag 选项加载。
- 资源列表加载和应用。
- 控件模板保存和插入。
- 编辑/运行预览切换。

`DesignerCanvasBehavior` 负责画布交互，如拖拽、选择、缩放和 Canvas 布局同步。

### 当前运行器能力

`HmiRuntimeViewModel` 已提供：

- 加载指定页面。
- 加载外部传入页面文档用于预览。
- 订阅 Tag 管线并更新控件状态。
- 执行写 Tag 动作。
- 执行页面跳转动作。
- 维护运行状态消息。

### 下一阶段 HMI 重点

对标传统组态工具后，HMI 应优先补齐：

1. HMI 工程模型：`hmi-project.json`、启动页、页面树、资源索引、模板索引、工程导入导出。
2. 页面管理：新建、复制、重命名、删除、设置启动页和页面引用校验。
3. 编辑器体验：撤销/重做、快捷键、框选、吸附参考线、等距分布、控件锁定、组合、脏状态和保存冲突提示。
4. 正式运行态：操作员全屏模式、导航菜单、写入确认、错误反馈、权限校验和审计。
5. 控件体系：工业图元、仪表、液位罐、阀门、电机、管道、趋势和报警控件接入真实服务。

## 8. 逻辑编辑器架构

逻辑编辑器用于编排运行时控制逻辑，而不是画面逻辑。核心模型在 `Core/Logic`：

- `LogicGraphDocument`
- `LogicNodeDefinition`
- `LogicConnectionDefinition`
- `LogicPlcStructDefinition`
- `ILogicGraphStore`
- `ILogicCodeGenerator`
- `ILogicCompiler`
- `IGeneratedHostLogic`
- `IHostLogicContext`

当前节点类型包括：

- Timer
- OnTagChanged
- ReadTag
- ReadTagCached
- ReadTagDirect
- ReadPlcStruct
- Compare
- Switch
- WriteTag
- PulseBit
- Delay
- Expression
- Log

逻辑运行链路：

1. 逻辑图保存到 `Config/logicgraph.json`。
2. `CSharpLogicCodeGenerator` 生成 C# 代码。
3. `NatashaLogicCompiler` 编译并加载逻辑实例。
4. `HostRuntime` 周期执行生成逻辑。
5. `IHostLogicContext` 提供读缓存 Tag、直接读 PLC、读结构、写 Tag、Tag 变更检测和日志能力。

下一阶段应强化：

- 更严格的图 IR 和端口类型检查。
- 编译诊断定位到节点。
- 执行审计和性能统计。
- 逻辑启停控制。
- HMI 动作触发受控逻辑命令。
- 更细的安全白名单和沙箱策略。

## 9. 数据库、报警、趋势和配方

当前 SQLite 查看器已能创建和读取基础表：

- `AlarmHistory`
- `Recipes`
- `SystemConfig`

这些表目前主要服务数据库查看和后续功能预留。项目还缺少独立业务服务：

- `IAlarmService`：报警规则、触发、确认、恢复、等级和历史。
- `ITrendService`：实时趋势缓存、历史趋势查询和采样策略。
- `IRecipeService`：配方编辑、下载、上传、版本和权限。
- `IAuditLogService`：用户操作、HMI 写入、权限拒绝和系统事件审计。

HMI 的 `TrendChart` 和 `AlarmList` 控件应在这些服务完成后从真实服务读取数据，而不是停留在占位显示。

## 10. 权限和工作区

当前 `Core/Security/RbacContracts.cs` 已定义：

- `WorkspaceKind`
- `Permission`
- `RoleDefinition`
- `UserSession`
- `KnownPermissions`
- `IAuthorizationService`

主界面当前使用 `Operator / Maintenance / Engineering / Administration` 模拟工作区，控制部分按钮可用性。下一阶段需要实现真实 RBAC：

- 登录和退出。
- 用户、角色和权限配置。
- 工作区默认权限。
- 页面访问权限。
- 控件可见和可操作权限。
- Tag 写入权限。
- 逻辑命令权限。
- 权限拒绝日志。
- 写入和关键操作审计。

权限字段已经在 `HmiWidgetDefinition.PermissionKey` 中预留，后续应从 HMI 运行器和写 Tag 端口两侧同时校验，避免只依赖 UI 隐藏。

## 11. 配置和文件布局

首次运行后，应用会在运行目录下创建或使用这些文件：

```text
Config/
  hostsettings.json             设备、Tag、通信和管线配置
  logicgraph.json               可视化逻辑图
  hmi-pages/
    main.json                   HMI 页面文档
  hmi-resources/
    templates/                  HMI 控件模板
Data/
  host.db                       SQLite 数据库
Logs/
  host-.log                     Serilog 滚动日志
startup-trace.log               启动诊断日志
startup-error.log               启动错误日志
```

建议下一阶段增加：

```text
Config/
  hmi-project.json              HMI 工程索引、启动页、页面树、资源和模板清单
  alarms.json                   报警规则
  trends.json                   趋势配置
  recipes.json                  配方定义或配方索引
  users.json                    用户、角色和权限配置
```

## 12. 依赖注入组合根

`Infrastructure/DependencyInjection/CompositionRoot.cs` 是桌面端组合根，当前负责：

- 注册 `HostSettings` 存储和验证器。
- 注册 `ITagDataPipeline`。
- 注册通信驱动工厂和连接池。
- 注册 SQLite 查询和健康监控。
- 注册 HMI 页面存储、控件目录、资源存储、模板存储和写 Tag 服务。
- 注册逻辑图存储、代码生成器和编译器。
- 注册 `HostRuntime`。
- 配置 Serilog 文件日志和 UI 实时日志。
- 注册各窗口 ViewModel 和工厂。

保持组合根集中有助于后续拆分项目、替换基础设施实现和做集成测试。

## 13. 发布和 CI

`.github/workflows/dotnet.yml` 在推送 `v*` 标签时构建 Release：

- `linux-x64`
- `win-x64`
- `osx-x64`

发布方式为自包含单文件，构建产物会上传到 GitHub Release。

后续建议增加：

- 普通分支/PR 的 `dotnet build` 和测试检查。
- HMI 文档 round-trip 测试。
- 设置验证器测试。
- 逻辑代码生成器和编译器测试。
- 通信驱动最小集成测试。
- ViewModel 层单元测试。

## 14. 路线图

### M1：文档和工程模型

- 补齐 `hmi-project.json`。
- 增加启动页、页面树、资源索引和模板索引。
- 增加 HMI 文档校验和版本迁移。
- 增加页面引用和 Tag 引用校验。

### M2：实时数据库快照

- 增加 `ITagSnapshotService` 或同类实时数据库服务。
- 保存最新值、质量、时间戳、写入状态和报警状态。
- HMI 页面加载时先读快照再订阅管线。
- 支持变量浏览、分组、搜索和批量导入导出。

### M3：HMI 编辑器产品化

- 撤销/重做。
- 快捷键。
- 框选。
- 参考线。
- 控件锁定。
- 分组。
- 脏状态。
- 保存冲突提示。
- 更完整的动态属性面板。

### M4：正式 HMI 运行器

- 启动页自动加载。
- 操作员全屏模式。
- 页面导航。
- 写入确认。
- 权限校验。
- 操作审计。
- 运行异常提示。

### M5：报警、趋势、配方和审计

- 报警规则服务。
- 实时和历史趋势服务。
- 配方服务。
- 审计日志服务。
- HMI `AlarmList`、`TrendChart`、配方控件接入真实数据。

### M6：权限和用户管理

- 登录。
- 用户管理。
- 角色管理。
- 权限配置。
- 页面、控件、Tag 和逻辑命令级权限。

### M7：工程化和测试

- 单元测试和集成测试基线。
- CI 常规构建。
- 示例 HMI 工程。
- 示例 PLC/Simulator 配置。
- 版本迁移测试。
