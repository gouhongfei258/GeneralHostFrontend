# General Host Frontend

General Host Frontend 是一个基于 .NET 8 和 Avalonia 的通用工业上位机/HMI 客户端框架。项目当前提供运行时启停、实时 Tag 看板、实时日志、设备配置、Tag 配置、SQLite 数据库查看器、可视化逻辑编辑器、HMI 画面编辑器和 HMI 运行窗口。

默认配置使用 Simulator 驱动，首次启动不需要真实 PLC，也可以看到基础数据采集和画面刷新流程。

## 功能状态

### 已实现

- Avalonia 桌面主界面。
- `HostRuntime` 运行时启停。
- Simulator、HSL PLC、HTTP、TCP Client、TCP Server 通信驱动。
- 设备配置窗口。
- Tag 配置窗口。
- 有界异步 Tag 数据管线。
- 实时 Tag 看板。
- Serilog 文件日志和 UI 实时日志。
- SQLite 数据库查看器。
- NodifyM.Avalonia 可视化逻辑编辑器。
- C# 逻辑代码生成和 Natasha 动态编译。
- HMI 页面 JSON 存储。
- HMI 控件目录。
- HMI 资源目录和模板目录。
- HMI 编辑器。
- HMI 运行窗口。
- HMI 控件事件和基础动作。
- HMI 写 Tag 原型链路。

### 原型中

- HMI 正式工程管理。
- HMI 运行态权限控制。
- HMI 写 Tag 到真实硬件的统一命令链路。
- 报警服务。
- 趋势服务。
- 配方服务。
- 用户登录、角色和权限配置。
- 操作审计。

## 技术栈

- .NET 8
- Avalonia 12
- CommunityToolkit.Mvvm
- Microsoft.Extensions.DependencyInjection
- Microsoft.Data.Sqlite
- Serilog
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

应用入口是 `Program.Main`。启动后 `App` 会加载配置，并通过 `CompositionRoot.Build(...)` 组装依赖。

首次启动时，如果运行目录下没有配置文件，应用会自动创建默认配置：

```text
Config/
  hostsettings.json
  logicgraph.json
  hmi-pages/
  hmi-resources/
Data/
  host.db
Logs/
  host-.log
startup-trace.log
startup-error.log
```

默认 `hostsettings.json` 会创建一个模拟设备 `SIM-PLC-01`，并包含示例 Tag，例如：

- `Line.Speed`
- `Line.Temperature`
- `Line.Pressure`
- `Station.Ready`

## 主界面

主窗口标题为 `General Host Framework`，主要入口包括：

- `Start` / `Stop`：启动或停止 `HostRuntime`。
- `Live Tags`：显示当前 Tag 值、质量、单位和时间戳。
- `Live Log Viewer`：显示实时日志，支持日志级别和关键字过滤。
- `Open Devices`：打开设备配置窗口。
- `Open Tags`：打开 Tag 配置窗口。
- `Open Database`：打开 SQLite 数据库查看器。
- `Open Logic`：打开可视化逻辑编辑器。
- `Open HMI`：打开 HMI 画面编辑器。
- `Run HMI`：打开 HMI 运行窗口。

工作区下拉项包括：

- `Operator`
- `Maintenance`
- `Engineering`
- `Administration`

当前工作区主要用于模拟权限状态，例如是否允许强制 I/O 或编辑设置。真实登录和 RBAC 仍在规划中。

## 配置说明

核心配置模型是 `Application/HostSettings.cs`：

- `Communication`：连接超时、心跳周期、重连延迟、并发和限流。
- `Pipeline`：Tag 管线容量、批量大小、批量延迟和 UI 发布间隔。
- `Devices`：设备端点列表，包括设备 ID、驱动类型、地址、端口和协议参数。
- `Tags`：Tag 列表，包括名称、设备、地址、数据类型、读写模式、扫描周期、单位、上下限和缩放规则。

示例片段：

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

保存配置时会经过 `HostSettingsValidator` 校验。运行中修改配置后，主界面会接收热更新；如果运行时正在扫描，`HostRuntime` 会重启扫描任务以应用新设置。

## 通信驱动

通信接口定义在 `Core/Communication/CommunicationContracts.cs`。核心接口包括：

- `ICommunicationDriver`
- `ICommunicationDriverFactory`
- `ICommunicationConnectionPool`

当前驱动包括：

- `Simulator`
- HSL 系列 PLC 驱动
- `Http`
- `TcpClient`
- `TcpServer`

HSL 适配器覆盖常见 PLC 协议，包括 Modbus、Siemens、Omron、Melsec、Keyence、Panasonic、Allen-Bradley、Beckhoff、Delta、Inovance、Yaskawa 等。

## HMI 设计器

HMI 设计器使用 Avalonia 原生实现，不生成 AXAML 或 C# 画面代码。用户编辑的是 JSON 页面文档，运行时根据文档动态渲染。

当前 HMI 能力包括：

- 页面加载和保存。
- 控件工具箱。
- 画布拖拽。
- 控件选择。
- 多选。
- 复制和粘贴。
- 删除。
- 层级调整。
- 基础对齐。
- 网格显示和吸附。
- 属性面板。
- Tag 绑定。
- 资源列表。
- 控件模板保存和插入。
- 编辑/运行预览切换。
- 独立 HMI 运行窗口。

当前控件包括：

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

当前动作包括：

- `WriteTag`
- `NavigatePage`
- `SetVisible`
- `SetEnabled`
- `SetProperty`
- `Delay`

需要注意：`TrendChart` 和 `AlarmList` 已进入控件目录，但真实趋势服务和报警服务仍待补齐。

## 逻辑编辑器

