# Yanzi UI：公共设计系统（Windows / WPF 第一阶段）

版本：0.1.0 · 2026-10-08

## 目标与约束

减少重复开发，让一致性成为可执行样式与控件行为，而不只是口头视觉规范。兼容原有 WPF 小程序、稳定版 Runtime、热更新与用户数据。跨平台只共享 Design Tokens、语义及交互规范，不强制共用 WPF 渲染引擎。

Windows 主宿主基于 .NET 9 / WPF，已有主题资源位于 src/OpenQuickHost/Themes。新的 Yanzi.UI.Wpf 是独立程序集，随 Windows 宿主和 Yanzi.Runtime 构建，不侵入旧界面。

## 代码结构

    src/Yanzi.UI.Wpf/
      Themes/Tokens.Dark.xaml    深色设计令牌
      Themes/Tokens.Light.xaml   浅色设计令牌
      Themes/Controls.xaml       组件样式
      YanziUi.cs                 显式加载、主题切换和样式绑定
      YanziDialog.cs             标准确认弹窗
      YanziToast.cs              非阻塞消息提示
    src/Yanzi.UI.Verification/   WPF STA 自动验证
    src/Yanzi.UI.Gallery/        人工组件展示程序

## 视觉令牌

语义化颜色：Yanzi.Brush.Window、Surface、SurfaceHover、Input、Border、Text、TextSecondary、TextMuted、Accent、OnAccent、Danger、Success、Warning。
尺寸：Yanzi.Space.1/2/3/4/6（4、8、12、16、24）；Yanzi.Font.Small/Body/Title（12、13、18）；Yanzi.Radius.Control/Card（8、12）。

深色基准窗口 #161616、蓝色强调 #3B82F6。通用按钮与反馈应该引用语义令牌；白板画布等专业业务配色可以保持独立。

## 第一批组件

| 类别 | 样式资源键或 API | 行为 |
| --- | --- | --- |
| Button | Yanzi.Button.Primary / Secondary / Danger | 悬停、按下、焦点、禁用 |
| Input | Yanzi.Input | 选择编辑、键盘、输入法、焦点 |
| Dialog | YanziDialog.Confirm(...) | Owner、Esc 取消、Enter 确认 |
| Toast | YanziToast.Show(...) | 非阻塞、自动关闭、状态明确 |
| Loading | Yanzi.Loading | 不确定进度，业务未完成前展示 |
| Switch | Yanzi.Toggle | CheckBox 的开关外观，支持键盘 |
| Menu | Yanzi.Menu / Yanzi.MenuItem | WPF 原生菜单 |
| ListItem | Yanzi.List / Yanzi.ListItem | 悬停选中、回收虚拟化 |
| 辅助 | Yanzi.CheckBox / Yanzi.Card / Yanzi.Text.Secondary | 字体与表面 |

列表虚拟化效果仍需正确的数据绑定、ItemsPanel 和实际数据量测试，不能只依靠样式声明。

## 小程序示例（C# 原生 WPF）

前提：小程序编译与运行使用的燕子宿主已经部署 Yanzi.UI.Wpf.dll。
在小程序的 WPF UI 线程中：

    using System.Windows;
    using System.Windows.Controls;
    using Yanzi.UI.Wpf;

    var window = new Window { Title = "示例", Width = 420, Height = 240 };
    YanziUi.ApplyTo(window, YanziTheme.Dark);
    window.SetResourceReference(Window.BackgroundProperty, "Yanzi.Brush.Window");

    var stack = new StackPanel { Margin = new Thickness(16) };
    var button = YanziUi.WithStyle(
        new Button { Content = "保存" }, YanziUi.Styles.PrimaryButton);
    button.Click += (_, _) =>
    {
        // 真实业务已经确认成功，才显示成功消息。
        YanziToast.Show(window, "保存成功", YanziToastKind.Success);
    };
    stack.Children.Add(button);
    window.Content = stack;
    window.Show();

    // 每个窗口可独立切换，且不会重复加载资源。
    YanziUi.ApplyTo(window, YanziTheme.Light);

