# General Host Frontend

General Host Frontend 是一个基于 .NET 8 和 Avalonia 的通用工业上位机客户端壳。项目当前采用单个 `csproj` 承载，但代码已经按 Core、Application、Infrastructure、ViewModels、Views 分层组织，方便后续拆分为多个程序集。

当前应用提供运行时启停、实时 Tag 看板、实时日志、设备配置、Tag 配置、SQLite 数据查看器和可视化逻辑编辑器等能力。默认配置使用 Simulator 驱动，因此首次启动后不需要真实 PLC 也能看到基础流程。

## 功能概览

- 通过 Avalonia 构建桌面端主界面和工具窗口。
- 使用 `HostRuntime` 启动设备扫描、心跳检测、Tag 缓存和逻辑执行循环。
- 通过 `ITagDataPipeline` 将高频 Tag 数据批量发布到 UI。
- 支持 Simulator、HSL PLC 驱动以及 HTTP/TCP 网络通信驱动。
- 使用 JSON 文件保存设备、Tag、通信和数据管道配置。
- 使用 NodifyM.Avalonia 编辑节点式逻辑图，并生成/编译 C# 运行逻辑。
- 使用 Serilog 同时写入实时 UI 日志和滚动文件日志。
- 使用 SQLite 提供内置数据查看器。

## 技术栈

- .NET 8
- Avalonia 12
- CommunityToolkit.Mvvm
- Microsoft.Extensions.DependencyInjection
- Serilog
- Microsoft.Data.Sqlite
- HslCommunication
- NodifyM.Avalonia
- DotNetCore.Natasha.CSharp.Compiler

## 快速开始

环境要求：

- .NET SDK 8.0 或更高版本
- Windows、Linux 或 macOS 桌面环境

还原、构建和运行：

```powershell
dotnet restore .\GeneralHostFrontend.sln
dotnet build .\GeneralHostFrontend.sln
dotnet run --project .\GeneralHostFrontend.csproj
```

发布 Release：

```powershell
dotnet publish .\GeneralHostFrontend.csproj -c Release
```

## 首次启动

应用入口是 `Program.Main`，随后由 `App` 加载配置并调用 `CompositionRoot.Build(...)` 组装依赖。首次启动时，如果运行目录下没有配置文件，应用会自动创建默认配置：

- `Config/hostsettings.json`：设备、Tag、通信参数和管道参数。
- `Config/logicgraph.json`：可视化逻辑编辑器保存的逻辑图。
- `Data/host.db`：SQLite 数据库查看器使用的数据库。
- `Logs/host-.log`：Serilog 滚动日志文件。
- `startup-trace.log` / `startup-error.log`：启动过程诊断文件。

默认 `HostSettings` 会创建一个模拟设备 `SIM-PLC-01`，并包含 `Line.Speed`、`Line.Temperature`、`Line.Pressure`、`Station.Ready` 四个示例 Tag。

## 主界面

主窗口标题为 `General Host Framework`，主要区域包括：

- `Start` / `Stop`：启动或停止 `HostRuntime`。
- `Live Tags`：显示当前配置中的 Tag 值、质量、单位和时间戳。
- `Live Log Viewer`：显示实时日志，支持最低日志级别和关键字过滤。
- `Open Logic`：打开可视化逻辑编辑器。
- `Open Devices`：打开设备配置窗口。
- `Open Tags`：打开 Tag 配置窗口。
- `Open Database`：打开内置数据库查看器。

工作区下拉项包括 `Operator`、`Maintenance`、`Engineering` 和 `Administration`。当前实现中它们用于展示模拟权限状态，例如是否允许强制 I/O 或编辑配置。

## 目录结构

```text
Application/                     应用层编排，包含 HostRuntime 和 HostSettings
Core/                            领域模型和端口接口
  Communication/                 通信驱动、连接池、端点和通信参数契约
  Database/                      数据库查看器契约
  Logging/                       实时日志契约
  Logic/                         逻辑图、代码生成、编译和运行上下文契约
  Network/                       Pub/Sub、请求响应和载荷序列化契约
  Pipelines/                     Tag 数据管道契约
  Security/                      RBAC 和工作区权限契约
  Settings/                      配置存储和验证契约
  Tags/                          TagDefinition、TagValue、质量、类型和缩放规则
Infrastructure/                  具体适配器和外部技术实现
  Communication/                 Simulator、HSL、HTTP、TCP 驱动和连接池
  Database/                      SQLite 数据查看器实现
  DependencyInjection/           CompositionRoot 依赖注入组合根
  Logging/                       内存实时日志服务和 Serilog Sink
  Logic/                         JSON 逻辑图存储、C# 生成器、Natasha 编译器
  Pipelines/                     Channel 驱动的 Tag 数据管道
  Settings/                      JSON 配置存储
ViewModels/                      Avalonia MVVM 状态和命令
Views/                           Avalonia XAML 视图和窗口
Assets/                          应用图标和资源
```

