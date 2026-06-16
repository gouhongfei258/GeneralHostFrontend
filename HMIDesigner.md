# Avalonia HMI 设计器开发方案

本文档描述 `GeneralHostFrontend` 中 HMI 设计器的 Avalonia 原生实现方案。方案目标是在当前 .NET、Avalonia、MVVM、DI、Tag 管道和 JSON 配置体系内，重写一个可编辑、可预览、可运行、可扩展的工业 HMI 画面设计器。

参考项目 [VelsonWang/HmiFuncDesigner](https://github.com/VelsonWang/HmiFuncDesigner) 只作为功能参照：它证明一个 HMI 工具通常需要项目管理、变量管理、画面编辑、属性面板、运行解析、控件库、动作函数、权限和实时数据库。当前工程的实现方式应完全 Avalonia 化：文档模型在 Core，状态在 ViewModel，渲染由 DataTemplate/ControlTheme/Behavior 完成，运行态通过 `ITagDataPipeline` 和应用层命令端口与设备系统协作。

## 1. 当前基础

当前工程已经具备 HMI 原型：

- `Core/Hmi/HmiContracts.cs`
  - `HmiPageDocument`：页面名称、尺寸、背景、控件定义。
  - `HmiWidgetDefinition`：控件 ID、类型、位置、尺寸、层级、绑定、属性。
  - `HmiWidgetKind`：已有 `ValueText`、`StateIndicator`、`CommandButton`。
  - `IHmiPageStore`：页面列表、加载、保存接口。
- `Infrastructure/Hmi/JsonHmiPageStore.cs`
  - 保存到 `Config/hmi-pages/*.json`。
  - 页面不存在时生成默认页面。
- `ViewModels/Hmi/HmiEditorViewModel.cs`
  - 支持加载、保存、添加、删除、复制、层级调整、编辑/运行预览切换。
  - 已订阅 `ITagDataPipeline`，可刷新控件实时值。
- `ViewModels/Hmi/HmiWidgetViewModel.cs`
  - 封装控件几何、绑定、属性、运行显示值和状态。
- `Views/Hmi/HmiEditorWindow.axaml`
  - 已有三栏布局：工具箱、画布、属性面板。
- `Views/Hmi/DesignerCanvasBehavior.cs`
  - 已有拖拽、缩放、选中、Canvas 布局同步。
- 主窗口已有 `Open HMI` 入口。

后续开发应沿着这条 Avalonia 原型继续演进，而不是引入外部 UI 框架或生成 AXAML/C# 代码。

## 2. 产品定位

HMI 设计器是一个内置于桌面宿主中的画面组态工具，用于让工程人员直接在应用里完成操作界面配置：

- 放置控件：文本、数值、状态灯、按钮、开关、图片、图元、趋势、报警。
- 绑定 Tag：读取实时值、显示状态、写入控制量。
- 调整属性：位置、尺寸、颜色、字体、格式、权限、动作。
- 预览运行：在同一窗口切换到运行预览，验证实时刷新和按钮动作。
- 保存文档：写入 JSON 页面文档，运行时动态解析，不重新编译应用。

设计器服务两个角色：

- 工程配置人员：需要高效拖拽、对齐、绑定、保存、复用模板。
- 操作人员：只接触运行态页面，不看到编辑框、工具箱和属性面板。

## 3. Avalonia 实现原则

- 不保存 Avalonia 控件实例，只保存可序列化的 HMI 文档。
- 不生成 AXAML 或 C# 代码，运行时由文档驱动渲染。
- UI 采用 MVVM：ViewModel 维护设计状态，View 只负责绑定和交互呈现。
- 控件渲染使用 `DataTemplate`、`ContentControl`、`ItemsControl`、`Canvas`。
- 设计期装饰使用 Behavior/Adorner 思路实现，避免污染运行态控件模板。
- 控件定义使用元数据目录驱动工具箱、默认值、属性面板和运行绑定。
- HMI 只通过 `ITagDataPipeline` 消费实时数据，通过应用层命令端口写入 Tag。
- 编辑器和运行器共享同一份页面文档和控件目录，避免两套逻辑分裂。

## 4. 总体结构

推荐分为六个部分：

```text
Core/Hmi
  HmiContracts.cs          页面、控件、绑定、动作、资源等纯模型
  HmiWidgetCatalog.cs      控件描述、属性描述、绑定槽描述
  HmiValidation.cs         文档校验与兼容性检查

Infrastructure/Hmi
  JsonHmiPageStore.cs      JSON 页面存储
  HmiDocumentMigration.cs  文档版本升级

Application/Hmi
  HmiRuntimeService.cs     运行态页面加载、Tag 快照、动作调度
  HmiTagCommandService.cs  HMI 写 Tag/控制命令端口

ViewModels/Hmi
  HmiEditorViewModel.cs    编辑器主状态
  HmiPageViewModel.cs      当前页面状态
  HmiWidgetViewModel.cs    控件状态
  HmiSelectionViewModel.cs 选择集、多选、对齐
  HmiPropertyViewModel.cs  属性面板数据源
  HmiRuntimeViewModel.cs   运行态页面状态

Views/Hmi
  HmiEditorWindow.axaml    设计器窗口
  HmiRuntimeWindow.axaml   正式运行窗口
  DesignerCanvasBehavior.cs
  HmiWidgetTemplates.axaml
  HmiPropertyEditors.axaml
```

层间关系：

- Core 不引用 Avalonia。
- Infrastructure 只负责文件读写和版本升级。
- Application 连接 Tag 管道、命令端口、权限和日志。
- ViewModels 引用 Core/Application，暴露可绑定状态。
- Views 引用 ViewModels 和 Avalonia，使用模板渲染控件。

## 5. 文档模型

第一版保留当前 `HmiPageDocument`，但建议升级为带版本的页面文档：

```csharp
public sealed record HmiPageDocument(
    int SchemaVersion,
    string Id,
    string Name,
    double Width,
    double Height,
    string Background,
    HmiGridDefinition Grid,
    IReadOnlyList<HmiWidgetDefinition> Widgets);

public sealed record HmiWidgetDefinition(
    string Id,
    string Kind,
    string Title,
    double X,
    double Y,
    double Width,
    double Height,
    double Rotation,
    int ZIndex,
    bool IsLocked,
    bool IsVisible,
    Dictionary<string, HmiBindingDefinition> Bindings,
    Dictionary<string, string> Properties,
    IReadOnlyList<HmiEventDefinition> Events,
    string? PermissionKey);
```

`Kind` 建议从 enum 逐步改成字符串。原因是 Avalonia 侧可以用控件目录动态注册 `Kind -> DataTemplate/属性描述/默认值`，不必每增加控件就修改核心枚举。

绑定模型：

```csharp
public sealed record HmiBindingDefinition(
    string TagName,
    HmiBindingMode Mode,
    string? Format,
    string? FallbackValue);
```

动作模型：

```csharp
public sealed record HmiEventDefinition(
    string EventName,
    IReadOnlyList<HmiActionDefinition> Actions);

public sealed record HmiActionDefinition(
    string Kind,
    Dictionary<string, string> Parameters);
```

第一批动作：

- `WriteTag`：写固定值、反转值、脉冲值。
- `NavigatePage`：跳转页面。
- `SetVisible`：显示/隐藏控件。
- `SetEnabled`：启用/禁用控件。
- `SetProperty`：修改控件属性。
- `Delay`：延时执行后续动作。

## 6. 控件目录

当前 `WidgetKinds = Enum.GetValues<HmiWidgetKind>()` 只能支撑很小的原型。Avalonia 版设计器应引入控件目录：

```csharp
public interface IHmiWidgetCatalog
{
    IReadOnlyList<HmiWidgetDescriptor> List();
    HmiWidgetDescriptor Get(string kind);
}

public sealed record HmiWidgetDescriptor(
    string Kind,
    string DisplayName,
    string Category,
    double DefaultWidth,
    double DefaultHeight,
    IReadOnlyList<HmiPropertyDescriptor> Properties,
    IReadOnlyList<HmiBindingSlotDescriptor> Bindings,
    IReadOnlyList<string> Events);
```

控件目录用途：

- 工具箱按 `Category` 分组显示控件。
- 添加控件时读取默认尺寸、默认属性和默认绑定槽。
- 属性面板按属性描述生成编辑器。
- 运行器按控件描述校验绑定和动作。
- DataTemplate 使用 `Kind` 选择渲染模板。

第一阶段可用静态类注册，后续改成 DI 多实现注册：

```csharp
services.AddSingleton<IHmiWidgetCatalog, DefaultHmiWidgetCatalog>();
```

## 7. Avalonia 控件渲染

控件渲染推荐采用模板选择器：

```csharp
public sealed class HmiWidgetTemplateSelector : IDataTemplate
{
    public bool Match(object? data) => data is HmiWidgetViewModel;
    public Control Build(object? data) => ...
}
```

也可以先用 XAML 中的多个 `ContentControl` + `IsVisible`，当前原型已经如此实现。控件数量增多后应改为模板选择器或按 `Kind` 注册的模板字典。

设计态与运行态要分离：

- 运行态模板只渲染控件本体。
- 设计态外层包装 `DesignerItem`，负责选中边框、拖拽、缩放手柄、锁定状态、悬浮状态。

推荐结构：

```text
Canvas
  ItemsControl
    DesignerItem
      ContentControl -> HmiWidgetTemplateSelector
      ResizeAdorners
```

这样运行窗口可以直接使用：

```text
Canvas
  ItemsControl
    ContentControl -> HmiWidgetTemplateSelector
```

避免运行态里出现设计器边框和交互手柄。

## 8. 编辑器界面

主窗口保持三栏布局，但内部职责调整：

- 顶部工具栏
  - 保存、重载、撤销、重做、运行预览、缩放、网格吸附、对齐、层级。
- 左侧
  - 页面列表。
  - 控件工具箱。
  - 资源列表。
- 中间
  - 可滚动画布。
  - 页面尺寸固定，视图缩放不改变文档尺寸。
  - 网格、参考线、选择框、吸附提示。
- 右侧
  - 页面属性。
  - 选中控件属性。
  - Tag 绑定。
  - 事件动作。

短期可以继续用单窗口，后续如果编辑项增多，可把页面树、工具箱、属性面板抽成可停靠面板，但不建议第一版引入复杂 Dock 框架。

## 9. 画布交互

在现有 `DesignerCanvasBehavior` 上演进：

- 单选：点击控件选中。
- 多选：Ctrl 点击追加/移除。
- 框选：在空白画布拖出矩形。
- 拖拽：选中项整体移动。
- 缩放：八方向手柄，最小尺寸保护。
- 键盘：
  - Delete 删除。
  - Ctrl+C / Ctrl+V 复制粘贴。
  - Ctrl+Z / Ctrl+Y 撤销重做。
  - 方向键微调。
  - Shift+方向键大步移动。
- 网格：
  - 显示/隐藏。
  - 设置网格大小。
  - 移动和缩放时吸附。
- 对齐：
  - 左、右、上、下、水平居中、垂直居中。
  - 等宽、等高。
  - 水平分布、垂直分布。

拖拽时 ViewModel 允许实时更新坐标，命令历史只在拖拽结束时记录一次。

## 10. 撤销重做

编辑器需要命令栈：

```csharp
public interface IHmiEditCommand
{
    string Name { get; }
    void Execute();
    void Undo();
}
```

第一批命令：

- 添加控件。
- 删除控件。
- 移动控件。
- 缩放控件。
- 修改属性。
- 修改绑定。
- 修改动作。
- 修改层级。
- 复制粘贴。

命令栈位于 `HmiEditorViewModel` 或独立 `HmiEditHistoryService`，通过 `CanUndo`、`CanRedo` 绑定工具栏按钮。

## 11. 属性面板

属性面板不应继续硬编码每个字段。Avalonia 中可以用属性描述 + DataTemplate 实现动态编辑器：

```csharp
public sealed record HmiPropertyDescriptor(
    string Name,
    string DisplayName,
    HmiPropertyEditorKind Editor,
    string Group,
    string? DefaultValue,
    IReadOnlyList<string> Options);
```

编辑器类型：

- `Text`：普通文本。
- `Number`：数字。
- `Boolean`：开关。
- `Color`：颜色。
- `Enum`：下拉。
- `Tag`：Tag 选择器。
- `Resource`：图片/资源选择器。
- `ActionList`：动作列表。

右侧属性面板按分组显示：

- 布局：X、Y、Width、Height、Rotation、ZIndex、锁定。
- 外观：背景、前景、边框、字体、透明度。
- 数据：Tag、格式、单位、上下限、状态映射。
- 交互：点击、按下、释放、值变化动作。
- 权限：可见权限、操作权限。

Tag 选择器从当前配置中的 Tag 列表读取数据，显示 Tag 名称、类型、设备、地址、读写能力。

## 12. 控件清单

第一批必须可用：

- `ValueText`
  - 读取 Tag，显示标题、数值、单位、格式化文本。
- `StateIndicator`
  - 读取 bool/number/string，按状态显示颜色。
- `CommandButton`
  - 点击执行动作，第一版支持页面跳转和写 Tag。
- `Text`
  - 静态文本，可选绑定动态文本。
- `InputBox`
  - 输入数字或文本，确认后写 Tag。
- `SwitchButton`
  - 布尔读写。
- `Image`
  - 显示资源图片，可按状态切换图片。
- `Rectangle` / `Ellipse` / `Line`
  - 基础图元。
- `Container`
  - 组合背景、分组和局部面板。

第二批控件：

- `TrendChart`
  - 实时曲线，后续接历史数据。
- `AlarmList`
  - 报警列表，后续接报警服务。
- `ProgressBar`
  - 百分比和上下限显示。
- `Gauge`
  - 仪表盘。
- `Tank`
  - 液位罐。
- `Pipe` / `Valve` / `Motor`
  - 工业图元。

## 13. 运行器

运行器是 Avalonia HMI 的核心，不是编辑器的附属按钮。它负责把页面文档变成可操作 UI。

```csharp
public sealed class HmiRuntimeViewModel
{
    public HmiPageDocument CurrentPage { get; }
    public ObservableCollection<HmiWidgetViewModel> Widgets { get; }
    public Task NavigateAsync(string pageId);
    public Task ExecuteAsync(HmiActionDefinition action);
}
```

运行器职责：

- 加载页面文档。
- 根据控件目录构建控件 ViewModel。
- 订阅 `ITagDataPipeline`，批量更新绑定控件。
- 维护 Tag 最新快照，新页面打开后立即显示当前值。
- 执行事件动作。
- 写 Tag 时调用应用层端口。
- 权限校验失败时阻止动作并记录日志。

编辑器的 `Run/Edit` 只是运行器的一种嵌入式预览。正式操作员界面应使用 `HmiRuntimeWindow` 或主工作区中的运行视图。

## 14. Tag 写入端口

当前读取路径已经有 `ITagDataPipeline`。写入路径需要新增端口：

```csharp
public interface ITagWriteService
{
    Task<TagWriteResult> WriteAsync(string tagName, object? value, CancellationToken cancellationToken = default);
}
```

执行流程：

1. 用户点击 HMI 控件。
2. 运行器解析控件事件动作。
3. 权限服务校验用户、页面、控件、动作、Tag。
4. `ITagWriteService` 校验 Tag 是否存在、是否可写、类型是否匹配。
5. Application/Runtime 层执行写入。
6. 日志记录写入结果。

第一版底层写入未完成时，可以先提供 `SimulatorTagWriteService`，用于验证按钮、开关、输入框的交互闭环。

## 15. 页面管理

需要从“单页面编辑器”升级为“多页面设计器”：

- 页面列表：显示所有页面。
- 新建页面：选择尺寸和背景。
- 复制页面：复制页面及控件，生成新页面 ID。
- 重命名页面。
- 删除页面。
- 设置启动页。
- 页面跳转动作选择目标页面。

短期存储仍可保持每页一个 JSON：

```text
Config/hmi-pages/main.json
Config/hmi-pages/overview.json
Config/hmi-pages/settings.json
```

中期增加页面索引：

```text
Config/hmi-project.json
Config/hmi-pages/*.json
Config/hmi-resources/*
```

## 16. 资源管理

资源管理用于图片、图标、SVG、控件模板：

```text
Config/hmi-resources/
  images/
  symbols/
  templates/
```

页面文档只保存资源 ID 或相对路径。图片控件通过资源服务解析实际路径。这样项目目录可以整体移动，页面文档不会绑死绝对路径。

## 17. 权限与审计

HMI 写入动作必须从第一版就预留权限字段：

- 页面权限：能否打开页面。
- 控件权限：能否看见、能否操作、能否编辑。
- Tag 权限：能否写入某个 Tag。
- 动作权限：能否执行写入、复位、启停、页面跳转。

文档中保留 `PermissionKey`。运行器执行动作前调用 RBAC 服务。按钮即使在 UI 上被隐藏，写入端口仍必须做二次校验。

审计日志至少记录：

- 用户。
- 页面。
- 控件。
- 动作。
- Tag 名称和值。
- 成功/失败。
- 时间戳。

## 18. 开发里程碑

### M1：强化当前 Avalonia 原型

- 修正默认页面名称和存储名称一致性。
- 增加 `SchemaVersion`、页面 ID、网格配置。
- 增加页面列表和页面切换。
- 增加多选、框选、快捷键、复制粘贴。
- 增加网格显示、吸附和基础对齐命令。
- 增加 HMI JSON round-trip 测试。

验收：

- 打开 HMI 后可以编辑、保存、重载页面。
- 拖拽、缩放、复制、删除、层级调整稳定。
- Simulator Tag 能刷新数值和状态灯。

### M2：控件目录和动态属性面板

- 引入 `IHmiWidgetCatalog`。
- 工具箱由控件目录生成。
- 属性面板由属性描述生成。
- 新增 `Text`、`InputBox`、`SwitchButton`、`Image`、基础图元。
- Tag 选择器接入当前 Tag 配置。

验收：

- 新控件只需注册描述和模板。
- 属性面板能随控件类型变化。
- 旧页面文档能兼容加载。

### M3：运行器和动作系统

- 新增 `HmiRuntimeViewModel`。
- `Run/Edit` 预览改为使用运行器。
- 新增 `HmiEventDefinition`、`HmiActionDefinition`。
- `CommandButton` 支持页面跳转和写 Tag。
- 新增 `ITagWriteService` 抽象和模拟实现。

验收：

- 运行预览中无编辑边框和缩放手柄。
- 按钮可以跳转页面。
- 模拟写入链路可用。

### M4：正式运行窗口和资源管理

- 新增 `HmiRuntimeWindow`。
- 新增图片资源管理。
- 图片控件可引用资源。
- 增加控件组/模板保存与复用。
- 新增进度条、趋势占位、报警占位。

验收：

- 操作员运行窗口可独立打开。
- 页面资源可随项目目录搬移。
- 模板可复用。

### M5：工业控件、权限、历史数据

- 增加仪表盘、液位罐、管道、阀门、电机等工业控件。
- 接入 RBAC。
- 趋势控件接实时和历史数据。
- 报警控件接报警事件。
- 写入动作进入审计日志。

验收：

- 未授权用户无法执行写入。
- 趋势/报警控件显示真实数据。
- 操作日志可追溯。

## 19. 测试策略

优先测试 ViewModel 和文档模型：

- `JsonHmiPageStore`
  - 不存在时生成默认页面。
  - 保存/加载 round-trip。
  - 文件名清洗。
- 文档兼容性
  - 缺失字段使用默认值。
  - 旧 `HmiWidgetKind` 页面可兼容升级到字符串 `Kind`。
- `HmiWidgetViewModel`
  - Tag 匹配大小写不敏感。
  - 数值格式化。
  - 布尔状态转换。
  - 状态色 fallback。
- 编辑命令
  - 添加、删除、移动、缩放、属性修改可撤销重做。
- 运行器
  - Tag 批量刷新。
  - 页面跳转。
  - 动作顺序执行。
  - 权限拒绝。

Avalonia UI 自动化测试放到画布交互稳定后补充，第一阶段先确保文档、ViewModel 和动作执行逻辑可靠。