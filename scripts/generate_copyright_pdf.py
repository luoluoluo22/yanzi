# -*- coding: utf-8 -*-
import os
import glob
from reportlab.lib.pagesizes import A4
from reportlab.lib import colors
from reportlab.lib.units import mm
from reportlab.pdfgen import canvas
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.platypus import SimpleDocTemplate, Paragraph, Spacer, Image, Table, TableStyle, PageBreak
from reportlab.lib.styles import getSampleStyleSheet, ParagraphStyle

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SOFTWARE_NAME = "燕子桌面快捷效率宿主软件"
SOFTWARE_VERSION = "V0.3.13"
AUTHOR_NAME = "罗名扬"

# 注册 Windows 系统自带的中文字体
font_path = "C:\\Windows\\Fonts\\simsun.ttc"
if not os.path.exists(font_path):
    font_path = "C:\\Windows\\Fonts\\msyh.ttc"
if not os.path.exists(font_path):
    font_path = "C:\\Windows\\Fonts\\simhei.ttf"

pdfmetrics.registerFont(TTFont("SimSun", font_path))
pdfmetrics.registerFont(TTFont("YaHei", "C:\\Windows\\Fonts\\msyh.ttc" if os.path.exists("C:\\Windows\\Fonts\\msyh.ttc") else font_path))

# -------------------------------------------------------------
# 1. 生成《源程序文档.pdf》（前30页+后30页共60页，每页50行）
# -------------------------------------------------------------
def build_source_code_pdf():
    cs_pattern = os.path.join(ROOT, "src", "OpenQuickHost", "**", "*.cs")
    cs_files = sorted(glob.glob(cs_pattern, recursive=True))
    
    valid_lines = []
    for f in cs_files:
        if "\\obj\\" in f or "\\bin\\" in f:
            continue
        try:
            with open(f, "r", encoding="utf-8", errors="ignore") as fp:
                for line in fp:
                    l = line.rstrip()
                    if l.strip():
                        valid_lines.append(l)
        except Exception:
            pass
            
    total = len(valid_lines)
    selected = []
    if total <= 3000:
        selected = valid_lines
    else:
        selected.extend(valid_lines[:1500])
        selected.extend(valid_lines[-1500:])
        
    lines_per_page = 50
    pages_count = (len(selected) + lines_per_page - 1) // lines_per_page
    
    out_pdf = os.path.join(ROOT, "软著申报材料_源程序文档.pdf")
    c = canvas.Canvas(out_pdf, pagesize=A4)
    width, height = A4
    
    margin_x = 20 * mm
    margin_top = 20 * mm
    margin_bottom = 20 * mm
    
    for p in range(pages_count):
        chunk = selected[p * lines_per_page : (p + 1) * lines_per_page]
        
        # 绘制页眉
        c.setFont("SimSun", 9)
        c.setFillColor(colors.HexColor("#444444"))
        c.drawString(margin_x, height - margin_top + 4 * mm, f"软件全称：{SOFTWARE_NAME} {SOFTWARE_VERSION}")
        c.drawRightString(width - margin_x, height - margin_top + 4 * mm, "源程序技术鉴定文档")
        c.setStrokeColor(colors.HexColor("#aaaaaa"))
        c.setLineWidth(0.5)
        c.line(margin_x, height - margin_top + 2 * mm, width - margin_x, height - margin_top + 2 * mm)
        
        # 绘制代码行
        c.setFont("SimSun", 8)
        c.setFillColor(colors.HexColor("#111111"))
        
        line_height = 4.2 * mm
        start_y = height - margin_top - 4 * mm
        
        for i, code_line in enumerate(chunk):
            # 截断过长字符防止溢出
            disp = code_line[:95]
            c.drawString(margin_x, start_y - (i * line_height), disp)
            
        # 绘制页脚
        c.setFont("SimSun", 9)
        c.setFillColor(colors.HexColor("#444444"))
        c.setStrokeColor(colors.HexColor("#aaaaaa"))
        c.setLineWidth(0.5)
        c.line(margin_x, margin_bottom - 2 * mm, width - margin_x, margin_bottom - 2 * mm)
        c.drawString(margin_x, margin_bottom - 6 * mm, f"著作权人：{AUTHOR_NAME}")
        c.drawRightString(width - margin_x, margin_bottom - 6 * mm, f"第 {p + 1} 页 / 共 {pages_count} 页")
        
        c.showPage()
        
    c.save()
    print(f"Generated: {out_pdf} (Total Pages: {pages_count})")

