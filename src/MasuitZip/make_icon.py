import struct, zlib

W = H = 256
BLUE   = (47, 111, 237, 255)
LIGHT  = (86, 144, 255, 255)
YELLOW = (255, 196, 0, 255)
WHITE  = (255, 255, 255, 255)
DARK   = (55, 60, 72, 255)
CLEAR  = (0, 0, 0, 0)

px = [[CLEAR for _ in range(W)] for _ in range(H)]


def rect(x0, y0, x1, y1, c):
    for y in range(max(0, y0), min(H, y1)):
        row = px[y]
        for x in range(max(0, x0), min(W, x1)):
            row[x] = c


# 文件夹主体 + 盖子
rect(24, 78, 232, 232, BLUE)
rect(24, 46, 124, 88, BLUE)

# 高光
rect(24, 46, 124, 58, LIGHT)
rect(24, 78, 232, 90, LIGHT)

# 黄色拉链条
rect(104, 28, 152, 244, YELLOW)

# 白色拉链头
rect(96, 14, 160, 46, WHITE)

# 拉链齿
y = 64
while y < 232:
    rect(110, y, 146, y + 10, DARK)
    y += 26

# 倒角，让边缘不那么硬
for y in range(H):
    for x in range(W):
        if px[y][x][3] == 0:
            continue
        # 主体右下角
        if x > 220 and y > 220 and (x - 220) + (y - 220) > 12:
            px[y][x] = CLEAR
        # 盖子左上角
        if x < 36 and 46 <= y < 88 and (36 - x) + (y - 46) > 10:
            px[y][x] = CLEAR

raw = bytearray()
for y in range(H):
    raw.append(0)
    for x in range(W):
        r, g, b, a = px[y][x]
        raw += bytes((r, g, b, a))


def chunk(tag, data):
    return struct.pack('>I', len(data)) + tag + data + struct.pack('>I', zlib.crc32(tag + data) & 0xFFFFFFFF)


png = b'\x89PNG\r\n\x1a\n'
png += chunk(b'IHDR', struct.pack('>IIBBBBB', W, H, 8, 6, 0, 0, 0))
png += chunk(b'IDAT', zlib.compress(bytes(raw), 9))
png += chunk(b'IEND', b'')

ico = struct.pack('<HHH', 0, 1, 1)
ico += struct.pack('<BBBBHHII', 0, 0, 0, 0, 1, 32, len(png), 22)
ico += png

open('app.ico', 'wb').write(ico)
print('app.ico', len(ico), 'bytes')
