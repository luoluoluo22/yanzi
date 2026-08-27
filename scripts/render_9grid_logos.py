# -*- coding: utf-8 -*-
import os
import math
from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# 加载中文字体
font_path = "C:\\Windows\\Fonts\\msyhbd.ttc"
if not os.path.exists(font_path):
    font_path = "C:\\Windows\\Fonts\\simhei.ttf"
    
font_title = ImageFont.truetype(font_path, 36)
font_card_num = ImageFont.truetype(font_path, 22)
font_card_title = ImageFont.truetype(font_path, 20)
font_card_desc = ImageFont.truetype("C:\\Windows\\Fonts\\msyh.ttc" if os.path.exists("C:\\Windows\\Fonts\\msyh.ttc") else font_path, 14)

# 颜色定义
BG_COLOR = (15, 23, 42)          # 深色科技背景
CARD_BG = (30, 41, 59)           # 卡片背景
CARD_BORDER = (51, 65, 85)       # 卡片边框
CYAN_COLOR = (6, 182, 212)        # 青色
BLUE_COLOR = (59, 130, 246)       # 蓝色
EMERALD_COLOR = (16, 185, 129)    # 翡翠绿
PURPLE_COLOR = (168, 85, 247)     # 紫色
ORANGE_COLOR = (249, 115, 22)     # 橙色
WHITE = (255, 255, 255)
TEXT_GRAY = (148, 163, 184)

def draw_logo_1(draw, cx, cy, s):
    # 方案1: 粗流线分叉剪刀尾飞燕 (极简高饱满)
    points = [
        (cx + s*0.6, cy - s*0.45), # 头部尖端
        (cx + s*0.3, cy - s*0.1),
        (cx - s*0.6, cy - s*0.4),  # 左翼尖
        (cx - s*0.2, cy + s*0.05),
        (cx - s*0.55, cy + s*0.5), # 燕尾左叉
        (cx - s*0.05, cy + s*0.25),# 燕尾凹陷
        (cx + s*0.15, cy + s*0.5), # 燕尾右叉
        (cx + s*0.1, cy + s*0.1),
        (cx + s*0.5, cy + s*0.15), # 右翼
    ]
    draw.polygon(points, fill=CYAN_COLOR)

def draw_logo_2(draw, cx, cy, s):
    # 方案2: 字母 "Y" 与俯冲飞燕融合
    # 燕子化作 Y 的左右两臂与下部躯干
    left_wing = [(cx, cy - s*0.05), (cx - s*0.55, cy - s*0.45), (cx - s*0.4, cy - s*0.1), (cx - s*0.15, cy + s*0.1)]
    right_wing = [(cx, cy - s*0.05), (cx + s*0.55, cy - s*0.45), (cx + s*0.4, cy - s*0.1), (cx + s*0.15, cy + s*0.1)]
    tail = [(cx - s*0.15, cy + s*0.1), (cx - s*0.35, cy + s*0.55), (cx, cy + s*0.35), (cx + s*0.35, cy + s*0.55), (cx + s*0.15, cy + s*0.1)]
    draw.polygon(left_wing, fill=BLUE_COLOR)
    draw.polygon(right_wing, fill=CYAN_COLOR)
    draw.polygon(tail, fill=(99, 102, 241))
    draw.ellipse([cx - s*0.1, cy - s*0.35, cx + s*0.1, cy - s*0.15], fill=WHITE)

def draw_logo_3(draw, cx, cy, s):
    # 方案3: 两道超粗极简流线双弧 (极简大弧度，16x16下最清晰)
    # 上弧翼
    draw.arc([cx - s*0.6, cy - s*0.6, cx + s*0.6, cy + s*0.6], start=180, end=330, fill=CYAN_COLOR, width=int(s*0.28))
    # 燕尾双分叉下弧
    draw.arc([cx - s*0.4, cy - s*0.2, cx + s*0.4, cy + s*0.6], start=30, end=170, fill=BLUE_COLOR, width=int(s*0.22))
    # 燕头
    draw.ellipse([cx + s*0.35, cy - s*0.4, cx + s*0.55, cy - s*0.2], fill=WHITE)

