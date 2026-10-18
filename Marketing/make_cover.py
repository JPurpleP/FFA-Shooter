"""Fluketown DM cover art: graded screenshot + logo. Outputs cover_630x500.png and banner_1920x1080.png."""
import os, sys
from PIL import Image, ImageDraw, ImageFont, ImageEnhance, ImageFilter, ImageChops

here = os.path.dirname(os.path.abspath(__file__))
src = Image.open(os.path.join(here, sys.argv[1] if len(sys.argv) > 1 else 'h2_01.png')).convert('RGB')
# Paint out the pickup-spot marker on the road with road texture from just beside it (soft-edged patch).
def clone(img, box, dx):
    x0, y0, x1, y1 = box
    patch = img.crop((x0 + dx, y0, x1 + dx, y1))
    m = Image.new('L', patch.size, 0)
    ImageDraw.Draw(m).ellipse([6, 4, patch.width - 6, patch.height - 4], fill=255)
    img.paste(patch, (x0, y0), m.filter(ImageFilter.GaussianBlur(6)))
clone(src, (410, 868, 670, 960), 300)
FONT = r'C:\Windows\Fonts\impact.ttf'
FONT_SMALL = r'C:\Windows\Fonts\bahnschrift.ttf'
ORANGE, YELLOW, DARK, RED = (243, 138, 42), (255, 214, 64), (24, 18, 30), (214, 38, 46)


def grade(im):
    im = ImageEnhance.Contrast(im).enhance(1.12)
    im = ImageEnhance.Color(im).enhance(1.18)
    # warm late-afternoon tint
    warm = Image.new('RGB', im.size, (255, 196, 120))
    im = Image.blend(im, ImageChops.multiply(im, warm), 0.18)
    # vignette
    w, h = im.size
    mask = Image.new('L', (w, h), 0)
    d = ImageDraw.Draw(mask)
    d.ellipse([-w * 0.25, -h * 0.35, w * 1.25, h * 1.35], fill=255)
    mask = mask.filter(ImageFilter.GaussianBlur(min(w, h) * 0.18))
    dark = ImageEnhance.Brightness(im).enhance(0.45)
    return Image.composite(im, dark, mask)


def gradient_text(size, text, font, top, bottom):
    """Text filled with a vertical gradient, on a transparent layer of `size`."""
    mask = Image.new('L', size, 0)
    ImageDraw.Draw(mask).text((0, 0), text, font=font, fill=255)
    grad = Image.new('RGB', size)
    gd = ImageDraw.Draw(grad)
    for y in range(size[1]):
        t = y / max(1, size[1] - 1)
        gd.line([(0, y), (size[0], y)], fill=tuple(int(top[i] + (bottom[i] - top[i]) * t) for i in range(3)))
    layer = Image.new('RGBA', size, (0, 0, 0, 0))
    layer.paste(grad, (0, 0), mask)
    return layer


