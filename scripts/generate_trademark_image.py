# -*- coding: utf-8 -*-
import os
from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# 字体加载
font_title_path = "C:\\Windows\\Fonts\\msyhbd.ttc"  # 微软雅黑粗体
font_sub_path = "C:\\Windows\\Fonts\\msyh.ttc"    # 微软雅黑常规
if not os.path.exists(font_title_path):
    font_title_path = "C:\\Windows\\Fonts\\simhei.ttf"
if not os.path.exists(font_sub_path):
    font_sub_path = "C:\\Windows\\Fonts\\simsun.ttc"

font_zh = ImageFont.truetype(font_title_path, 88)
font_en = ImageFont.truetype(font_sub_path, 38)

# 1. 生成垂直上下居中版（标准商标申报版，1000x1000）
def generate_vertical_trademark(is_black_and_white=True):
    canvas_size = 1000
    bg_color = (255, 255, 255)
    img = Image.new("RGB", (canvas_size, canvas_size), bg_color)
    draw = ImageDraw.Draw(img)
    
    # 加载 Logo
    logo_file = os.path.join(ROOT, "src", "OpenQuickHost", "logo-black.png" if is_black_and_white else "logo.png")
    if not os.path.exists(logo_file):
        logo_file = os.path.join(ROOT, "src", "OpenQuickHost", "logo.png")
        
    logo = Image.open(logo_file).convert("RGBA")
    
    # 调整 Logo 大小 (例如 380x380)
    logo_size = 380
    logo = logo.resize((logo_size, logo_size), Image.Resampling.LANCZOS)
    
    if is_black_and_white:
        # 转换为纯黑墨稿
        r, g, b, a = logo.split()
        black_logo = Image.new("RGBA", logo.size, (20, 20, 20, 255))
        black_logo.putalpha(a)
        logo = black_logo
        
    logo_x = (canvas_size - logo_size) // 2
    logo_y = 130
    
    img.paste(logo, (logo_x, logo_y), logo)
    
    # 绘制中文 "燕子启动器"
    zh_text = "燕子启动器"
    zh_bbox = draw.textbbox((0, 0), zh_text, font=font_zh)
    zh_w = zh_bbox[2] - zh_bbox[0]
    zh_x = (canvas_size - zh_w) // 2
    zh_y = 570
    
    text_color = (20, 20, 20) if is_black_and_white else (15, 23, 42)
    draw.text((zh_x, zh_y), zh_text, font=font_zh, fill=text_color)
    
    # 绘制英文 "YANZI LAUNCHER"
    en_text = "YANZI  LAUNCHER"
    en_bbox = draw.textbbox((0, 0), en_text, font=font_en)
    en_w = en_bbox[2] - en_bbox[0]
    en_x = (canvas_size - en_w) // 2
    en_y = 700
    
    sub_color = (80, 80, 80) if is_black_and_white else (71, 85, 105)
    draw.text((en_x, en_y), en_text, font=font_en, fill=sub_color)
    
    # 中间装饰细线
    line_w = 420
    line_x1 = (canvas_size - line_w) // 2
    line_x2 = line_x1 + line_w
    line_y = 680
    line_color = (200, 200, 200) if is_black_and_white else (203, 213, 225)
    draw.line([(line_x1, line_y), (line_x2, line_y)], fill=line_color, width=2)
    
    suffix = "黑白墨稿" if is_black_and_white else "彩色品牌"
    filename = f"商标申报图样_{suffix}_燕子启动器.png"
    out_path = os.path.join(ROOT, filename)
    img.save(out_path, dpi=(300, 300))
    print(f"Generated: {out_path}")
    return out_path

if __name__ == "__main__":
    generate_vertical_trademark(is_black_and_white=True)
    generate_vertical_trademark(is_black_and_white=False)
