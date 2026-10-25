import pathlib,struct,zlib,hashlib,json,math,sys
if len(sys.argv) != 2:
 raise SystemExit('Usage: python3 fixtures/macos/generate_ui_demo.py <new-empty-directory>')
root=pathlib.Path(sys.argv[1]).resolve()
if root.exists() and any(root.iterdir()):
 raise SystemExit('Destination must be empty; existing files are never replaced.')
root.mkdir(parents=True, exist_ok=True)
def png(path,w,h,palette):
 raw=bytearray(); bg,glow=palette
 for y in range(h):
  raw.append(0)
  for x in range(w):
   t=min(1,max(0,1-math.hypot((x-w*.6)/(w*.9),(y-h*.34)/(h*.7))))
   rgb=[int(bg[i]*(1-t*.45)+glow[i]*t*.45) for i in range(3)]
   if (x-w*.64)**2+(y-h*.29)**2<(w*.16)**2: rgb=[220,209,171]
   if y>h*.64+(math.sin(x*.024)*h*.025):rgb=[int(c*.45) for c in bg]
   for n in range(10):
    bx=(n*47+15)%w; by=h*(.61+(.18*(n%3)/3))
    if bx<x<bx+w*.09 and by<y:rgb=[int(c*.65)+n*2 for c in bg]
   if y>h*.88 and (x+y)%31<2:rgb=glow
   raw.extend(bytes(rgb))
 def chunk(k,v):return struct.pack('!I',len(v))+k+v+struct.pack('!I',zlib.crc32(k+v)&0xffffffff)
 path.write_bytes(b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('!2I5B',w,h,8,2,0,0,0))+chunk(b'IDAT',zlib.compress(raw,9))+chunk(b'IEND',b''))
films=[('DEMO-001','星港纪录 · 夜行列车',((25,49,63),(100,171,169))),('DEMO-002','雨后山城 · 短篇',((62,41,58),(205,135,117))),('DEMO-003','群岛来信 · 秋日',((46,61,43),(155,182,101)))]
hashes={}
for id,title,palette in films:
 d=root/id;d.mkdir(parents=True,exist_ok=True)
 v=d/(id+'.mp4');v.write_bytes(('SYNTHETIC NON-PLAYABLE VIDEO FIXTURE '+id).encode());hashes[str(v)]=hashlib.sha256(v.read_bytes()).hexdigest()
 (d/'movie.nfo').write_text(f'''<?xml version="1.0" encoding="utf-8"?>
<movie collection="synthetic-only"><id>{id}</id><title>{title}</title><originaltitle>Notes from the Night</originaltitle><premiered>2026-10-21</premiered><runtime>42</runtime><rating>N/A</rating><studio>Northlight Test Studio</studio><director>Demo Director</director><actor><name>合成角色</name><role>旁白</role><custom>preserve actor extension</custom></actor><genre>纪录短片</genre><genre>合成测试</genre><plot>一段关于城市、光影与旅途的虚构资料。\n此视频和图片仅用于本地功能验收，不含真实片库数据。</plot><tag>Label: Test Collection</tag><opaque-extension retained="true">必须保留的未知节点</opaque-extension><resume><position>12</position><total>42</total></resume></movie>''')
 png(d/'poster.png',460,640,palette);png(d/'fanart.png',960,540,palette)
(root/'video-sha256.json').write_text(json.dumps(hashes,indent=2))
print(root)