def draw_logo_4(draw, cx, cy, s):
    # 方案4: 环形轮盘徽章飞燕 (燕环概念)
    # 外圈断环
    draw.arc([cx - s*0.55, cy - s*0.55, cx + s*0.55, cy + s*0.55], start=45, end=330, fill=CARD_BORDER, width=int(s*0.12))
    # 内嵌冲破圆环的飞燕
    pts = [
        (cx + s*0.6, cy - s*0.3),  # 冲出圆环的燕头
        (cx + s*0.2, cy - s*0.05),
        (cx - s*0.4, cy - s*0.45), # 左翼
        (cx - s*0.1, cy + s*0.05),
        (cx - s*0.45, cy + s*0.45),# 燕尾左
        (cx, cy + s*0.2),          # 燕尾中
        (cx + s*0.2, cy + s*0.4),  # 燕尾右
        (cx + s*0.15, cy + s*0.1),
    ]
    draw.polygon(pts, fill=EMERALD_COLOR)

def draw_logo_5(draw, cx, cy, s):
    # 方案5: 现代圆角 Squircle App 图标 + 负空间纯白极简剪影
    # 渐变底板
    box = [cx - s*0.55, cy - s*0.55, cx + s*0.55, cy + s*0.55]
    draw.rounded_rectangle(box, radius=int(s*0.25), fill=BLUE_COLOR)
    # 纯白极简剪影燕
    pts = [
        (cx + s*0.35, cy - s*0.25),
        (cx + s*0.15, cy - s*0.05),
        (cx - s*0.35, cy - s*0.25),
        (cx - s*0.1, cy + s*0.05),
        (cx - s*0.3, cy + s*0.35),
        (cx, cy + s*0.18),
        (cx + s*0.15, cy + s*0.3),
        (cx + s*0.1, cy + s*0.05),
    ]
    draw.polygon(pts, fill=WHITE)

def draw_logo_6(draw, cx, cy, s):
    # 方案6: 硬核多边形赛博几何燕 (45度斜角与高科技切割感)
    pts1 = [(cx + s*0.5, cy - s*0.2), (cx + s*0.1, cy - s*0.05), (cx - s*0.45, cy - s*0.4), (cx - s*0.1, cy)]
    pts2 = [(cx - s*0.1, cy), (cx - s*0.4, cy + s*0.45), (cx, cy + s*0.2), (cx + s*0.2, cy + s*0.4), (cx + s*0.1, cy - s*0.05)]
    draw.polygon(pts1, fill=PURPLE_COLOR)
    draw.polygon(pts2, fill=CYAN_COLOR)

def draw_logo_7(draw, cx, cy, s):
    # 方案7: 太极/旋风动态双翼燕 (极速旋转流线感)
    draw.arc([cx - s*0.5, cy - s*0.5, cx + s*0.5, cy + s*0.5], start=210, end=30, fill=ORANGE_COLOR, width=int(s*0.24))
    draw.arc([cx - s*0.4, cy - s*0.4, cx + s*0.4, cy + s*0.4], start=30, end=210, fill=CYAN_COLOR, width=int(s*0.24))
    # 燕尾分叉
    draw.polygon([(cx - s*0.35, cy + s*0.2), (cx - s*0.5, cy + s*0.45), (cx - s*0.25, cy + s*0.35)], fill=CYAN_COLOR)

def draw_logo_8(draw, cx, cy, s):
    # 方案8: 极简双胶囊圆头粗线条 (Ultra-bold Capsule Strokes)
    # 主翼粗胶囊
    draw.line([(cx - s*0.45, cy - s*0.25), (cx + s*0.45, cy - s*0.25)], fill=CYAN_COLOR, width=int(s*0.24))
    # 机身与剪刀尾粗胶囊
    draw.line([(cx + s*0.2, cy - s*0.25), (cx - s*0.2, cy + s*0.35)], fill=BLUE_COLOR, width=int(s*0.2))
    draw.line([(cx - s*0.2, cy + s*0.15), (cx + s*0.15, cy + s*0.4)], fill=EMERALD_COLOR, width=int(s*0.18))

def draw_logo_9(draw, cx, cy, s):
    # 方案9: 超音速前掠翼飞燕 (扁平极速箭头感)
    pts = [
        (cx + s*0.65, cy),          # 极速箭头鼻锥
        (cx + s*0.1, cy - s*0.1),
        (cx - s*0.45, cy - s*0.45), # 左前掠翼
        (cx - s*0.2, cy),
        (cx - s*0.5, cy + s*0.45),  # 燕尾左
        (cx - s*0.05, cy + s*0.15), # 燕尾深V
        (cx + s*0.1, cy + s*0.35),  # 燕尾右
    ]
    draw.polygon(pts, fill=WHITE)
    # 装饰光斑
    draw.ellipse([cx + s*0.2, cy - s*0.08, cx + s*0.35, cy + s*0.08], fill=CYAN_COLOR)

