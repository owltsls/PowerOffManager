# -*- coding: utf-8 -*-
"""生成「准点·定时关机管家」应用图标：时钟 + 电源符号融合设计，输出多尺寸 ICO。"""
from PIL import Image, ImageDraw, ImageFilter
import os

OUT_DIR = os.path.dirname(os.path.abspath(__file__))
os.makedirs(OUT_DIR, exist_ok=True)

S = 1024  # 母版分辨率

def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(len(a)))

def make_base():
    """圆角方形背景 + 对角渐变 + 柔和内高光。"""
    r = int(S * 0.225)
    # 对角渐变 深蓝 -> 靛蓝
    top = (30, 58, 138)      # #1E3A8A
    bottom = (79, 70, 229)   # #4F46E5
    grad = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    gd = ImageDraw.Draw(grad)
    for y in range(S):
        t = y / (S - 1)
        gd.line([(0, y), (S, y)], fill=lerp(top, bottom, t) + (255,))

    # 顶部高光带（增强质感）
    hi = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    hd = ImageDraw.Draw(hi)
    hd.rounded_rectangle([0, 0, S - 1, int(S * 0.5)], radius=r,
                         fill=(255, 255, 255, 22))
    grad = Image.alpha_composite(grad, hi)

    # 圆角裁剪：把 alpha 设置为圆角蒙版
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, S - 1, S - 1],
                                           radius=r, fill=255)
    grad.putalpha(mask)
    return grad, mask

def draw_icon(img):
    d = ImageDraw.Draw(img)
    cx = cy = S // 2

    # ---- 时钟外圈（白色粗环）----
    ring_r = int(S * 0.305)
    ring_w = int(S * 0.055)
    box = [cx - ring_r, cy - ring_r, cx + ring_r, cy + ring_r]
    d.ellipse(box, outline=(255, 255, 255, 245), width=ring_w)

    # ---- 12 个刻度 ----
    import math
    for i in range(12):
        ang = math.radians(i * 30 - 90)
        r1 = ring_r - ring_w - int(S * 0.035)
        r2 = ring_r - ring_w - int(S * 0.008)
        x1 = cx + r1 * math.cos(ang); y1 = cy + r1 * math.sin(ang)
        x2 = cx + r2 * math.cos(ang); y2 = cy + r2 * math.sin(ang)
        w = int(S * 0.014) if i % 3 == 0 else int(S * 0.008)
        d.line([(x1, y1), (x2, y2)], fill=(255, 255, 255, 210), width=w)

    # ---- 时针 / 分针（指向约 10:10 的经典造型）----
    # 分针
    ang_min = math.radians(60 - 90)
    rmin = int(S * 0.205)
    xm = cx + rmin * math.cos(ang_min); ym = cy + rmin * math.sin(ang_min)
    d.line([(cx, cy), (xm, ym)], fill=(255, 255, 255, 255), width=int(S * 0.026))
    # 时针
    ang_hour = math.radians(305 - 90)
    rh = int(S * 0.145)
    xh = cx + rh * math.cos(ang_hour); yh = cy + rh * math.sin(ang_hour)
    d.line([(cx, cy), (xh, yh)], fill=(255, 255, 255, 255), width=int(S * 0.032))
    # 中心轴
    cr = int(S * 0.028)
    d.ellipse([cx - cr, cy - cr, cx + cr, cy + cr], fill=(255, 255, 255, 255))

    # ---- 电源符号（右下角橙色圆形徽章）----
    bx, by = int(S * 0.755), int(S * 0.755)
    br = int(S * 0.155)
    # 徽章底
    d.ellipse([bx - br, by - br, bx + br, by + br],
              fill=(239, 68, 68, 255), outline=(255, 255, 255, 235),
              width=int(S * 0.012))
    # 电源竖直短杆
    d.line([(bx, by - int(br * 0.62)), (bx, by - int(br * 0.08))],
           fill=(255, 255, 255, 255), width=int(S * 0.022))
    # 电源圆弧（开口朝上）
    import math
    arc_box = [bx - int(br * 0.55), by - int(br * 0.42),
               bx + int(br * 0.55), by + int(br * 0.62)]
    # PIL 角度：0=3点方向，顺时针；开口朝上 => 从 45° 到 135° 缺，画 135->405
    d.arc(arc_box, start=135, end=405, fill=(255, 255, 255, 255),
          width=int(S * 0.022))
    return img

def main():
    base, mask = make_base()
    icon = draw_icon(base)
    # 保存母版 PNG
    png_path = os.path.join(OUT_DIR, "app-icon-1024.png")
    icon.save(png_path)
    print("saved", png_path)

    # 多尺寸缩放（LANCZOS 高质量）
    sizes = [16, 24, 32, 48, 64, 128, 256]
    imgs = {}
    for s in sizes:
        imgs[s] = icon.resize((s, s), Image.Resampling.LANCZOS)

    ico_path = os.path.join(OUT_DIR, "app-icon.ico")
    imgs[256].save(ico_path, format="ICO",
                   sizes=[(s, s) for s in sizes])
    print("saved", ico_path, "sizes:", sizes)

    # 额外导出 256 PNG 便于预览
    prev = os.path.join(OUT_DIR, "app-icon-256.png")
    imgs[256].save(prev)
    print("saved", prev)

if __name__ == "__main__":
    main()
