# Dynamic Island for Windows

แคปซูลสีดำแบบ iPhone ลอยอยู่กลางขอบบนจอ ขยาย/หดด้วยแอนิเมชันสปริง (WPF, .NET 10)

| สิ่งที่แสดง | ที่มา |
|---|---|
| เพลงที่กำลังเล่น (ปก, ชื่อเพลง, ปุ่ม ⏮ ⏯ ⏭) | Windows media session: Spotify, YouTube ในเบราว์เซอร์ ฯลฯ |
| ระดับเสียง / ปิดเสียง | อุปกรณ์เสียงหลัก |
| เสียบสายชาร์จ, แบตต่ำ | Windows power API (เฉพาะโน้ตบุ๊ก) |
| นาฬิกา + ตั้งเวลาด่วน | เอาเมาส์ชี้ island ตอนว่าง หรือคลิกขวา |
| สถานะ Claude Code | Claude Code hooks → `POST /claude` |
| ข้อความอะไรก็ได้ | `POST /notify` |
| ชั้นวางไฟล์ (File shelf) | ลากไฟล์/โฟลเดอร์มาวางบน island |
| ไฟล์ที่กำลังดาวน์โหลดจากเบราว์เซอร์ | โฟลเดอร์ Downloads (Chrome, Edge, Firefox ฯลฯ) |

progress bar มีให้ในหลายที่: การอัปเดตแอป (MB / %), timer นับถอยหลัง, Claude ตอนกำลังทำงาน (แถบวิ่ง),
แจ้งเตือน (แถบเล็กๆ ด้านล่างบอกว่าเหลือเวลาอีกเท่าไหร่ก่อนหายไป), ดาวน์โหลด และ `POST /notify` ที่ส่ง `progress`

เอาเมาส์ชี้ = ขยาย, คลิกขวา = Quick Panel (ตั้งเวลา, เปิด/ปิดแต่ละระบบ, ⚙ Settings, ออก), คลิกไอคอนใน tray = เปิดหน้าต่าง Settings
island จะซ่อนตัวเองเมื่อมีแอปเต็มจอ (เกม, วิดีโอ)

## Download