# -------------------------------------------------------------
# 2. 生成《用户操作手册.pdf》（图文并茂、标准版式）
# -------------------------------------------------------------
class NumberedCanvas(canvas.Canvas):
    def __init__(self, *args, **kwargs):
        super(NumberedCanvas, self).__init__(*args, **kwargs)
        self._saved_page_states = []

    def showPage(self):
        self._saved_page_states.append(dict(self.__dict__))
        self._startPage()

    def save(self):
        num_pages = len(self._saved_page_states)
        for state in self._saved_page_states:
            self.__dict__.update(state)
            self.draw_header_footer(num_pages)
            canvas.Canvas.showPage(self)
        canvas.Canvas.save(self)

    def draw_header_footer(self, page_count):
        if self._pageNumber == 1:
            return  # 封面不绘制页眉页脚
        self.saveState()
        self.setFont("SimSun", 9)
        self.setFillColor(colors.HexColor("#555555"))
        self.setStrokeColor(colors.HexColor("#cccccc"))
        self.setLineWidth(0.5)
        width, height = A4
        # 页眉
        self.drawString(20 * mm, height - 16 * mm, f"{SOFTWARE_NAME} {SOFTWARE_VERSION} 使用说明书")
        self.line(20 * mm, height - 18 * mm, width - 20 * mm, height - 18 * mm)
        # 页脚
        self.line(20 * mm, 18 * mm, width - 20 * mm, 18 * mm)
        self.drawString(20 * mm, 13 * mm, f"著作权人：{AUTHOR_NAME}")
        self.drawRightString(width - 20 * mm, 13 * mm, f"第 {self._pageNumber} 页 / 共 {page_count} 页")
        self.restoreState()

