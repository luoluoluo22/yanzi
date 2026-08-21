using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace OpenQuickHost.Sync;

public sealed class ReleaseNoteEntry
{
    public string Version { get; set; } = string.Empty;
    public string ReleaseDate { get; set; } = string.Empty;
    public bool IsCurrentVersion { get; set; }
    public List<string> Highlights { get; set; } = new();
}

public static class ReleaseHistoryProvider
{
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(5) };

    public static async Task<ObservableCollection<ReleaseNoteEntry>?> FetchOnlineHistoryAsync(string? currentVersion)
    {
        var cleanCurrent = (currentVersion ?? string.Empty).TrimStart('v', 'V').Trim();
        var urls = new[]
        {
            "https://api.github.com/repos/luoluoluo22/yanzi/releases?per_page=15",
            "https://ghfast.top/https://api.github.com/repos/luoluoluo22/yanzi/releases?per_page=15"
        };

        foreach (var url in urls)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Add("User-Agent", "Yanzi-Launcher");
                req.Headers.Add("Accept", "application/vnd.github.v3+json");

                using var resp = await _httpClient.SendAsync(req);
                if (!resp.IsSuccessStatusCode) continue;

                var json = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Array) continue;

                var list = new List<ReleaseNoteEntry>();
                foreach (var el in root.EnumerateArray())
                {
                    var tagName = el.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() ?? "" : "";
                    if (string.IsNullOrWhiteSpace(tagName) || tagName.StartsWith("android", StringComparison.OrdinalIgnoreCase)) continue;

                    var pubAt = el.TryGetProperty("published_at", out var pubProp) ? pubProp.GetString() ?? "" : "";
                    var releaseDate = pubAt.Length >= 10 ? pubAt.Substring(0, 10) : "";

                    var body = el.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() ?? "" : "";
                    var highlights = new List<string>();

                    var lines = body.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var rawLine in lines)
                    {
                        var line = rawLine.Trim();
                        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#") || line.StartsWith("---") || line.StartsWith("安装包") || line.StartsWith("一键安装包") || line.StartsWith("SHA256"))
                        {
                            continue;
                        }

                        if (line.StartsWith("-") || line.StartsWith("*"))
                        {
                            line = line.TrimStart('-', '*', ' ').Trim();
                        }

                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            highlights.Add(line);
                        }
                    }

                    if (highlights.Count == 0)
                    {
                        highlights.Add("系统常规性能优化与稳定性提升。");
                    }

                    var cleanTag = tagName.TrimStart('v', 'V').Trim();
                    list.Add(new ReleaseNoteEntry
                    {
                        Version = tagName.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? tagName : $"v{tagName}",
                        ReleaseDate = releaseDate,
                        IsCurrentVersion = !string.IsNullOrWhiteSpace(cleanCurrent) && string.Equals(cleanTag, cleanCurrent, StringComparison.OrdinalIgnoreCase),
                        Highlights = highlights
                    });
                }

                if (list.Count > 0)
                {
                    return new ObservableCollection<ReleaseNoteEntry>(list);
                }
            }
            catch (Exception ex)
            {
                HostAssets.AppendLog($"[ReleaseHistoryProvider] Failed to fetch online releases from {url}: {ex.Message}");
            }
        }

        return null;
    }

    public static ObservableCollection<ReleaseNoteEntry> GetHistory(string? currentVersion)
    {
        var cleanCurrent = (currentVersion ?? string.Empty).TrimStart('v', 'V').Trim();

        var list = new List<ReleaseNoteEntry>
        {
            new()
            {
                Version = "v0.3.14",
                ReleaseDate = "2026-08-21",
                Highlights = new List<string>
                {
                    "【轮盘编辑即时响应】点击编辑按钮瞬间原本轮盘立即隐去，深色遮罩 0ms 覆盖，按钮呈现圆环加载动效，异步秒级呈现全景轮盘。",
                    "【多轮盘垂直向下滚动】全景编辑模式支持滚轮垂直向下滚动浏览视口，右侧浮现沉浸式极简滚动条，全屏操作精准映射。",
                    "【轮盘主体及中心圆选中】点击任意轮盘中心大圆或背景主体区域，即可立即选中激活该轮盘（点亮外发光蓝边并置顶）。",
                    "【性能极致优化与 UI 精简】停用后台高频定时器轮询，引入 AABB 空间快速裁剪，CPU 占用直接降为 0%，去除右上角冗余大圆圈。"
                }
            },
            new()
            {
                Version = "v0.3.13",
                ReleaseDate = "2026-08-20",
                Highlights = new List<string>
                {
                    "【前台应用感知】鼠标手势支持多应用白名单绑定，新增【黑名单应用 (禁用手势)】Tab，处于黑名单时静默放行杜绝冲突。",
                    "【EXE 真实高清图标】应用列表完整接入系统真实应用程序图标提取，高清渲染各类已安装及运行中的 EXE 图标。",
                    "【手势卡片轻量化】重构为单行 3 列紧凑内嵌式布局（高度仅 74px），点击卡片直接唤起配置弹窗，Hover 平滑浮现更换与解绑操作。"
                }
            },
            new()
            {
                Version = "v0.3.12",
                ReleaseDate = "2026-08-20",
                Highlights = new List<string>
                {
                    "【起步 0 毫秒零延迟预热】彻底修复手势画布 Hide 时误调用 Close 导致每次起步都要重新 new 窗口的严重性能缺陷，改为常驻单例热备 + 启动后台预热，起步 0.01ms 极致跟手！",
                    "【双屏新建手势单屏对齐】新建手势录制器重构为单屏精准覆盖，提示文案、中央星星、绿色流光轨迹与保存手势弹窗 100% 居中展现在当前操作屏幕，彻底根除双屏分开显示与弹窗丢失缺陷。"
                }
            },
            new()
            {
                Version = "v0.3.11",
                ReleaseDate = "2026-08-20",
                Highlights = new List<string>
                {
                    "【双屏起步性能暴增】重构手势全透明画布为单屏动态自适应 Bounds，显存开销暴降 75%，杜绝 DWM 跨显示器 Blit 拷贝卡顿，双屏下起步与单屏一样 0 毫秒丝滑秒开。",
                    "【彻底消除强同步阻塞】剔除手势起步时的同步布局遍历与系统强刷新，释放主线程渲染压力。",
                    "【双屏手势录制与弹窗吸附】引入多显示器智能吸附算法，无论在主屏还是副屏绘制手势，录制提示、中间指示与保存手势弹窗 100% 精确居中呈现在当前操作屏幕下方，彻底解决跨屏看不见弹窗的问题。"
                }
            },
            new()
            {
                Version = "v0.3.10",
                ReleaseDate = "2026-08-20",
                Highlights = new List<string>
                {
                    "【远程控制全面支持】修复 ToDesk、向日葵等远程控制软件下鼠标手势与全局鼠标触发器无法激发的缺陷（采用专属签名防重放机制替代粗暴拦截）。",
                    "【高分屏像素级贴合】重构手势画布为硬件级 ScreenToClient 坐标映射，彻底解决 125%/150%/200% 缩放及多显示器排布下鼠标手势轨迹偏移的问题。",
                    "【更新历史滚动面板】关于界面自动更新下方新增历史版本更新日志滚动视图，最新版本置顶，便于随时查阅演进历史。"
                }
            },
            new()
            {
                Version = "v0.3.9",
                ReleaseDate = "2026-08-20",
                Highlights = new List<string>
                {
                    "【界面交互规范】精简关于面板日志文案与按钮样式，确保更新按钮作为唯一蓝色视觉焦点。",
                    "【健壮性加固】修复设置窗口实例初始化与资源引用异常，增强多层 Fallback 恢复机制。"
                }
            },
            new()
            {
                Version = "v0.3.8",
                ReleaseDate = "2026-08-20",
                Highlights = new List<string>
                {
                    "【系统诊断与日志】关于面板新增运行日志与日志目录一键打开功能，方便一键发送 host.log 协助排查。",
                    "【手势深度埋点】手势全生命周期注入原始物理坐标、DPI 矩阵与模板评分详细追踪。"
                }
            },
            new()
            {
                Version = "v0.3.7",
                ReleaseDate = "2026-08-20",
                Highlights = new List<string>
                {
                    "【Velopack 1.2 LTS】全面升级 Velopack LTS 稳定版协议，打包工具与代码 SDK 自动强制对齐。",
                    "【一键秒级增量】本地智能差分基准缓存，增量补丁压缩至几百 KB，实现秒级组装与无感升级。"
                }
            },
            new()
            {
                Version = "v0.3.6",
                ReleaseDate = "2026-08-20",
                Highlights = new List<string>
                {
                    "【自动更新控制台】新增国内高速镜像源与官方源下拉切换，新增下载进度百分比进度条。",
                    "【Ctrl+左键手势】优化快捷手势与长按触发的互斥逻辑，杜绝按键冲突。"
                }
            },
            new()
            {
                Version = "v0.3.5",
                ReleaseDate = "2026-08-20",
                Highlights = new List<string>
                {
                    "【镜像更新加速】支持国内多线路自动更新镜像加速，大幅提升海外源直连失败时的更新成功率。"
                }
            },
            new()
            {
                Version = "v0.3.4",
                ReleaseDate = "2026-08-19",
                Highlights = new List<string>
                {
                    "【手势视觉升级】全新白绿流光渐变笔刷与起始动作栏（置顶/编辑/取消），新增手势速查看板。",
                    "【显存防残影】画布采用独立纯净生命周期管理，彻底杜绝 Direct3D 显存微小残留。"
                }
            },
            new()
            {
                Version = "v0.3.0",
                ReleaseDate = "2026-08-18",
                Highlights = new List<string>
                {
                    "【全新架构】燕子启动器正式发布，支持小程序热插拔、手势与云端多端同步。"
                }
            }
        };

        foreach (var item in list)
        {
            var itemClean = item.Version.TrimStart('v', 'V').Trim();
            if (!string.IsNullOrWhiteSpace(cleanCurrent) && string.Equals(itemClean, cleanCurrent, StringComparison.OrdinalIgnoreCase))
            {
                item.IsCurrentVersion = true;
            }
        }

        return new ObservableCollection<ReleaseNoteEntry>(list);
    }
}