โหลดได้จากหน้า [Releases](https://github.com/StarNight339/Dynamic-Island-for-Windows/releases) มี 2 แบบ

| ไฟล์ | ขนาด | |
|---|---|---|
| `DynamicIsland-<version>-win-x64.exe` | ~30 MB | ต้องติดตั้ง [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (x64) ก่อน |
| `DynamicIsland-<version>-win-x64-standalone.exe` | ~80 MB | รวม runtime มาแล้ว ดับเบิลคลิกได้เลย |

### เลขเวอร์ชันและ release อัตโนมัติ

ทุกครั้งที่ push เข้า `main` (รวมถึงตอน merge PR) GitHub Actions จะ build, รันเทสต์ แล้วปล่อย release ใหม่ให้เอง
ถ้าไม่อยากให้ปล่อย release ให้ใส่ `[skip release]` ไว้ในข้อความ commit

รูปแบบเวอร์ชันคือ **`0.X.Y-Z`** เช่น `v0.4.1-F`

| ส่วน | ความหมาย |
|---|---|
| `X` (0–9) | version ขยับเมื่อ patch เกิน 9 (0.4.9 → 0.5.0) |
| `Y` (0–9) | patch ขยับ +1 ทุก release |
| `Z` (A–Z) | ประเภทการเปลี่ยนแปลง ดูจากชื่อ branch ของ PR (`feature/...`, `fix/...`) หรือคำนำหน้า commit (`feat:`, `fix:`, `Add ...`) |

| Z | ประเภท | branch / คำนำหน้า commit |
|---|---|---|
| F | ฟีเจอร์ใหม่ | feat, feature, add |
| B | แก้บั๊ก | fix, bugfix, hotfix, bug |
| U | หน้าตา / UI | ui, style, design |
| P | ความเร็ว | perf, performance |
| R | ปรับโค้ด | refactor, cleanup |
| S | ความปลอดภัย | security, sec |
| D | เอกสาร | docs, doc, readme |
| T | เทสต์ | test, tests |
| C | CI / build | ci, build, release |
| M | อื่นๆ | chore และคำอื่นทั้งหมด |

คำนวณเวอร์ชันถัดไปเองได้ด้วย `.github/scripts/next-version.ps1` ถึง 0.9.9 แล้ว CI จะหยุดให้ตัดสินใจรูปแบบต่อไปเอง
ยังสร้าง release เองบน GitHub ได้เหมือนเดิม workflow จะ build ไฟล์แล้วแนบให้

### Auto update

ตั้งแต่ v0.3.0 แอปเช็ค release ล่าสุดบน GitHub หลังเปิด 30 วินาที และทุก 6 ชั่วโมง ถ้ามีเวอร์ชันใหม่จะโหลดไฟล์แบบเดียวกับที่ใช้อยู่
(standalone หรือไม่) มาแทนที่ แล้วรีสตาร์ทเอง ปิดได้ หรือกดเช็คเองได้ที่ Settings → About

- ระหว่างโหลดจะมี progress bar บน island บอก MB และ % ที่โหลดไปแล้ว
- tag ของ release ต้องเป็นเลขเวอร์ชัน เช่น `v0.4.1-F` หรือ `v0.3.0` (workflow ฝังเลขนี้ลงใน exe) และต้องไม่ติ๊ก pre-release
- เวอร์ชัน v0.4.0 ลงไปอ่าน tag ที่มี `-Z` ไม่ออก ต้องโหลด v0.4.1 ไปติดตั้งเองหนึ่งครั้ง หลังจากนั้นจะอัปเดตเองได้ตามปกติ
- อัปเดตเองได้เฉพาะไฟล์ exe เดี่ยวจากหน้า Releases ตัวที่ build เอง (`dotnet run`) จะแค่เปิดหน้าดาวน์โหลดให้
- ถ้าวาง exe ไว้ในโฟลเดอร์ที่ไม่มีสิทธิ์เขียน (เช่น Program Files) จะอัปเดตไม่ได้

## Run

```powershell
dotnet run
# หรือ build แบบไฟล์เดียว (ใช้ --no-self-contained; บน .NET 10 "--self-contained false" จะกลายเป็น true)
dotnet publish -c Release -r win-x64 --no-self-contained -p:PublishSingleFile=true
```

## Local API (`http://localhost:5179/`)

รับเฉพาะ request จากโปรแกรมในเครื่อง (curl, Python, hook ฯลฯ) ถ้ามี header `Origin` (เช่นเรียกจากหน้าเว็บในเบราว์เซอร์) จะได้ 403 เพื่อกันเว็บอื่นแอบส่งแจ้งเตือนปลอม และ body ต้องไม่เกิน 8 MB

```powershell
# แจ้งเตือน: icon = ตัวอักษร Segoe Fluent Icons หรืออีโมจิ, image = รูป (แทน icon), color = hex, duration = วินาที
curl.exe -s localhost:5179/notify -H "Content-Type: application/json" -d '{\"title\":\"Build passed\",\"body\":\"All 42 tests green\",\"color\":\"#34C759\",\"duration\":5}'

# อีโมจิสี (ใช้ได้ทั้งใน icon, title, body)
curl.exe -s localhost:5179/notify -H "Content-Type: application/json" -d '{\"title\":\"เสร็จแล้ว 🎉\",\"icon\":\"🤖\",\"color\":\"#3A3A3C\"}'

# รูปภาพแทน icon: URL, path ในเครื่อง หรือ data:image/png;base64,... (ไม่เกิน 5 MB, PNG/JPG/GIF/BMP/ICO; GIF หลายเฟรมจะเล่นวนเป็นภาพเคลื่อนไหว)
curl.exe -s localhost:5179/notify -H "Content-Type: application/json" -d '{\"title\":\"GitHub\",\"body\":\"New PR\",\"image\":\"https://github.com/fluidicon.png\"}'

# แถบความคืบหน้า: progress = 0-100, ส่ง id เดิมซ้ำเพื่ออัปเดต (ฟิลด์ที่ไม่ส่งจะคงค่าเดิม)
# ถึง 100 จะเป็นสีเขียวแล้วหายไปใน ~3 วินาที, ถ้าไม่มีอัปเดตเกิน 10 นาทีจะหายเอง
curl.exe -s localhost:5179/progress -H "Content-Type: application/json" -d '{\"id\":\"dl\",\"title\":\"Downloading\",\"body\":\"model.bin\",\"progress\":42}'
curl.exe -s localhost:5179/progress -H "Content-Type: application/json" -d '{\"id\":\"dl\",\"dismiss\":true}'

# ตัวจับเวลา (0 = ยกเลิก)
curl.exe -s localhost:5179/timer -H "Content-Type: application/json" -d '{\"seconds\":300}'

# สถานะ AI แบบง่าย: event = working | waiting | done | idle
curl.exe -s localhost:5179/claude -H "Content-Type: application/json" -d '{\"event\":\"working\",\"project\":\"my-agent\",\"message\":\"Thinking...\"}'
```

จาก Python / AI agent อื่น:

```python
import requests
requests.post("http://localhost:5179/notify", json={"title": "Agent", "body": "Task finished"})

for i, item in enumerate(items, 1):
    process(item)
    requests.post("http://localhost:5179/progress",
                  json={"id": "batch", "title": "Processing", "body": f"{i}/{len(items)}", "progress": i * 100 / len(items)})
```

Unit test (ไม่ต้องเปิดแอป, รันใน GitHub Actions ทุก push / PR ด้วย):

```
dotnet test tests/DynamicIsland.Tests
```

ทดสอบ API ทั้งหมด (ต้องเปิดแอปไว้ก่อน ใช้แค่ Python standard library):

```powershell
python -m unittest discover -s tests -v
```

## File shelf

ลากไฟล์หรือโฟลเดอร์จาก Explorer มาวางบน island ตัว island จะขยายเป็นที่วางไฟล์ แล้วเก็บไฟล์ไว้บนชั้นวาง
(เก็บแค่ path ไม่ได้คัดลอกไฟล์จริง และยังอยู่หลังปิดแล้วเปิดแอปใหม่ บันทึกใน `%AppData%\DynamicIsland\shelf.json`)

| ทำอะไร | วิธี |
|---|---|
| Copy file (เอาไปวางใน Explorer / แชท) | ปุ่ม 📋 บนไฟล์ หรือ **Copy all** |
| Copy path (เป็นข้อความ) | ปุ่ม 🔗 บนไฟล์ หรือ **Paths** |
| ลากออกไปวางที่อื่น | ลากไฟล์จาก shelf ไปวางในแอปอื่นได้เลย |
| เปิดไฟล์ / เปิดโฟลเดอร์ | ดับเบิลคลิกที่ไฟล์ / ปุ่ม 📂 |
| ลบออกจาก shelf / ล้างทั้งหมด | ปุ่ม ✕ / **Clear** (ไฟล์จริงไม่ถูกลบ) |

เปิด shelf ได้จากปุ่มโฟลเดอร์ใน Quick Panel (คลิกขวา) หรือชี้ที่ pill "N files" ไฟล์ที่ถูกย้าย/ลบไปแล้วจะหายจาก shelf เอง
ตั้งค่าได้ใน Settings → File shelf

## Downloads

ตอนดาวน์โหลดไฟล์จากเบราว์เซอร์ island จะแสดงชื่อไฟล์ ขนาดที่โหลดมาแล้ว และความเร็ว
เบราว์เซอร์ไม่บอกขนาดไฟล์ทั้งหมด แถบเลยเป็นแบบวิ่งไปเรื่อยๆ แทน % พอโหลดเสร็จจะขึ้น "Downloaded" สีเขียว

- ดูไฟล์ชั่วคราวในโฟลเดอร์ Downloads: `.crdownload` (Chrome, Edge, Brave), `.part` (Firefox), `.partial`, `.opdownload`
- ถ้าเบราว์เซอร์ตั้งให้เซฟไปโฟลเดอร์อื่น จะไม่เห็นการดาวน์โหลดนั้น
- Settings → Downloads ปิดได้ และตั้งให้ไฟล์ที่โหลดเสร็จเข้า File shelf อัตโนมัติได้

## Claude Code integration

คัดลอกบล็อก `hooks` จาก [`hooks/claude-settings.json`](hooks/claude-settings.json) ไปใส่ใน `~/.claude/settings.json`
(ถ้ามี `hooks` อยู่แล้ว ให้รวมเข้าด้วยกัน) hook จะส่ง JSON ของ Claude Code ไปที่ island ตรงๆ:

| Hook | Island |
|---|---|
| `UserPromptSubmit` | ✳ Working + เวลาที่ผ่านไป |
| `PreToolUse` | "Running Bash" ฯลฯ |
| `Notification` | เด้ง "Claude needs you" (รออนุญาต / รอ input) |
| `Stop` | เด้ง "Claude finished" |
| `SessionEnd` | ลบออก |

แต่ละ session แสดงแยกกัน ถ้า island ไม่ได้เปิดอยู่ `curl` จะ fail เงียบๆ และไม่ block Claude

## Settings

ค่าทั้งหมดเก็บที่ `%AppData%\DynamicIsland\settings.json` (แยก key ตามระบบ) และมีผลทันทีโดยไม่ต้องรีสตาร์ท
ยกเว้น port / เปิด-ปิด API ถ้าไฟล์เสีย แอปจะเริ่มด้วยค่า default และเก็บไฟล์เดิมไว้เป็น `settings.json.bak`

### เพิ่มหน้า settings ให้ระบบใหม่

1. เพิ่ม model ใน `Core/Settings/Sections.cs` (`static string SectionId` + property พร้อมค่า default)
2. สร้าง `Settings/Sections/XxxSection.cs` สืบจาก `SettingsSection<XxxSettings>` แล้ว `yield return` รายการ
   `Toggle(...)`, `Number(...)`, `NumberList(...)` หรือ `InfoItem` / `ActionItem` / `CustomItem`
   (ใส่ `quick: true` ใน toggle เพื่อให้ขึ้นใน Quick Panel ด้วย)
3. ลงทะเบียนใน `SettingsRegistry` ที่ `App.OnStartup`
4. ใน provider อ่านค่าด้วย `settings.Get<XxxSettings>()` และฟัง `settings.Changed` ถ้าต้องตอบสนองทันที

ไม่ต้องเขียน XAML เพิ่ม หน้าต่าง Settings และ Quick Panel สร้าง UI จากรายการเหล่านี้ให้เอง

## ข้อจำกัด

- ยังอ่านแจ้งเตือน toast ของแอปอื่นไม่ได้ (Windows กำหนดให้แอปต้องมี package identity / MSIX)
- แสดงเฉพาะบนจอหลัก