def build_manual_pdf():
    out_pdf = os.path.join(ROOT, "软著申报材料_用户操作手册.pdf")
    doc = SimpleDocTemplate(
        out_pdf,
        pagesize=A4,
        leftMargin=20 * mm,
        rightMargin=20 * mm,
        topMargin=22 * mm,
        bottomMargin=22 * mm
    )
    
    styles = getSampleStyleSheet()
    
    title_style = ParagraphStyle(
        "CoverTitle",
        parent=styles["Normal"],
        fontName="YaHei",
        fontSize=24,
        leading=32,
        textColor=colors.HexColor("#1E3A8A"),
        alignment=1, # 居中
        spaceAfter=15
    )
    
    subtitle_style = ParagraphStyle(
        "CoverSubtitle",
        parent=styles["Normal"],
        fontName="YaHei",
        fontSize=15,
        leading=22,
        textColor=colors.HexColor("#4B5563"),
        alignment=1,
        spaceAfter=40
    )
    
    cover_meta = ParagraphStyle(
        "CoverMeta",
        parent=styles["Normal"],
        fontName="YaHei",
        fontSize=11,
        leading=22,
        textColor=colors.HexColor("#374151"),
        alignment=1
    )
    
    h1_style = ParagraphStyle(
        "Heading1_Custom",
        parent=styles["Normal"],
        fontName="YaHei",
        fontSize=14,
        leading=20,
        textColor=colors.HexColor("#1E40AF"),
        spaceBefore=14,
        spaceAfter=8,
        keepWithNext=True
    )

    h2_style = ParagraphStyle(
        "Heading2_Custom",
        parent=styles["Normal"],
        fontName="YaHei",
        fontSize=11,
        leading=16,
        textColor=colors.HexColor("#1F2937"),
        spaceBefore=10,
        spaceAfter=5,
        keepWithNext=True
    )
    
    p_style = ParagraphStyle(
        "Body_Custom",
        parent=styles["Normal"],
        fontName="SimSun",
        fontSize=9.5,
        leading=15,
        textColor=colors.HexColor("#222222"),
        firstLineIndent=20,
        spaceAfter=6
    )
    
    caption_style = ParagraphStyle(
        "Caption_Custom",
        parent=styles["Normal"],
        fontName="SimSun",
        fontSize=8.5,
        leading=12,
        textColor=colors.HexColor("#6B7280"),
        alignment=1,
        spaceAfter=10
    )
    
    story = []
    
    # 1. 封面
    story.append(Spacer(1, 45 * mm))
    story.append(Paragraph(SOFTWARE_NAME, title_style))
    story.append(Paragraph("用户使用与操作说明书", subtitle_style))
    story.append(Spacer(1, 30 * mm))
    story.append(Paragraph(f"<b>软件版本：</b>{SOFTWARE_VERSION}", cover_meta))
    story.append(Paragraph(f"<b>著作权人：</b>{AUTHOR_NAME}", cover_meta))
    story.append(Paragraph("<b>文档类型：</b>计算机软件著作权登记材料", cover_meta))
    story.append(Paragraph("<b>发布日期：</b>2026年08月", cover_meta))
    story.append(PageBreak())
    
    # 2. 第一章 概述与运行环境
    story.append(Paragraph("第一章 软件概述与运行环境", h1_style))
    story.append(Paragraph("1.1 软件总体概述", h2_style))
    story.append(Paragraph(f"“{SOFTWARE_NAME}”是一款专为 Windows 操作系统深度定制的高性能桌面快捷效率启动、手势交互拓扑识别与多端云同步系统。软件基于现代化 .NET 平台与原生 Win32 低层事件挂钩架构，具备毫秒级输入响应、模块化扩展插拔及低内存占用等特性。", p_style))
    story.append(Paragraph("系统旨在解决日常办公与桌面操作中频繁查找程序、切换窗口及多步按键的高操作成本，通过创新的拼音检索、鼠标矢量手势、多应用前台规则感知与沉浸式桌面小组件，大幅提升桌面人机交互效率。", p_style))
    
    story.append(Paragraph("1.2 运行环境要求", h2_style))
    table_data = [
        ["配置项目", "要求指标"],
        ["处理器 (CPU)", "Intel Core i3 / AMD Ryzen 3 及以上 x64 架构处理器"],
        ["物理内存 (RAM)", "4GB 及以上可用内存"],
        ["硬盘存储空间", "至少 500MB 可用磁盘空间"],
        ["操作系统", "Windows 10 64位 / Windows 11 64位"],
        ["运行依赖支撑", "内置自包含运行时（或 .NET Desktop Runtime 9.0）"]
    ]
    t = Table(table_data, colWidths=[40 * mm, 125 * mm])
    t.setStyle(TableStyle([
        ('FONTNAME', (0,0), (-1,-1), 'SimSun'),
        ('FONTSIZE', (0,0), (-1,-1), 8.5),
        ('BACKGROUND', (0,0), (-1,0), colors.HexColor("#F3F4F6")),
        ('GRID', (0,0), (-1,-1), 0.5, colors.HexColor("#D1D5DB")),
        ('TOPPADDING', (0,0), (-1,-1), 4),
        ('BOTTOMPADDING', (0,0), (-1,-1), 4),
    ]))
    story.append(t)
    story.append(Spacer(1, 4 * mm))
    
    img_cover = os.path.join(ROOT, "readme-cover-16x9.png")
    if os.path.exists(img_cover):
        story.append(Image(img_cover, width=155 * mm, height=87 * mm))
        story.append(Paragraph("图 1-1 燕子桌面效率启动系统整体架构概览", caption_style))
        
    story.append(PageBreak())
    
    # 3. 第二章 软件安装与全局启动器
    story.append(Paragraph("第二章 软件安装与全局启动器检索", h1_style))
    story.append(Paragraph("2.1 安装与启动运行", h2_style))
    story.append(Paragraph("运行安装程序 Yanzi-win-Setup.exe，系统自动完成轻量化解压与托盘常驻。软件默认全局呼出快捷键为 Alt + Space（可根据个人习惯在设置中心自由更改）。", p_style))
    
    story.append(Paragraph("2.2 全局启动器与命令检索", h2_style))
    story.append(Paragraph("按下全局热键后即刻平滑呼出搜索中心。用户输入中文全拼、简拼首字母或英文指令，系统即刻通过流水线索引算法高亮匹配本地程序、系统功能与小程序。", p_style))
    
    img_launcher = os.path.join(ROOT, "launcher-and-quick-panel.png")
    if os.path.exists(img_launcher):
        story.append(Image(img_launcher, width=155 * mm, height=87 * mm))
        story.append(Paragraph("图 2-1 全局热键唤醒启动器检索与操作面板", caption_style))
        
    story.append(Paragraph("支持通过键盘方向键或鼠标悬停预览匹配条目，回车键直接执行，或使用 Ctrl + K 呼出专属动作快捷菜单。", p_style))
    story.append(PageBreak())
    
    # 4. 第三章 前台应用感知鼠标手势
    story.append(Paragraph("第三章 前台应用感知鼠标手势与冲突拦截", h1_style))
    story.append(Paragraph("3.1 鼠标矢量轨迹手势识别", h2_style))
    story.append(Paragraph("软件内置自研鼠标轨迹拓扑识别引擎，支持按住鼠标右键（或中键）在屏幕任意位置绘制方向序列（如 ↑、↓→、Z字形 等）。系统将动态渲染矢量抗锯齿轨迹并在释放按键后毫秒级判定触发对应动作。", p_style))
    
    story.append(Paragraph("3.2 前台窗口多应用白名单与黑名单感知", h2_style))
    story.append(Paragraph("系统深度集成了 Windows 前台活动窗口句柄嗅探技术：", p_style))
    story.append(Paragraph("（1）白名单应用限定：支持将手势限定在特定应用中生效（如仅在 Edge 浏览器中触发网页后退）。目标应用在前台时手势命中，其余应用中静默放行。", p_style))
    story.append(Paragraph("（2）黑名单应用禁用：用户可将全屏游戏、专业绘图或远程控制软件添加至黑名单。前台处于黑名单程序时完全放行右键拖拽，彻底杜绝手势按键冲突。", p_style))
    
    img_grid = os.path.join(ROOT, "quick-panel-grid.png")
    if os.path.exists(img_grid):
        story.append(Image(img_grid, width=155 * mm, height=87 * mm))
        story.append(Paragraph("图 3-1 常用手势快速绑定与多应用前台规则配置", caption_style))
        
    story.append(PageBreak())
    
    # 5. 第四章 JSON 自定义扩展与多端同步
    story.append(Paragraph("第四章 JSON 小程序扩展与多端云同步", h1_style))
    story.append(Paragraph("4.1 JSON 小程序热加载机制", h2_style))
    story.append(Paragraph("软件支持开放的标准 JSON 扩展协议。用户点击状态栏 + 按钮即可进入可视化编辑器，配置指令 ID、名称、图标、打开路径或参数模板，保存后即可即时热生效。", p_style))
    
    img_editor = os.path.join(ROOT, "json-extension-editor.png")
    if os.path.exists(img_editor):
        story.append(Image(img_editor, width=155 * mm, height=87 * mm))
        story.append(Paragraph("图 4-1 JSON 小程序可视化配置与管理编辑器", caption_style))
        
    story.append(Paragraph("4.2 桌面小组件与多端加密同步", h2_style))
    story.append(Paragraph("系统提供了沉浸式桌面便签组件，支持置顶悬浮与多屏漫游。所有手势配置、小程序及便签数据均通过端到端加密与 Cloudflare / WebDAV 保持多设备实时同步。", p_style))
    
    doc.build(story, canvasmaker=NumberedCanvas)
    print(f"Generated: {out_pdf}")

if __name__ == "__main__":
    build_source_code_pdf()
    build_manual_pdf()