## 运行时流程

1. `App` 从运行目录加载 `Config/hostsettings.json`。
2. `CompositionRoot` 注册配置、通信驱动、连接池、Tag 管道、日志、数据库、逻辑编译和 ViewModel。
3. 主窗口解析 `MainWindowViewModel` 并订阅 Tag、日志和配置变化。
4. 点击 `Start` 后，`HostRuntime` 按设备分组启动扫描任务和心跳任务。
5. 通信驱动读取 Tag 后发布到 `ITagDataPipeline`。
6. UI 订阅管道批次，在 UI 线程更新实时 Tag 看板。
7. `HostRuntime` 加载逻辑图，生成 C# 逻辑代码，编译成功后循环执行生成逻辑。
8. 点击 `Stop` 或应用退出时，运行时取消任务、卸载编译逻辑并释放服务容器。

## 配置说明

核心配置模型是 `Application/HostSettings.cs`：

- `Communication`：连接超时、心跳周期、重连延迟、并发数和限流。
- `Pipeline`：Tag 管道容量、批量大小、批量延迟和 UI 发布间隔。
- `Devices`：设备端点列表，每个端点包含设备 ID、驱动类型、地址、端口和协议参数。
- `Tags`：Tag 列表，每个 Tag 包含名称、设备、地址、数据类型、访问模式、扫描周期、工程单位、上下限和线性缩放规则。

示例配置片段：

```json
{
  "devices": [
    {
      "deviceId": "SIM-PLC-01",
      "kind": "Simulator",
      "address": "sim://line-1",
      "port": 0
    }
  ],
  "tags": [
    {
      "name": "Line.Speed",
      "deviceId": "SIM-PLC-01",
      "address": "D100",
      "dataType": "Float64",
      "access": "ReadWrite",
      "scanPeriod": "00:00:00.250",
      "engineeringUnit": "pcs/min",
      "lowerLimit": 0,
      "upperLimit": 120
    }
  ]
}
```

保存配置时会通过 `HostSettingsValidator` 校验。运行中修改配置后，主界面会接收热更新；如果运行时正在扫描，`HostRuntime` 会重启扫描任务以应用新设置。

## 通信驱动

通信端口定义在 `Core/Communication/CommunicationContracts.cs`。核心接口包括：

- `ICommunicationDriver`：连接、断开、读取、批量读取、写入、心跳和状态订阅。
- `ICommunicationDriverFactory`：根据 `CommunicationEndpoint` 创建驱动。
- `ICommunicationConnectionPool`：复用设备连接并聚合驱动状态。

当前驱动类型枚举包含 HSL 常见 PLC 协议、HTTP、TCP Client、TCP Server、SerialPort、USB 和 Simulator。是否已经完整实现取决于 `Infrastructure/Communication` 中对应驱动代码。

## 逻辑编辑器

逻辑编辑器相关契约定义在 `Core/Logic/LogicContracts.cs`，实现位于 `Infrastructure/Logic`、`ViewModels/Logic` 和 `Views/Logic`。

当前逻辑链路：

- `JsonLogicGraphStore` 保存和加载 `Config/logicgraph.json`。
- `CSharpLogicCodeGenerator` 将逻辑图转换为 C# 代码。
- `NatashaLogicCompiler` 编译生成代码并加载 `IGeneratedHostLogic` 实例。
- `HostRuntime` 创建 `IHostLogicContext`，为生成逻辑提供读 Tag、写 Tag、读 PLC 结构、Tag 变更检测和日志能力。

编译器包含基础安全检查，会限制部分命名空间、反射、进程、文件、网络和非白名单 Task 调用。

## 数据库和日志

数据库查看器默认使用 `Data/host.db`。`SqliteDataViewerQueryService` 会负责读取 SQLite 元数据、分页查询和健康状态。

日志系统通过 Serilog 配置：

- UI 实时日志：`LiveLogSerilogSink` 写入 `InMemoryLiveLogService`。
- 文件日志：`Logs/host-.log`，按天滚动，单文件默认 20 MB，保留 31 个文件。

## 开发说明

- 依赖注册集中在 `Infrastructure/DependencyInjection/CompositionRoot.cs`。
- 新增领域模型或端口时优先放在 `Core/`。
- 新增外部系统适配器时放在 `Infrastructure/`，并通过接口暴露给应用层。
- UI 状态和命令放在 `ViewModels/`，XAML 视图放在 `Views/`。
- 配置文件使用 `JsonSettingsStore<TSettings>` 保存，枚举会以字符串形式写入 JSON。

更详细的架构背景可参考 `ARCHITECTURE.md`。