def logo(width):
    """FLUKETOWN with a DM tag and tagline, scaled to `width`, on transparency."""
    big = ImageFont.truetype(FONT, 400)
    word = 'FLUKETOWN'
    bbox = ImageDraw.Draw(Image.new('L', (1, 1))).textbbox((0, 0), word, font=big)
    tw, th = bbox[2], bbox[3]
    pad = 70
    W, H = tw + pad * 2 + 520, th + pad * 2 + 170
    canvas = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    # drop shadow
    sh = Image.new('L', (W, H), 0)
    ImageDraw.Draw(sh).text((pad + 26, pad + 30), word, font=big, fill=200, stroke_width=26, stroke_fill=200)
    sh = sh.filter(ImageFilter.GaussianBlur(16))
    canvas.paste(Image.new('RGBA', (W, H), (0, 0, 0, 255)), (0, 0), sh)
    # thick outline, then the gradient fill
    ImageDraw.Draw(canvas).text((pad, pad), word, font=big, fill=DARK, stroke_width=24, stroke_fill=DARK)
    fill = gradient_text((W, H), '', big, YELLOW, ORANGE)
    textmask = Image.new('L', (W, H), 0)
    ImageDraw.Draw(textmask).text((pad, pad), word, font=big, fill=255)
    grad = gradient_text((W, H), '', big, YELLOW, ORANGE)
    g = Image.new('RGB', (W, H))
    gd = ImageDraw.Draw(g)
    top_y, bot_y = pad + bbox[1], pad + th
    for y in range(H):
        t = min(1, max(0, (y - top_y) / max(1, bot_y - top_y)))
        gd.line([(0, y), (W, y)], fill=tuple(int(YELLOW[i] + (ORANGE[i] - YELLOW[i]) * t) for i in range(3)))
    canvas.paste(g, (0, 0), textmask)
    # a highlight stripe across the top third of the letters
    hl = Image.new('L', (W, H), 0)
    ImageDraw.Draw(hl).rectangle([0, top_y, W, top_y + (bot_y - top_y) * 0.28], fill=70)
    hl = ImageChops.multiply(hl, textmask)
    canvas.paste(Image.new('RGBA', (W, H), (255, 255, 255, 255)), (0, 0), hl)

    # DM tag: a tilted red badge to the right of the word
    tag_font = ImageFont.truetype(FONT, 215)
    tag = Image.new('RGBA', (470, 380), (0, 0, 0, 0))
    td = ImageDraw.Draw(tag)
    td.rounded_rectangle([20, 30, 450, 350], radius=40, fill=RED, outline=DARK, width=22)
    tb = td.textbbox((0, 0), 'DM', font=tag_font)
    td.text(((470 - (tb[2] - tb[0])) / 2 - tb[0], (380 - (tb[3] - tb[1])) / 2 - tb[1]), 'DM', font=tag_font, fill='white',
            stroke_width=9, stroke_fill=DARK)
    tag = tag.rotate(8, resample=Image.BICUBIC, expand=True)
    canvas.alpha_composite(tag, (pad + tw + 30, pad + bbox[1] + (th - bbox[1]) // 2 - tag.height // 2))

    # tagline
    small = ImageFont.truetype(FONT_SMALL, 96)
    line = 'FREE-FOR-ALL  DEATHMATCH'
    lb = ImageDraw.Draw(canvas).textbbox((0, 0), line, font=small)
    lx = pad + (tw - (lb[2] - lb[0])) // 2
    ly = pad + th + 40
    ImageDraw.Draw(canvas).text((lx, ly), line, font=small, fill='white', stroke_width=10, stroke_fill=DARK)

    canvas = canvas.crop(canvas.getbbox())
    canvas = canvas.rotate(4, resample=Image.BICUBIC, expand=True)  # a little attitude
    scale = width / canvas.width
    return canvas.resize((width, int(canvas.height * scale)), Image.LANCZOS)


def compose(base, size, logo_width_frac, logo_top_frac):
    im = base.copy()
    # cover-crop to the target aspect around the centre of the lineup
    tw, th = size
    target = tw / th
    w, h = im.size
    if w / h > target:
        nw = int(h * target)
        x0 = (w - nw) // 2
        im = im.crop((x0, 0, x0 + nw, h))
    else:
        nh = int(w / target)
        im = im.crop((0, 0, w, nh))
    im = im.resize(size, Image.LANCZOS)
    im = grade(im).convert('RGBA')
    lg = logo(int(tw * logo_width_frac))
    im.alpha_composite(lg, ((tw - lg.width) // 2, int(th * logo_top_frac)))
    return im.convert('RGB')


banner = compose(src, (1920, 1080), 0.62, 0.035)
banner.save(os.path.join(here, 'banner_1920x1080.png'))
cover = compose(src, (630, 500), 0.86, 0.03)
cover.save(os.path.join(here, 'cover_630x500.png'))
print('ok')