逻辑编辑器相关契约定义在 `Core/Logic/LogicContracts.cs`，实现位于 `Infrastructure/Logic`、`ViewModels/Logic` 和 `Views/Logic`。

当前链路：

1. `JsonLogicGraphStore` 保存和加载 `Config/logicgraph.json`。
2. `CSharpLogicCodeGenerator` 将逻辑图转换为 C# 代码。
3. `NatashaLogicCompiler` 编译生成代码。
4. `HostRuntime` 加载编译结果，并周期执行生成逻辑。
5. `IHostLogicContext` 向生成逻辑提供读 Tag、写 Tag、读 PLC 结构、Tag 变更检测和日志能力。

当前节点类型包括：

- `Timer`
- `OnTagChanged`
- `ReadTag`
- `ReadTagCached`
- `ReadTagDirect`
- `ReadPlcStruct`
- `Compare`
- `Switch`
- `WriteTag`
- `PulseBit`
- `Delay`
- `Expression`
- `Log`

## 数据库和日志

数据库查看器默认使用 `Data/host.db`。`SqliteDataViewerQueryService` 负责：

- 自动创建基础表。
- 枚举 SQLite 表和列。
- 分页查询。
- 关键字过滤。
- CSV/表格导出相关模型。
- 数据库健康状态展示。

当前预留表包括：

- `AlarmHistory`
- `Recipes`
- `SystemConfig`

日志系统使用 Serilog：

- UI 实时日志：`LiveLogSerilogSink` 写入 `InMemoryLiveLogService`。
- 文件日志：`Logs/host-.log`，按天滚动，单文件默认 20 MB，保留 31 个文件。

## 项目结构

```text
Application/                     应用编排、HostRuntime、HostSettings、HMI 应用服务
Core/                            领域模型和端口接口
  Communication/                 通信驱动、连接池、端点和参数契约
  Database/                      数据库查看器契约
  Hmi/                           HMI 页面、控件、绑定、动作、资源和模板契约
  Logging/                       实时日志契约
  Logic/                         逻辑图、代码生成、编译和运行上下文契约
  Network/                       Pub/Sub、请求响应和载荷序列化契约
  Pipelines/                     Tag 数据管线契约
  Security/                      RBAC 和工作区权限契约
  Settings/                      配置存储和验证契约
  Tags/                          TagDefinition、TagValue、质量、类型和缩放规则
Infrastructure/                  具体适配器和外部技术实现
  Communication/                 Simulator、HSL、HTTP、TCP 驱动和连接池
  Database/                      SQLite 数据查看器实现
  DependencyInjection/           组合根
  Hmi/                           JSON 页面存储、资源存储、模板存储
  Logging/                       内存实时日志和 Serilog Sink
  Logic/                         JSON 逻辑图、C# 生成器、Natasha 编译器
  Pipelines/                     Channel 驱动的 Tag 数据管线
  Settings/                      JSON 配置存储
ViewModels/                      Avalonia MVVM 状态和命令
Views/                           Avalonia XAML 窗口和行为
Assets/                          应用图标和资源
```

## 和 HmiFuncDesigner 的对标结论

对标 `VelsonWang/HmiFuncDesigner` 后，本项目的底层架构、通信扩展、异步数据管线和逻辑编译链路更现代；但传统组态产品闭环仍需完善。

下一阶段优先级：

1. HMI 工程管理：`hmi-project.json`、启动页、页面树、资源索引、模板索引、导入导出。
2. 实时数据库快照：最新值、质量、时间戳、写入状态、报警状态和变量引用校验。
3. HMI 编辑器产品化：撤销/重做、快捷键、框选、参考线、控件锁定、分组、脏状态和保存冲突提示。
4. 正式 HMI 运行器：操作员模式、全屏、页面导航、写入确认、权限校验和操作审计。
5. 报警、趋势和配方：独立服务、历史记录、HMI 控件接入真实数据。
6. 用户权限：登录、角色、权限、页面/控件/Tag 写入权限。
7. 测试基线：HMI 文档 round-trip、配置验证、逻辑代码生成、ViewModel 和通信驱动测试。

## 开发说明

- 依赖注册集中在 `Infrastructure/DependencyInjection/CompositionRoot.cs`。
- 新增领域模型或端口优先放在 `Core/`。
- 新增外部系统适配器放在 `Infrastructure/`。
- UI 状态和命令放在 `ViewModels/`。
- XAML 视图和交互行为放在 `Views/`。
- HMI 页面文档默认保存在 `Config/hmi-pages/*.json`。
- HMI 资源默认保存在 `Config/hmi-resources/`。
- HMI 模板默认保存在 `Config/hmi-resources/templates/`。

更详细的架构说明见 `ARCHITECTURE.md`。

## Issues
### TODO
1. 支持其他常见通信如can，openprotocal，同时优化设备编辑界面
2. 节点编辑器和HMI设计器支持lua脚本，js脚本
3. 优化节点编辑器如支持更多节点类型，优化节点编辑窗体界面
4. 优化HMI设计器，支持显示图表，实时曲线，优化数据流设计，优化设计体验
5. 工程化HMI设计器和节点编辑器文件，引入模板功能
6. 改HMI设计器为axaml模版生成，暂时不考虑图形托拉拽编辑
### PLANNING
1. 参照市面上开源物联网设计平台进行功能增加和优化

## 仙人指路[不用谢]
### 参考开源[github上都能找到]
1.[qt]hmifuncdesign
2.[winform]stnode
3.[avalonia]nodifyM,nodify-avalonia
4.[wpf]nodify
4.[PlcCommunications]hslcommunications
5.[c#]natasha
### 思路提供
1.b站搜索关键字：节点编辑器，通用上位机，可视化编程
## 有意向参与贡献此项目的可以联系我
1.if you want to take part in this project, please contact me.