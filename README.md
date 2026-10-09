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

เอาเมาส์ชี้ = ขยาย, คลิกขวา = Quick Panel (ตั้งเวลา, เปิด/ปิดแต่ละระบบ, ⚙ Settings, ออก), คลิกไอคอนใน tray = เปิดหน้าต่าง Settings
island จะซ่อนตัวเองเมื่อมีแอปเต็มจอ (เกม, วิดีโอ)

## Download

โหลดได้จากหน้า [Releases](https://github.com/StarNight339/Dynamic-Island-for-Windows/releases) มี 2 แบบ

| ไฟล์ | ขนาด | |
|---|---|---|
| `DynamicIsland-<version>-win-x64.exe` | ~30 MB | ต้องติดตั้ง [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (x64) ก่อน |
| `DynamicIsland-<version>-win-x64-standalone.exe` | ~80 MB | รวม runtime มาแล้ว ดับเบิลคลิกได้เลย |

ไฟล์ถูก build อัตโนมัติโดย GitHub Actions ทุกครั้งที่ publish release ใหม่

### Auto update

ตั้งแต่ v0.3.0 แอปเช็ค release ล่าสุดบน GitHub หลังเปิด 30 วินาที และทุก 6 ชั่วโมง ถ้ามีเวอร์ชันใหม่จะโหลดไฟล์แบบเดียวกับที่ใช้อยู่
(standalone หรือไม่) มาแทนที่ แล้วรีสตาร์ทเอง ปิดได้ หรือกดเช็คเองได้ที่ Settings → About

- tag ของ release ต้องเป็นเลขเวอร์ชัน เช่น `v0.3.0` (workflow ฝังเลขนี้ลงใน exe) และต้องไม่ติ๊ก pre-release
- อัปเดตเองได้เฉพาะไฟล์ exe เดี่ยวจากหน้า Releases ตัวที่ build เอง (`dotnet run`) จะแค่เปิดหน้าดาวน์โหลดให้
- ถ้าวาง exe ไว้ในโฟลเดอร์ที่ไม่มีสิทธิ์เขียน (เช่น Program Files) จะอัปเดตไม่ได้

## Run

```powershell
dotnet run
# หรือ build แบบไฟล์เดียว (ใช้ --no-self-contained; บน .NET 10 "--self-contained false" จะกลายเป็น true)
dotnet publish -c Release -r win-x64 --no-self-contained -p:PublishSingleFile=true
```

## Local API (`http://localhost:5179/`)

```powershell
# แจ้งเตือน: icon = ตัวอักษร Segoe Fluent Icons หรืออีโมจิ, image = รูป (แทน icon), color = hex, duration = วินาที
curl.exe -s localhost:5179/notify -H "Content-Type: application/json" -d '{\"title\":\"Build passed\",\"body\":\"All 42 tests green\",\"color\":\"#34C759\",\"duration\":5}'

# อีโมจิสี (ใช้ได้ทั้งใน icon, title, body)
curl.exe -s localhost:5179/notify -H "Content-Type: application/json" -d '{\"title\":\"เสร็จแล้ว 🎉\",\"icon\":\"🤖\",\"color\":\"#3A3A3C\"}'

# รูปภาพแทน icon: URL, path ในเครื่อง หรือ data:image/png;base64,... (ไม่เกิน 5 MB, PNG/JPG/GIF/BMP/ICO; GIF หลายเฟรมจะเล่นวนเป็นภาพเคลื่อนไหว)
curl.exe -s localhost:5179/notify -H "Content-Type: application/json" -d '{\"title\":\"GitHub\",\"body\":\"New PR\",\"image\":\"https://github.com/fluidicon.png\"}'

# ตัวจับเวลา (0 = ยกเลิก)
curl.exe -s localhost:5179/timer -H "Content-Type: application/json" -d '{\"seconds\":300}'

# สถานะ AI แบบง่าย: event = working | waiting | done | idle
curl.exe -s localhost:5179/claude -H "Content-Type: application/json" -d '{\"event\":\"working\",\"project\":\"my-agent\",\"message\":\"Thinking...\"}'
```

จาก Python / AI agent อื่น:

```python
import requests
requests.post("http://localhost:5179/notify", json={"title": "Agent", "body": "Task finished"})
```

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
