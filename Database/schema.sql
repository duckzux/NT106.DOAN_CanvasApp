-- ============== USERS ==============
CREATE TABLE users (
  id              INT AUTO_INCREMENT PRIMARY KEY,
  username        VARCHAR(50)  NOT NULL UNIQUE,
  password_hash   VARCHAR(255) NOT NULL,
  email           VARCHAR(100),
  avatar_color    VARCHAR(7)   DEFAULT '#3498db',
  created_at      TIMESTAMP    DEFAULT CURRENT_TIMESTAMP,
  last_login_at   TIMESTAMP    NULL,
  INDEX idx_users_username (username)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- ============== ROOMS ==============
CREATE TABLE rooms (
  id              VARCHAR(36)  PRIMARY KEY,             -- GUID
  name            VARCHAR(100) NOT NULL,
  owner_id        INT          NOT NULL,
  password_hash   VARCHAR(255) NULL,                    -- mật khẩu phòng (tùy chọn)
  max_users       INT          DEFAULT 8,
  is_active       BOOLEAN      DEFAULT TRUE,            -- cờ xóa mềm (soft-delete)
  canvas_width    INT          DEFAULT 1920,
  canvas_height   INT          DEFAULT 1080,
  template        VARCHAR(50)  DEFAULT 'Blank',        -- mẫu phòng (Blank, Kanban, etc.)
  invite_code     VARCHAR(8)   NULL,                    -- mã mời 6 ký tự (A-Z2-9)
  created_at      TIMESTAMP    DEFAULT CURRENT_TIMESTAMP,
  updated_at      TIMESTAMP    DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  FOREIGN KEY (owner_id) REFERENCES users(id),
  INDEX idx_rooms_active (is_active, updated_at),
  UNIQUE KEY uk_invite_code (invite_code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- ============== ROOM_MEMBERS (tư cách thành viên, không phải trạng thái online) ==============
CREATE TABLE room_members (
  room_id         VARCHAR(36)  NOT NULL,
  user_id         INT          NOT NULL,
  role            ENUM('OWNER','MEMBER','VIEWER') NOT NULL DEFAULT 'MEMBER',
  joined_at       TIMESTAMP    DEFAULT CURRENT_TIMESTAMP,
  last_seen_at    TIMESTAMP    DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (room_id, user_id),
  FOREIGN KEY (room_id) REFERENCES rooms(id) ON DELETE CASCADE,
  FOREIGN KEY (user_id) REFERENCES users(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- LƯU Ý: Trạng thái "hiện đang online" KHÔNG được lưu ở đây. Nó nằm trong RAM của server.

-- ============== CANVAS_SNAPSHOTS ==============
CREATE TABLE canvas_snapshots (
  id              BIGINT AUTO_INCREMENT PRIMARY KEY,
  room_id         VARCHAR(36)  NOT NULL,
  version         INT          NOT NULL,                -- tăng dần đều cho mỗi phòng
  snapshot_data   LONGTEXT     NOT NULL,                -- JSON hoặc blob đã nén
  action_seq_at   BIGINT       NOT NULL,                -- số thứ tự (seq) của draw_action cuối cùng bao gồm trong đây
  byte_size       INT          NOT NULL,                -- để giám sát
  created_at      TIMESTAMP    DEFAULT CURRENT_TIMESTAMP,
  FOREIGN KEY (room_id) REFERENCES rooms(id) ON DELETE CASCADE,
  UNIQUE KEY uk_room_version (room_id, version),
  INDEX idx_room_created (room_id, created_at DESC)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- ============== DRAW_ACTIONS (nhật ký delta, dọn rác (GC) thường xuyên) ==============
CREATE TABLE draw_actions (
  id              BIGINT AUTO_INCREMENT PRIMARY KEY,
  room_id         VARCHAR(36)  NOT NULL,
  user_id         INT          NOT NULL,
  seq_no          BIGINT       NOT NULL,                -- tăng dần đều cho mỗi phòng (để sắp xếp)
  action_type     VARCHAR(20)  NOT NULL,                -- STROKE / SHAPE / TEXT / CLEAR / UNDO
  action_data     JSON         NOT NULL,
  client_ts       BIGINT       NOT NULL,                -- timestamp của client (ms)
  server_ts       TIMESTAMP    DEFAULT CURRENT_TIMESTAMP,
  is_undone       BOOLEAN      DEFAULT FALSE,           -- hoàn tác mềm (đừng dùng DELETE)
  FOREIGN KEY (room_id) REFERENCES rooms(id) ON DELETE CASCADE,
  FOREIGN KEY (user_id) REFERENCES users(id),
  INDEX idx_room_seq (room_id, seq_no),
  INDEX idx_gc (room_id, server_ts)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- ============== CHAT_MESSAGES ==============
CREATE TABLE chat_messages (
  id              BIGINT AUTO_INCREMENT PRIMARY KEY,
  room_id         VARCHAR(36)  NOT NULL,
  user_id         INT          NOT NULL,
  message         TEXT         NOT NULL,
  sent_at         TIMESTAMP    DEFAULT CURRENT_TIMESTAMP,
  FOREIGN KEY (room_id) REFERENCES rooms(id) ON DELETE CASCADE,
  FOREIGN KEY (user_id) REFERENCES users(id),
  INDEX idx_chat_room (room_id, id),
  INDEX user_id (user_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- ============== EMAIL_OTP_CODES (OTP cho forgot password và registration) ==============
CREATE TABLE email_otp_codes (
  token           CHAR(36)     PRIMARY KEY,             -- UUID token
  username        VARCHAR(50)  NOT NULL,                -- username để reset
  email           VARCHAR(100) NOT NULL,
  code_hash       VARCHAR(255) NOT NULL,                -- hashed OTP code (bcrypt hoặc SHA256)
  expires_at      DATETIME     NOT NULL,                -- khi nào OTP hết hạn
  attempts        INT          DEFAULT 0,               -- số lần nhập sai
  used            TINYINT(1)   DEFAULT 0,               -- 1 = đã dùng
  created_at      DATETIME     DEFAULT CURRENT_TIMESTAMP,
  INDEX idx_otp_email (email)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;