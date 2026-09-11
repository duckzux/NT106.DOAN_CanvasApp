-- ============== RESET DATABASE ==============
-- Script này xóa toàn bộ dữ liệu trong database để demo lại từ đầu
-- Dùng: mysql -h localhost -u root canvasapp < reset.sql

SET FOREIGN_KEY_CHECKS = 0;

TRUNCATE TABLE chat_messages;
TRUNCATE TABLE draw_actions;
TRUNCATE TABLE canvas_snapshots;
TRUNCATE TABLE email_otp_codes;
TRUNCATE TABLE room_members;
TRUNCATE TABLE rooms;
TRUNCATE TABLE users;

SET FOREIGN_KEY_CHECKS = 1;

-- Hoặc nếu muốn xóa toàn bộ database và tạo lại:
-- DROP DATABASE canvasapp;
-- CREATE DATABASE canvasapp;
-- Rồi chạy: mysql -h localhost -u root canvasapp < schema.sql