动态编译器从宿主的应用目录收集程序集引用。开发目录里新增 DLL 不会让正在运行的旧 Runtime 自动支持这个 API。只有完整部署和激活后才可以让生产小程序依赖该库。

## 构建与验证

    dotnet build src/Yanzi.UI.Wpf/Yanzi.UI.Wpf.csproj -c Release
    dotnet run --project src/Yanzi.UI.Verification/Yanzi.UI.Verification.csproj -c Release
    dotnet build src/Yanzi.UI.Gallery/Yanzi.UI.Gallery.csproj -c Release
    dotnet build src/Yanzi.Runtime/Yanzi.Runtime.csproj -c Release -p:SkipStopRunningApp=true
    # 人工体验组件展示程序
    dotnet run --project src/Yanzi.UI.Gallery/Yanzi.UI.Gallery.csproj -c Release

第一阶段自动测试：设计资源存在、正确类型、深浅色切换、重复调用不增加资源字典、旧窗口不受影响。Gallery 用于人工测试菜单、焦点、中文输入法、Esc/Enter 和 DPI。通过编译不等同于通过完整的人工 UI 验收。

## 迁移与版本

第一阶段只交付独立库 + Gallery，不修改用户正在运行的小程序。

第二阶段逐步迁移：延时关机的基础表单和危险动作；剪贴板的搜索框、菜单和列表；截图 OCR 的加载与反馈。每个试点必须先备份原文件并记录功能行为，完成回归后才推广。白板画布、截图选区和日历网格维持业务特有实现。

组件库按 SemVer 管理。破坏性 API 变更必须升主版本并提供兼容处理。小程序不应私带不同版本 DLL，也不应将业务服务、云同步、权限管理写入控件库。

Windows 正式后台采用版本化部署，且开发版和稳定版仍共享生产 Runtime。仅编译不会自动升级线上 Runtime。需要正式激活时遵循 docs/desktop-development-isolation.md 的 install-shared-runtime.ps1 -Activate 流程和回滚机制。


## 评估中心（Windows 可操作 UI）

评估界面是独立的 Windows WPF 程序，项目在 src/Yanzi.UI.Gallery。
提供总览、按钮、输入、选择、列表、反馈、设计令牌、评价与记录八个页面。
使用真实组件及主题样式，可以切换深浅主题、模拟交互、记录本地评分与改进建议。

构建并安装：

    powershell -ExecutionPolicy Bypass -File .\scripts\install-yanzi-ui-gallery.ps1 -Launch

打开方式：

* 在燕子里搜索“组件评估”并打开（本地小程序 id: yanzi-ui-gallery）。
* 双击桌面上的“燕子 UI 组件评估中心”快捷方式。
* 直接运行安装目录 Tools/YanziUiGallery/preview-* 内的 Yanzi.UI.Gallery.exe。

评价保存在 %LOCALAPPDATA%\OpenQuickHost\UiReview 中，每次保存产生一份 JSON；
评估程序不会上传评价或修改任何既有小程序的数据。可以在评价页点“打开评价目录”查看。
安装脚本使用不可变版本目录，不会强制结束旧的预览进程。

自动化检查：

    powershell -ExecutionPolicy Bypass -File .\scripts\verify-yanzi-ui-gallery.ps1

验收脚本使用 Windows UI Automation，在已打开的评估中心中切换全部八页，
检查可访问控件、输入、开关、列表选中与主题切换，最后返回“总览”。
它不操作生产小程序，也不保存测试评价。验证结果：28 项通过。

另已增加独立 Runtime 的真实 C# 小程序动态编译验证，用于测试引用
Yanzi.UI.Wpf 程序集是否可行。该验证仅在临时测试数据目录启动独立 Runtime，
不激活替换生产 Runtime。
