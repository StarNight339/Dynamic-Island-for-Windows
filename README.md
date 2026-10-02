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

เอาเมาส์ชี้ = ขยาย, คลิกขวา = เมนู (ตั้งเวลา / ออก), ไอคอนใน tray = เปิดพร้อม Windows / ออก
island จะซ่อนตัวเองเมื่อมีแอปเต็มจอ (เกม, วิดีโอ)

## Run

```powershell
dotnet run
# หรือ build แบบไฟล์เดียว
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

## Local API (`http://localhost:5179/`)

```powershell
# แจ้งเตือน: icon = ตัวอักษร Segoe Fluent Icons หรืออีโมจิ, image = รูป (แทน icon), color = hex, duration = วินาที
curl.exe -s localhost:5179/notify -H "Content-Type: application/json" -d '{\"title\":\"Build passed\",\"body\":\"All 42 tests green\",\"color\":\"#34C759\",\"duration\":5}'

# อีโมจิสี (ใช้ได้ทั้งใน icon, title, body)
curl.exe -s localhost:5179/notify -H "Content-Type: application/json" -d '{\"title\":\"เสร็จแล้ว 🎉\",\"icon\":\"🤖\",\"color\":\"#3A3A3C\"}'

# รูปภาพแทน icon: URL, path ในเครื่อง หรือ data:image/png;base64,... (ไม่เกิน 5 MB, PNG/JPG/GIF/BMP/ICO)
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

## ข้อจำกัด

- ยังอ่านแจ้งเตือน toast ของแอปอื่นไม่ได้ (Windows กำหนดให้แอปต้องมี package identity / MSIX)
- แสดงเฉพาะบนจอหลัก
