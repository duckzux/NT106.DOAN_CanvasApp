# CanvasApp — Documentation Index

## Tổng quan kiến trúc

| Tài liệu | Mô tả |
|---|---|
| [ARCHITECTURE.md](ARCHITECTURE.md) | Kiến trúc hệ thống đầy đủ: 5 executable + 1 shared lib, wire protocol, LB routing rules, peer mesh, drawing pipeline, persistence, security, lifecycle diagrams, trade-offs, index of changes. |
| [explain/all_message.md](explain/all_message.md) | Catalogue đầy đủ mọi `MessageType`: sender → receiver, transport (short-lived / persistent / peer mesh), JSON payload mẫu. |
| [explain/app_logic_socket_logic.md](explain/app_logic_socket_logic.md) | Luồng xử lý App Logic + Socket Logic end-to-end (vẽ, join, broadcast, peer relay). |
| [explain/io_file_network.md](explain/io_file_network.md) | I/O file (export PNG, import ảnh nền, image overlay, chat file) + I/O network (TCP line-delimited JSON, AES module opt-in). |
| [explain/thread_multithreading.md](explain/thread_multithreading.md) | Pattern đa luồng: async/await per-client, write-behind queue, timers (autosave/health-check/peer-heartbeat/OTP-cleanup), lock + ConcurrentDictionary. |

## Demo

| Tài liệu | Mô tả |
|---|---|
| [DEMO_GUIDE.md](DEMO_GUIDE.md) | **Tài liệu demo đầy đủ rubric 10 điểm.** A. Chuẩn bị → B. Khởi động → C.1–C.11 (mỗi mục có code evidence + demo runtime + bằng chứng quan sát) → D. Phụ lục Wireshark / AES / netstat / troubleshooting / checklist / presentation flow. |
| [script_present.md](script_present.md) | Script trình bày ngắn theo rubric (Nói gì → Bằng chứng → Câu hỏi thường gặp) cho mỗi mục. |

## Hỗ trợ

| Tài liệu | Mô tả |
|---|---|
| [BUGS.md](BUGS.md) | Notes về các issue thường gặp khi build/run (lock `CanvasApp.Common.dll`, …). |

## Tài liệu liên quan ở project root

- [../CHANGELOG.md](../CHANGELOG.md) — Lịch sử thay đổi chi tiết kèm root cause + fix per problem.
- [../PROJECT_HANDOFF.md](../PROJECT_HANDOFF.md) — Project brief: thông tin nhóm, scope, rubric mapping.
- [../Database/schema.sql](../Database/schema.sql) — DDL chính thức cho tất cả bảng MySQL.
- [../Database/seed_data.sql](../Database/seed_data.sql), [../Database/reset.sql](../Database/reset.sql) — Seed + reset cho demo.

## Quick map: tìm chỗ đang nói về gì

| Vấn đề | Mở file |
|---|---|
| Token format (HMAC) | [ARCHITECTURE.md §3, §9](ARCHITECTURE.md) |
| `ROOM_RESOLVE` vs double-join cũ | [ARCHITECTURE.md §4](ARCHITECTURE.md) |
| LB routing table + sniff | [ARCHITECTURE.md §5](ARCHITECTURE.md), [LoadBalancer.cs](../CanvasApp.LoadBalancer/LoadBalancer.cs) |
| Peer mesh + self-loop guard | [ARCHITECTURE.md §6](ARCHITECTURE.md), [CHANGELOG.md §2 (2026-05-22)](../CHANGELOG.md) |
| Image overlay DRAW_IMAGE | [ARCHITECTURE.md §7](ARCHITECTURE.md), [explain/all_message.md](explain/all_message.md) |
| OTP rate-limit / forgot-password | [ARCHITECTURE.md §9](ARCHITECTURE.md), [Services/OtpService.cs](../CanvasApp.AuthServer/Services/OtpService.cs) |
| AES module + opt-in wiring | [DEMO_GUIDE.md § Demo AES](DEMO_GUIDE.md), [Utils/MessageCrypto.cs](../CanvasApp.Common/Utils/MessageCrypto.cs) |
| 3-strike health check | [ARCHITECTURE.md §2.3](ARCHITECTURE.md), [DEMO_GUIDE.md § Bước 2](DEMO_GUIDE.md) |
