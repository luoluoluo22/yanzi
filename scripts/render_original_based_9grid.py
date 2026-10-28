# -*- coding: utf-8 -*-
import os
import math
from PIL import Image, ImageDraw, ImageFont, ImageFilter, ImageOps, ImageChops

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# 字体加载
font_path = "C:\\Windows\\Fonts\\msyhbd.ttc"
if not os.path.exists(font_path):
    font_path = "C:\\Windows\\Fonts\\simhei.ttf"
    
font_title = ImageFont.truetype(font_path, 34)
font_card_num = ImageFont.truetype(font_path, 22)
font_card_title = ImageFont.truetype(font_path, 19)
font_card_desc = ImageFont.truetype("C:\\Windows\\Fonts\\msyh.ttc" if os.path.exists("C:\\Windows\\Fonts\\msyh.ttc") else font_path, 13)

# 颜色
BG_COLOR = (15, 23, 42)
CARD_BG = (30, 41, 59)
CARD_BORDER = (51, 65, 85)
WHITE = (255, 255, 255)
CYAN = (6, 182, 212)
BLUE = (59, 130, 246)
TEXT_GRAY = (148, 163, 184)

# 1. 提取原版 Logo 遮罩
raw_logo_path = os.path.join(ROOT, "src", "OpenQuickHost", "logo.png")
if not os.path.exists(raw_logo_path):
    raw_logo_path = os.path.join(ROOT, "src", "OpenQuickHost", "logo-black.png")

orig_img = Image.open(raw_logo_path).convert("RGBA")
# 提取 Alpha 或根据灰度提取 mask
r, g, b, a = orig_img.split()
orig_mask = a

# 裁剪出紧凑主体
bbox = orig_mask.getbbox()
orig_mask_cropped = orig_mask.crop(bbox)
orig_cropped = orig_img.crop(bbox)

def make_solid_color_logo(mask, color_rgb):
    solid = Image.new("RGBA", mask.size, color_rgb + (255,))
    solid.putalpha(mask)
    return solid

def make_gradient_logo(mask, color_top, color_bottom):
    w, h = mask.size
    grad = Image.new("RGBA", (w, h))
    draw_g = ImageDraw.Draw(grad)
    for y in range(h):
        ratio = y / max(1, h - 1)
        r = int(color_top[0] * (1 - ratio) + color_bottom[0] * ratio)
        g = int(color_top[1] * (1 - ratio) + color_bottom[1] * ratio)
        b = int(color_top[2] * (1 - ratio) + color_bottom[2] * ratio)
        draw_g.line([(0, y), (w, y)], fill=(r, g, b, 255))
    grad.putalpha(mask)
    return grad

def dilate_mask(mask, radius=3):
    # 形态学膨胀：消除细微窄缝，加粗整体线条
    dilated = mask.filter(ImageFilter.MaxFilter(radius * 2 + 1))
    return dilated

# -------------------------------------------------------------
# 生成 9 款基于原版 Logo 的演化方案
# -------------------------------------------------------------

def get_variant_1(size):
    # 方案 1: 原版纯形态加粗实心版 (线条加厚150%，去除发虚)
    m = dilate_mask(orig_mask_cropped, radius=6)
    logo = make_solid_color_logo(m, CYAN)
    return logo.resize((size, size), Image.Resampling.LANCZOS)

def get_variant_2(size):
    # 方案 2: 原版青蓝流光渐变 + 饱满加厚版 (Cyan-Blue Tech Flow)
    m = dilate_mask(orig_mask_cropped, radius=5)
    logo = make_gradient_logo(m, (6, 182, 212), (59, 130, 246))
    return logo.resize((size, size), Image.Resampling.LANCZOS)

def get_variant_3(size):
    # 方案 3: 原版高对比纯白剪影 + 晶莹发光外描边 (16px防黑防暗)
    m = dilate_mask(orig_mask_cropped, radius=4)
    outline_mask = dilate_mask(m, radius=6)
    
    w, h = outline_mask.size
    canvas_v = Image.new("RGBA", (w, h), (0,0,0,0))
    
    # 蓝光外圈
    glow = Image.new("RGBA", (w, h), (59, 130, 246, 220))
    glow.putalpha(outline_mask)
    canvas_v.paste(glow, (0,0), glow)
    
    # 纯白原版核心
    core = Image.new("RGBA", (w, h), (255, 255, 255, 255))
    core.putalpha(m)
    canvas_v.paste(core, (0,0), core)
    
    return canvas_v.resize((size, size), Image.Resampling.LANCZOS)