DESIGNS = [
    ("方案 1: 粗流线分叉剪刀尾", "高对比度粗线条，大分叉燕尾，小尺寸下极为醒目", draw_logo_1),
    ("方案 2: 字母 Y 展翅冲刺", "将首字母 Y 与冲刺飞燕自然融合，品牌专属性极强", draw_logo_2),
    ("方案 3: 极简超粗双弧流光", "仅用两道极简饱满弧线勾勒燕身与尾羽，绝对不糊", draw_logo_3),
    ("方案 4: 破环而出 燕环徽章", "呼应燕环与全局手势，冲破圆环的动态破框设计", draw_logo_4),
    ("方案 5: 负空间 Squircle 瓷贴", "深色圆角矩形内嵌纯白极简剪影，符合现代桌面风格", draw_logo_5),
    ("方案 6: 45度几何硬核切角", "现代赛博切割线条，硬朗科技感，无细小碎线", draw_logo_6),
    ("方案 7: 动态旋风太极双燕", "橙青撞色双向旋转，象征多端同步与急速响应", draw_logo_7),
    ("方案 8: 极简圆头粗胶囊线条", "全圆角粗笔刷构建，无论缩放到多小都清晰可见", draw_logo_8),
    ("方案 9: 超音速前掠翼箭头", "极速前掠翼 + 剪刀尾剪影，象征极速响应启动器", draw_logo_9),
]

def render_9grid_portfolio():
    img_w = 1600
    img_h = 1700
    canvas_img = Image.new("RGB", (img_w, img_h), BG_COLOR)
    draw = ImageDraw.Draw(canvas_img)
    
    # 顶部大标题
    title_text = "燕子 (Yanzi) 全新 Logo 探索设计方案集合（九宫格）"
    draw.text((80, 50), title_text, font=font_title, fill=WHITE)
    draw.text((80, 105), "专为桌面极速启动器设计 · 粗线条高对比度 · 完美兼顾 16x16 托盘极小尺寸与高分辨率大图标", font=font_card_desc, fill=TEXT_GRAY)
    
    start_x = 80
    start_y = 160
    card_w = 440
    card_h = 460
    gap_x = 40
    gap_y = 40
    
    for idx, (name, desc, draw_func) in enumerate(DESIGNS):
        row = idx // 3
        col = idx % 3
        cx = start_x + col * (card_w + gap_x)
        cy = start_y + row * (card_h + gap_y)
        
        # 绘制卡片背景
        draw.rounded_rectangle([cx, cy, cx + card_w, cy + card_h], radius=16, fill=CARD_BG, outline=CARD_BORDER, width=2)
        
        # 卡片顶部编号与标题
        draw.text((cx + 20, cy + 18), f"#{idx+1:02d}", font=font_card_num, fill=CYAN_COLOR)
        draw.text((cx + 75, cy + 20), name.split(": ")[1] if ": " in name else name, font=font_card_title, fill=WHITE)
        
        # 绘制 Logo 图形区域 (居中放置于卡片中上半部)
        logo_cx = cx + card_w // 2
        logo_cy = cy + 190
        draw_func(draw, logo_cx, logo_cy, s=110)
        
        # 绘制底部说明文字
        draw.line([(cx + 20, cy + 370), (cx + card_w - 20, cy + 370)], fill=CARD_BORDER, width=1)
        draw.text((cx + 20, cy + 390), desc, font=font_card_desc, fill=TEXT_GRAY)
        
        # 在右下角绘制一个 16x16 / 32x32 小尺寸缩略预览对比
        small_box_x = cx + card_w - 60
        small_box_y = cy + 310
        draw.rounded_rectangle([small_box_x, small_box_y, small_box_x + 40, small_box_y + 40], radius=6, fill=(15, 23, 42), outline=CARD_BORDER, width=1)
        draw_func(draw, small_box_x + 20, small_box_y + 20, s=14)
        draw.text((small_box_x - 30, small_box_y + 12), "32px", font=font_card_desc, fill=(100, 116, 139))
        
    out_file = os.path.join(ROOT, "燕子Logo_九宫格备选设计方案.png")
    canvas_img.save(out_file, dpi=(300, 300))
    print(f"Portfolio generated successfully: {out_file}")

if __name__ == "__main__":
    render_9grid_portfolio()