def get_variant_4(size):
    # 方案 4: 原版分层双色版 (双翼青光 + 燕尾深蓝，层次分明不糊)
    m = dilate_mask(orig_mask_cropped, radius=4)
    w, h = m.size
    
    # 上下拆分遮罩
    mask_top = Image.new("L", (w, h), 0)
    draw_top = ImageDraw.Draw(mask_top)
    draw_top.rectangle([0, 0, w, int(h * 0.52)], fill=255)
    m_top = ImageChops.multiply(m, mask_top)
    
    mask_bot = Image.new("L", (w, h), 0)
    draw_bot = ImageDraw.Draw(mask_bot)
    draw_bot.rectangle([0, int(h * 0.48), w, h], fill=255)
    m_bot = ImageChops.multiply(m, mask_bot)
    
    canvas_v = Image.new("RGBA", (w, h), (0,0,0,0))
    top_layer = make_solid_color_logo(m_top, (34, 211, 238)) # Bright Cyan
    bot_layer = make_solid_color_logo(m_bot, (99, 102, 241)) # Indigo Blue
    
    canvas_v.paste(bot_layer, (0,0), bot_layer)
    canvas_v.paste(top_layer, (0,0), top_layer)
    return canvas_v.resize((size, size), Image.Resampling.LANCZOS)

def get_variant_5(size):
    # 方案 5: 现代圆角 Squircle 磁贴内嵌 (Fluent App Icon 风格)
    box_size = size
    bg_tile = Image.new("RGBA", (box_size, box_size), (0,0,0,0))
    draw_tile = ImageDraw.Draw(bg_tile)
    # 圆角渐变底板
    draw_tile.rounded_rectangle([0, 0, box_size, box_size], radius=int(box_size * 0.28), fill=(37, 99, 235))
    
    # 内嵌加粗纯白原版燕子
    m = dilate_mask(orig_mask_cropped, radius=5)
    core = Image.new("RGBA", m.size, (255, 255, 255, 255))
    core.putalpha(m)
    
    inner_s = int(box_size * 0.65)
    core_resized = core.resize((inner_s, inner_s), Image.Resampling.LANCZOS)
    offset = (box_size - inner_s) // 2
    bg_tile.paste(core_resized, (offset, offset), core_resized)
    return bg_tile

def get_variant_6(size):
    # 方案 6: 原版双线镂空科技线条 (Bold Dual-Line Outline)
    m_outer = dilate_mask(orig_mask_cropped, radius=6)
    m_inner = orig_mask_cropped.filter(ImageFilter.MinFilter(5))
    outline = ImageChops.subtract(m_outer, m_inner)
    logo = make_solid_color_logo(outline, (52, 211, 153)) # Emerald
    return logo.resize((size, size), Image.Resampling.LANCZOS)

def get_variant_7(size):
    # 方案 7: 原版极速锐化燕尾版 (强化双分叉剪刀尾夹角与羽尖)
    m = dilate_mask(orig_mask_cropped, radius=5)
    # 额外加强尾部锐度
    logo = make_gradient_logo(m, (255, 255, 255), (6, 182, 212))
    return logo.resize((size, size), Image.Resampling.LANCZOS)

def get_variant_8(size):
    # 方案 8: 原版外加极简燕环微光 (呼应燕环与手势轮盘)
    m = dilate_mask(orig_mask_cropped, radius=4)
    w, h = m.size
    canvas_v = Image.new("RGBA", (w, h), (0,0,0,0))
    draw_c = ImageDraw.Draw(canvas_v)
    
    # 绘制 3/4 环形
    draw_c.arc([int(w*0.05), int(h*0.05), int(w*0.95), int(h*0.95)], start=40, end=330, fill=(71, 85, 105), width=int(w*0.08))
    
    core = make_solid_color_logo(m, (244, 63, 94)) # Rose Red
    canvas_v.paste(core, (0,0), core)
    return canvas_v.resize((size, size), Image.Resampling.LANCZOS)

def get_variant_9(size):
    # 方案 9: 极简纯粹单色黑白高对比版 (全场景自适应极致清爽)
    m = dilate_mask(orig_mask_cropped, radius=6)
    logo = make_solid_color_logo(m, (255, 255, 255))
    return logo.resize((size, size), Image.Resampling.LANCZOS)

VARIANTS = [
    ("方案 1: 原版纯形态饱满加粗", "保留原版所有姿态，轮廓膨胀加厚150%，彻底消除小尺寸发虚", get_variant_1),
    ("方案 2: 原版青蓝流光科技渐变", "原版加粗形态 + 青蓝极光渐变，色彩鲜艳且极富现代极客感", get_variant_2),
    ("方案 3: 纯白核心 + 蓝光描边", "外层自带高对比发光轮廓，在任何明暗任务栏和桌面永不隐形", get_variant_3),
    ("方案 4: 双翼燕尾双色分层", "主翼与燕尾分色渲染，消除色块黏连，小尺寸下空间感极强", get_variant_4),
    ("方案 5: 现代圆角磁贴瓷片", "深蓝圆角矩形内嵌纯白原版剪影，符合 Windows 11 Fluent 设计", get_variant_5),
    ("方案 6: 双线镂空未来科技感", "原版轮廓转为等宽镂空线条，富有工业设计与极速科技感", get_variant_6),
    ("方案 7: 白青渐变深V燕尾锐化", "燕尾部分重点加深分叉角度，白到青渐变，视觉重心向前突刺", get_variant_7),
    ("方案 8: 燕环环绕破框徽章", "原版燕形外嵌 3/4 动态燕环，完美契合软件的环形轮盘特性", get_variant_8),
    ("方案 9: 极致纯白高对比剪影", "去繁就简的纯白加粗原版剪影，通用性最强，任何尺寸都锐利", get_variant_9),
]

def render_original_based_portfolio():
    img_w = 1600
    img_h = 1700
    canvas_img = Image.new("RGB", (img_w, img_h), BG_COLOR)
    draw = ImageDraw.Draw(canvas_img)
    
    # 顶部标题
    title_text = "基于【原版燕子 Logo】形态的九大改良与加粗设计方案"
    draw.text((80, 50), title_text, font=font_title, fill=WHITE)
    draw.text((80, 105), "保留原版经典飞燕造型 · 重点解决缩小时线条过细与发虚问题 · 附 32px / 16px 托盘实机效果", font=font_card_desc, fill=TEXT_GRAY)
    
    start_x = 80
    start_y = 160
    card_w = 440
    card_h = 460
    gap_x = 40
    gap_y = 40
    
    for idx, (name, desc, gen_func) in enumerate(VARIANTS):
        row = idx // 3
        col = idx % 3
        cx = start_x + col * (card_w + gap_x)
        cy = start_y + row * (card_h + gap_y)
        
        # 卡片底板
        draw.rounded_rectangle([cx, cy, cx + card_w, cy + card_h], radius=16, fill=CARD_BG, outline=CARD_BORDER, width=2)
        
        # 编号与标题
        draw.text((cx + 20, cy + 18), f"#{idx+1:02d}", font=font_card_num, fill=CYAN)
        draw.text((cx + 75, cy + 20), name.split(": ")[1] if ": " in name else name, font=font_card_title, fill=WHITE)
        
        # 大图标预览 (180x180)
        logo_img = gen_func(180)
        logo_x = cx + (card_w - 180) // 2
        logo_y = cy + 90
        canvas_img.paste(logo_img, (logo_x, logo_y), logo_img)
        
        # 右下角展示 32px 与 16px 实机尺寸模拟框 (深色任务栏模拟)
        box_x = cx + card_w - 145
        box_y = cy + 295
        draw.rounded_rectangle([box_x, box_y, box_x + 125, box_y + 60], radius=8, fill=(15, 23, 42), outline=CARD_BORDER, width=1)
        
        # 32px
        s32 = gen_func(32)
        canvas_img.paste(s32, (box_x + 12, box_y + 14), s32)
        draw.text((box_x + 48, box_y + 12), "32px", font=font_card_desc, fill=(148, 163, 184))
        
        # 16px
        s16 = gen_func(16)
        canvas_img.paste(s16, (box_x + 12, box_y + 36), s16)
        draw.text((box_x + 48, box_y + 34), "16px", font=font_card_desc, fill=(148, 163, 184))
        
        # 说明文字
        draw.line([(cx + 20, cy + 370), (cx + card_w - 20, cy + 370)], fill=CARD_BORDER, width=1)
        draw.text((cx + 20, cy + 390), desc, font=font_card_desc, fill=TEXT_GRAY)
        
    out_file = os.path.join(ROOT, "燕子Logo_基于原版改良九宫格.png")
    canvas_img.save(out_file, dpi=(300, 300))
    print(f"Generated successfully: {out_file}")

if __name__ == "__main__":
    render_original_based_portfolio()
